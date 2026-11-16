namespace AssistenteBuscaVagas.Comum.Busca;

/// <summary>
/// BM25 escrito à mão, em memória. O plano previa Lucene.NET, mas para algumas centenas de vagas um índice
/// em memória basta, e escrever a fórmula deixa explícito o que o Lucene faria por baixo. Mais importante:
/// dá para devolver a contribuição de cada termo, que é a "justificativa" de um funil sem LLM.
///
/// Os documentos chegam com frequências já ponderadas por campo (título vale mais que descrição):
/// é uma aproximação simples do BM25F, com uma única normalização de comprimento.
///
///   pontos(d, q) = Σ  peso(t) · idf(t) · tf(t,d)·(k1+1) / (tf(t,d) + k1·(1 − b + b·|d|/média))
///   idf(t)       = ln(1 + (N − df(t) + 0,5) / (df(t) + 0,5))
/// </summary>
public sealed class IndiceBm25
{
    private readonly double _k1;
    private readonly double _b;
    private readonly IReadOnlyList<IReadOnlyDictionary<string, double>> _docs;
    private readonly double[] _comprimentos;
    private readonly double _comprimentoMedio;
    private readonly Dictionary<string, int> _df = [];

    public IndiceBm25(IReadOnlyList<IReadOnlyDictionary<string, double>> documentos, double k1 = 1.2, double b = 0.75)
    {
        _k1 = k1;
        _b = b;
        _docs = documentos;
        _comprimentos = documentos.Select(d => d.Values.Sum()).ToArray();
        _comprimentoMedio = _comprimentos.Length == 0 ? 0 : _comprimentos.Average();

        foreach (var doc in documentos)
            foreach (var termo in doc.Keys)
                _df[termo] = _df.GetValueOrDefault(termo) + 1;
    }

    public int Total => _docs.Count;

    public double Idf(string termo)
    {
        var df = _df.GetValueOrDefault(termo);
        return Math.Log(1 + (Total - df + 0.5) / (df + 0.5));
    }

    /// <summary>Pontos de cada termo da consulta presente no documento, do maior para o menor.</summary>
    public IReadOnlyList<TermoPontuado> Contribuicoes(int doc, IReadOnlyDictionary<string, double> consulta)
    {
        var d = _docs[doc];
        var norma = _comprimentoMedio == 0 ? 1 : 1 - _b + _b * _comprimentos[doc] / _comprimentoMedio;
        var resultado = new List<TermoPontuado>();

        foreach (var (termo, peso) in consulta)
        {
            if (!d.TryGetValue(termo, out var tf) || tf <= 0) continue;
            var pontos = peso * Idf(termo) * tf * (_k1 + 1) / (tf + _k1 * norma);
            resultado.Add(new(termo, pontos));
        }

        return resultado.OrderByDescending(t => t.Pontos).ToList();
    }

    public double Pontuar(int doc, IReadOnlyDictionary<string, double> consulta) =>
        Contribuicoes(doc, consulta).Sum(t => t.Pontos);
}

/// <summary>Quanto um termo da consulta somou na pontuação de um documento.</summary>
public sealed record TermoPontuado(string Termo, double Pontos);
