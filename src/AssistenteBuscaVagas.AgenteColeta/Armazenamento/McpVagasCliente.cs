using System.Text.Json;
using System.Text.Json.Serialization;
using AssistenteBuscaVagas.AgenteColeta.Fontes;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AssistenteBuscaVagas.AgenteColeta.Armazenamento;

public sealed record FonteInfo(string Nome, string Tipo, bool Ativa, DateTimeOffset? UltimaColetaEm, int TotalVagas);

public sealed record VagaRejeitada(int Indice, string? Titulo, string Motivo);

public sealed record ResultadoSalvamento(string Fonte, int Recebidas, int Novas, int Atualizadas, IReadOnlyList<VagaRejeitada> Rejeitadas);

/// <summary>
/// Cliente MCP do mcp_vagas. O endereço "http://mcp-vagas" é resolvido pelo service discovery do Aspire
/// (o AppHost faz WithReference(mcpVagas) e o ServiceDefaults liga a descoberta em todo HttpClient).
/// </summary>
public sealed class McpVagasCliente(McpClient mcp) : IAsyncDisposable
{
    public const string NomeHttpClient = "mcp-vagas";
    private const int LoteMaximo = 100; // limite de salvar_vagas por chamada

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<McpVagasCliente> ConectarAsync(IHttpClientFactory fabrica, ILoggerFactory logs, CancellationToken ct)
    {
        var transporte = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri("http://mcp-vagas/mcp"), Name = "mcp-vagas" },
            fabrica.CreateClient(NomeHttpClient), logs, ownsHttpClient: true);
        return new McpVagasCliente(await McpClient.CreateAsync(transporte, loggerFactory: logs, cancellationToken: ct));
    }

    /// <summary>Ferramentas do mcp_vagas que o LLM pode chamar diretamente (só leitura).</summary>
    public async Task<IReadOnlyList<AITool>> FerramentasDeLeituraAsync(CancellationToken ct)
    {
        var todas = await mcp.ListToolsAsync(cancellationToken: ct);
        return todas.Where(t => t.Name is "listar_fontes" or "listar_vagas").Cast<AITool>().ToList();
    }

    public async Task<IReadOnlyList<FonteInfo>> ListarFontesAsync(CancellationToken ct)
    {
        var r = await ChamarAsync("listar_fontes", new Dictionary<string, object?>(), ct);
        return Ler<List<FonteInfo>>(r) ?? [];
    }

    /// <summary>Grava em lotes de até 100 e soma os resultados. Chamado pelo código, nunca pelo LLM.</summary>
    public async Task<ResultadoSalvamento> SalvarAsync(string fonte, IReadOnlyList<VagaColetada> vagas, CancellationToken ct)
    {
        int novas = 0, atualizadas = 0;
        var rejeitadas = new List<VagaRejeitada>();

        for (var inicio = 0; inicio < vagas.Count; inicio += LoteMaximo)
        {
            var lote = vagas.Skip(inicio).Take(LoteMaximo).ToList();
            var r = await ChamarAsync("salvar_vagas", new Dictionary<string, object?> { ["fonte"] = fonte, ["vagas"] = lote }, ct);
            var parcial = Ler<ResultadoSalvamento>(r)
                ?? throw new InvalidOperationException("salvar_vagas devolveu uma resposta inesperada.");
            novas += parcial.Novas;
            atualizadas += parcial.Atualizadas;
            rejeitadas.AddRange(parcial.Rejeitadas.Select(x => x with { Indice = x.Indice + inicio }));
        }
        return new ResultadoSalvamento(fonte, vagas.Count, novas, atualizadas, rejeitadas);
    }

    private async Task<CallToolResult> ChamarAsync(string ferramenta, Dictionary<string, object?> args, CancellationToken ct)
    {
        var r = await mcp.CallToolAsync(ferramenta, args, cancellationToken: ct);
        if (r.IsError == true)
            throw new InvalidOperationException($"{ferramenta} falhou: {TextoDe(r)}");
        return r;
    }

    // Ferramentas que devolvem lista vêm embrulhadas em { "result": [...] } no structuredContent;
    // o bloco de texto sempre traz o JSON puro, então lemos dele.
    private static T? Ler<T>(CallToolResult r) => JsonSerializer.Deserialize<T>(TextoDe(r), Json);

    private static string TextoDe(CallToolResult r) =>
        string.Concat(r.Content.OfType<TextContentBlock>().Select(c => c.Text));

    public ValueTask DisposeAsync() => mcp.DisposeAsync();
}
