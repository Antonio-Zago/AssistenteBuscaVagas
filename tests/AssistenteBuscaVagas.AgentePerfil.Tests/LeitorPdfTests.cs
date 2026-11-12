using AssistenteBuscaVagas.AgentePerfil.Perfil;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace AssistenteBuscaVagas.AgentePerfil.Tests;

public class LeitorPdfTests
{
    // Gera um PDF de verdade em memória, com uma linha de texto por item.
    private static MemoryStream CriarPdf(int paginas, params string[] linhas)
    {
        var builder = new PdfDocumentBuilder();
        var fonte = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var p = 0; p < paginas; p++)
        {
            var pagina = builder.AddPage(PageSize.A4);
            var y = 800.0;
            foreach (var linha in linhas)
            {
                pagina.AddText(linha, 11, new PdfPoint(40, y), fonte);
                y -= 16;
            }
        }
        return new MemoryStream(builder.Build());
    }

    private static readonly string[] LinhasCurriculo =
    [
        "Desenvolvedor Backend Senior",
        "Experiencia: Empresa X, 2020 ate hoje. APIs em C# e ASP.NET Core hospedadas no Azure.",
        "Experiencia: Empresa Y, 2016 a 2020. Sistemas em .NET Framework com SQL Server.",
        "Skills: C#, .NET, Azure, Docker, PostgreSQL, TDD.",
        "Idiomas: Ingles avancado.",
    ];

    [Fact]
    public void Extrai_texto_em_ordem_de_leitura()
    {
        using var pdf = CriarPdf(1, LinhasCurriculo);

        var texto = LeitorPdf.ExtrairTexto(pdf);

        Assert.Contains("Desenvolvedor Backend Senior", texto);
        Assert.Contains("C#, .NET, Azure", texto);
        Assert.True(texto.IndexOf("Empresa X", StringComparison.Ordinal) < texto.IndexOf("Empresa Y", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejeita_pdf_quase_sem_texto()
    {
        using var pdf = CriarPdf(1, "Curriculo");
        Assert.Throws<CurriculoInvalidoException>(() => LeitorPdf.ExtrairTexto(pdf));
    }

    [Fact]
    public void Rejeita_pdf_com_paginas_demais()
    {
        using var pdf = CriarPdf(LeitorPdf.MaximoPaginas + 1, LinhasCurriculo);
        Assert.Throws<CurriculoInvalidoException>(() => LeitorPdf.ExtrairTexto(pdf));
    }

    [Fact]
    public void Rejeita_arquivo_que_nao_e_pdf()
    {
        using var arquivo = new MemoryStream("isto não é um PDF"u8.ToArray());
        Assert.Throws<CurriculoInvalidoException>(() => LeitorPdf.ExtrairTexto(arquivo));
    }
}
