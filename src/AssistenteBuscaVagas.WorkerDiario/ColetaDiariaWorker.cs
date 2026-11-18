using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AssistenteBuscaVagas.WorkerDiario;

public sealed record StatusWorker(bool AgendaAtiva, bool EmExecucao, string Horario, IReadOnlyList<DayOfWeek> DiasDaSemana,
    string FusoHorario, string Situacao, DateTimeOffset? ProximoHorario, string UltimaDecisao, ExecucaoRegistrada? UltimaExecucao);

/// <summary>
/// Decide quando rodar e garante uma execução por vez (a agendada e a disparada à mão nunca se sobrepõem).
/// O estado ("o último horário agendado já foi atendido?") fica no banco, não na memória: reiniciar o AppHost
/// não faz a mesma rodada acontecer duas vezes.
/// </summary>
public sealed class Agendador(
    ExecucaoDiariaServico servico,
    IHttpClientFactory http,
    ILoggerFactory logs,
    IOptions<AgendaOptions> options,
    TimeProvider relogio)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _trava = new(1, 1);
    private readonly ILogger _log = logs.CreateLogger<Agendador>();

    public string UltimaDecisao { get; private set; } = "ainda não verificou";

    private TimeZoneInfo Fuso => Agenda.Fuso(options.Value.FusoHorario);
    private TimeOnly Horario => TimeOnly.ParseExact(options.Value.Horario, "HH:mm");
    private DayOfWeek[] Dias => options.Value.DiasDaSemana;

    /// <summary>Chamado pelo laço do worker: roda se o último horário agendado ainda não foi atendido.</summary>
    public async Task VerificarAsync(CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var execucoes = await ExecucoesRecentesAsync(ct);
        var decisao = Decidir(agora, execucoes);
        UltimaDecisao = $"{agora:O}: {decisao.Motivo}";

        if (decisao.Executar)
        {
            _log.LogInformation("Agenda: {Motivo}", decisao.Motivo);
            await RodarAsync(agora, execucoes, ct);
        }
    }

    /// <summary>Disparo manual, fora da agenda. Devolve null se já houver uma execução em andamento.</summary>
    public async Task<ResultadoExecucao?> RodarAgoraAsync(CancellationToken ct) =>
        await RodarAsync(relogio.GetUtcNow(), await ExecucoesRecentesAsync(ct), ct);

    public async Task<StatusWorker> StatusAsync(CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var execucoes = await ExecucoesRecentesAsync(ct);
        var decisao = Decidir(agora, execucoes);
        var o = options.Value;

        var situacao = !o.Ativa ? "agenda desligada: só roda por POST /execucoes/agora"
            : decisao.Executar ? $"pendente: roda na próxima verificação ({decisao.Motivo})"
            : decisao.Motivo;
        return new(o.Ativa, _trava.CurrentCount == 0, o.Horario, Dias, o.FusoHorario, situacao,
            Agenda.ProximoHorario(agora, Fuso, Horario, Dias), UltimaDecisao, execucoes.FirstOrDefault());
    }

    private Decisao Decidir(DateTimeOffset agora, IReadOnlyList<ExecucaoRegistrada> execucoes) =>
        Agenda.Decidir(agora, Fuso, Horario, Dias,
            execucoes.Select(e => new ExecucaoAnterior(DateOnly.ParseExact(e.Data, "yyyy-MM-dd"), e.Status, e.Tentativas)).ToList(),
            options.Value.MaximoTentativas);

    private async Task<ResultadoExecucao?> RodarAsync(DateTimeOffset agora, IReadOnlyList<ExecucaoRegistrada> execucoes, CancellationToken ct)
    {
        if (!await _trava.WaitAsync(0, ct))
        {
            _log.LogInformation("Já existe uma execução em andamento.");
            return null;
        }
        try
        {
            // A execução é gravada no dia em que roda. Se hoje já rodou (disparo manual de novo),
            // as vagas já mostradas hoje continuam no resumo do dia.
            var dia = Agenda.DiaLocal(agora, Fuso);
            var hoje = execucoes.FirstOrDefault(e => e.Data == dia.ToString("yyyy-MM-dd"));
            var jaMostradas = hoje is not null && hoje.Resumo.TryGetProperty("vagasRecomendadas", out var v)
                ? v.Deserialize<List<VagaDoDia>>(Json) ?? []
                : [];
            return await servico.ExecutarAsync(dia, (hoje?.Tentativas ?? 0) + 1, jaMostradas, ct);
        }
        finally
        {
            _trava.Release();
        }
    }

    private async Task<IReadOnlyList<ExecucaoRegistrada>> ExecucoesRecentesAsync(CancellationToken ct)
    {
        await using var mcp = await McpVagasCliente.ConectarAsync(http, logs, ct);
        return await mcp.ListarExecucoesAsync(8, ct);
    }
}

/// <summary>
/// O laço do worker: confere a cada <see cref="AgendaOptions.VerificarACadaMinutos"/> se é hora de rodar.
/// Conferir de tempos em tempos (em vez de dormir até o horário) é o que permite recuperar um horário perdido
/// quando o computador estava desligado.
/// </summary>
public sealed class ColetaDiariaWorker(Agendador agendador, IOptions<AgendaOptions> options, ILogger<ColetaDiariaWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Ativa)
        {
            logger.LogInformation("Agenda desligada (Agenda:Ativa = false): só roda por POST /execucoes/agora.");
            return;
        }

        // Dá um tempo para os agentes terminarem de subir junto com o AppHost.
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, options.Value.VerificarACadaMinutos)));
        do
        {
            try
            {
                await agendador.VerificarAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Ex.: mcp_vagas fora do ar. Tenta de novo na próxima verificação.
                logger.LogError(ex, "Falha ao verificar a agenda");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
