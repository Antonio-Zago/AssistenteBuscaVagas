using System.ComponentModel;
using AssistenteBuscaVagas.McpVagas.Armazenamento;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AssistenteBuscaVagas.McpVagas.Ferramentas;

/// <summary>
/// Ferramentas MCP do perfil. O conteúdo é dado pessoal: o servidor não registra currículo nem preferências em log.
/// </summary>
[McpServerToolType]
public static class PerfilTools
{
    [McpServerTool(Name = "salvar_perfil", Destructive = true, Idempotent = true)]
    [Description("Grava o perfil do usuário. Cada parte é opcional e substitui inteira a versão anterior: " +
                 "envie só o currículo para atualizar o currículo, só as preferências para atualizar as preferências. " +
                 "O currículo não pode conter nome, e-mail, telefone nem endereço. Devolve o perfil completo após a gravação.")]
    public static async Task<PerfilCompleto> SalvarPerfil(
        ArmazenamentoPerfil armazenamento,
        [Description("Currículo estruturado (opcional).")] Curriculo? curriculo = null,
        [Description("Preferências de busca (opcional).")] Preferencias? preferencias = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await armazenamento.SalvarAsync(curriculo, preferencias, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            throw new McpException(ex.Message);
        }
    }

    [McpServerTool(Name = "obter_perfil", ReadOnly = true)]
    [Description("Lê o perfil do usuário: currículo estruturado (skills, senioridade, experiências) e preferências de busca. " +
                 "Devolve vazio se o perfil ainda não foi cadastrado.")]
    public static Task<PerfilCompleto?> ObterPerfil(ArmazenamentoPerfil armazenamento, CancellationToken cancellationToken = default) =>
        armazenamento.ObterAsync(cancellationToken);
}
