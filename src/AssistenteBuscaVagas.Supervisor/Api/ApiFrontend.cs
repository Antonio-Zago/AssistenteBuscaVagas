using System.Text.Json;
using A2A;
using AssistenteBuscaVagas.Comum.A2A;
using AssistenteBuscaVagas.Supervisor.Orquestracao;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AssistenteBuscaVagas.Supervisor.Api;

/// <summary>Falha que o frontend mostra ao usuário (validação, agente fora do ar). Vira ProblemDetails.</summary>
public sealed class ErroApiException(string mensagem, int status) : Exception(mensagem)
{
    public int Status { get; } = status;
}

/// <summary>
/// Acesso aos dados para a API do frontend: ferramentas do mcp_vagas e habilidades dos agentes, chamadas pelo código,
/// sem LLM. Devolve o JSON como veio (JsonElement): o supervisor só repassa, quem tipa é o frontend.
/// </summary>
public sealed class ClienteDados(IHttpClientFactory http, ILoggerFactory logs, IConfiguration config)
{
    private readonly ILogger _log = logs.CreateLogger<ClienteDados>();

    public async Task<JsonElement> FerramentaAsync(string ferramenta, Dictionary<string, object?> args, CancellationToken ct)
    {
        var transporte = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri("http://mcp-vagas/mcp"), Name = "mcp-vagas" },
            http.CreateClient(ServicoSupervisor.ClienteMcp), logs, ownsHttpClient: true);
        await using var mcp = await McpClient.CreateAsync(transporte, loggerFactory: logs, cancellationToken: ct);

        var r = await mcp.CallToolAsync(ferramenta, args, cancellationToken: ct);
        var texto = string.Concat(r.Content.OfType<TextContentBlock>().Select(c => c.Text));
        if (r.IsError == true)
            throw new ErroApiException(texto.Replace($"An error occurred invoking '{ferramenta}': ", ""), StatusCodes.Status422UnprocessableEntity);
        return string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone();
    }

    public async Task<JsonElement> HabilidadeAsync(string agente, string habilidade, string texto, object? dados, CancellationToken ct)
    {
        var url = config[$"Agentes:{agente}"] ?? $"http://{agente}/a2a";
        try
        {
            using var cliente = new A2AClient(new Uri(url), http.CreateClient(ServicoSupervisor.ClienteA2A));
            var pedido = new PedidoHabilidade(habilidade, dados is null ? null : JsonSerializer.SerializeToElement(dados), texto);
            var r = RespostaHabilidade.Ler(await cliente.SendMessageAsync(new SendMessageRequest { Message = pedido.ParaMensagem() }, ct));
            if (r.Erro is not null)
                throw new ErroApiException(r.Erro, StatusCodes.Status422UnprocessableEntity);
            return r.Resultado ?? default;
        }
        catch (Exception ex) when (ex is HttpRequestException or A2AException)
        {
            _log.LogWarning("Falha ao chamar {Agente}.{Habilidade}: {Erro}", agente, habilidade, ex.Message);
            throw new ErroApiException($"O {agente} não respondeu: {ex.Message}", StatusCodes.Status502BadGateway);
        }
    }
}

public sealed record CandidaturaEntrada(string Status, string? Notas);

/// <summary>
/// A API do frontend (fase 6). O frontend só fala com o supervisor: aqui ficam as leituras e gravações
/// estruturadas, feitas em código e grátis; /conversa continua sendo a porta em linguagem natural (paga).
/// Gravar candidaturas direto no mcp_vagas é uma exceção consciente à regra "gravar passa pelos agentes":
/// quem decide é o usuário clicando, não um LLM, e o mcp_vagas valida a etapa.
/// </summary>
public static class ApiFrontend
{
    public static void MapApiFrontend(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        // Histórico do worker: as vagas recomendadas em cada rodada (grátis).
        api.MapGet("/execucoes", (int? limite, ClienteDados dados, CancellationToken ct) =>
            Executar(() => dados.FerramentaAsync("listar_execucoes_diarias", new() { ["limite"] = Math.Clamp(limite ?? 14, 1, 60) }, ct)));

        // Ranking atual com o Claude: top 5 com nota e lacunas. PAGO para vagas ainda não avaliadas (as outras vêm do cache).
        api.MapPost("/recomendacoes/avaliar", (bool? somenteNovas, ClienteDados dados, CancellationToken ct) =>
            Executar(() => dados.HabilidadeAsync("agente-match", "recomendar_vagas", "ranking atual pedido pelo frontend",
                new { somenteNovas = somenteNovas == true }, ct)));

        // Histórico de todas as vagas já avaliadas pelo Claude (do cache de avaliações), grátis.
        api.MapGet("/avaliacoes", (int? notaMinima, bool? soPerfilAtual, bool? soRecomendadas, string? busca, int? limite,
            ClienteDados dados, CancellationToken ct) =>
            Executar(() => dados.FerramentaAsync("listar_avaliacoes", new()
            {
                ["notaMinima"] = Math.Clamp(notaMinima ?? 0, 0, 100),
                ["soPerfilAtual"] = soPerfilAtual ?? true,
                ["soRecomendadas"] = soRecomendadas ?? false,
                ["busca"] = string.IsNullOrWhiteSpace(busca) ? null : busca.Trim(),
                ["limite"] = Math.Clamp(limite ?? 100, 1, 200),
            }, ct)));

        // Ranking sem IA (filtros + BM25), grátis.
        api.MapGet("/funil", (int? limite, ClienteDados dados, CancellationToken ct) =>
            Executar(() => dados.HabilidadeAsync("agente-match", "ver_funil", "ranking sem IA pedido pelo frontend",
                new { limite = Math.Clamp(limite ?? 20, 1, 100) }, ct)));

        // Lacunas de skills nas vagas avaliadas (grátis).
        api.MapGet("/lacunas", (int? notaMinima, ClienteDados dados, CancellationToken ct) =>
            Executar(() => dados.FerramentaAsync("estatisticas_skills", new() { ["notaMinima"] = Math.Clamp(notaMinima ?? 0, 0, 100) }, ct)));

        // Kanban.
        api.MapGet("/candidaturas", (ClienteDados dados, CancellationToken ct) =>
            Executar(() => dados.FerramentaAsync("listar_candidaturas", [], ct)));

        api.MapPut("/candidaturas/{vagaId:int}", (int vagaId, CandidaturaEntrada entrada, ClienteDados dados, CancellationToken ct) =>
            Executar(() => dados.FerramentaAsync("atualizar_candidatura",
                new() { ["vagaId"] = vagaId, ["status"] = entrada.Status, ["notas"] = entrada.Notas }, ct)));

        api.MapDelete("/candidaturas/{vagaId:int}", async (int vagaId, ClienteDados dados, CancellationToken ct) =>
        {
            try
            {
                var removida = await dados.FerramentaAsync("remover_candidatura", new() { ["vagaId"] = vagaId }, ct);
                return removida.ValueKind == JsonValueKind.True ? Results.NoContent() : Results.NotFound();
            }
            catch (ErroApiException ex)
            {
                return Results.Problem(ex.Message, statusCode: ex.Status);
            }
        });

        api.MapGet("/vagas/{id:int}", (int id, ClienteDados dados, CancellationToken ct) =>
            Executar(() => dados.FerramentaAsync("obter_vaga", new() { ["id"] = id }, ct)));
    }

    private static async Task<IResult> Executar(Func<Task<JsonElement>> acao)
    {
        try
        {
            var r = await acao();
            return r.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? Results.NotFound() : Results.Json(r);
        }
        catch (ErroApiException ex)
        {
            return Results.Problem(ex.Message, statusCode: ex.Status);
        }
    }
}
