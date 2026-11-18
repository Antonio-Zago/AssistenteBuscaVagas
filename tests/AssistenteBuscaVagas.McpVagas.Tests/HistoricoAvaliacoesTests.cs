using AssistenteBuscaVagas.McpVagas.Armazenamento;

namespace AssistenteBuscaVagas.McpVagas.Tests;

public class HistoricoAvaliacoesTests
{
    private static readonly DateTimeOffset Dia16 = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static (int, DateTimeOffset, VagaAvaliada) Linha(int vagaId, int nota, DateTimeOffset em) =>
        (vagaId, em, new VagaAvaliada(vagaId, $"Vaga {vagaId}", "E", null, true, "https://e.com", "Remotive", null,
            nota, "motivo", [], [], [], em, "claude-opus-5-5", null));

    [Fact]
    public void UmaLinhaPorVagaComAAvaliacaoMaisRecente()
    {
        // A vaga 1 foi avaliada duas vezes (perfil antigo: 80; perfil novo: 40). Vale a mais recente.
        var r = ArmazenamentoAvaliacoes.MaisRecentePorVaga(
        [
            Linha(1, 80, Dia16),
            Linha(1, 40, Dia16.AddDays(1)),
            Linha(2, 55, Dia16),
        ]);

        Assert.Equal([2, 1], r.Select(v => v.VagaId));
        Assert.Equal(40, r.Single(v => v.VagaId == 1).Nota);
    }

    [Fact]
    public void EmpateNaNotaMostraAAvaliacaoMaisNovaPrimeiro()
    {
        var r = ArmazenamentoAvaliacoes.MaisRecentePorVaga([Linha(1, 70, Dia16), Linha(2, 70, Dia16.AddHours(1))]);
        Assert.Equal([2, 1], r.Select(v => v.VagaId));
    }

    [Fact]
    public void Vazio() => Assert.Empty(ArmazenamentoAvaliacoes.MaisRecentePorVaga([]));
}
