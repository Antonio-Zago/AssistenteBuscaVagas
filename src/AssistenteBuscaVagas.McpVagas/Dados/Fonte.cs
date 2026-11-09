namespace AssistenteBuscaVagas.McpVagas.Dados;

/// <summary>Uma origem de vagas (API pública). Tabela <c>fontes</c> do plano.</summary>
public sealed class Fonte
{
    public int Id { get; set; }
    public required string Nome { get; set; }
    public required string Tipo { get; set; }
    public required string UrlBase { get; set; }
    public bool Ativa { get; set; } = true;

    /// <summary>Usada para respeitar o limite de requisições combinado com a fonte.</summary>
    public DateTimeOffset? UltimaColetaEm { get; set; }

    public List<Vaga> Vagas { get; set; } = [];
}
