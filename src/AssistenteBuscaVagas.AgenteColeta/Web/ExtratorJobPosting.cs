using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AssistenteBuscaVagas.AgenteColeta.Fontes;

namespace AssistenteBuscaVagas.AgenteColeta.Web;

/// <summary>
/// Extrai a vaga do JSON-LD schema.org <c>JobPosting</c> que sites de carreira publicam para o Google for Jobs.
/// É código determinístico, sem LLM: o dado vem do próprio site, nada é inventado,
/// e um texto malicioso na página não tem como dar ordens a ninguém aqui.
/// </summary>
public static partial class ExtratorJobPosting
{
    /// <summary>Devolve as vagas encontradas na página (pode haver mais de uma) ou lista vazia.</summary>
    public static IReadOnlyList<VagaColetada> Extrair(string html, string urlPagina)
    {
        var vagas = new List<VagaColetada>();
        foreach (Match m in BlocoJsonLd().Matches(html))
        {
            // Normalmente o JSON vem cru; alguns sites o escapam como HTML. Tenta cru primeiro.
            var bruto = m.Groups["json"].Value.Trim();
            if ((Ler(bruto) ?? Ler(WebUtility.HtmlDecode(bruto))) is not { } doc) continue; // malformado: ignora

            using (doc)
            {
                foreach (var no in Nos(doc.RootElement))
                    if (EhJobPosting(no) && Converter(no, urlPagina) is { } vaga)
                        vagas.Add(vaga);
            }
        }
        return vagas;
    }

    private static JsonDocument? Ler(string json)
    {
        try { return JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
        catch (JsonException) { return null; }
    }

    // O JSON-LD pode vir como objeto, lista ou { "@graph": [...] }.
    private static IEnumerable<JsonElement> Nos(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in e.EnumerateArray())
                foreach (var n in Nos(item)) yield return n;
        }
        else if (e.ValueKind == JsonValueKind.Object)
        {
            yield return e;
            if (e.TryGetProperty("@graph", out var grafo))
                foreach (var n in Nos(grafo)) yield return n;
        }
    }

    private static bool EhJobPosting(JsonElement e) =>
        e.TryGetProperty("@type", out var tipo) &&
        (tipo.ValueKind == JsonValueKind.String ? Igual(tipo.GetString())
            : tipo.ValueKind == JsonValueKind.Array && tipo.EnumerateArray().Any(t => Igual(t.GetString())));

    private static bool Igual(string? tipo) => tipo is not null &&
        (tipo.Equals("JobPosting", StringComparison.OrdinalIgnoreCase) || tipo.EndsWith("/JobPosting", StringComparison.OrdinalIgnoreCase));

    private static VagaColetada? Converter(JsonElement e, string urlPagina)
    {
        var titulo = Texto(e, "title");
        var empresa = Nome(e, "hiringOrganization");
        var descricao = Texto(e, "description");
        if (titulo is null || empresa is null || descricao is null) return null; // sem o mínimo, não é vaga utilizável

        var url = Texto(e, "url") is { } u && Uri.TryCreate(new Uri(urlPagina), u, out var abs) ? abs.ToString() : urlPagina;
        var remoto = Texto(e, "jobLocationType")?.Contains("TELECOMMUTE", StringComparison.OrdinalIgnoreCase) == true;

        return new VagaColetada(
            IdExterno: Identificador(e) ?? url,
            Empresa: empresa,
            Titulo: titulo,
            Descricao: descricao, // pode ser HTML; o mcp_vagas converte para texto
            Local: Local(e) ?? (remoto ? Requisito(e) : null),
            Remoto: remoto,
            Categoria: Texto(e, "occupationalCategory") ?? Texto(e, "industry"),
            TipoContrato: Texto(e, "employmentType"),
            Salario: Salario(e),
            Tags: Tags(e),
            Url: url,
            PublicadaEm: DateTimeOffset.TryParse(Texto(e, "datePosted"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null);
    }

    /// <summary>Texto de uma propriedade que pode ser string, número, lista de strings ou objeto com "name".</summary>
    private static string? Texto(JsonElement e, string prop)
    {
        if (!e.TryGetProperty(prop, out var v)) return null;
        var s = v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.Array => string.Join(", ", v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : NomeDe(x)).Where(x => !string.IsNullOrWhiteSpace(x))),
            JsonValueKind.Object => NomeDe(v),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private static string? Nome(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() : NomeDe(v))?.Trim() : null;

    private static string? NomeDe(JsonElement v) =>
        v.ValueKind == JsonValueKind.Object && v.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;

    private static string? Identificador(JsonElement e)
    {
        if (!e.TryGetProperty("identifier", out var id)) return null;
        return id.ValueKind switch
        {
            JsonValueKind.String => id.GetString(),
            JsonValueKind.Number => id.GetRawText(),
            JsonValueKind.Object when id.TryGetProperty("value", out var val) => val.ValueKind == JsonValueKind.String ? val.GetString() : val.GetRawText(),
            _ => null,
        };
    }

    private static string? Local(JsonElement e)
    {
        if (!e.TryGetProperty("jobLocation", out var loc)) return null;
        var partes = new List<string>();
        foreach (var lugar in loc.ValueKind == JsonValueKind.Array ? loc.EnumerateArray().ToList() : [loc])
        {
            if (lugar.ValueKind != JsonValueKind.Object || !lugar.TryGetProperty("address", out var end)) continue;
            if (end.ValueKind == JsonValueKind.String) { partes.Add(end.GetString()!); continue; }
            var texto = string.Join(", ", new[] { Texto(end, "addressLocality"), Texto(end, "addressRegion"), Texto(end, "addressCountry") }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            if (texto.Length > 0) partes.Add(texto);
        }
        return partes.Count > 0 ? string.Join(" | ", partes.Distinct()) : null;
    }

    private static string? Requisito(JsonElement e) => Texto(e, "applicantLocationRequirements");

    private static string? Salario(JsonElement e)
    {
        if (!e.TryGetProperty("baseSalary", out var s) || s.ValueKind != JsonValueKind.Object) return null;
        var moeda = Texto(s, "currency");
        if (!s.TryGetProperty("value", out var v)) return null;
        if (v.ValueKind is JsonValueKind.Number or JsonValueKind.String) return $"{moeda} {v}".Trim();
        if (v.ValueKind != JsonValueKind.Object) return null;
        var min = Texto(v, "minValue");
        var max = Texto(v, "maxValue");
        var valor = Texto(v, "value");
        var unidade = Texto(v, "unitText");
        var faixa = min is not null && max is not null ? $"{min}-{max}" : valor ?? min ?? max;
        return faixa is null ? null : $"{moeda} {faixa} {unidade}".Trim();
    }

    private static IReadOnlyList<string> Tags(JsonElement e)
    {
        var tags = new List<string>();
        foreach (var prop in new[] { "skills", "keywords" })
        {
            if (!e.TryGetProperty(prop, out var v)) continue;
            if (v.ValueKind == JsonValueKind.Array)
                tags.AddRange(v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!));
            else if (v.ValueKind == JsonValueKind.String)
                tags.AddRange(v.GetString()!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        return tags.Where(t => t.Length is > 0 and <= 50).Distinct(StringComparer.OrdinalIgnoreCase).Take(30).ToList();
    }

    [GeneratedRegex(@"<script[^>]*type\s*=\s*[""']application/ld\+json[""'][^>]*>(?<json>.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BlocoJsonLd();
}
