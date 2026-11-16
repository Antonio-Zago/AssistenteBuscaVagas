using AssistenteBuscaVagas.Roteador.Catalogo;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<RoteadorOptions>(builder.Configuration.GetSection("Roteador"));
builder.Services.AddHttpClient(Descoberta.NomeHttpClient);
builder.Services.AddSingleton<Descoberta>();
builder.Services.AddSingleton<Catalogo>();
builder.Services.AddSingleton<AtualizadorCatalogo>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AtualizadorCatalogo>());

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => "Roteador: descobre os Agent Cards (A2A) e as ferramentas MCP e indexa tudo com BM25. " +
                      "GET /roteador/capacidades, GET /roteador/buscar?q=..., POST /roteador/atualizar.");

// Tudo o que o sistema sabe fazer, e o estado da última descoberta (inclusive agentes que não responderam).
app.MapGet("/roteador/capacidades", (Catalogo catalogo) =>
{
    var atual = catalogo.Atual;
    return Results.Ok(new { estado = atual.Estado, capacidades = atual.Indice.Todas });
});

// As k capacidades mais parecidas com o pedido, com os termos que pontuaram. É o que o supervisor consulta.
app.MapGet("/roteador/buscar", (string? q, int? k, Catalogo catalogo) =>
{
    if (string.IsNullOrWhiteSpace(q))
        return Results.Problem("Informe o pedido em q.", statusCode: StatusCodes.Status400BadRequest);
    return Results.Ok(catalogo.Atual.Indice.Buscar(q, k ?? 6));
});

// Redescobre agora (ex.: depois de mudar o card de um agente).
app.MapPost("/roteador/atualizar", async (AtualizadorCatalogo atualizador, CancellationToken ct) =>
    await atualizador.AtualizarAsync(ct) is { } estado ? Results.Ok(estado) : Results.Problem("Falha ao atualizar o catálogo."));

app.Run();
