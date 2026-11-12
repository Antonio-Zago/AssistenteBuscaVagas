namespace AssistenteBuscaVagas.AgentePerfil.Perfil;

// Mesmo formato do contrato do mcp_vagas (ContratoPerfil.cs). Cada serviço tem a sua cópia, como já acontece
// com as vagas (VagaColetada x VagaEntrada): o contrato entre eles é o JSON das ferramentas MCP, não uma DLL.

public sealed record Curriculo(
    string Resumo,
    string? CargoAtual,
    string Senioridade,
    int? AnosExperiencia,
    IReadOnlyList<Skill> Skills,
    IReadOnlyList<Idioma> Idiomas,
    IReadOnlyList<Experiencia> Experiencias,
    IReadOnlyList<Formacao> Formacao,
    IReadOnlyList<string> Certificacoes);

public sealed record Skill(string Nome, string Categoria, int? Anos, string Evidencia);

public sealed record Idioma(string Nome, string Nivel);

public sealed record Experiencia(string Empresa, string Cargo, string? Inicio, string? Fim, IReadOnlyList<string> Destaques);

public sealed record Formacao(string Instituicao, string Curso, string? Conclusao);

public sealed record Preferencias(
    IReadOnlyList<string> Cargos,
    IReadOnlyList<string> Modalidades,
    IReadOnlyList<string> Cidades,
    string? Senioridade,
    decimal? SalarioMinimoMensal,
    string? Moeda,
    IReadOnlyList<string> Idiomas);

public sealed record PerfilCompleto(
    Curriculo? Curriculo,
    DateTimeOffset? CurriculoAtualizadoEm,
    Preferencias? Preferencias,
    DateTimeOffset? PreferenciasAtualizadasEm);

/// <summary>Erro que o usuário consegue corrigir (arquivo errado, PDF sem texto etc.). Vira HTTP 422.</summary>
public sealed class CurriculoInvalidoException(string mensagem) : Exception(mensagem);
