using AssistenteBuscaVagas.WorkerDiario;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<AgendaOptions>(builder.Configuration.GetSection("Agenda"));
builder.Services.AddHttpClient(McpVagasCliente.NomeHttpClient);

// A coleta na web leva minutos e a recomendação chama o Claude várias vezes. A resiliência padrão corta em 30 s
// e repete pedidos que falham (inclusive POST), o que repetiria ações pagas. Este cliente fica sem ela.
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers ainda é marcada como experimental no pacote.
builder.Services.AddHttpClient(ClienteAgentes.NomeHttpClient, c => c.Timeout = TimeSpan.FromMinutes(20))
    .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

builder.Services.AddSingleton<ClienteAgentes>();
builder.Services.AddSingleton<INotificador, NotificadorLog>();
builder.Services.AddSingleton<ExecucaoDiariaServico>();
builder.Services.AddSingleton<Agendador>();
builder.Services.AddHostedService<ColetaDiariaWorker>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", async (Agendador agendador, CancellationToken ct) => Results.Ok(await agendador.StatusAsync(ct)));

// Histórico: o que rodou em cada dia, com o resumo (vagas recomendadas, passos, custo).
app.MapGet("/execucoes", async (int? limite, IHttpClientFactory http, ILoggerFactory logs, CancellationToken ct) =>
{
    await using var mcp = await McpVagasCliente.ConectarAsync(http, logs, ct);
    return Results.Ok(await mcp.ListarExecucoesAsync(Math.Clamp(limite ?? 7, 1, 60), ct));
});

// Roda agora, fora do horário. PAGO (coleta na web + Claude). Só vagas ainda não recomendadas entram.
app.MapPost("/execucoes/agora", async (Agendador agendador, CancellationToken ct) =>
    await agendador.RodarAgoraAsync(ct) is { } resultado
        ? Results.Ok(resultado)
        : Results.Problem("Já existe uma execução em andamento.", statusCode: StatusCodes.Status409Conflict));

app.Run();
