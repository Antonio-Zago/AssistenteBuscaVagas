using System.ComponentModel;

namespace AssistenteBuscaVagas.McpVagas.Armazenamento;

// Contrato do perfil exposto pelas ferramentas MCP. As descrições viram o JSON Schema que os clientes enxergam.
// Os valores permitidos (senioridade, categoria, nível, modalidade) estão em ValidacaoPerfil.

[Description("Currículo estruturado. Não contém nome, e-mail, telefone nem endereço.")]
public sealed record Curriculo(
    [property: Description("Resumo profissional em 2 a 4 frases.")] string Resumo,
    [property: Description("Cargo mais recente, se houver.")] string? CargoAtual,
    [property: Description("Estágio, Júnior, Pleno, Sênior, Especialista ou Liderança.")] string Senioridade,
    [property: Description("Anos de experiência profissional na área, se der para calcular.")] int? AnosExperiencia,
    IReadOnlyList<Skill> Skills,
    IReadOnlyList<Idioma> Idiomas,
    IReadOnlyList<Experiencia> Experiencias,
    IReadOnlyList<Formacao> Formacao,
    IReadOnlyList<string> Certificacoes);

public sealed record Skill(
    string Nome,
    [property: Description("Linguagem, Framework, Nuvem, BancoDeDados, Ferramenta, Pratica ou Outra.")] string Categoria,
    [property: Description("Anos de uso, se o currículo permitir calcular.")] int? Anos,
    [property: Description("Trecho curto do currículo que mostra a skill.")] string Evidencia);

public sealed record Idioma(
    string Nome,
    [property: Description("Básico, Intermediário, Avançado, Fluente ou Nativo.")] string Nivel);

public sealed record Experiencia(
    string Empresa,
    string Cargo,
    [property: Description("Início no formato AAAA ou AAAA-MM.")] string? Inicio,
    [property: Description("Fim no formato AAAA ou AAAA-MM; vazio = emprego atual.")] string? Fim,
    [property: Description("Realizações e responsabilidades, como estão no currículo.")] IReadOnlyList<string> Destaques);

public sealed record Formacao(string Instituicao, string Curso, [property: Description("Ano de conclusão (AAAA), se houver.")] string? Conclusao);

[Description("Preferências de busca definidas pelo usuário.")]
public sealed record Preferencias(
    [property: Description("Cargos desejados (ex.: \"Desenvolvedor .NET\").")] IReadOnlyList<string> Cargos,
    [property: Description("Remoto, Híbrido e/ou Presencial.")] IReadOnlyList<string> Modalidades,
    [property: Description("Cidades aceitas para híbrido/presencial.")] IReadOnlyList<string> Cidades,
    [property: Description("Senioridade desejada, mesmos valores do currículo.")] string? Senioridade,
    [property: Description("Salário mínimo mensal aceito.")] decimal? SalarioMinimoMensal,
    [property: Description("Moeda do salário (ex.: BRL, USD).")] string? Moeda,
    [property: Description("Idiomas em que aceita trabalhar (ex.: pt, en).")] IReadOnlyList<string> Idiomas);

public sealed record PerfilCompleto(
    Curriculo? Curriculo,
    DateTimeOffset? CurriculoAtualizadoEm,
    Preferencias? Preferencias,
    DateTimeOffset? PreferenciasAtualizadasEm);
