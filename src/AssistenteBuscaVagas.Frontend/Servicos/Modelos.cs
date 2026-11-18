namespace AssistenteBuscaVagas.Frontend.Servicos;

// Cópias do JSON que o supervisor devolve em /api/* e /conversa (que por sua vez vem do mcp_vagas e dos agentes).
// Como nos outros serviços, o contrato é o JSON, não uma DLL compartilhada. Campos ausentes ficam com o padrão.

public static class Etapas
{
    public static readonly (string Id, string Nome)[] Todas =
    [
        ("salva", "Salva"), ("aplicada", "Aplicada"), ("entrevista", "Entrevista"), ("oferta", "Oferta"), ("recusada", "Recusada"),
    ];

    public static string Nome(string id) => Todas.FirstOrDefault(e => e.Id == id).Nome ?? id;
}

// ---- Worker (listar_execucoes_diarias) ----
public sealed record ExecucaoDia(string Data, string Status, int Tentativas, DateTimeOffset IniciadaEm, DateTimeOffset ConcluidaEm, ResumoDia? Resumo);
public sealed record ResumoDia(List<PassoExecucao>? Passos, List<VagaDoDia>? VagasRecomendadas, int AbaixoDaNotaMinima, decimal CustoMatchUsd, string? Texto);
public sealed record PassoExecucao(string Nome, bool Sucesso, string Resumo, long DuracaoMs);
public sealed record VagaDoDia(int VagaId, string Titulo, string Empresa, string? Local, int Nota, string Justificativa,
    List<string>? Lacunas, List<string>? Impeditivos, string Url);

// ---- agente_match (recomendar_vagas) ----
public sealed record ResultadoMatch(List<EtapaFunil>? Etapas, List<RecomendacaoFinal>? Recomendacoes, List<LacunaContada>? LacunasFrequentes, CustoExecucao? Custo);
public sealed record EtapaFunil(string Etapa, int Vagas, Dictionary<string, int>? Descartes);
public sealed record RecomendacaoFinal(int Posicao, int VagaId, string Titulo, string Empresa, string? Local, bool Remoto, string Fonte, string Url,
    int Nota, string Justificativa, List<string>? PontosFortes, List<string>? Lacunas, List<string>? Impeditivos, int PosicaoBm25, bool DoCache);
public sealed record CustoExecucao(int ChamadasLlm, int ReaproveitadasDoCache, int Falhas, decimal CustoEstimadoUsd);

// ---- Histórico (listar_avaliacoes) ----
public sealed record ResultadoAvaliacoes(int Total, bool SoPerfilAtual, List<VagaAvaliada>? Vagas);
public sealed record VagaAvaliada(int VagaId, string Titulo, string Empresa, string? Local, bool Remoto, string Url, string Fonte, DateTimeOffset? PublicadaEm,
    int Nota, string Justificativa, List<string>? PontosFortes, List<string>? Lacunas, List<string>? Impeditivos,
    DateTimeOffset AvaliadaEm, string Modelo, DateTimeOffset? RecomendadaEm);

// ---- Kanban (listar_candidaturas) ----
public sealed record Candidatura(int VagaId, string Status, string? Notas, DateTimeOffset CriadaEm, DateTimeOffset AtualizadaEm,
    string Titulo, string Empresa, string? Local, bool Remoto, string Url, string Fonte, int? Nota);

// ---- Lacunas (estatisticas_skills) ----
public sealed record EstatisticasSkills(int VagasAvaliadas, int NotaMinima, int VagasConsideradas, DateTimeOffset? AvaliadasAte, List<LacunaContada>? Lacunas);
public sealed record LacunaContada(string Skill, int Vagas, int Percentual);

// ---- Conversa (supervisor) ----
public sealed record RespostaConversa(string Resposta, List<PassoConversa>? Passos, List<CapacidadeOferecida>? CapacidadesOferecidas);
public sealed record PassoConversa(int Ordem, string Ferramenta, string Tipo, bool Sucesso, string Resumo, long DuracaoMs);
public sealed record CapacidadeOferecida(string Ferramenta, string Tipo, double Pontuacao, bool Paga);
public sealed record RotaPrevista(List<CapacidadeOferecida>? Oferecidas);

/// <summary>Uma vaga pronta para o cartão, venha do worker, do ranking atual ou do kanban.</summary>
public sealed record VagaCartao(int VagaId, string Titulo, string Empresa, string? Local, int? Nota, string? Justificativa,
    IReadOnlyList<string> PontosFortes, IReadOnlyList<string> Lacunas, IReadOnlyList<string> Impeditivos, string Url)
{
    public static VagaCartao De(VagaDoDia v) => new(v.VagaId, v.Titulo, v.Empresa, v.Local, v.Nota, v.Justificativa, [], v.Lacunas ?? [], v.Impeditivos ?? [], v.Url);

    public static VagaCartao De(VagaAvaliada v) => new(v.VagaId, v.Titulo, v.Empresa, v.Local, v.Nota, v.Justificativa,
        v.PontosFortes ?? [], v.Lacunas ?? [], v.Impeditivos ?? [], v.Url);

    public static VagaCartao De(RecomendacaoFinal r) => new(r.VagaId, r.Titulo, r.Empresa, r.Local, r.Nota, r.Justificativa,
        r.PontosFortes ?? [], r.Lacunas ?? [], r.Impeditivos ?? [], r.Url);
}
