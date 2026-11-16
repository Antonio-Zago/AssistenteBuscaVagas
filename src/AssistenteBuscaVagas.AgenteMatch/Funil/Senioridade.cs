using AssistenteBuscaVagas.Comum.Busca;

namespace AssistenteBuscaVagas.AgenteMatch.Funil;

/// <summary>
/// Senioridade como número, para medir distância. As fontes quase nunca informam a senioridade,
/// então ela é inferida do título ("Senior Backend Engineer", "Desenvolvedor Pleno").
/// </summary>
public static class Senioridade
{
    // Mesmos valores de ValidacaoPerfil.Senioridades, na ordem.
    public static readonly string[] Niveis = ["Estágio", "Júnior", "Pleno", "Sênior", "Especialista", "Liderança"];

    private static readonly (int Nivel, string[] Termos)[] Marcadores =
    [
        // "lead" fica em Especialista, não em Liderança: "Tech Lead" costuma ser um cargo técnico.
        (5, ["head", "manager", "director", "diretor", "diretora", "gerente", "coordenador", "coordenadora", "cto", "vp", "chief"]),
        (4, ["staff", "principal", "lead", "especialista", "lider", "distinguished"]),
        (3, ["senior", "sr", "iii"]),
        (2, ["pleno", "mid", "intermediate", "ii"]),
        (1, ["junior", "jr", "entry"]),
        (0, ["intern", "internship", "estagio", "estagiario", "estagiaria", "trainee"]),
    ];

    private static readonly HashSet<string> TodosMarcadores = Marcadores.SelectMany(m => m.Termos).ToHashSet();

    /// <summary>"senior", "pleno", "lead"...: dizem o nível, não a área. Não entram na consulta do BM25.</summary>
    public static bool EhMarcador(string termo) => TodosMarcadores.Contains(termo);

    public static int? Nivel(string? senioridade)
    {
        if (string.IsNullOrWhiteSpace(senioridade)) return null;
        var i = Array.FindIndex(Niveis, n => string.Equals(n, senioridade.Trim(), StringComparison.OrdinalIgnoreCase));
        return i < 0 ? null : i;
    }

    /// <summary>O nível mais alto citado no título, ou null se o título não diz. "Senior Engineering Manager" → Liderança.</summary>
    public static int? InferirDoTitulo(string titulo)
    {
        var termos = Tokenizador.Termos(titulo, removerStopwords: false).ToHashSet();
        foreach (var (nivel, marcadores) in Marcadores)
            if (marcadores.Any(termos.Contains))
                return nivel;
        return null;
    }
}
