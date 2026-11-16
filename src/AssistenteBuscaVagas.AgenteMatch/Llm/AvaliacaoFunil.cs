using AssistenteBuscaVagas.AgenteMatch.Funil;

namespace AssistenteBuscaVagas.AgenteMatch.Llm;

public sealed record Rotulo(int VagaId, bool Relevante, string? Observacao);

public sealed record ItemRanking(int Posicao, int VagaId, string Titulo, bool Relevante);

public sealed record DescarteRelevante(int VagaId, string Titulo, string Motivo);

public sealed record Metricas(
    [property: System.ComponentModel.Description("Relevantes no top 5 ÷ 5.")] double PrecisaoEm5,
    IReadOnlyList<ItemRanking> Top5);

public sealed record ResultadoAvaliacao(
    int VagasRotuladas,
    int Relevantes,
    [property: System.ComponentModel.Description("O melhor valor possível com estes rótulos: mín(relevantes, 5) ÷ 5.")] double PrecisaoEm5Maxima,
    Metricas SemLlm,
    Metricas? ComLlm,
    [property: System.ComponentModel.Description("Relevantes que chegaram ao top N do BM25 (a entrada do LLM) ÷ total de relevantes. O LLM não recupera o que o BM25 deixou para trás.")] double RecallBm25,
    IReadOnlyList<DescarteRelevante> RelevantesDescartadosPelosFiltros,
    CustoExecucao? Custo);

/// <summary>
/// Mede o funil contra o conjunto de avaliação (vagas rotuladas à mão). O funil roda só sobre as vagas
/// rotuladas, então toda vaga no top 5 tem rótulo e a precision@5 é exata.
/// </summary>
public static class AvaliacaoFunil
{
    public const int K = 5;

    public static double Precisao(IReadOnlyList<int> ranking, IReadOnlySet<int> relevantes, int k = K) =>
        Math.Round((double)ranking.Take(k).Count(relevantes.Contains) / k, 3);

    public static double Recall(IReadOnlyList<int> ranking, IReadOnlySet<int> relevantes) =>
        relevantes.Count == 0 ? 0 : Math.Round((double)ranking.Count(relevantes.Contains) / relevantes.Count, 3);

    public static async Task<ResultadoAvaliacao> ExecutarAsync(
        FunilMatch funil, Recomendador? recomendador, AvaliacaoOptions opcoes,
        PerfilCompleto perfil, IReadOnlyList<VagaParaMatch> vagas, IReadOnlyList<Rotulo> rotulos, CancellationToken ct)
    {
        var relevantes = rotulos.Where(r => r.Relevante).Select(r => r.VagaId).ToHashSet();
        var titulos = vagas.ToDictionary(v => v.Id, v => v.Titulo);

        // Sem LLM: o ranking do BM25.
        var bm25 = funil.Executar(perfil, vagas, opcoes.CandidatosLlm).Recomendacoes.Select(r => r.VagaId).ToList();

        // Relevantes que nem chegaram ao BM25: o filtro rígido errou (ou o rótulo está em conflito com as preferências).
        var filtros = funil.Filtros(perfil);
        var descartados = vagas
            .Where(v => relevantes.Contains(v.Id))
            .Select(v => (v, d: filtros.Avaliar(v)))
            .Where(x => x.d is not null)
            .Select(x => new DescarteRelevante(x.v.Id, x.v.Titulo, x.d!.Detalhe))
            .ToList();

        Metricas Medir(IReadOnlyList<int> ranking) => new(
            Precisao(ranking, relevantes),
            ranking.Take(K).Select((id, i) => new ItemRanking(i + 1, id, titulos[id], relevantes.Contains(id))).ToList());

        ResultadoMatch? comLlm = recomendador is null ? null : await recomendador.ExecutarAsync(perfil, vagas, ct);

        return new ResultadoAvaliacao(
            rotulos.Count,
            relevantes.Count,
            Math.Round((double)Math.Min(relevantes.Count, K) / K, 3),
            Medir(bm25),
            comLlm is null ? null : Medir(comLlm.OrdemFinal),
            Recall(bm25, relevantes),
            descartados,
            comLlm?.Custo);
    }
}
