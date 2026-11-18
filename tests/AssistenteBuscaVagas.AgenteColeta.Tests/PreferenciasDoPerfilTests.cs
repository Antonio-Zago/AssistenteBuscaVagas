using AssistenteBuscaVagas.AgenteColeta.Configuracao;

namespace AssistenteBuscaVagas.AgenteColeta.Tests;

public class PreferenciasDoPerfilTests
{
    private static readonly PreferenciasBusca Reserva = new()
    {
        Cargos = ["Backend Engineer"],
        Skills = ["C#", "Azure"],
        Senioridade = "Sênior",
        Locais = ["Remoto", "Brasil"],
        Idiomas = ["pt", "en"],
    };

    private static readonly PerfilColeta PerfilCompleto = new(
        new CurriculoColeta("Desenvolvedor pleno", "Pleno",
        [
            new("Delphi", 4), new(".NET", null), new("ASP.NET MVC", 3), new("C#", 4), new("SQL", 2),
        ]),
        new PreferenciasColeta(["Desenvolvedor .NET"], ["Remoto", "Híbrido"], ["São José dos Campos"], null, ["pt"]));

    [Fact]
    public void UsaOPerfilQuandoExiste()
    {
        var (p, fonte) = PreferenciasDoPerfil.Montar(PerfilCompleto, Reserva, "Brasil");

        Assert.Equal(["Desenvolvedor .NET"], p.Cargos);
        // Skills com mais anos primeiro; sem anos vai para o fim.
        Assert.Equal(["Delphi", "C#", "ASP.NET MVC", "SQL", ".NET"], p.Skills);
        // Sem senioridade nas preferências, vale a do currículo (não o "Sênior" do appsettings).
        Assert.Equal("Pleno", p.Senioridade);
        Assert.Equal(["Remoto", "São José dos Campos", "Brasil"], p.Locais);
        Assert.Equal(["pt"], p.Idiomas);
        Assert.True(fonte.TudoDoPerfil);
    }

    [Fact]
    public void SenioridadeDesejadaVenceADoCurriculo()
    {
        var perfil = PerfilCompleto with { Preferencias = PerfilCompleto.Preferencias! with { Senioridade = "Sênior" } };
        Assert.Equal("Sênior", PreferenciasDoPerfil.Montar(perfil, Reserva, "Brasil").Preferencias.Senioridade);
    }

    [Fact]
    public void SemPerfilUsaAReserva()
    {
        var (p, fonte) = PreferenciasDoPerfil.Montar(null, Reserva, "Brasil");

        Assert.Equal(Reserva.Cargos, p.Cargos);
        Assert.Equal(Reserva.Skills, p.Skills);
        Assert.Equal("Sênior", p.Senioridade);
        Assert.Equal("appsettings", fonte.Cargos);
        Assert.False(fonte.TudoDoPerfil);
    }

    [Fact]
    public void CompletaCampoACampo()
    {
        // Só currículo, sem preferências: cargos vêm do cargo atual; locais e idiomas, da reserva.
        var (p, fonte) = PreferenciasDoPerfil.Montar(PerfilCompleto with { Preferencias = null }, Reserva, "Brasil");

        Assert.Equal(["Desenvolvedor pleno"], p.Cargos);
        Assert.Equal("perfil", fonte.Cargos);
        Assert.Equal(Reserva.Locais, p.Locais);
        Assert.Equal("appsettings", fonte.Locais);
        Assert.Equal("appsettings", fonte.Idiomas);
    }

    [Fact]
    public void LimitaASkillsMaisUsadas()
    {
        var muitas = Enumerable.Range(1, 20).Select(i => new SkillColeta($"S{i}", i)).ToList();
        var (p, _) = PreferenciasDoPerfil.Montar(new PerfilColeta(new CurriculoColeta(null, "Pleno", muitas), null), Reserva, "Brasil");

        Assert.Equal(PreferenciasDoPerfil.MaximoSkills, p.Skills.Count);
        Assert.Equal("S20", p.Skills[0]);
    }

    [Fact]
    public void SoRemotoNaoIncluiCidades()
    {
        var perfil = PerfilCompleto with { Preferencias = PerfilCompleto.Preferencias! with { Modalidades = ["Remoto"] } };
        Assert.Equal(["Remoto", "Brasil"], PreferenciasDoPerfil.Montar(perfil, Reserva, "Brasil").Preferencias.Locais);
    }

    [Fact]
    public void ReservaNaoEhAlteradaPeloResultado()
    {
        var (p, _) = PreferenciasDoPerfil.Montar(null, Reserva, "Brasil");
        p.Cargos.Add("outro");
        Assert.Single(Reserva.Cargos);
    }
}
