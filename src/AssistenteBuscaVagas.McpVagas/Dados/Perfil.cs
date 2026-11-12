namespace AssistenteBuscaVagas.McpVagas.Dados;

/// <summary>
/// O perfil do usuário (tabela <c>perfis</c> do plano). O sistema é pessoal, então existe uma única linha.
/// Currículo e preferências são guardados como jsonb: o formato é validado na entrada (<c>ValidacaoPerfil</c>)
/// e ainda vai mudar bastante nas próximas fases, então não vale normalizar em tabelas agora.
/// Não há colunas para nome, e-mail, telefone ou endereço: esses dados nunca são guardados.
/// </summary>
public sealed class Perfil
{
    public const int IdUnico = 1;

    public int Id { get; set; }

    /// <summary>Currículo estruturado (JSON de <c>Curriculo</c>). Extraído pelo agente_perfil.</summary>
    public string? Curriculo { get; set; }
    public DateTimeOffset? CurriculoAtualizadoEm { get; set; }

    /// <summary>Preferências de busca (JSON de <c>Preferencias</c>). Definidas pelo usuário.</summary>
    public string? Preferencias { get; set; }
    public DateTimeOffset? PreferenciasAtualizadasEm { get; set; }
}
