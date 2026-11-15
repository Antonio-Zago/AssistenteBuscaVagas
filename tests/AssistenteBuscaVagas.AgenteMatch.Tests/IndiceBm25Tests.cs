using AssistenteBuscaVagas.AgenteMatch.Funil;

namespace AssistenteBuscaVagas.AgenteMatch.Tests;

public class IndiceBm25Tests
{
    private static Dictionary<string, double> Doc(params string[] termos) =>
        termos.GroupBy(t => t).ToDictionary(g => g.Key, g => (double)g.Count());

    [Fact]
    public void TermoRaroValeMaisQueTermoComum()
    {
        var indice = new IndiceBm25([Doc("c#", "developer"), Doc("java", "developer"), Doc("python", "developer")]);
        Assert.True(indice.Idf("c#") > indice.Idf("developer"));
    }

    [Fact]
    public void DocumentoComOTermoGanhaDoQueNaoTem()
    {
        var indice = new IndiceBm25([Doc("java", "spring"), Doc("c#", ".net"), Doc("python")]);
        var consulta = new Dictionary<string, double> { ["c#"] = 1, [".net"] = 1 };

        Assert.Equal(0, indice.Pontuar(0, consulta));
        Assert.True(indice.Pontuar(1, consulta) > 0);
    }

    [Fact]
    public void FrequenciaSatura()
    {
        // Repetir o termo ajuda, mas cada repetição vale menos (o k1 limita o ganho).
        var indice = new IndiceBm25([Doc("c#"), Doc("c#", "c#"), Doc("c#", "c#", "c#", "c#"), Doc("java")]);
        var consulta = new Dictionary<string, double> { ["c#"] = 1 };

        var umaVez = indice.Pontuar(0, consulta);
        var duas = indice.Pontuar(1, consulta);
        var quatro = indice.Pontuar(2, consulta);
        Assert.True(umaVez < duas && duas < quatro);
        Assert.True(duas / umaVez < 2);
        Assert.True(quatro / duas < 2);
    }

    [Fact]
    public void ContribuicoesSomamOTotalEVemOrdenadas()
    {
        var indice = new IndiceBm25([Doc("c#", "azure", "azure"), Doc("java"), Doc("go")]);
        var consulta = new Dictionary<string, double> { ["c#"] = 1, ["azure"] = 2, ["kubernetes"] = 1 };

        var contribuicoes = indice.Contribuicoes(0, consulta);

        Assert.Equal(["azure", "c#"], contribuicoes.Select(c => c.Termo));
        Assert.Equal(indice.Pontuar(0, consulta), contribuicoes.Sum(c => c.Pontos), 9);
    }

    [Fact]
    public void IndiceVazioNaoQuebra()
    {
        var indice = new IndiceBm25([]);
        Assert.Equal(0, indice.Total);
    }
}
