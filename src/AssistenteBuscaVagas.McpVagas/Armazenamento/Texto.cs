using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AssistenteBuscaVagas.McpVagas.Armazenamento;

public static partial class Texto
{
    /// <summary>Converte a descrição em HTML para texto puro, preservando quebras de parágrafo.</summary>
    public static string HtmlParaTexto(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";

        var texto = QuebraDeBloco().Replace(html, "\n");
        texto = Tag().Replace(texto, " ");
        texto = WebUtility.HtmlDecode(texto);
        texto = EspacosNaLinha().Replace(texto, " ");
        texto = EspacoEmVoltaDaQuebra().Replace(texto, "\n");
        texto = LinhasEmBranco().Replace(texto, "\n\n");
        return texto.Trim();
    }

    /// <summary>
    /// Hash de empresa + título normalizados (minúsculas, sem acento, sem pontuação).
    /// Primeira tentativa de deduplicação; a fase 8 decide se basta.
    /// </summary>
    public static string HashDedup(string empresa, string titulo)
    {
        var chave = $"{Normalizar(empresa)}|{Normalizar(titulo)}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(chave)));
    }

    public static string Normalizar(string s)
    {
        var semAcento = new StringBuilder(s.Length);
        foreach (var c in s.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                semAcento.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }
        return EspacosNaLinha().Replace(semAcento.ToString(), " ").Trim();
    }

    [GeneratedRegex(@"<\s*(br|/p|/li|/h[1-6]|/div)\s*/?\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex QuebraDeBloco();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"[ \t ]+")]
    private static partial Regex EspacosNaLinha();

    [GeneratedRegex(@" *\n *")]
    private static partial Regex EspacoEmVoltaDaQuebra();

    [GeneratedRegex(@"\s*\n\s*(\n\s*)+")]
    private static partial Regex LinhasEmBranco();
}
