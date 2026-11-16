using AssistenteBuscaVagas.AgentePerfil;
using AssistenteBuscaVagas.AgentePerfil.Perfil;

namespace AssistenteBuscaVagas.AgentePerfil.Tests;

public class PreferenciasParciaisTests
{
    private static readonly Preferencias Atual = new(
        [".NET"], ["Remoto"], ["São Paulo"], "Sênior", 15000m, "BRL", ["pt", "en"]);

    [Fact]
    public void SoOsCamposInformadosMudam()
    {
        var nova = new PreferenciasParciais(Modalidades: ["Remoto", "Híbrido"], Cidades: ["Campinas"]).AplicarSobre(Atual);

        Assert.Equal(["Remoto", "Híbrido"], nova.Modalidades);
        Assert.Equal(["Campinas"], nova.Cidades);
        Assert.Equal([".NET"], nova.Cargos);
        Assert.Equal("Sênior", nova.Senioridade);
        Assert.Equal(15000m, nova.SalarioMinimoMensal);
        Assert.Equal(["pt", "en"], nova.Idiomas);
    }

    [Fact]
    public void SemPreferenciasAnterioresUsaListasVazias()
    {
        var nova = new PreferenciasParciais(Modalidades: ["Remoto"]).AplicarSobre(null);
        Assert.Equal(["Remoto"], nova.Modalidades);
        Assert.Empty(nova.Cargos);
        Assert.Null(nova.Senioridade);
    }

    [Fact]
    public void VaziaQuandoNadaFoiInformado()
    {
        Assert.True(new PreferenciasParciais().Vazia);
        Assert.False(new PreferenciasParciais(Moeda: "USD").Vazia);
    }
}
