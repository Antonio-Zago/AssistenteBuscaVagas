using AssistenteBuscaVagas.AgenteMatch.Funil;

namespace AssistenteBuscaVagas.AgenteMatch.Tests;

public class TokenizadorTests
{
    [Fact]
    public void MantemNomesDeTecnologiaInteiros()
    {
        var termos = Tokenizador.Termos("Experience with C#, .NET, ASP.NET Core, Node.js and C++.");
        Assert.Equal(["experience", "c#", ".net", "asp.net", "core", "node.js", "c++"], termos);
    }

    [Fact]
    public void NormalizaAcentoESinonimos()
    {
        var termos = Tokenizador.Termos("Desenvolvedor dotnet com Postgres e K8s");
        Assert.Equal(["developer", ".net", "postgresql", "kubernetes"], termos);
    }

    [Fact]
    public void JuntaCompostos()
    {
        Assert.Equal(["sql-server", "backend"], Tokenizador.Termos("SQL Server back-end"));
    }

    [Fact]
    public void BarraEntreTecnologias()
    {
        Assert.Equal(["c#", ".net"], Tokenizador.Termos("C#/.NET"));
    }

    [Fact]
    public void DescartaLetrasSoltasMenosLinguagens()
    {
        Assert.Equal(["c", "r", "python"], Tokenizador.Termos("C, R e x Python"));
    }

    [Fact]
    public void StopwordsSaoOpcionais()
    {
        Assert.Empty(Tokenizador.Termos("the and of"));
        Assert.Equal(["the", "and"], Tokenizador.Termos("the and", removerStopwords: false));
    }
}
