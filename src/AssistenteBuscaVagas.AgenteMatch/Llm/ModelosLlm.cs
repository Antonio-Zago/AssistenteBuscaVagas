using AssistenteBuscaVagas.AgenteMatch.Funil;

namespace AssistenteBuscaVagas.AgenteMatch.Llm;

/// <summary>Configuração da etapa com LLM (seção "Avaliacao" do appsettings).</summary>
public sealed class AvaliacaoOptions
{
    public string Modelo { get; set; } = "claude-opus-5-5";

    /// <summary>low, medium ou high. Medium é o padrão do Opus 5.5; vale medir low com o conjunto de avaliação.</summary>
    public string Esforco { get; set; } = "medium";

    /// <summary>Quantas vagas saem do BM25 e vão para o LLM (a etapa cara do funil).</summary>
    public int CandidatosLlm { get; set; } = 20;

    /// <summary>Quantas vagas são recomendadas no fim.</summary>
    public int RecomendacoesFinais { get; set; } = 5;

    /// <summary>Chamadas ao Claude em paralelo.</summary>
    public int Paralelismo { get; set; } = 4;

    /// <summary>Nota mínima para a vaga contar como "combina" nas lacunas frequentes.</summary>
    public int NotaMinimaCombina { get; set; } = 60;

    /// <summary>Onde o candidato mora. Vai para o LLM, que precisa disso para julgar restrições de país e fuso.</summary>
    public string LocalizacaoCandidato { get; set; } = "Brasil";

    /// <summary>Preços em US$ por milhão de tokens, só para estimar o custo de cada execução.</summary>
    public decimal PrecoEntrada { get; set; } = 4m;
    public decimal PrecoSaida { get; set; } = 20m;
    public decimal PrecoCacheLeitura { get; set; } = 0.20m;
    public decimal PrecoCacheEscrita { get; set; } = 5m;
}

/// <summary>
/// O que o LLM vê do perfil: só o necessário para comparar com a vaga. Fica de fora o resumo livre,
/// o nome das empresas e as evidências (trechos do currículo).
/// </summary>
public sealed record PerfilParaLlm(
    string LocalizacaoCandidato,
    string Senioridade,
    int? AnosExperiencia,
    string? CargoAtual,
    IReadOnlyList<SkillLlm> Skills,
    IReadOnlyList<Idioma> Idiomas,
    IReadOnlyList<Experiencia> Experiencias,
    IReadOnlyList<string> Formacao,
    IReadOnlyList<string> Certificacoes,
    PreferenciasLlm? Preferencias)
{
    public static PerfilParaLlm De(PerfilCompleto perfil, string localizacao)
    {
        var c = perfil.Curriculo;
        var p = perfil.Preferencias;
        return new(
            localizacao,
            c?.Senioridade ?? "não informada",
            c?.AnosExperiencia,
            c?.CargoAtual,
            (c?.Skills ?? []).Select(s => new SkillLlm(s.Nome, s.Anos)).ToList(),
            c?.Idiomas ?? [],
            c?.Experiencias ?? [],
            (c?.Formacao ?? []).Select(f => f.Conclusao is null ? f.Curso : $"{f.Curso} ({f.Conclusao})").ToList(),
            c?.Certificacoes ?? [],
            p is null ? null : new(p.Cargos, p.Modalidades, p.Cidades, p.Senioridade, p.SalarioMinimoMensal, p.Moeda, p.Idiomas ?? []));
    }
}

public sealed record SkillLlm(string Nome, int? Anos);

public sealed record PreferenciasLlm(
    IReadOnlyList<string> Cargos,
    IReadOnlyList<string> Modalidades,
    IReadOnlyList<string> Cidades,
    string? SenioridadeDesejada,
    decimal? SalarioMinimoMensal,
    string? Moeda,
    IReadOnlyList<string> Idiomas);

/// <summary>A avaliação de uma vaga. Mesmo formato de MatchEntrada no mcp_vagas (salvar_matches).</summary>
public sealed record AvaliacaoVaga(
    int VagaId,
    int Nota,
    string Justificativa,
    IReadOnlyList<string> PontosFortes,
    IReadOnlyList<string> Lacunas,
    IReadOnlyList<string> Impeditivos);

public sealed record UsoTokens(long Entrada, long Saida, long CacheLeitura, long CacheEscrita)
{
    public static readonly UsoTokens Zero = new(0, 0, 0, 0);

    public UsoTokens Somar(UsoTokens o) => new(Entrada + o.Entrada, Saida + o.Saida, CacheLeitura + o.CacheLeitura, CacheEscrita + o.CacheEscrita);

    public decimal CustoUsd(AvaliacaoOptions p) => Math.Round(
        (Entrada * p.PrecoEntrada + Saida * p.PrecoSaida + CacheLeitura * p.PrecoCacheLeitura + CacheEscrita * p.PrecoCacheEscrita) / 1_000_000m, 4);
}

/// <summary>Quem avalia uma vaga. Na aplicação é o Claude; nos testes, um avaliador falso.</summary>
public interface IAvaliadorVaga
{
    /// <summary>Identifica modelo + prompt + esforço. Entra na chave do cache: mudou, as avaliações antigas não valem mais.</summary>
    string Assinatura { get; }
    string Modelo { get; }
    int VersaoPrompt { get; }

    Task<(AvaliacaoVaga Avaliacao, UsoTokens Uso)> AvaliarAsync(PerfilParaLlm perfil, VagaParaMatch vaga, CancellationToken ct);
}

/// <summary>Onde as avaliações ficam guardadas. Na aplicação é o mcp_vagas; nos testes, um dicionário.</summary>
public interface IRepositorioMatches
{
    Task<IReadOnlyList<AvaliacaoVaga>> ObterAsync(string chave, IReadOnlyList<int> vagaIds, CancellationToken ct);
    Task SalvarAsync(string chave, string modelo, int versaoPrompt, IReadOnlyList<AvaliacaoVaga> avaliacoes, CancellationToken ct);
}
