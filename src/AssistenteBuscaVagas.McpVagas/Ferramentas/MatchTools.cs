using System.ComponentModel;
using AssistenteBuscaVagas.McpVagas.Armazenamento;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AssistenteBuscaVagas.McpVagas.Ferramentas;

/// <summary>
/// Ferramentas MCP do match: cache das avaliações do LLM e rótulos manuais do conjunto de avaliação.
/// Quem calcula notas é o agente_match; aqui só se guarda e consulta.
/// </summary>
[McpServerToolType]
public static class MatchTools
{
    [McpServerTool(Name = "salvar_matches", Destructive = true, Idempotent = true)]
    [Description("Grava avaliações de vagas feitas pelo LLM, sob uma chave de avaliação (hash de perfil + modelo + versão do prompt). " +
                 "Se já existe avaliação da mesma vaga com a mesma chave, ela é substituída. Máximo de 100 por chamada. Devolve quantas foram gravadas.")]
    public static async Task<int> SalvarMatches(
        ArmazenamentoMatches armazenamento,
        [Description("Hash hexadecimal (64 caracteres, minúsculo) que identifica perfil + modelo + versão do prompt.")] string chaveAvaliacao,
        [Description("Modelo que avaliou (ex.: claude-opus-5-5).")] string modelo,
        [Description("Versão do prompt de avaliação.")] int versaoPrompt,
        [Description("Avaliações a gravar.")] IReadOnlyList<MatchEntrada> matches,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await armazenamento.SalvarAsync(chaveAvaliacao, modelo, versaoPrompt, matches, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            throw new McpException(ex.Message);
        }
    }

    [McpServerTool(Name = "obter_matches", ReadOnly = true)]
    [Description("Lê as avaliações já gravadas para uma chave de avaliação e uma lista de vagas. Vagas sem avaliação não aparecem.")]
    public static async Task<IReadOnlyList<MatchSalvo>> ObterMatches(
        ArmazenamentoMatches armazenamento,
        [Description("A mesma chave usada em salvar_matches.")] string chaveAvaliacao,
        [Description("Ids das vagas.")] IReadOnlyList<int> vagaIds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await armazenamento.ObterAsync(chaveAvaliacao, vagaIds, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            throw new McpException(ex.Message);
        }
    }

    [McpServerTool(Name = "listar_avaliacoes", ReadOnly = true)]
    [Description("Histórico de vagas já avaliadas pelo LLM, da maior nota para a menor: nota, justificativa, pontos fortes, lacunas, " +
                 "impeditivos, quando foi avaliada e se entrou num resumo do worker. Inclui vagas com nota baixa. " +
                 "Use para \"quais vagas já foram avaliadas?\" ou \"por que tal vaga teve nota baixa?\".")]
    public static Task<ResultadoAvaliacoes> ListarAvaliacoes(
        ArmazenamentoAvaliacoes armazenamento,
        [Description("Só vagas com nota a partir disto (0 a 100).")] int notaMinima = 0,
        [Description("true (padrão): só avaliações do perfil atual. false: a avaliação mais recente de cada vaga, de qualquer perfil.")] bool soPerfilAtual = true,
        [Description("Só vagas que já entraram num resumo do worker.")] bool soRecomendadas = false,
        [Description("Texto procurado no título ou na empresa (opcional).")] string? busca = null,
        [Description("Quantidade máxima (1 a 200).")] int limite = 100,
        CancellationToken cancellationToken = default) =>
        armazenamento.ListarAsync(Math.Clamp(notaMinima, 0, 100), soPerfilAtual, soRecomendadas, busca, limite, cancellationToken);

    [McpServerTool(Name = "rotular_vaga", Destructive = true, Idempotent = true)]
    [Description("Marca uma vaga como relevante (boa para o usuário) ou não. É o conjunto de avaliação usado para medir a precision@5 do funil. " +
                 "Rotular de novo substitui o rótulo anterior.")]
    public static async Task<RotuloResumo> RotularVaga(
        ArmazenamentoMatches armazenamento,
        [Description("Id da vaga.")] int vagaId,
        [Description("true = a vaga é boa para o usuário; false = não é.")] bool relevante,
        [Description("Motivo opcional (ex.: \"stack certa, mas exige inglês fluente\").")] string? observacao = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await armazenamento.RotularAsync(vagaId, relevante, observacao, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            throw new McpException(ex.Message);
        }
    }

    [McpServerTool(Name = "remover_rotulo", Destructive = true, Idempotent = true)]
    [Description("Remove o rótulo de uma vaga do conjunto de avaliação. Devolve false se a vaga não tinha rótulo.")]
    public static Task<bool> RemoverRotulo(
        ArmazenamentoMatches armazenamento,
        [Description("Id da vaga.")] int vagaId,
        CancellationToken cancellationToken = default) =>
        armazenamento.RemoverRotuloAsync(vagaId, cancellationToken);

    [McpServerTool(Name = "listar_rotulos", ReadOnly = true)]
    [Description("Lista todas as vagas rotuladas no conjunto de avaliação, com o rótulo e o título.")]
    public static Task<IReadOnlyList<RotuloResumo>> ListarRotulos(ArmazenamentoMatches armazenamento, CancellationToken cancellationToken = default) =>
        armazenamento.ListarRotulosAsync(cancellationToken);
}
