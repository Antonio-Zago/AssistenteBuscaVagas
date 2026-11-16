using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AssistenteBuscaVagas.AgenteMatch.Funil;

namespace AssistenteBuscaVagas.AgenteMatch.Llm;

public sealed record RecomendacaoFinal(
    int Posicao,
    int VagaId,
    string Titulo,
    string Empresa,
    string? Local,
    bool Remoto,
    string Fonte,
    string Url,
    int Nota,
    string Justificativa,
    IReadOnlyList<string> PontosFortes,
    IReadOnlyList<string> Lacunas,
    IReadOnlyList<string> Impeditivos,
    int PosicaoBm25,
    bool DoCache);

public sealed record CustoExecucao(int ChamadasLlm, int ReaproveitadasDoCache, int Falhas, UsoTokens Tokens, decimal CustoEstimadoUsd);

public sealed record ResultadoMatch(
    IReadOnlyList<EtapaFunil> Etapas,
    IReadOnlyList<RecomendacaoFinal> Recomendacoes,
    IReadOnlyList<LacunaFrequente> LacunasFrequentes,
    CustoExecucao Custo,
    // Todos os candidatos avaliados, na ordem final (inclui os que não entraram no top). Usado pela avaliação.
    IReadOnlyList<int> OrdemFinal);

/// <summary>
/// O funil completo da fase 4: filtros rígidos → BM25 (top <see cref="AvaliacaoOptions.CandidatosLlm"/>) → LLM → top
/// <see cref="AvaliacaoOptions.RecomendacoesFinais"/>. O LLM só é chamado para vagas sem avaliação guardada
/// com a mesma chave (perfil + modelo + prompt + esforço).
/// </summary>
public sealed class Recomendador(FunilMatch funil, IAvaliadorVaga avaliador, IRepositorioMatches repositorio, AvaliacaoOptions opcoes, ILogger log)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string ChaveAvaliacao(PerfilParaLlm perfil, string assinaturaAvaliador)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(perfil, Json) + "|" + assinaturaAvaliador);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public async Task<ResultadoMatch> ExecutarAsync(PerfilCompleto perfil, IReadOnlyList<VagaParaMatch> vagas, CancellationToken ct)
    {
        // Etapas 1 e 2 (sem LLM).
        var resultadoFunil = funil.Executar(perfil, vagas, opcoes.CandidatosLlm);
        var porId = vagas.ToDictionary(v => v.Id);
        var candidatos = resultadoFunil.Recomendacoes;

        // Etapa 3: LLM, com cache.
        var perfilLlm = PerfilParaLlm.De(perfil, opcoes.LocalizacaoCandidato);
        var chave = ChaveAvaliacao(perfilLlm, avaliador.Assinatura);
        var ids = candidatos.Select(c => c.VagaId).ToList();

        var guardadas = ids.Count == 0 ? [] : await repositorio.ObterAsync(chave, ids, ct);
        var avaliacoes = guardadas.ToDictionary(a => a.VagaId);
        var doCache = avaliacoes.Keys.ToHashSet();

        var faltando = candidatos.Where(c => !avaliacoes.ContainsKey(c.VagaId)).Select(c => porId[c.VagaId]).ToList();
        var (novas, uso, falhas) = await AvaliarAsync(perfilLlm, faltando, ct);
        if (novas.Count > 0)
            await repositorio.SalvarAsync(chave, avaliador.Modelo, avaliador.VersaoPrompt, novas, ct);
        foreach (var a in novas) avaliacoes[a.VagaId] = a;

        // Ranking final: nota do LLM; empate decidido pela posição no BM25. Vaga que falhou no LLM não é recomendada.
        var ordenadas = candidatos
            .Where(c => avaliacoes.ContainsKey(c.VagaId))
            .OrderByDescending(c => avaliacoes[c.VagaId].Nota)
            .ThenBy(c => c.Posicao)
            .ToList();

        var recomendacoes = ordenadas
            .Take(opcoes.RecomendacoesFinais)
            .Select((c, i) =>
            {
                var a = avaliacoes[c.VagaId];
                return new RecomendacaoFinal(i + 1, c.VagaId, c.Titulo, c.Empresa, c.Local, c.Remoto, c.Fonte, c.Url,
                    a.Nota, a.Justificativa, a.PontosFortes, a.Lacunas, a.Impeditivos, c.Posicao, doCache.Contains(c.VagaId));
            })
            .ToList();

        var etapas = resultadoFunil.Etapas.Where(e => e.Etapa != "recomendadas").ToList();
        etapas.Add(new($"top do BM25 enviado ao LLM", candidatos.Count));
        etapas.Add(new("avaliadas pelo LLM", avaliacoes.Count, new SortedDictionary<string, int>
        {
            ["do cache"] = doCache.Count,
            ["novas"] = novas.Count,
            ["falhas"] = falhas,
        }));
        etapas.Add(new("recomendadas", recomendacoes.Count));

        return new ResultadoMatch(
            etapas,
            recomendacoes,
            LacunasFrequentes(avaliacoes.Values),
            new CustoExecucao(novas.Count + falhas, doCache.Count, falhas, uso, uso.CustoUsd(opcoes)),
            ordenadas.Select(c => c.VagaId).ToList());
    }

    private async Task<(List<AvaliacaoVaga> Novas, UsoTokens Uso, int Falhas)> AvaliarAsync(
        PerfilParaLlm perfil, IReadOnlyList<VagaParaMatch> vagas, CancellationToken ct)
    {
        var novas = new List<AvaliacaoVaga>();
        var uso = UsoTokens.Zero;
        var falhas = 0;
        var trava = new object();

        // A primeira chamada sozinha grava o prefixo (instruções + perfil) no cache; as seguintes, em paralelo, já leem dele.
        var primeira = vagas.Take(1);
        var resto = vagas.Skip(1);

        async Task Uma(VagaParaMatch vaga, CancellationToken token)
        {
            try
            {
                var (a, u) = await avaliador.AvaliarAsync(perfil, vaga, token);
                lock (trava) { novas.Add(a); uso = uso.Somar(u); }
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not Anthropic.Exceptions.AnthropicUnauthorizedException)
            {
                // Uma vaga problemática não derruba a recomendação inteira. Só o id vai para o log.
                log.LogWarning(ex, "Falha ao avaliar a vaga {VagaId}", vaga.Id);
                lock (trava) falhas++;
            }
        }

        foreach (var v in primeira) await Uma(v, ct);
        await Parallel.ForEachAsync(resto, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, opcoes.Paralelismo), CancellationToken = ct },
            async (v, token) => await Uma(v, token));

        return (novas, uso, falhas);
    }

    /// <summary>"40% das vagas que combinam com você pedem Docker": lacunas entre as vagas com nota ≥ NotaMinimaCombina.</summary>
    private IReadOnlyList<LacunaFrequente> LacunasFrequentes(IEnumerable<AvaliacaoVaga> avaliacoes)
    {
        var combinam = avaliacoes.Where(a => a.Nota >= opcoes.NotaMinimaCombina).ToList();
        if (combinam.Count == 0) return [];

        return combinam
            .SelectMany(a => a.Lacunas.Select(l => l.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            .GroupBy(l => l, StringComparer.OrdinalIgnoreCase)
            .Select(g => new LacunaFrequente(g.First(), g.Count(), (int)Math.Round(100.0 * g.Count() / combinam.Count)))
            .OrderByDescending(l => l.Vagas).ThenBy(l => l.Skill, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }
}
