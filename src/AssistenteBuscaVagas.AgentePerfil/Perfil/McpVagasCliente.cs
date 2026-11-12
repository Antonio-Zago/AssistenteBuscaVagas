using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AssistenteBuscaVagas.AgentePerfil.Perfil;

/// <summary>
/// Cliente MCP do mcp_vagas, só com as ferramentas de perfil. Aqui o código chama as ferramentas
/// diretamente: nenhuma delas é entregue a um LLM.
/// </summary>
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

    public async Task<PerfilCompleto> SalvarAsync(Curriculo? curriculo, Preferencias? preferencias, CancellationToken ct)
    {
        var args = new Dictionary<string, object?>();
        if (curriculo is not null) args["curriculo"] = curriculo;
        if (preferencias is not null) args["preferencias"] = preferencias;

        return Ler<PerfilCompleto>(await ChamarAsync("salvar_perfil", args, ct))
            ?? throw new InvalidOperationException("salvar_perfil devolveu uma resposta inesperada.");
    }

    public async Task<PerfilCompleto?> ObterAsync(CancellationToken ct) =>
        Ler<PerfilCompleto>(await ChamarAsync("obter_perfil", [], ct));

    private async Task<CallToolResult> ChamarAsync(string ferramenta, Dictionary<string, object?> args, CancellationToken ct)
    {
        var r = await mcp.CallToolAsync(ferramenta, args, cancellationToken: ct);
        // Erro de validação do mcp_vagas: a mensagem explica o que corrigir (não contém o currículo).
        if (r.IsError == true)
            throw new PerfilRejeitadoException($"{ferramenta} rejeitou os dados: {TextoDe(r)}");
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

/// <summary>O mcp_vagas recusou os dados (validação). Vira HTTP 422.</summary>
public sealed class PerfilRejeitadoException(string mensagem) : Exception(mensagem);
