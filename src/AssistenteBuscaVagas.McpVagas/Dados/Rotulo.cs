namespace AssistenteBuscaVagas.McpVagas.Dados;

/// <summary>
/// Rótulo manual de uma vaga: "boa para mim" ou "ruim para mim". É o conjunto de avaliação usado para medir
/// a precision@5 do funil. Não é o feedback da fase 7 (que altera as recomendações): rótulo só mede.
/// </summary>
public sealed class Rotulo
{
    public int VagaId { get; set; }
    public Vaga Vaga { get; set; } = null!;

    public bool Relevante { get; set; }
    public string? Observacao { get; set; }
    public DateTimeOffset AtualizadoEm { get; set; }
}
