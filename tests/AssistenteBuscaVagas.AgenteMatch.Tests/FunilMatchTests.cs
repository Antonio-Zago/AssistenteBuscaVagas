using AssistenteBuscaVagas.AgenteMatch.Funil;

namespace AssistenteBuscaVagas.AgenteMatch.Tests;

public class FunilMatchTests
{
    private static int _id;

    private static VagaParaMatch Vaga(string titulo, string descricao, bool remoto = true, string? local = "Worldwide", params string[] tags) =>
        new(++_id, titulo, "Empresa", local, remoto, null, null, tags, DateTimeOffset.UtcNow, "Remotive", $"https://example.com/{_id}", descricao);

    private static PerfilCompleto Perfil(
        string senioridade = "Sênior",
        IReadOnlyList<string>? modalidades = null,
        IReadOnlyList<string>? cidades = null) =>
        new(
            new Curriculo("Desenvolvedor .NET", senioridade,
            [
                new Skill("C#", "Linguagem", 8),
                new Skill(".NET", "Framework", 8),
                new Skill("Azure", "Nuvem", 3),
                new Skill("SQL Server", "BancoDeDados", null),
            ]),
            new Preferencias(["Desenvolvedor Backend"], modalidades ?? ["Remoto"], cidades ?? [], null));

    private static FunilMatch Funil() => new(new FunilOptions());

    [Fact]
    public void RanqueiaPelaAderenciaAsSkills()
    {
        var dotnet = Vaga("Senior Backend Developer", "We use C#, .NET 10 and Azure. SQL Server is a plus.");
        var java = Vaga("Senior Java Developer", "Java, Spring Boot, AWS.");
        var nada = Vaga("Senior Designer", "Figma and user research.");

        var r = Funil().Executar(Perfil(), [java, nada, dotnet]);

        Assert.Equal(dotnet.Id, r.Recomendacoes[0].VagaId);
        Assert.Equal(100, r.Recomendacoes[0].NotaRelativa);
        Assert.DoesNotContain(r.Recomendacoes, x => x.VagaId == nada.Id);
        Assert.Contains("C#", r.Recomendacoes[0].SkillsEncontradas);
        Assert.Contains("SQL Server", r.Recomendacoes[0].SkillsEncontradas);
        Assert.Contains("backend", r.Recomendacoes[0].TermosDoCargoNoTitulo);
    }

    [Fact]
    public void DescartaSenioridadeDistante()
    {
        var estagio = Vaga("Software Engineering Intern", "C# and .NET");
        var gerente = Vaga("Engineering Manager", "C# and .NET");
        var lead = Vaga("Tech Lead .NET", "C# and .NET");
        var semNivel = Vaga("Backend Developer", "C# and .NET");

        var r = Funil().Executar(Perfil("Sênior"), [estagio, gerente, lead, semNivel]);

        Assert.Equal([lead.Id, semNivel.Id], r.Recomendacoes.Select(x => x.VagaId).Order());
        Assert.Equal(2, r.Etapas[1].Descartes!["senioridade"]);
    }

    [Fact]
    public void DescartaRemotaRestritaAOutraRegiao()
    {
        var eua = Vaga("Backend Developer", "C#", local: "USA only");
        var latam = Vaga("Backend Developer", "C#", local: "LATAM");
        var semLocal = Vaga("Backend Developer", "C#", local: null);

        var r = Funil().Executar(Perfil(), [eua, latam, semLocal]);

        Assert.DoesNotContain(r.Recomendacoes, x => x.VagaId == eua.Id);
        Assert.Equal(1, r.Etapas[1].Descartes!["local"]);
    }

    [Fact]
    public void PresencialPrecisaDaCidadeDesejada()
    {
        var sp = Vaga("Desenvolvedor C#", "C#", remoto: false, local: "São Paulo, SP");
        var rio = Vaga("Desenvolvedor C#", "C#", remoto: false, local: "Rio de Janeiro, RJ");
        var remota = Vaga("Desenvolvedor C#", "C#");

        var r = Funil().Executar(Perfil(modalidades: ["Presencial"], cidades: ["sao paulo"]), [sp, rio, remota]);

        Assert.Equal([sp.Id], r.Recomendacoes.Select(x => x.VagaId));
        Assert.Equal(1, r.Etapas[1].Descartes!["local"]);
        Assert.Equal(1, r.Etapas[1].Descartes!["modalidade"]);
    }

    [Fact]
    public void ApontaLacunasPelasTags()
    {
        var a = Vaga("Backend Developer", "C# .NET", tags: ["c#", "docker", "kubernetes", "senior"]);
        var b = Vaga("Backend Developer", "C# .NET Azure", tags: ["docker", "azure"]);

        var r = Funil().Executar(Perfil(), [a, b]);

        var lacunasA = r.Recomendacoes.Single(x => x.VagaId == a.Id).Lacunas;
        Assert.Equal(["docker", "kubernetes"], lacunasA);
        Assert.Equal(new LacunaFrequente("docker", 2, 100), r.LacunasFrequentes[0]);
        Assert.Contains("Falta no perfil: docker", r.Recomendacoes.Single(x => x.VagaId == b.Id).Justificativa);
    }

    [Fact]
    public void ConsultaIgnoraSenioridadeEDivideSkillsCompostas()
    {
        var perfil = new PerfilCompleto(
            new Curriculo("Desenvolvedor pleno", "Pleno", [new Skill("ASP.NET Core", "Framework", null), new Skill("C#", "Linguagem", null)]),
            null);

        var consulta = Funil().Executar(perfil, []).Consulta.ToDictionary(t => t.Termo, t => t.Pontos);

        Assert.DoesNotContain("pleno", consulta.Keys);
        Assert.Equal(1.5, consulta["developer"]);
        Assert.Equal(1, consulta["c#"]);
        Assert.Equal(0.5, consulta["core"]);
    }

    [Fact]
    public void LimitaQuantidade()
    {
        var vagas = Enumerable.Range(0, 10).Select(i => Vaga($"Backend Developer {i}", "C#")).ToList();
        var r = Funil().Executar(Perfil(), vagas, limite: 3);
        Assert.Equal(3, r.Recomendacoes.Count);
        Assert.Equal(10, r.Etapas[2].Vagas);
    }

    [Fact]
    public void PerfilSemSkillsNemCargosFalha()
    {
        var vazio = new PerfilCompleto(null, new Preferencias([], ["Remoto"], [], null));
        Assert.Throws<PerfilIncompletoException>(() => Funil().Executar(vazio, [Vaga("x", "y")]));
    }

    [Fact]
    public void SoRemotasQuandoPreferenciaEhSoRemoto()
    {
        Assert.True(Funil().Filtros(Perfil(modalidades: ["Remoto"])).SoRemotas);
        Assert.False(Funil().Filtros(Perfil(modalidades: ["Remoto", "Híbrido"])).SoRemotas);
    }
}
