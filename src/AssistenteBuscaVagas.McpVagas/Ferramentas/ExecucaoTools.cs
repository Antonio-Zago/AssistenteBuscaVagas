using System.ComponentModel;
using System.Text.Json;
using AssistenteBuscaVagas.McpVagas.Armazenamento;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AssistenteBuscaVagas.McpVagas.Ferramentas;

/// <summary>Ferramentas MCP do worker diário: histórico das execuções e vagas já mostradas ao usuário.</summary>
[McpServerToolType]
public static class ExecucaoTools
{
    [McpServerTool(Name = "registrar_execucao_diaria", Destructive = true, Idempotent = true)]
    [Description("Grava (ou substitui) a execução do worker diário de um dia e marca as vagas recomendadas nesse resumo como já " +
                 "recomendadas, para não voltarem nos dias seguintes. Uma vaga já marcada antes continua com a data original.")]
    public static async Task<ExecucaoResumo> RegistrarExecucaoDiaria(
        ArmazenamentoExecucoes armazenamento,
        [Description("Dia da execução no fuso do usuário (AAAA-MM-DD).")] string data,
        [Description("sucesso, parcial ou falha.")] string status,
        [Description("Quantas vezes o dia já foi tentado.")] int tentativas,
        [Description("Início (ISO 8601).")] DateTimeOffset iniciadaEm,
        [Description("Fim (ISO 8601).")] DateTimeOffset concluidaEm,
        [Description("Resumo do dia em JSON (objeto).")] JsonElement resumo,
        [Description("Vagas mostradas no resumo do dia.")] IReadOnlyList<VagaRecomendadaEntrada>? recomendadas = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await armazenamento.RegistrarAsync(data, status, tentativas, iniciadaEm, concluidaEm, resumo, recomendadas, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            throw new McpException(ex.Message);
        }
    }

    [McpServerTool(Name = "listar_execucoes_diarias", ReadOnly = true)]
    [Description("Lista as execuções do worker diário, da mais recente para a mais antiga: status, horários e o resumo do dia " +
                 "(vagas coletadas, vagas recomendadas com nota e link, custo e erros). Use para responder \"quais vagas foram recomendadas hoje?\".")]
    public static Task<IReadOnlyList<ExecucaoResumo>> ListarExecucoesDiarias(
        ArmazenamentoExecucoes armazenamento,
        [Description("Quantos dias (1 a 60).")] int limite = 7,
        CancellationToken cancellationToken = default) =>
        armazenamento.ListarAsync(limite, cancellationToken);

    [McpServerTool(Name = "listar_vagas_recomendadas", ReadOnly = true)]
    [Description("Lista as vagas que já foram recomendadas ao usuário em resumos diários, com a nota e o dia.")]
    public static Task<IReadOnlyList<VagaJaRecomendada>> ListarVagasRecomendadas(
        ArmazenamentoExecucoes armazenamento, CancellationToken cancellationToken = default) =>
        armazenamento.ListarRecomendadasAsync(cancellationToken);
}
