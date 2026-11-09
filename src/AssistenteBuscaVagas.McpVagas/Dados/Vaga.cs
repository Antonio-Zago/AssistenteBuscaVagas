namespace AssistenteBuscaVagas.McpVagas.Dados;

/// <summary>Uma vaga coletada. Tabela <c>vagas</c> do plano (o embedding entra numa fase posterior).</summary>
public sealed class Vaga
{
    public int Id { get; set; }

    public int FonteId { get; set; }
    public Fonte Fonte { get; set; } = null!;

    /// <summary>Id da vaga na fonte. Junto com <see cref="FonteId"/> garante que a coleta não duplica.</summary>
    public required string IdExterno { get; set; }

    public required string Empresa { get; set; }
    public required string Titulo { get; set; }

    /// <summary>Texto puro (HTML convertido). Vem de terceiros: tratar como dado não confiável ao mandar para um LLM.</summary>
    public required string Descricao { get; set; }

    /// <summary>Descrição como chegou da fonte (pode ser HTML). Guardada para reprocessar se a conversão mudar.</summary>
    public required string DescricaoOriginal { get; set; }

    public string? Local { get; set; }
    public bool Remoto { get; set; }
    public string? Senioridade { get; set; }
    public string? Categoria { get; set; }
    public string? TipoContrato { get; set; }
    public string? Salario { get; set; }
    public List<string> Tags { get; set; } = [];

    /// <summary>Link original da vaga. Os termos das fontes (ex.: Remotive) exigem sempre apontar para ele.</summary>
    public required string Url { get; set; }

    public DateTimeOffset? PublicadaEm { get; set; }
    public DateTimeOffset ColetadaEm { get; set; }

    /// <summary>Hash de empresa + título normalizados, para a deduplicação entre fontes (fase 8).</summary>
    public required string HashDedup { get; set; }
}
