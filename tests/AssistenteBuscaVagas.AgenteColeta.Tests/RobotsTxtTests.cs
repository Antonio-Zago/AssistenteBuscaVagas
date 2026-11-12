using AssistenteBuscaVagas.AgenteColeta.Web;

namespace AssistenteBuscaVagas.AgenteColeta.Tests;

public class RobotsTxtTests
{
    private const string Robots = """
        # comentário
        User-agent: *
        Disallow: /search
        Disallow: /*.pdf$
        Allow: /search/about

        User-agent: AssistenteBuscaVagas
        User-agent: OutroBot
        Disallow: /privado/
        """;

    [Theory]
    [InlineData("/careers/123", true)]
    [InlineData("/privado/x", false)]
    [InlineData("/search?q=dev", true)] // nosso grupo específico substitui o "*"
    public void Usa_grupo_do_nosso_user_agent(string caminho, bool esperado) =>
        Assert.Equal(esperado, RobotsTxt.Regras.Ler(Robots, RobotsTxt.UserAgent).Permite(caminho));

    [Theory]
    [InlineData("/careers/123", true)]
    [InlineData("/search?q=dev", false)]
    [InlineData("/search/about", true)] // regra mais longa vence
    [InlineData("/vaga.pdf", false)]
    [InlineData("/vaga.pdf?x=1", true)] // "$" ancora no fim
    public void Usa_grupo_curinga_para_outros(string caminho, bool esperado) =>
        Assert.Equal(esperado, RobotsTxt.Regras.Ler(Robots, "BotQualquer").Permite(caminho));

    [Fact]
    public void Disallow_raiz_bloqueia_tudo() =>
        Assert.False(RobotsTxt.Regras.Ler("User-agent: *\nDisallow: /", RobotsTxt.UserAgent).Permite("/careers"));
}
