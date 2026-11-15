using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AssistenteBuscaVagas.AgenteMatch.Funil;

/// <summary>
/// Quebra texto em termos para o BM25. Feito à mão porque nomes de tecnologia não sobrevivem a um tokenizador
/// comum: "C#", ".NET", "Node.js" e "C++" viram "c", "net", "node"... Aqui eles ficam inteiros.
/// Também normaliza acentos e sinônimos ("dotnet" → ".net", "k8s" → "kubernetes"), para que currículo
/// em português e vaga em inglês falem a mesma língua técnica.
/// </summary>
public static partial class Tokenizador
{
    private static readonly Dictionary<string, string> Sinonimos = new()
    {
        ["dotnet"] = ".net",
        ["csharp"] = "c#",
        ["k8s"] = "kubernetes",
        ["postgres"] = "postgresql",
        ["golang"] = "go",
        ["js"] = "javascript",
        ["ts"] = "typescript",
        ["nodejs"] = "node.js",
        ["reactjs"] = "react",
        ["react.js"] = "react",
        ["vuejs"] = "vue",
        ["vue.js"] = "vue",
        ["angularjs"] = "angular",
        ["nextjs"] = "next.js",
        ["mssql"] = "sql-server",
        ["gcp"] = "google-cloud",
        ["desenvolvedor"] = "developer",
        ["desenvolvedora"] = "developer",
        ["programador"] = "developer",
        ["engenheiro"] = "engineer",
        ["engenheira"] = "engineer",
        ["arquiteto"] = "architect",
    };

    // Pares que só fazem sentido juntos. Juntá-los evita que "server" e "sql" pontuem sozinhos.
    private static readonly Dictionary<(string, string), string> Compostos = new()
    {
        [("sql", "server")] = "sql-server",
        [("google", "cloud")] = "google-cloud",
        [("back", "end")] = "backend",
        [("front", "end")] = "frontend",
        [("full", "stack")] = "fullstack",
        [("machine", "learning")] = "machine-learning",
    };

    // Palavras sem valor de busca, em inglês e português. Termos de senioridade ficam de fora daqui
    // de propósito: o filtro de senioridade usa Termos(..., removerStopwords: false).
    private static readonly HashSet<string> Stopwords =
    [
        "a", "an", "and", "are", "as", "at", "be", "by", "for", "from", "has", "have", "in", "is", "it", "its", "of", "on",
        "or", "our", "that", "the", "their", "this", "to", "we", "will", "with", "you", "your", "us", "who", "what", "can",
        "o", "os", "as", "um", "uma", "e", "de", "do", "da", "dos", "das", "em", "no", "na", "nos", "nas", "por", "para",
        "com", "sem", "que", "se", "ou", "ao", "aos", "sua", "seu", "suas", "seus", "como", "mais", "voce", "nosso", "nossa",
    ];

    // Letras soltas que são linguagens. Qualquer outra letra solta é ruído.
    private static readonly HashSet<string> LetrasValidas = ["c", "r"];

    /// <summary>Termos normalizados, na ordem em que aparecem (com repetição).</summary>
    public static List<string> Termos(string? texto, bool removerStopwords = true)
    {
        var termos = new List<string>();
        if (string.IsNullOrWhiteSpace(texto)) return termos;

        string? anterior = null;
        foreach (Match m in Token().Matches(SemAcento(texto.ToLowerInvariant())))
        {
            var t = m.Value.TrimEnd('.');
            if (t.Length == 0) continue;
            if (t.Length == 1 && !LetrasValidas.Contains(t)) { anterior = null; continue; }
            t = Sinonimos.GetValueOrDefault(t, t);

            if (anterior is not null && termos.Count > 0 && termos[^1] == anterior && Compostos.TryGetValue((anterior, t), out var composto))
            {
                termos[^1] = composto;
                anterior = null;
                continue;
            }

            anterior = t;
            if (removerStopwords && Stopwords.Contains(t)) continue;
            termos.Add(t);
        }
        return termos;
    }

    public static string SemAcento(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    // Começa com letra/dígito (ou "." colado, como em ".net") e pode conter # + . no meio ou no fim (c#, c++, node.js).
    [GeneratedRegex(@"(?<![\p{L}\p{N}])\.?[\p{L}\p{N}][\p{L}\p{N}#+.]*")]
    private static partial Regex Token();
}
