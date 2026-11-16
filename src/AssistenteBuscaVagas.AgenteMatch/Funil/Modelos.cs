using AssistenteBuscaVagas.Comum.Busca;

namespace AssistenteBuscaVagas.AgenteMatch.Funil;

// Cópias parciais do contrato do mcp_vagas (ContratoPerfil.cs e VagasTools.cs). Cada serviço tem a sua cópia:
// o contrato entre eles é o JSON das ferramentas MCP, não uma DLL. Só entram os campos que o funil usa;
// o desserializador ignora o resto.

// Os campos opcionais no fim só são usados pela etapa com LLM (fase 4). O nome das empresas das experiências
// não é declarado de propósito: não é lido, então não vai para o LLM.

public sealed record Curriculo(
    string? CargoAtual,
    string Senioridade,
    IReadOnlyList<Skill> Skills,
    int? AnosExperiencia = null,
    IReadOnlyList<Idioma>? Idiomas = null,
    IReadOnlyList<Experiencia>? Experiencias = null,
    IReadOnlyList<Formacao>? Formacao = null,
    IReadOnlyList<string>? Certificacoes = null);

public sealed record Skill(string Nome, string Categoria, int? Anos);

public sealed record Idioma(string Nome, string Nivel);

public sealed record Experiencia(string Cargo, string? Inicio, string? Fim, IReadOnlyList<string>? Destaques);

public sealed record Formacao(string Curso, string? Conclusao);

public sealed record Preferencias(
    IReadOnlyList<string> Cargos,
    IReadOnlyList<string> Modalidades,
    IReadOnlyList<string> Cidades,
    string? Senioridade,
    decimal? SalarioMinimoMensal = null,
    string? Moeda = null,
    IReadOnlyList<string>? Idiomas = null);

public sealed record PerfilCompleto(Curriculo? Curriculo, Preferencias? Preferencias);

public sealed record VagaParaMatch(
    int Id,
    string Titulo,
    string Empresa,
    string? Local,
    bool Remoto,
    string? Categoria,
    string? Salario,
    IReadOnlyList<string> Tags,
    DateTimeOffset? PublicadaEm,
    string Fonte,
    string Url,
    string Descricao);

// ---- Resultado do funil ----

public sealed record EtapaFunil(string Etapa, int Vagas, IReadOnlyDictionary<string, int>? Descartes = null);


public sealed record Recomendacao(
    int Posicao,
    int VagaId,
    string Titulo,
    string Empresa,
    string? Local,
    bool Remoto,
    string? SenioridadeInferida,
    string Fonte,
    string Url,
    double Pontuacao,
    [property: System.ComponentModel.Description("0 a 100, relativa à primeira colocada. Não é uma nota absoluta.")] int NotaRelativa,
    IReadOnlyList<string> SkillsEncontradas,
    IReadOnlyList<string> TermosDoCargoNoTitulo,
    IReadOnlyList<string> Lacunas,
    string Justificativa,
    IReadOnlyList<TermoPontuado> TermosQuePontuaram);

public sealed record LacunaFrequente(string Skill, int Vagas, int Percentual);

public sealed record ResultadoFunil(
    IReadOnlyList<EtapaFunil> Etapas,
    IReadOnlyList<Recomendacao> Recomendacoes,
    IReadOnlyList<LacunaFrequente> LacunasFrequentes,
    IReadOnlyList<TermoPontuado> Consulta);

/// <summary>O perfil não tem o mínimo para montar a consulta. Vira HTTP 422.</summary>
public sealed class PerfilIncompletoException(string mensagem) : Exception(mensagem);

/// <summary>Rótulo como o mcp_vagas devolve (listar_rotulos / rotular_vaga).</summary>
public sealed record RotuloResumo(int VagaId, bool Relevante, string? Observacao, DateTimeOffset AtualizadoEm, string Titulo, string Empresa);
