using System.Text.RegularExpressions;

namespace AssistenteBuscaVagas.McpVagas.Armazenamento;

/// <summary>
/// Validação na fronteira de dados: qualquer cliente MCP pode chamar salvar_perfil, então o formato
/// é conferido aqui mesmo que o agente_perfil já peça JSON com schema ao Claude.
/// Devolve uma cópia normalizada (espaços aparados, duplicatas removidas, valores canônicos).
/// Erros viram <see cref="ArgumentException"/> com uma mensagem que o cliente consegue corrigir.
/// </summary>
public static partial class ValidacaoPerfil
{
    public static readonly string[] Senioridades = ["Estágio", "Júnior", "Pleno", "Sênior", "Especialista", "Liderança"];
    public static readonly string[] CategoriasSkill = ["Linguagem", "Framework", "Nuvem", "BancoDeDados", "Ferramenta", "Pratica", "Outra"];
    public static readonly string[] NiveisIdioma = ["Básico", "Intermediário", "Avançado", "Fluente", "Nativo"];
    public static readonly string[] Modalidades = ["Remoto", "Híbrido", "Presencial"];

    private const int MaxSkills = 100;
    private const int MaxItens = 30;
    private const int MaxDestaques = 15;
    private const int MaxAnos = 60;

    public static Curriculo Validar(Curriculo c)
    {
        ArgumentNullException.ThrowIfNull(c);

        var resumo = Obrigatorio(c.Resumo, "resumo", 2000);
        var senioridade = Canonico(c.Senioridade, Senioridades, "senioridade");
        var anos = Anos(c.AnosExperiencia, "anosExperiencia");

        var skills = (c.Skills ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s?.Nome))
            .Select(s => new Skill(
                Limitar(s.Nome.Trim(), 100),
                Canonico(s.Categoria, CategoriasSkill, $"categoria da skill '{s.Nome.Trim()}'"),
                Anos(s.Anos, $"anos da skill '{s.Nome.Trim()}'"),
                Limitar((s.Evidencia ?? "").Trim(), 300)))
            // A mesma skill pode aparecer em várias experiências: fica a ocorrência com mais anos.
            .GroupBy(s => s.Nome, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(s => s.Anos ?? -1).First())
            .ToList();
        if (skills.Count == 0)
            throw new ArgumentException("O currículo precisa ter pelo menos uma skill.");
        if (skills.Count > MaxSkills)
            throw new ArgumentException($"No máximo {MaxSkills} skills.");

        var idiomas = (c.Idiomas ?? [])
            .Where(i => !string.IsNullOrWhiteSpace(i?.Nome))
            .Select(i => new Idioma(Limitar(i.Nome.Trim(), 50), Canonico(i.Nivel, NiveisIdioma, $"nível do idioma '{i.Nome.Trim()}'")))
            .DistinctBy(i => i.Nome, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var experiencias = (c.Experiencias ?? [])
            .Where(e => e is not null)
            .Select(e => new Experiencia(
                Obrigatorio(e.Empresa, "empresa da experiência", 200),
                Obrigatorio(e.Cargo, "cargo da experiência", 200),
                Data(e.Inicio, "início da experiência"),
                Data(e.Fim, "fim da experiência"),
                (e.Destaques ?? []).Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => Limitar(d.Trim(), 500)).Take(MaxDestaques).ToList()))
            .Take(MaxItens)
            .ToList();

        var formacao = (c.Formacao ?? [])
            .Where(f => f is not null)
            .Select(f => new Formacao(
                Obrigatorio(f.Instituicao, "instituição da formação", 200),
                Obrigatorio(f.Curso, "curso da formação", 200),
                Data(f.Conclusao, "conclusão da formação")))
            .Take(MaxItens)
            .ToList();

        var certificacoes = Lista(c.Certificacoes, 200);

        return new Curriculo(resumo, Opcional(c.CargoAtual, 200), senioridade, anos, skills, idiomas, experiencias, formacao, certificacoes);
    }

    public static Preferencias Validar(Preferencias p)
    {
        ArgumentNullException.ThrowIfNull(p);

        var modalidades = (p.Modalidades ?? [])
            .Select(m => Canonico(m, Modalidades, "modalidade"))
            .Distinct()
            .ToList();
        if (modalidades.Count == 0)
            throw new ArgumentException($"Informe pelo menos uma modalidade ({string.Join(", ", Modalidades)}).");

        if (p.SalarioMinimoMensal is < 0)
            throw new ArgumentException("O salário mínimo não pode ser negativo.");

        var moeda = Opcional(p.Moeda, 3)?.ToUpperInvariant();
        if (p.SalarioMinimoMensal is not null && moeda is null)
            throw new ArgumentException("Informe a moeda do salário (ex.: BRL).");

        return new Preferencias(
            Lista(p.Cargos, 100),
            modalidades,
            Lista(p.Cidades, 100),
            string.IsNullOrWhiteSpace(p.Senioridade) ? null : Canonico(p.Senioridade, Senioridades, "senioridade desejada"),
            p.SalarioMinimoMensal,
            moeda,
            Lista(p.Idiomas, 20).Select(i => i.ToLowerInvariant()).Distinct().ToList());
    }

    private static string Obrigatorio(string? valor, string campo, int max) =>
        string.IsNullOrWhiteSpace(valor) ? throw new ArgumentException($"O campo {campo} é obrigatório.") : Limitar(valor.Trim(), max);

    private static string? Opcional(string? valor, int max) =>
        string.IsNullOrWhiteSpace(valor) ? null : Limitar(valor.Trim(), max);

    private static string Limitar(string valor, int max) => valor.Length <= max ? valor : valor[..max];

    private static List<string> Lista(IEnumerable<string>? itens, int max) =>
        (itens ?? [])
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Select(i => Limitar(i.Trim(), max))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxItens)
            .ToList();

    /// <summary>Aceita o valor sem diferenciar maiúsculas e devolve a grafia canônica.</summary>
    private static string Canonico(string? valor, string[] permitidos, string campo) =>
        permitidos.FirstOrDefault(p => string.Equals(p, valor?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Valor inválido para {campo}: '{valor}'. Use um de: {string.Join(", ", permitidos)}.");

    private static int? Anos(int? anos, string campo) =>
        anos is null or (>= 0 and <= MaxAnos) ? anos : throw new ArgumentException($"O campo {campo} deve estar entre 0 e {MaxAnos}.");

    private static string? Data(string? valor, string campo)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;
        var v = valor.Trim();
        return FormatoData().IsMatch(v) ? v : throw new ArgumentException($"O campo {campo} deve estar no formato AAAA ou AAAA-MM (recebido: '{v}').");
    }

    [GeneratedRegex(@"^(19|20)\d{2}(-(0[1-9]|1[0-2]))?$")]
    private static partial Regex FormatoData();
}
