using AssistenteBuscaVagas.McpVagas.Dados;
using Microsoft.EntityFrameworkCore;

namespace AssistenteBuscaVagas.McpVagas.Armazenamento;

public sealed record CandidaturaResumo(
    int VagaId, string Status, string? Notas, DateTimeOffset CriadaEm, DateTimeOffset AtualizadaEm,
    string Titulo, string Empresa, string? Local, bool Remoto, string Url, string Fonte,
    // Nota da avaliação mais recente do LLM para a vaga, se houver.
    int? Nota);

public sealed record LacunaContada(string Skill, int Vagas, int Percentual);

public sealed record EstatisticasSkills(
    int VagasAvaliadas, int NotaMinima, int VagasConsideradas, DateTimeOffset? AvaliadasAte, IReadOnlyList<LacunaContada> Lacunas);

/// <summary>Kanban de candidaturas e estatísticas de lacunas. O usuário move os cartões; o sistema só guarda.</summary>
public sealed class ArmazenamentoCandidaturas(VagasDbContext db, TimeProvider relogio)
{
    public static readonly string[] Etapas = ["salva", "aplicada", "entrevista", "oferta", "recusada"];
    private const int TamanhoMaximoNotas = 2000;
    private const int MaximoLacunas = 20;

    public async Task<CandidaturaResumo> AtualizarAsync(int vagaId, string status, string? notas, CancellationToken ct)
    {
        var etapa = Etapas.FirstOrDefault(e => string.Equals(e, status?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Etapa inválida: '{status}'. Use uma de: {string.Join(", ", Etapas)}.");
        if (notas?.Length > TamanhoMaximoNotas)
            throw new ArgumentException($"Notas com mais de {TamanhoMaximoNotas} caracteres.");
        if (!await db.Vagas.AnyAsync(v => v.Id == vagaId, ct))
            throw new ArgumentException($"Vaga {vagaId} não existe.");

        var agora = relogio.GetUtcNow();
        var c = await db.Candidaturas.FindAsync([vagaId], ct);
        if (c is null)
        {
            c = new Candidatura { VagaId = vagaId, Status = etapa, CriadaEm = agora };
            db.Candidaturas.Add(c);
        }
        c.Status = etapa;
        // notas nulas mantêm as anteriores; texto vazio apaga.
        if (notas is not null) c.Notas = string.IsNullOrWhiteSpace(notas) ? null : notas.Trim();
        c.AtualizadaEm = agora;
        await db.SaveChangesAsync(ct);

        return (await ListarAsync(vagaId, ct)).Single();
    }

    public async Task<bool> RemoverAsync(int vagaId, CancellationToken ct) =>
        await db.Candidaturas.Where(c => c.VagaId == vagaId).ExecuteDeleteAsync(ct) > 0;

    public async Task<IReadOnlyList<CandidaturaResumo>> ListarAsync(int? vagaId, CancellationToken ct)
    {
        var consulta = db.Candidaturas.AsNoTracking();
        if (vagaId is { } id) consulta = consulta.Where(c => c.VagaId == id);

        return await consulta
            .OrderByDescending(c => c.AtualizadaEm)
            .Select(c => new CandidaturaResumo(
                c.VagaId, c.Status, c.Notas, c.CriadaEm, c.AtualizadaEm,
                c.Vaga.Titulo, c.Vaga.Empresa, c.Vaga.Local, c.Vaga.Remoto, c.Vaga.Url, c.Vaga.Fonte.Nome,
                db.Matches.Where(m => m.VagaId == c.VagaId).OrderByDescending(m => m.CriadoEm).Select(m => (int?)m.Nota).FirstOrDefault()))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Lacunas mais pedidas nas vagas avaliadas pelo LLM. Usa só a chave de avaliação mais recente (perfil, modelo
    /// e prompt atuais): avaliações feitas contra um perfil antigo apontariam lacunas que talvez já não existam.
    /// </summary>
    public async Task<EstatisticasSkills> EstatisticasAsync(int notaMinima, CancellationToken ct)
    {
        var ultima = await db.Matches.AsNoTracking().OrderByDescending(m => m.CriadoEm)
            .Select(m => new { m.ChaveAvaliacao, m.CriadoEm }).FirstOrDefaultAsync(ct);
        if (ultima is null)
            return new(0, notaMinima, 0, null, []);

        var avaliadas = await db.Matches.AsNoTracking()
            .Where(m => m.ChaveAvaliacao == ultima.ChaveAvaliacao)
            .Select(m => new { m.Nota, m.Lacunas })
            .ToListAsync(ct);

        var consideradas = avaliadas.Where(a => a.Nota >= notaMinima).Select(a => (IReadOnlyList<string>)a.Lacunas).ToList();
        return new(avaliadas.Count, notaMinima, consideradas.Count, ultima.CriadoEm, ContarLacunas(consideradas, MaximoLacunas));
    }

    /// <summary>Em quantas vagas cada lacuna aparece (uma vez por vaga, sem diferenciar maiúsculas).</summary>
    public static IReadOnlyList<LacunaContada> ContarLacunas(IReadOnlyList<IReadOnlyList<string>> lacunasPorVaga, int maximo)
    {
        if (lacunasPorVaga.Count == 0) return [];
        return lacunasPorVaga
            .SelectMany(l => l.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(g => new LacunaContada(g.First(), g.Count(), (int)Math.Round(100.0 * g.Count() / lacunasPorVaga.Count)))
            .OrderByDescending(x => x.Vagas).ThenBy(x => x.Skill, StringComparer.OrdinalIgnoreCase)
            .Take(maximo)
            .ToList();
    }
}
