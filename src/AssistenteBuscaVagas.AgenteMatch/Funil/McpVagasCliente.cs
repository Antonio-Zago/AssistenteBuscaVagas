using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using AssistenteBuscaVagas.AgenteMatch.Llm;

namespace AssistenteBuscaVagas.AgenteMatch.Funil;

/// <summary>
/// Cliente MCP do mcp_vagas, só com as ferramentas de leitura que o funil usa. O código chama as ferramentas
/// diretamente: nesta fase não há LLM no agente_match.
/// </summary>
public sealed class McpVagasCliente(McpClient mcp) : IRepositorioMatches, IAsyncDisposable
{
    public const string NomeHttpClient = "mcp-vagas";
    private const int TamanhoPagina = 200;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<McpVagasCliente> ConectarAsync(IHttpClientFactory fabrica, ILoggerFactory logs, CancellationToken ct)
    {
        var transporte = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri("http://mcp-vagas/mcp"), Name = "mcp-vagas" },
            fabrica.CreateClient(NomeHttpClient), logs, ownsHttpClient: true);
        return new McpVagasCliente(await McpClient.CreateAsync(transporte, loggerFactory: logs, cancellationToken: ct));
    }

    public async Task<PerfilCompleto?> ObterPerfilAsync(CancellationToken ct) =>
        Ler<PerfilCompleto>(await ChamarAsync("obter_perfil", [], ct));

    /// <summary>Lê todas as vagas da janela, página por página, até <paramref name="maximo"/>.</summary>
    public async Task<IReadOnlyList<VagaParaMatch>> ListarVagasAsync(bool? remoto, int dias, int maximo, CancellationToken ct)
    {
        var vagas = new List<VagaParaMatch>();
        var aposId = 0;
        while (vagas.Count < maximo)
        {
            var args = new Dictionary<string, object?>
            {
                ["publicadasNosUltimosDias"] = dias,
                ["aposId"] = aposId,
                ["limite"] = Math.Min(TamanhoPagina, maximo - vagas.Count),
            };
            if (remoto is not null) args["remoto"] = remoto;

            var pagina = Ler<List<VagaParaMatch>>(await ChamarAsync("listar_vagas_para_match", args, ct)) ?? [];
            vagas.AddRange(pagina);
            if (pagina.Count < (int)args["limite"]!) break;
            aposId = pagina[^1].Id;
        }
        return vagas;
    }

    /// <summary>Vagas específicas, sem janela de datas (as rotuladas do conjunto de avaliação).</summary>
    public async Task<IReadOnlyList<VagaParaMatch>> ListarVagasPorIdsAsync(IReadOnlyList<int> ids, CancellationToken ct)
    {
        var vagas = new List<VagaParaMatch>();
        foreach (var lote in ids.Distinct().Chunk(TamanhoPagina))
        {
            var args = new Dictionary<string, object?> { ["ids"] = lote, ["limite"] = TamanhoPagina };
            vagas.AddRange(Ler<List<VagaParaMatch>>(await ChamarAsync("listar_vagas_para_match", args, ct)) ?? []);
        }
        return vagas;
    }

    // ---- Cache das avaliações do LLM (IRepositorioMatches) ----

    public async Task<IReadOnlyList<AvaliacaoVaga>> ObterAsync(string chave, IReadOnlyList<int> vagaIds, CancellationToken ct) =>
        Ler<List<AvaliacaoVaga>>(await ChamarAsync("obter_matches", new() { ["chaveAvaliacao"] = chave, ["vagaIds"] = vagaIds }, ct)) ?? [];

    public async Task SalvarAsync(string chave, string modelo, int versaoPrompt, IReadOnlyList<AvaliacaoVaga> avaliacoes, CancellationToken ct)
    {
        foreach (var lote in avaliacoes.Chunk(100))
            await ChamarAsync("salvar_matches", new()
            {
                ["chaveAvaliacao"] = chave,
                ["modelo"] = modelo,
                ["versaoPrompt"] = versaoPrompt,
                ["matches"] = lote,
            }, ct);
    }

    // ---- Rótulos do conjunto de avaliação ----

    public async Task<IReadOnlyList<RotuloResumo>> ListarRotulosAsync(CancellationToken ct) =>
        Ler<List<RotuloResumo>>(await ChamarAsync("listar_rotulos", [], ct)) ?? [];

    public async Task<RotuloResumo> RotularAsync(int vagaId, bool relevante, string? observacao, CancellationToken ct) =>
        Ler<RotuloResumo>(await ChamarAsync("rotular_vaga", new() { ["vagaId"] = vagaId, ["relevante"] = relevante, ["observacao"] = observacao }, ct))
        ?? throw new InvalidOperationException("rotular_vaga devolveu uma resposta inesperada.");

    public async Task<bool> RemoverRotuloAsync(int vagaId, CancellationToken ct) =>
        Ler<bool>(await ChamarAsync("remover_rotulo", new() { ["vagaId"] = vagaId }, ct));

    private async Task<CallToolResult> ChamarAsync(string ferramenta, Dictionary<string, object?> args, CancellationToken ct)
    {
        var r = await mcp.CallToolAsync(ferramenta, args, cancellationToken: ct);
        if (r.IsError == true)
            throw new InvalidOperationException($"{ferramenta} devolveu erro: {TextoDe(r)}");
        return r;
    }

    private static T? Ler<T>(CallToolResult r)
    {
        var texto = TextoDe(r);
        return string.IsNullOrWhiteSpace(texto) ? default : JsonSerializer.Deserialize<T>(texto, Json);
    }

    private static string TextoDe(CallToolResult r) =>
        string.Concat(r.Content.OfType<TextContentBlock>().Select(c => c.Text));

    public ValueTask DisposeAsync() => mcp.DisposeAsync();
}
