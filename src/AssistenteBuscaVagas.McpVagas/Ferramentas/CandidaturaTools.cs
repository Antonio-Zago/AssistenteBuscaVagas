using System.ComponentModel;
using AssistenteBuscaVagas.McpVagas.Armazenamento;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AssistenteBuscaVagas.McpVagas.Ferramentas;

/// <summary>Kanban de candidaturas e estatísticas de skills. O sistema registra; quem se candidata é o usuário.</summary>
[McpServerToolType]
public static class CandidaturaTools
{
    [McpServerTool(Name = "atualizar_candidatura", Destructive = true, Idempotent = true)]
    [Description("Coloca uma vaga no kanban de candidaturas ou muda a etapa dela: salva, aplicada, entrevista, oferta ou recusada. " +
                 "Só registra o andamento: não envia candidatura nenhuma. Notas vazias apagam as anteriores; omitidas, mantêm.")]
    public static async Task<CandidaturaResumo> AtualizarCandidatura(
        ArmazenamentoCandidaturas armazenamento,
        [Description("Id da vaga.")] int vagaId,
        [Description("salva, aplicada, entrevista, oferta ou recusada.")] string status,
        [Description("Anotações do usuário (opcional).")] string? notas = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await armazenamento.AtualizarAsync(vagaId, status, notas, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            throw new McpException(ex.Message);
        }
    }

    [McpServerTool(Name = "remover_candidatura", Destructive = true, Idempotent = true)]
    [Description("Tira uma vaga do kanban de candidaturas. Devolve false se ela não estava lá.")]
    public static Task<bool> RemoverCandidatura(
        ArmazenamentoCandidaturas armazenamento,
        [Description("Id da vaga.")] int vagaId,
        CancellationToken cancellationToken = default) =>
        armazenamento.RemoverAsync(vagaId, cancellationToken);

    [McpServerTool(Name = "listar_candidaturas", ReadOnly = true)]
    [Description("Lista as vagas do kanban de candidaturas com a etapa (salva, aplicada, entrevista, oferta, recusada), " +
                 "as notas do usuário, os dados da vaga e a nota do match. Use para \"em quais vagas eu me candidatei?\".")]
    public static Task<IReadOnlyList<CandidaturaResumo>> ListarCandidaturas(
        ArmazenamentoCandidaturas armazenamento, CancellationToken cancellationToken = default) =>
        armazenamento.ListarAsync(null, cancellationToken);

    [McpServerTool(Name = "estatisticas_skills", ReadOnly = true)]
    [Description("Lacunas de skills: quais requisitos que faltam no perfil aparecem em mais vagas avaliadas pelo LLM " +
                 "(ex.: \"Docker falta em 40% das vagas\"). Considera só as avaliações do perfil atual. Use para \"quais skills me faltam?\".")]
    public static Task<EstatisticasSkills> EstatisticasSkills(
        ArmazenamentoCandidaturas armazenamento,
        [Description("Só vagas com nota a partir disto (0 a 100). Padrão 0: todas as avaliadas.")] int notaMinima = 0,
        CancellationToken cancellationToken = default) =>
        armazenamento.EstatisticasAsync(Math.Clamp(notaMinima, 0, 100), cancellationToken);
}
