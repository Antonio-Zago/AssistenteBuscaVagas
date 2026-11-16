namespace AssistenteBuscaVagas.McpVagas.Dados;

/// <summary>
/// Avaliação de uma vaga feita pelo LLM (tabela <c>matches</c> do plano). Funciona como cache: o agente_match
/// calcula a <see cref="ChaveAvaliacao"/> a partir do perfil, do modelo e da versão do prompt; se nada disso
/// mudou, a avaliação guardada é reaproveitada e o LLM não é chamado de novo para a mesma vaga.
/// </summary>
public sealed class Match
{
    public int Id { get; set; }

    public int VagaId { get; set; }
    public Vaga Vaga { get; set; } = null!;

    /// <summary>Hash (hex, 64) de perfil + modelo + versão do prompt, calculado pelo agente_match.</summary>
    public required string ChaveAvaliacao { get; set; }

    public required string Modelo { get; set; }
    public int VersaoPrompt { get; set; }

    /// <summary>0 a 100.</summary>
    public int Nota { get; set; }
    public required string Justificativa { get; set; }
    public List<string> PontosFortes { get; set; } = [];
    public List<string> Lacunas { get; set; } = [];
    public List<string> Impeditivos { get; set; } = [];

    public DateTimeOffset CriadoEm { get; set; }
}
