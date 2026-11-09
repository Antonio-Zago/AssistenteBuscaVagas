namespace AssistenteBuscaVagas.AgenteColeta.Fontes;

/// <summary>
/// Formato comum que toda fonte devolve, independente do JSON de cada API.
/// É o que o agente envia ao mcp_vagas pela ferramenta <c>salvar_vagas</c>.
/// </summary>
public sealed record VagaColetada(
    string IdExterno,
    string Empresa,
    string Titulo,
    string Descricao,
    string? Local,
    bool Remoto,
    string? Categoria,
    string? TipoContrato,
    string? Salario,
    IReadOnlyList<string> Tags,
    string Url,
    DateTimeOffset? PublicadaEm);

/// <summary>Contrato de uma fonte de vagas. Na fase 1b entram Remotive e Firecrawl (JSON-LD).</summary>
public interface IFonteVagas
{
    /// <summary>Nome da fonte como cadastrado no mcp_vagas (tabela <c>fontes</c>).</summary>
    string Nome { get; }

    Task<IReadOnlyList<VagaColetada>> BuscarAsync(CancellationToken cancellationToken);
}
