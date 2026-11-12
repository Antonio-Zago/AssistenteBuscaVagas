using System.Text.Json.Serialization;

namespace AssistenteBuscaVagas.AgenteColeta.Web;

public sealed class FirecrawlOptions
{
    public string UrlBase { get; set; } = "https://api.firecrawl.dev/v2/";

    /// <summary>Chave da API. Vem do AppHost (parâmetro secreto firecrawl-api-key), nunca do appsettings.</summary>
    public string? ApiKey { get; set; }
}

public sealed record ResultadoBusca(string Url, string Titulo, string Trecho);

/// <summary>
/// Cliente da API REST v2 do Firecrawl. Usamos a API e não o MCP hospedado porque precisamos de
/// opções que o MCP não expõe: <c>rawHtml</c> (para ler o JSON-LD), <c>tbs</c> e <c>excludeDomains</c>.
/// </summary>
public sealed class FirecrawlCliente(HttpClient http)
{
    public async Task<IReadOnlyList<ResultadoBusca>> BuscarAsync(
        string consulta, int limite, string? periodo, IReadOnlyList<string> dominiosExcluidos, CancellationToken ct)
    {
        var corpo = new BuscaRequisicao(consulta, limite, periodo, dominiosExcluidos.Count > 0 ? dominiosExcluidos : null);
        using var resposta = await http.PostAsJsonAsync("search", corpo, ct);
        await GarantirSucessoAsync(resposta, ct);

        var json = await resposta.Content.ReadFromJsonAsync<BuscaResposta>(ct);
        return (json?.Data?.Web ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r.Url))
            .Select(r => new ResultadoBusca(r.Url!, r.Title ?? "", r.Description ?? ""))
            .ToList();
    }

    /// <summary>HTML bruto da página, sem limpeza (os blocos &lt;script type="application/ld+json"&gt; ficam intactos).</summary>
    public async Task<PaginaBaixada> ObterHtmlAsync(string url, CancellationToken ct)
    {
        var corpo = new ScrapeRequisicao(url, ["rawHtml"], OnlyMainContent: false, Timeout: 30000);
        using var resposta = await http.PostAsJsonAsync("scrape", corpo, ct);
        await GarantirSucessoAsync(resposta, ct);

        var json = await resposta.Content.ReadFromJsonAsync<ScrapeResposta>(ct);
        return new PaginaBaixada(
            json?.Data?.Metadata?.Url ?? url,
            json?.Data?.Metadata?.StatusCode ?? 0,
            json?.Data?.RawHtml ?? "");
    }

    private static async Task GarantirSucessoAsync(HttpResponseMessage resposta, CancellationToken ct)
    {
        if (resposta.IsSuccessStatusCode) return;
        var detalhe = await resposta.Content.ReadAsStringAsync(ct);
        if (detalhe.Length > 300) detalhe = detalhe[..300];
        throw new HttpRequestException($"Firecrawl respondeu {(int)resposta.StatusCode}: {detalhe}", null, resposta.StatusCode);
    }

    private sealed record BuscaRequisicao(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("limit")] int Limit,
        [property: JsonPropertyName("tbs"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Tbs,
        [property: JsonPropertyName("excludeDomains"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? ExcludeDomains);

    private sealed record BuscaResposta([property: JsonPropertyName("data")] BuscaDados? Data);
    private sealed record BuscaDados([property: JsonPropertyName("web")] List<BuscaItem>? Web);
    private sealed record BuscaItem(
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("description")] string? Description);

    private sealed record ScrapeRequisicao(
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("formats")] string[] Formats,
        [property: JsonPropertyName("onlyMainContent")] bool OnlyMainContent,
        [property: JsonPropertyName("timeout")] int Timeout);

    private sealed record ScrapeResposta([property: JsonPropertyName("data")] ScrapeDados? Data);
    private sealed record ScrapeDados(
        [property: JsonPropertyName("rawHtml")] string? RawHtml,
        [property: JsonPropertyName("metadata")] ScrapeMetadados? Metadata);
    private sealed record ScrapeMetadados(
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("statusCode")] int? StatusCode);
}

public sealed record PaginaBaixada(string UrlFinal, int StatusCode, string Html);
