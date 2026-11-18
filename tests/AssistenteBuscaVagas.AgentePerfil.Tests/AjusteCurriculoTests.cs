using AssistenteBuscaVagas.AgentePerfil.Perfil;

namespace AssistenteBuscaVagas.AgentePerfil.Tests;

public class AjusteCurriculoTests
{
    private static readonly Curriculo Atual = new(
        "Desenvolvedor .NET.", "Desenvolvedor pleno", "Pleno", 5,
        [
            new Skill("C#", "Linguagem", 4, "C# desde 2021"),
            new Skill("Delphi", "Linguagem", 4, "Delphi na empresa X"),
        ],
        [new Idioma("Inglês", "Intermediário")],
        [new Experiencia("Empresa", "Dev", "2021", null, ["fez X"])],
        [new Formacao("Fatec", "Banco de Dados", "2020")],
        ["AZ-900"]);

    [Fact]
    public void SoOQueVemPreenchidoMuda()
    {
        var (novo, avisos) = new AjusteCurriculo(Senioridade: "Sênior").AplicarSobre(Atual);

        Assert.Equal("Sênior", novo.Senioridade);
        Assert.Equal(Atual.Resumo, novo.Resumo);
        Assert.Equal(Atual.CargoAtual, novo.CargoAtual);
        Assert.Equal(Atual.Skills, novo.Skills);
        Assert.Equal(Atual.Experiencias, novo.Experiencias);
        Assert.Empty(avisos);
    }

    [Fact]
    public void AdicionaSkillNovaComEvidenciaManual()
    {
        var (novo, _) = new AjusteCurriculo(AdicionarSkills: [new("Docker", "Ferramenta", 1)]).AplicarSobre(Atual);

        var docker = Assert.Single(novo.Skills, s => s.Nome == "Docker");
        Assert.Equal("Ferramenta", docker.Categoria);
        Assert.Equal(1, docker.Anos);
        Assert.Equal(AjusteCurriculo.EvidenciaManual, docker.Evidencia);
        Assert.Equal(3, novo.Skills.Count);
    }

    [Fact]
    public void SkillExistenteEhAtualizadaSemDuplicar()
    {
        var (novo, _) = new AjusteCurriculo(AdicionarSkills: [new("c#", Anos: 5)]).AplicarSobre(Atual);

        var csharp = Assert.Single(novo.Skills, s => s.Nome == "C#");
        Assert.Equal(5, csharp.Anos);
        Assert.Equal("Linguagem", csharp.Categoria);      // não informada: mantém
        Assert.Equal("C# desde 2021", csharp.Evidencia);  // não informada: mantém
    }

    [Fact]
    public void SkillNovaSemCategoriaViraOutra()
    {
        var (novo, _) = new AjusteCurriculo(AdicionarSkills: [new("Scrum")]).AplicarSobre(Atual);
        Assert.Equal("Outra", novo.Skills.Single(s => s.Nome == "Scrum").Categoria);
    }

    [Fact]
    public void RemoveSkillEAvisaQuandoNaoExiste()
    {
        var (novo, avisos) = new AjusteCurriculo(RemoverSkills: ["delphi", "Cobol"]).AplicarSobre(Atual);

        Assert.DoesNotContain(novo.Skills, s => s.Nome == "Delphi");
        Assert.Equal("Skill \"Cobol\" não estava no currículo.", Assert.Single(avisos));
    }

    [Fact]
    public void RemoverEAdicionarAMesmaSkillSubstitui()
    {
        var (novo, _) = new AjusteCurriculo(RemoverSkills: ["Delphi"], AdicionarSkills: [new("Delphi", "Linguagem", 2)]).AplicarSobre(Atual);

        var delphi = Assert.Single(novo.Skills, s => s.Nome == "Delphi");
        Assert.Equal(2, delphi.Anos);
        Assert.Equal(AjusteCurriculo.EvidenciaManual, delphi.Evidencia);
    }

    [Fact]
    public void IdiomaEhAtualizadoPeloNome()
    {
        var (novo, _) = new AjusteCurriculo(AdicionarIdiomas: [new("inglês", "Avançado"), new("Espanhol", "Básico")]).AplicarSobre(Atual);

        Assert.Equal(2, novo.Idiomas.Count);
        Assert.Contains(new Idioma("inglês", "Avançado"), novo.Idiomas);
        Assert.Contains(new Idioma("Espanhol", "Básico"), novo.Idiomas);
    }

    [Fact]
    public void CertificacoesSemDuplicar()
    {
        var (novo, avisos) = new AjusteCurriculo(AdicionarCertificacoes: ["az-900", "AZ-204"], RemoverCertificacoes: ["PMP"]).AplicarSobre(Atual);

        Assert.Equal(["AZ-900", "AZ-204"], novo.Certificacoes);
        Assert.Single(avisos);
    }

    [Fact]
    public void CargoAtualVazioApagaOCargo()
    {
        Assert.Null(new AjusteCurriculo(CargoAtual: "").AplicarSobre(Atual).Curriculo.CargoAtual);
        Assert.Equal("Desenvolvedor pleno", new AjusteCurriculo(Senioridade: "Pleno").AplicarSobre(Atual).Curriculo.CargoAtual);
    }

    [Fact]
    public void Vazio()
    {
        Assert.True(new AjusteCurriculo().Vazio);
        Assert.True(new AjusteCurriculo(AdicionarSkills: []).Vazio);
        Assert.False(new AjusteCurriculo(RemoverSkills: ["x"]).Vazio);
        Assert.False(new AjusteCurriculo(CargoAtual: "").Vazio);
    }

    [Fact]
    public void NaoAlteraOOriginal()
    {
        new AjusteCurriculo(RemoverSkills: ["C#"], AdicionarSkills: [new("Go")]).AplicarSobre(Atual);
        Assert.Equal(2, Atual.Skills.Count);
    }
}
