using AssistenteBuscaVagas.AgenteMatch.Funil;
using AssistenteBuscaVagas.AgenteMatch.Llm;

namespace AssistenteBuscaVagas.AgenteMatch.Tests;

public class AvaliacaoFunilTests
{
    [Fact]
    public void PrecisaoContaRelevantesNoTopK()
    {
        var relevantes = new HashSet<int> { 1, 3, 9 };
        Assert.Equal(0.4, AvaliacaoFunil.Precisao([1, 2, 3, 4, 5, 9], relevantes));
        // Menos de 5 no ranking: o denominador continua 5.
        Assert.Equal(0.2, AvaliacaoFunil.Precisao([1], relevantes));
        Assert.Equal(0, AvaliacaoFunil.Precisao([], relevantes));
    }

    [Fact]
    public void RecallMedeQuantosRelevantesChegaramAoTopN()
    {
        Assert.Equal(0.667, AvaliacaoFunil.Recall([1, 2, 3], new HashSet<int> { 1, 3, 9 }));
        Assert.Equal(0, AvaliacaoFunil.Recall([1], new HashSet<int>()));
    }

    [Fact]
    public async Task SemLlmMedeOBm25EApontaRelevantesDescartados()
    {
        var perfil = new PerfilCompleto(
            new Curriculo(null, "Sênior", [new Skill("C#", "Linguagem", null)]),
            new Preferencias([], ["Remoto"], [], null));
        VagaParaMatch Vaga(int id, string titulo, string desc, string local = "Worldwide") =>
            new(id, titulo, "E", local, true, null, null, [], null, "Remotive", $"https://e.com/{id}", desc);

        var vagas = new List<VagaParaMatch>
        {
            Vaga(1, "C# Developer", "C#"),
            Vaga(2, "C# Developer", "C# C#"),
            Vaga(3, "C# Developer", "C#", local: "USA only"),
        };
        var rotulos = new List<Rotulo> { new(1, true, null), new(2, false, null), new(3, true, null) };

        var r = await AvaliacaoFunil.ExecutarAsync(new FunilMatch(new FunilOptions()), null, new AvaliacaoOptions(), perfil, vagas, rotulos, default);

        Assert.Equal(3, r.VagasRotuladas);
        Assert.Equal(2, r.Relevantes);
        Assert.Equal(0.4, r.PrecisaoEm5Maxima);
        Assert.Equal(0.2, r.SemLlm.PrecisaoEm5);
        Assert.Null(r.ComLlm);
        Assert.Equal(0.5, r.RecallBm25);
        Assert.Equal(3, Assert.Single(r.RelevantesDescartadosPelosFiltros).VagaId);
    }
}
