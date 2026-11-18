using System.Text.Json;
using AssistenteBuscaVagas.Comum.A2A;

namespace AssistenteBuscaVagas.WorkerDiario.Tests;

public class AgendaTests
{
    private static readonly TimeZoneInfo SaoPaulo = Agenda.Fuso("America/Sao_Paulo");
    private static readonly TimeOnly NoveHoras = new(9, 0);
    private static readonly DayOfWeek[] SegundaEQuinta = [DayOfWeek.Monday, DayOfWeek.Thursday];

    // Novembro de 2026: dia 16 é segunda, 17 terça, 19 quinta. São Paulo é UTC-3 (sem horário de verão desde 2019).
    private static DateTimeOffset Sp(int dia, int hora, int minuto = 0) => new(2026, 11, dia, hora, minuto, 0, TimeSpan.FromHours(-3));
    private static ExecucaoAnterior Exec(int dia, string status, int tentativas = 1) => new(new DateOnly(2026, 11, dia), status, tentativas);

    private static Decisao Decidir(DateTimeOffset agora, params ExecucaoAnterior[] execucoes) =>
        Agenda.Decidir(agora, SaoPaulo, NoveHoras, SegundaEQuinta, execucoes, 2);

    [Fact]
    public void Datas() => Assert.Equal(DayOfWeek.Monday, new DateOnly(2026, 11, 16).DayOfWeek);

    [Fact]
    public void SegundaAntesDasNoveNaoRodaPoisQuintaPassadaJaRodou() =>
        Assert.False(Decidir(Sp(16, 8, 59), Exec(12, "sucesso")).Executar);

    [Fact]
    public void SegundaDepoisDasNoveRoda() =>
        Assert.True(Decidir(Sp(16, 9), Exec(12, "sucesso")).Executar);

    [Fact]
    public void ComputadorDesligadoNaSegunda_RecuperaNaTerca()
    {
        var d = Decidir(Sp(17, 20), Exec(12, "sucesso"));
        Assert.True(d.Executar);
        Assert.Equal(Sp(16, 9), d.HorarioAtendido);
    }

    [Fact]
    public void RecuperacaoNaTercaAtendeOHorarioDeSegunda() =>
        // Rodou terça (recuperando segunda): quarta não roda de novo.
        Assert.False(Decidir(Sp(18, 10), Exec(17, "sucesso"), Exec(12, "sucesso")).Executar);

    [Fact]
    public void QuintaRodaMesmoComSegundaFeita() =>
        Assert.True(Decidir(Sp(19, 9, 5), Exec(16, "sucesso")).Executar);

    [Theory]
    [InlineData("sucesso", 1, false)]
    [InlineData("parcial", 1, false)]
    [InlineData("falha", 1, true)]
    [InlineData("falha", 2, false)]
    public void ResultadoDoHorario(string status, int tentativas, bool esperado) =>
        Assert.Equal(esperado, Decidir(Sp(16, 10), Exec(16, status, tentativas)).Executar);

    [Fact]
    public void TentativasContamPorHorarioNaoPorDia() =>
        // Falhou segunda e terça (1 vez cada): o horário de segunda já gastou as 2 tentativas.
        Assert.False(Decidir(Sp(18, 10), Exec(17, "falha"), Exec(16, "falha")).Executar);

    [Fact]
    public void SemDiasConfiguradosRodaTodoDia()
    {
        Assert.True(Agenda.Decidir(Sp(17, 10), SaoPaulo, NoveHoras, [], [Exec(16, "sucesso")], 2).Executar);
        Assert.Equal(Sp(18, 9), Agenda.ProximoHorario(Sp(17, 10), SaoPaulo, NoveHoras, []));
    }

    [Fact]
    public void ProximoHorario()
    {
        Assert.Equal(Sp(16, 9), Agenda.ProximoHorario(Sp(16, 7), SaoPaulo, NoveHoras, SegundaEQuinta));
        Assert.Equal(Sp(19, 9), Agenda.ProximoHorario(Sp(16, 10), SaoPaulo, NoveHoras, SegundaEQuinta));
        Assert.Equal(new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.FromHours(-3)), Agenda.ProximoHorario(Sp(19, 9, 1), SaoPaulo, NoveHoras, SegundaEQuinta));
    }

    [Fact]
    public void DiaLocalNaoEhODiaEmUtc()
    {
        // 23:30 em São Paulo do dia 16 já é dia 17 em UTC.
        var agora = Sp(16, 23, 30);
        Assert.Equal(17, agora.UtcDateTime.Day);
        Assert.Equal(new DateOnly(2026, 11, 16), Agenda.DiaLocal(agora, SaoPaulo));
    }
}

public class ResumoTests
{
    private static RespostaHabilidade RespostaMatch(params (int Id, int Nota)[] vagas) => RespostaHabilidade.Ok("ok", new
    {
        recomendacoes = vagas.Select(v => new
        {
            vagaId = v.Id, titulo = $"Vaga {v.Id}", empresa = "Empresa", local = "Brazil", url = $"https://e.com/{v.Id}",
            nota = v.Nota, justificativa = "porque sim", lacunas = new[] { "Docker" }, impeditivos = Array.Empty<string>(),
        }),
        custo = new { custoEstimadoUsd = 0.12m },
    });

    [Fact]
    public void SoEntramVagasComNotaMinima()
    {
        var (vagas, abaixo, custo) = ExecucaoDiariaServico.LerRecomendacoes(RespostaMatch((1, 90), (2, 60), (3, 45)), 60);

        Assert.Equal([1, 2], vagas.Select(v => v.VagaId));
        Assert.Equal(1, abaixo);
        Assert.Equal(0.12m, custo);
    }

    [Fact]
    public void RecomendacaoComErroNaoTemVagas()
    {
        var (vagas, abaixo, _) = ExecucaoDiariaServico.LerRecomendacoes(RespostaHabilidade.Falha("sem perfil"), 60);
        Assert.Empty(vagas);
        Assert.Equal(0, abaixo);
        Assert.Empty(ExecucaoDiariaServico.LerRecomendacoes(null, 60).Vagas);
    }

    [Fact]
    public void TextoDoResumo()
    {
        var (vagas, abaixo, custo) = ExecucaoDiariaServico.LerRecomendacoes(RespostaMatch((1, 90)), 60);
        var resumo = new ResumoDiario("2026-11-16", [new("coleta Remotive", true, "3 novas", 10), new("coleta web", false, "timeout", 5)], vagas, abaixo, custo, "");

        var texto = ExecucaoDiariaServico.Formatar(resumo, "parcial", 60);

        Assert.StartsWith("Vagas do dia 16/11/2026", texto);
        Assert.Contains("1. Vaga 1 — Empresa (Brazil) · nota 90", texto);
        Assert.Contains("Falta: Docker", texto);
        Assert.Contains("https://e.com/1", texto);
        Assert.Contains("coleta web: falhou (timeout)", texto);
    }

    [Fact]
    public void DiaSemVagaBoaDizIsso()
    {
        var resumo = new ResumoDiario("2026-11-16", [], [], 3, 0, "");
        Assert.Contains("Nenhuma vaga nova com nota 60 ou mais hoje (3 avaliadas abaixo disso)", ExecucaoDiariaServico.Formatar(resumo, "sucesso", 60));
    }

    [Fact]
    public void ResumoSobreviveAoBancoEVolta()
    {
        // O agendador relê "vagasRecomendadas" do resumo gravado para não perder as do dia num segundo disparo.
        var vaga = new VagaDoDia(1, "T", "E", null, 80, "j", ["x"], [], "https://e.com/1");
        var json = JsonSerializer.SerializeToElement(new ResumoDiario("2026-11-16", [], [vaga], 0, 0, "t"), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var lidas = json.GetProperty("vagasRecomendadas").Deserialize<List<VagaDoDia>>(new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var lida = Assert.Single(lidas!);
        Assert.Equal(vaga with { Lacunas = [], Impeditivos = [] }, lida with { Lacunas = [], Impeditivos = [] });
        Assert.Equal(vaga.Lacunas, lida.Lacunas);
    }
}
