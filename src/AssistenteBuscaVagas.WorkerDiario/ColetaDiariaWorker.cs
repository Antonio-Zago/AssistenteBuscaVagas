namespace AssistenteBuscaVagas.WorkerDiario;

/// <summary>
/// Esqueleto do worker diário. Nas próximas fases: coletar vagas, executar o funil de match e notificar.
/// </summary>
public sealed class ColetaDiariaWorker(ILogger<ColetaDiariaWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Intervalo);

        do
        {
            logger.LogInformation("Execução diária iniciada (ainda sem coleta nem match).");
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
