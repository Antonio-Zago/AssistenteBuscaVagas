using AssistenteBuscaVagas.AgenteMatch.Funil;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.Configure<FunilOptions>(builder.Configuration.GetSection("Funil"));
builder.Services.AddHttpClient(McpVagasCliente.NomeHttpClient);

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => "Agente de match. GET /match/funil: funil sem LLM (filtros rígidos + BM25).");

// Fase 3: o funil inteiro em código, sem LLM. Lê o perfil e as vagas do mcp_vagas, filtra, ranqueia com BM25
// e devolve as N melhores com a justificativa montada a partir dos termos que pontuaram.
app.MapGet("/match/funil", async (
    int? limite, int? dias, IOptions<FunilOptions> opcoes, IHttpClientFactory http, ILoggerFactory logs, CancellationToken ct) =>
{
    var o = opcoes.Value;
    await using var mcp = await McpVagasCliente.ConectarAsync(http, logs, ct);

    var perfil = await mcp.ObterPerfilAsync(ct);
    if (perfil is null)
        return Results.Problem("Perfil não cadastrado. Use o agente_perfil primeiro.", statusCode: StatusCodes.Status404NotFound);

    var funil = new FunilMatch(o);
    var soRemotas = funil.Filtros(perfil).SoRemotas;
    var vagas = await mcp.ListarVagasAsync(soRemotas ? true : null, Math.Clamp(dias ?? o.DiasMaximos, 1, 365), o.MaximoVagasLidas, ct);

    try
    {
        var resultado = funil.Executar(perfil, vagas, limite);
        // Só contagens no log: o perfil é dado pessoal.
        app.Logger.LogInformation("Funil: {Etapas}", string.Join(" → ", resultado.Etapas.Select(e => $"{e.Etapa}: {e.Vagas}")));
        return Results.Ok(resultado);
    }
    catch (PerfilIncompletoException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
    }
});

app.Run();
