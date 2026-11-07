using AssistenteBuscaVagas.WorkerDiario;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHostedService<ColetaDiariaWorker>();

var app = builder.Build();

// O worker não tem API própria: o host web existe só para expor /health e /alive.
app.MapDefaultEndpoints();

app.MapGet("/", () => "Worker diário (coleta + match)");

app.Run();
