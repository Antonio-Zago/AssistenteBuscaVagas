using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace AssistenteBuscaVagas.AgentePerfil.Perfil;

/// <summary>
/// Extrai o texto do PDF do currículo localmente (PdfPig). Só o texto vai para o Claude: o arquivo em si
/// não sai da máquina e não é guardado. Nenhum trecho do conteúdo é escrito em log.
/// </summary>
public static class LeitorPdf
{
    public const int MaximoPaginas = 10;

    /// <summary>Abaixo disso o PDF provavelmente é uma imagem escaneada (sem camada de texto).</summary>
    public const int MinimoCaracteres = 200;

    public static string ExtrairTexto(Stream pdf)
    {
        PdfDocument documento;
        try
        {
            documento = PdfDocument.Open(pdf);
        }
        catch (Exception ex) when (ex is PdfDocumentFormatException or PdfDocumentEncryptedException or InvalidOperationException)
        {
            throw new CurriculoInvalidoException("Não foi possível abrir o arquivo como PDF (corrompido ou protegido por senha).");
        }

        using (documento)
        {
            if (documento.NumberOfPages > MaximoPaginas)
                throw new CurriculoInvalidoException($"O currículo tem {documento.NumberOfPages} páginas; o máximo é {MaximoPaginas}.");

            var texto = new StringBuilder();
            foreach (var pagina in documento.GetPages())
            {
                // ContentOrderTextExtractor respeita a ordem de leitura e as quebras de linha,
                // ao contrário de page.Text, que junta as letras sem espaços em muitos PDFs.
                texto.AppendLine(ContentOrderTextExtractor.GetText(pagina));
            }

            var resultado = texto.ToString().Trim();
            if (resultado.Length < MinimoCaracteres)
                throw new CurriculoInvalidoException(
                    "O PDF quase não tem texto. Se for uma imagem escaneada, exporte o currículo de novo a partir do editor de texto.");
            return resultado;
        }
    }
}
