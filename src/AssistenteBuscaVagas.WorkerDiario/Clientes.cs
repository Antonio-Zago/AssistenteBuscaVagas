using System.Text.Json;
using A2A;
using AssistenteBuscaVagas.Comum.A2A;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AssistenteBuscaVagas.WorkerDiario;

/// <summary>
/// Chama os agentes por A2A, com o envelope do projeto. O worker fala direto com cada agente (sem supervisor):
/// o fluxo é fixo, não há o que rotear nem decidir.
/// </summary>
public sealed class ClienteAgentes(IHttpClientFactory http, IConfiguration config)
{
    public const string NomeHttpClient = "a2a";

    public async Task<RespostaHabilidade> ChamarAsync(string agente, string habilidade, string texto, object? dados, CancellationToken ct)
    {
        // URL lógica, resolvida pelo service discovery do Aspire. Pode ser trocada em Agentes:{nome}.
        var url = config[$"Agentes:{agente}"] ?? $"http://{agente}/a2a";
        using var cliente = new A2AClient(new Uri(url), http.CreateClient(NomeHttpClient));
        var pedido = new PedidoHabilidade(habilidade, dados is null ? null : JsonSerializer.SerializeToElement(dados), texto);
        return RespostaHabilidade.Ler(await cliente.SendMessageAsync(new SendMessageRequest { Message = pedido.ParaMensagem() }, ct));
    }
}

/// <summary>Cliente MCP do mcp_vagas, só com as ferramentas do histórico do worker.</summary>
public sealed class McpVagasCliente(McpClient mcp) : IAsyncDisposable
{
    public const string NomeHttpClient = "mcp-vagas";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<McpVagasCliente> ConectarAsync(IHttpClientFactory fabrica, ILoggerFactory logs, CancellationToken ct)
    {
        var transporte = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri("http://mcp-vagas/mcp"), Name = "mcp-vagas" },
            fabrica.CreateClient(NomeHttpClient), logs, ownsHttpClient: true);
        return new McpVagasCliente(await McpClient.CreateAsync(transporte, loggerFactory: logs, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ExecucaoRegistrada>> ListarExecucoesAsync(int limite, CancellationToken ct) =>
        Ler<List<ExecucaoRegistrada>>(await ChamarAsync("listar_execucoes_diarias", new() { ["limite"] = limite }, ct)) ?? [];

    public async Task RegistrarAsync(DateOnly dia, string status, int tentativas, DateTimeOffset inicio, DateTimeOffset fim,
        ResumoDiario resumo, CancellationToken ct) =>
        await ChamarAsync("registrar_execucao_diaria", new()
        {
            ["data"] = dia.ToString("yyyy-MM-dd"),
            ["status"] = status,
            ["tentativas"] = tentativas,
            ["iniciadaEm"] = inicio,
            ["concluidaEm"] = fim,
            ["resumo"] = JsonSerializer.SerializeToElement(resumo, Json),
            ["recomendadas"] = resumo.VagasRecomendadas.Select(v => new { v.VagaId, v.Nota }).ToList(),
        }, ct);

    private async Task<CallToolResult> ChamarAsync(string ferramenta, Dictionary<string, object?> args, CancellationToken ct)
    {
        var r = await mcp.CallToolAsync(ferramenta, args, cancellationToken: ct);
        if (r.IsError == true)
            throw new InvalidOperationException($"{ferramenta} devolveu erro: {string.Concat(r.Content.OfType<TextContentBlock>().Select(c => c.Text))}");
        return r;
    }

    private static T? Ler<T>(CallToolResult r)
    {
        var texto = string.Concat(r.Content.OfType<TextContentBlock>().Select(c => c.Text));
        return string.IsNullOrWhiteSpace(texto) ? default : JsonSerializer.Deserialize<T>(texto, Json);
    }

    public ValueTask DisposeAsync() => mcp.DisposeAsync();
}

public sealed record ExecucaoRegistrada(string Data, string Status, int Tentativas, DateTimeOffset IniciadaEm, DateTimeOffset ConcluidaEm, JsonElement Resumo);
