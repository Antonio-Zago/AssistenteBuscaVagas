using AssistenteBuscaVagas.McpVagas.Dados;
using Microsoft.EntityFrameworkCore;

namespace AssistenteBuscaVagas.McpVagas.Armazenamento;

public sealed record VagaAvaliada(
    int VagaId, string Titulo, string Empresa, string? Local, bool Remoto, string Url, string Fonte, DateTimeOffset? PublicadaEm,
    int Nota, string Justificativa, IReadOnlyList<string> PontosFortes, IReadOnlyList<string> Lacunas, IReadOnlyList<string> Impeditivos,
    DateTimeOffset AvaliadaEm, string Modelo,
    // Se e quando a vaga entrou num resumo do worker.
    DateTimeOffset? RecomendadaEm);

public sealed record ResultadoAvaliacoes(int Total, bool SoPerfilAtual, IReadOnlyList<VagaAvaliada> Vagas);

/// <summary>
/// O histórico de avaliações do LLM (tabela matches): toda vaga que o Claude já avaliou, com nota e motivo.
/// É mais amplo que os resumos do worker, que só guardam as vagas com nota alta.
/// </summary>
public sealed class ArmazenamentoAvaliacoes(VagasDbContext db)
{
    public const int LimiteMaximo = 200;

    private sealed record Linha(int VagaId, string Chave, DateTimeOffset CriadoEm, VagaAvaliada Vaga);

    /// <param name="soPerfilAtual">
    /// true: só as avaliações da chave mais recente (perfil, modelo e prompt atuais). false: a avaliação mais recente
    /// de cada vaga, qualquer que seja a chave (inclui vagas avaliadas contra um perfil antigo).
    /// </param>
    public async Task<ResultadoAvaliacoes> ListarAsync(int notaMinima, bool soPerfilAtual, bool soRecomendadas, string? busca, int limite, CancellationToken ct)
    {
        var consulta = db.Matches.AsNoTracking().Where(m => m.Nota >= notaMinima);

        if (soPerfilAtual)
        {
            var chave = await db.Matches.AsNoTracking().OrderByDescending(m => m.CriadoEm).Select(m => m.ChaveAvaliacao).FirstOrDefaultAsync(ct);
            if (chave is null) return new(0, soPerfilAtual, []);
            consulta = consulta.Where(m => m.ChaveAvaliacao == chave);
        }

        if (soRecomendadas)
            consulta = consulta.Where(m => db.RecomendacoesEnviadas.Any(r => r.VagaId == m.VagaId));

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var padrao = $"%{busca.Trim()}%";
            consulta = consulta.Where(m => EF.Functions.ILike(m.Vaga.Titulo, padrao) || EF.Functions.ILike(m.Vaga.Empresa, padrao));
        }

        var linhas = await consulta
            .Select(m => new Linha(m.VagaId, m.ChaveAvaliacao, m.CriadoEm, new VagaAvaliada(
                m.VagaId, m.Vaga.Titulo, m.Vaga.Empresa, m.Vaga.Local, m.Vaga.Remoto, m.Vaga.Url, m.Vaga.Fonte.Nome, m.Vaga.PublicadaEm,
                m.Nota, m.Justificativa, m.PontosFortes, m.Lacunas, m.Impeditivos, m.CriadoEm, m.Modelo,
                db.RecomendacoesEnviadas.Where(r => r.VagaId == m.VagaId).Select(r => (DateTimeOffset?)r.RecomendadaEm).FirstOrDefault())))
            .ToListAsync(ct);

        var vagas = MaisRecentePorVaga(linhas.Select(l => (l.VagaId, l.CriadoEm, l.Vaga)));
        return new(vagas.Count, soPerfilAtual, vagas.Take(Math.Clamp(limite, 1, LimiteMaximo)).ToList());
    }

    /// <summary>Uma linha por vaga (a avaliação mais recente), da maior nota para a menor; empate pela avaliação mais nova.</summary>
    public static IReadOnlyList<VagaAvaliada> MaisRecentePorVaga(IEnumerable<(int VagaId, DateTimeOffset AvaliadaEm, VagaAvaliada Vaga)> linhas) =>
        linhas
            .GroupBy(l => l.VagaId)
            .Select(g => g.OrderByDescending(l => l.AvaliadaEm).First().Vaga)
            .OrderByDescending(v => v.Nota)
            .ThenByDescending(v => v.AvaliadaEm)
            .ToList();
}
