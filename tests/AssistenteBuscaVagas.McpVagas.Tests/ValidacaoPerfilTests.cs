using AssistenteBuscaVagas.McpVagas.Armazenamento;

namespace AssistenteBuscaVagas.McpVagas.Tests;

public class ValidacaoPerfilTests
{
    private static Curriculo CurriculoValido(
        string senioridade = "Sênior",
        IReadOnlyList<Skill>? skills = null,
        IReadOnlyList<Experiencia>? experiencias = null) => new(
        Resumo: "  Desenvolvedor backend com foco em .NET.  ",
        CargoAtual: " ",
        Senioridade: senioridade,
        AnosExperiencia: 8,
        Skills: skills ?? [new Skill("C#", "Linguagem", 8, "C# em todos os projetos")],
        Idiomas: [new Idioma("Inglês", "avançado")],
        Experiencias: experiencias ?? [new Experiencia("Empresa X", "Dev Sênior", "2020-03", null, ["Migrou sistema para Azure", " "])],
        Formacao: [new Formacao("USP", "Ciência da Computação", "2015")],
        Certificacoes: ["AZ-204", "az-204", " "]);

    [Fact]
    public void Normaliza_curriculo_valido()
    {
        var c = ValidacaoPerfil.Validar(CurriculoValido(senioridade: "sênior"));

        Assert.Equal("Desenvolvedor backend com foco em .NET.", c.Resumo);
        Assert.Null(c.CargoAtual);
        Assert.Equal("Sênior", c.Senioridade);
        Assert.Equal("Avançado", c.Idiomas[0].Nivel);
        Assert.Equal(["Migrou sistema para Azure"], c.Experiencias[0].Destaques);
        Assert.Equal(["AZ-204"], c.Certificacoes);
    }

    [Fact]
    public void Skill_repetida_fica_com_mais_anos()
    {
        var c = ValidacaoPerfil.Validar(CurriculoValido(skills:
        [
            new Skill("Azure", "Nuvem", 2, "a"),
            new Skill("azure", "Nuvem", 5, "b"),
            new Skill(" ", "Outra", null, "ignorada"),
        ]));

        var azure = Assert.Single(c.Skills);
        Assert.Equal(5, azure.Anos);
    }

    [Fact]
    public void Rejeita_curriculo_sem_skills() =>
        Assert.Throws<ArgumentException>(() => ValidacaoPerfil.Validar(CurriculoValido(skills: [])));

    [Theory]
    [InlineData("Senior Staff")]
    [InlineData("")]
    public void Rejeita_senioridade_fora_da_lista(string senioridade)
    {
        var ex = Assert.Throws<ArgumentException>(() => ValidacaoPerfil.Validar(CurriculoValido(senioridade: senioridade)));
        Assert.Contains("Use um de", ex.Message);
    }

    [Theory]
    [InlineData("03/2020")]
    [InlineData("2020-13")]
    [InlineData("atual")]
    public void Rejeita_data_fora_do_formato(string inicio) =>
        Assert.Throws<ArgumentException>(() => ValidacaoPerfil.Validar(
            CurriculoValido(experiencias: [new Experiencia("X", "Dev", inicio, null, [])])));

    [Fact]
    public void Rejeita_anos_absurdos() =>
        Assert.Throws<ArgumentException>(() => ValidacaoPerfil.Validar(
            CurriculoValido(skills: [new Skill("C#", "Linguagem", 99, "")])));

    [Fact]
    public void Normaliza_preferencias()
    {
        var p = ValidacaoPerfil.Validar(new Preferencias(
            Cargos: ["Dev .NET", "dev .net"],
            Modalidades: ["remoto", "Híbrido", "Remoto"],
            Cidades: ["São Paulo"],
            Senioridade: null,
            SalarioMinimoMensal: 15000,
            Moeda: "brl",
            Idiomas: ["PT", "en"]));

        Assert.Equal(["Dev .NET"], p.Cargos);
        Assert.Equal(["Remoto", "Híbrido"], p.Modalidades);
        Assert.Equal("BRL", p.Moeda);
        Assert.Equal(["pt", "en"], p.Idiomas);
    }

    [Fact]
    public void Rejeita_preferencias_sem_modalidade() =>
        Assert.Throws<ArgumentException>(() => ValidacaoPerfil.Validar(new Preferencias([], [], [], null, null, null, [])));

    [Fact]
    public void Rejeita_salario_sem_moeda() =>
        Assert.Throws<ArgumentException>(() => ValidacaoPerfil.Validar(new Preferencias([], ["Remoto"], [], null, 10000, null, [])));
}
