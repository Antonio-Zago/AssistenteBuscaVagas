using Microsoft.Extensions.Configuration;

namespace AssistenteBuscaVagas.WorkerDiario.Tests;

public class ConfiguracaoTests
{
    /// <summary>Lê o appsettings.json real do worker, do jeito que o host lê.</summary>
    private static AgendaOptions LerAgendaDoAppsettings()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AssistenteBuscaVagas.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(dir.FullName, "src", "AssistenteBuscaVagas.WorkerDiario", "appsettings.json"))
            .Build();
        return config.GetSection("Agenda").Get<AgendaOptions>()!;
    }

    [Fact]
    public void AppsettingsAgendaSegundaEQuintaAsNove()
    {
        var agenda = LerAgendaDoAppsettings();

        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Thursday], agenda.DiasDaSemana);
        Assert.Equal("09:00", agenda.Horario);
        Assert.Equal("America/Sao_Paulo", agenda.FusoHorario);
        Assert.Equal(2, agenda.MaximoTentativas);
        Assert.True(agenda.ColetaWeb);
    }

    [Fact]
    public void FusoDeSaoPauloExisteNestaMaquina() =>
        Assert.Equal(TimeSpan.FromHours(-3), Agenda.Fuso("America/Sao_Paulo").GetUtcOffset(new DateTime(2026, 11, 16)));
}
