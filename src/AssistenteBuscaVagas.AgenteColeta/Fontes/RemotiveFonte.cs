using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace AssistenteBuscaVagas.AgenteColeta.Fontes;

public sealed class RemotiveOptions
{
    /// <summary>Slug da categoria na Remotive (ex.: software-dev). Vazio = todas.</summary>
    public string? Categoria { get; set; } = "software-dev";

    /// <summary>Filtro opcional por texto (título e descrição).</summary>
    public string? Busca { get; set; }
}

/// <summary>
/// Cliente da API pública da Remotive (https://remotive.com/api-documentation).
/// Termos: citar a Remotive como fonte, sempre linkar a URL original da vaga,
/// não redistribuir as vagas e consultar no máximo ~4 vezes por dia.
/// </summary>
public sealed class RemotiveFonte(HttpClient http, IOptions<RemotiveOptions> options) : IFonteVagas
{
    public string Nome => "Remotive";

    public async Task<IReadOnlyList<VagaColetada>> BuscarAsync(CancellationToken cancellationToken)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.Value.Categoria))
            query.Add($"category={Uri.EscapeDataString(options.Value.Categoria)}");
        if (!string.IsNullOrWhiteSpace(options.Value.Busca))
            query.Add($"search={Uri.EscapeDataString(options.Value.Busca)}");

        var url = "api/remote-jobs" + (query.Count > 0 ? "?" + string.Join('&', query) : "");
        var resposta = await http.GetFromJsonAsync<RemotiveResposta>(url, cancellationToken)
            ?? throw new InvalidOperationException("A Remotive devolveu uma resposta vazia.");

        return resposta.Jobs.Select(Converter).ToList();
    }

    private static VagaColetada Converter(RemotiveJob j) => new(
        IdExterno: j.Id.ToString(CultureInfo.InvariantCulture),
        Empresa: j.CompanyName.Trim(),
        Titulo: j.Title.Trim(),
        Descricao: j.Description ?? "", // HTML; o mcp_vagas converte para texto
        Local: Vazio(j.CandidateRequiredLocation),
        Remoto: true, // a Remotive só publica vagas remotas
        Categoria: Vazio(j.Category),
        TipoContrato: Vazio(j.JobType),
        Salario: Vazio(j.Salary),
        Tags: j.Tags ?? [],
        Url: j.Url,
        PublicadaEm: LerData(j.PublicationDate));

    private static string? Vazio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // A Remotive manda a data sem fuso (ex.: "2026-11-07T01:10:55"); assumimos UTC.
    private static DateTimeOffset? LerData(string? s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
            ? new DateTimeOffset(d, TimeSpan.Zero)
            : null;

    private sealed record RemotiveResposta([property: JsonPropertyName("jobs")] List<RemotiveJob> Jobs);

    private sealed record RemotiveJob(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("company_name")] string CompanyName,
        [property: JsonPropertyName("category")] string? Category,
        [property: JsonPropertyName("tags")] List<string>? Tags,
        [property: JsonPropertyName("job_type")] string? JobType,
        [property: JsonPropertyName("publication_date")] string? PublicationDate,
        [property: JsonPropertyName("candidate_required_location")] string? CandidateRequiredLocation,
        [property: JsonPropertyName("salary")] string? Salary,
        [property: JsonPropertyName("description")] string? Description);
}
