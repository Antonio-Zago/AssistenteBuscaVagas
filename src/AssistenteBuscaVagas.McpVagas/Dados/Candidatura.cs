namespace AssistenteBuscaVagas.McpVagas.Dados;

/// <summary>
/// Uma vaga no kanban de candidaturas (tabela <c>candidaturas</c> do plano). Quem move os cartões é o usuário:
/// o sistema nunca se candidata sozinho (human-in-the-loop). Uma candidatura por vaga.
/// </summary>
public sealed class Candidatura
{
    public int VagaId { get; set; }
    public Vaga Vaga { get; set; } = null!;

    /// <summary>salva, aplicada, entrevista, oferta ou recusada.</summary>
    public required string Status { get; set; }

    /// <summary>Anotações do usuário (contato, data da entrevista...). Dado pessoal: não vai para log.</summary>
    public string? Notas { get; set; }

    public DateTimeOffset CriadaEm { get; set; }
    public DateTimeOffset AtualizadaEm { get; set; }
}
