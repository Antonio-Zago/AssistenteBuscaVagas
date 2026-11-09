using System.ComponentModel;
using AssistenteBuscaVagas.McpVagas.Dados;
using Microsoft.EntityFrameworkCore;

namespace AssistenteBuscaVagas.McpVagas.Armazenamento;

/// <summary>Uma vaga como chega do agente_coleta. Os nomes viram o JSON schema da ferramenta salvar_vagas.</summary>
public sealed record VagaEntrada(
    [property: Description("Título da vaga.")] string Titulo,
    [property: Description("Nome da empresa contratante.")] string Empresa,
    [property: Description("Link original da vaga (http/https). Sempre o da fonte, nunca um link intermediário.")] string Url,
    [property: Description("Descrição da vaga, em texto ou HTML. HTML é convertido para texto.")] string Descricao,
    [property: Description("Id da vaga na fonte. Se vazio, a URL é usada como id.")] string? IdExterno = null,
    [property: Description("Local exigido do candidato (ex.: \"Worldwide\", \"Brazil\", \"São Paulo, SP\").")] string? Local = null,
    [property: Description("Se a vaga é remota.")] bool Remoto = false,
    [property: Description("Categoria ou área (ex.: \"Software Development\").")] string? Categoria = null,
    [property: Description("Tipo de contrato (ex.: \"full_time\", \"contract\", \"CLT\", \"PJ\").")] string? TipoContrato = null,
    [property: Description("Faixa salarial como aparece na fonte.")] string? Salario = null,
    [property: Description("Skills ou palavras-chave da vaga.")] IReadOnlyList<string>? Tags = null,
    [property: Description("Data de publicação (ISO 8601).")] DateTimeOffset? PublicadaEm = null);

public sealed record VagaRejeitada(int Indice, string? Titulo, string Motivo);

public sealed record ResultadoSalvamento(
    string Fonte,
    int Recebidas,
    int Novas,
    int Atualizadas,
    IReadOnlyList<VagaRejeitada> Rejeitadas);

/// <summary>
/// Fronteira de dados do sistema: tudo que entra no banco passa por aqui.
/// Quem chama é um agente com LLM, então nada é aceito sem validação.
/// </summary>
public sealed class ArmazenamentoVagas(VagasDbContext db, TimeProvider relogio, ILogger<ArmazenamentoVagas> logger)
{
    public const int MaximoPorChamada = 100;
    private const int MaximoTags = 30;

    public async Task<ResultadoSalvamento> SalvarAsync(string nomeFonte, IReadOnlyList<VagaEntrada> vagas, CancellationToken cancellationToken)
    {
        var fonte = await db.Fontes.SingleOrDefaultAsync(f => f.Nome == nomeFonte, cancellationToken);
        if (fonte is null)
        {
            var nomes = await db.Fontes.Select(f => f.Nome).ToListAsync(cancellationToken);
            throw new ArgumentException($"Fonte \"{nomeFonte}\" não cadastrada. Fontes válidas: {string.Join(", ", nomes)}.");
        }
        if (!fonte.Ativa)
            throw new ArgumentException($"A fonte \"{nomeFonte}\" está desativada.");
        if (vagas.Count > MaximoPorChamada)
            throw new ArgumentException($"Envie no máximo {MaximoPorChamada} vagas por chamada (recebidas: {vagas.Count}).");

        var agora = relogio.GetUtcNow();
        var rejeitadas = new List<VagaRejeitada>();
        var validas = new Dictionary<string, VagaEntrada>();

        for (var i = 0; i < vagas.Count; i++)
        {
            var v = vagas[i];
            var motivo = Validar(v);
            if (motivo is not null)
            {
                rejeitadas.Add(new(i, v.Titulo, motivo));
                continue;
            }

            var id = string.IsNullOrWhiteSpace(v.IdExterno) ? v.Url.Trim() : v.IdExterno.Trim();
            if (!validas.TryAdd(id, v))
                rejeitadas.Add(new(i, v.Titulo, $"Id externo repetido na mesma chamada: {id}."));
        }

        var ids = validas.Keys.ToList();
        var existentes = await db.Vagas
            .Where(x => x.FonteId == fonte.Id && ids.Contains(x.IdExterno))
            .ToDictionaryAsync(x => x.IdExterno, cancellationToken);

        int novas = 0, atualizadas = 0;
        foreach (var (id, v) in validas)
        {
            if (!existentes.TryGetValue(id, out var vaga))
            {
                vaga = new Vaga
                {
                    FonteId = fonte.Id,
                    IdExterno = id,
                    Empresa = "",
                    Titulo = "",
                    Descricao = "",
                    DescricaoOriginal = "",
                    Url = "",
                    HashDedup = "",
                    ColetadaEm = agora,
                };
                db.Vagas.Add(vaga);
                novas++;
            }
            else
            {
                atualizadas++;
            }

            Preencher(vaga, v, agora);
        }

        fonte.UltimaColetaEm = agora;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("salvar_vagas {Fonte}: {Recebidas} recebidas, {Novas} novas, {Atualizadas} atualizadas, {Rejeitadas} rejeitadas",
            fonte.Nome, vagas.Count, novas, atualizadas, rejeitadas.Count);

        return new(fonte.Nome, vagas.Count, novas, atualizadas, rejeitadas);
    }

    private static string? Validar(VagaEntrada v)
    {
        if (string.IsNullOrWhiteSpace(v.Titulo)) return "Título vazio.";
        if (string.IsNullOrWhiteSpace(v.Empresa)) return "Empresa vazia.";
        if (string.IsNullOrWhiteSpace(v.Descricao)) return "Descrição vazia.";
        if (string.IsNullOrWhiteSpace(v.Url)) return "URL vazia.";
        if (v.Titulo.Length > 500) return "Título com mais de 500 caracteres.";
        if (v.Empresa.Length > 300) return "Empresa com mais de 300 caracteres.";
        if (v.Url.Length > 1000) return "URL com mais de 1000 caracteres.";
        if (v.IdExterno?.Length > 200) return "Id externo com mais de 200 caracteres.";
        if (!Uri.TryCreate(v.Url?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return "URL inválida: precisa ser absoluta, http ou https.";
        return null;
    }

    private static void Preencher(Vaga vaga, VagaEntrada v, DateTimeOffset agora)
    {
        vaga.Titulo = v.Titulo.Trim();
        vaga.Empresa = v.Empresa.Trim();
        vaga.Url = v.Url.Trim();
        vaga.DescricaoOriginal = v.Descricao;
        vaga.Descricao = Texto.HtmlParaTexto(v.Descricao);
        vaga.Local = Vazio(v.Local);
        vaga.Remoto = v.Remoto;
        vaga.Categoria = Vazio(v.Categoria);
        vaga.TipoContrato = Vazio(v.TipoContrato);
        vaga.Salario = Vazio(v.Salario);
        vaga.Tags = (v.Tags ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLowerInvariant())
            .Distinct()
            .Take(MaximoTags)
            .ToList();
        // Data de publicação no futuro é erro da fonte (ou do agente): melhor não ter data do que ter uma errada.
        vaga.PublicadaEm = v.PublicadaEm is { } p && p <= agora.AddDays(1) ? p.ToUniversalTime() : null;
        vaga.HashDedup = Texto.HashDedup(vaga.Empresa, vaga.Titulo);
    }

    private static string? Vazio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
