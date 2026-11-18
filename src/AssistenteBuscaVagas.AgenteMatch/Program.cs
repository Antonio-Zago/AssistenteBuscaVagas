using A2A;
using A2A.AspNetCore;
using Anthropic;
using AssistenteBuscaVagas.AgenteMatch;
using AssistenteBuscaVagas.AgenteMatch.Funil;
using AssistenteBuscaVagas.AgenteMatch.Llm;
using AssistenteBuscaVagas.Comum.A2A;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.Configure<FunilOptions>(builder.Configuration.GetSection("Funil"));
builder.Services.Configure<AvaliacaoOptions>(builder.Configuration.GetSection("Avaliacao"));
builder.Services.AddHttpClient(McpVagasCliente.NomeHttpClient);

// Claude via SDK oficial. A chave vem de ANTHROPIC_API_KEY, preenchida pelo AppHost a partir de um parâmetro secreto.
builder.Services.AddSingleton(_ => new AnthropicClient());
builder.Services.AddSingleton<AvaliadorClaude>();
builder.Services.AddSingleton<ServicoMatch>();

// A2A (fase 4.5): Agent Card + endpoint JSON-RPC. A URL é lógica; o service discovery do Aspire a resolve.
builder.Services.AddA2AAgent<AgenteMatchA2A>(CartaoA2A.Criar(
    "agente_match",
    "Compara as vagas coletadas com o perfil do usuário e recomenda as que mais combinam, com justificativa e lacunas de skills.",
    builder.Configuration["A2A:Url"] ?? "http://agente-match/a2a",
    AgenteMatchA2A.Lista));

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => "Agente de match. GET /match/funil (sem LLM), POST /match/recomendacoes (com LLM), " +
                      "/avaliacao/* (rótulos e precision@5), A2A em /a2a e card em /.well-known/agent-card.json.");

app.MapA2A("/a2a");
app.MapWellKnownAgentCard(app.Services.GetRequiredService<AgentCard>());

// ---------------- Funil ----------------

app.MapGet("/match/funil", (int? limite, int? dias, bool? somenteNovas, ServicoMatch servico, CancellationToken ct) =>
    ExecutarAsync(async () => Results.Ok(await servico.FunilAsync(limite, dias, somenteNovas == true, ct))));

// somenteNovas=true ignora vagas já mostradas em resumos diários (é o que o worker usa).
app.MapPost("/match/recomendacoes", (int? dias, bool? somenteNovas, ServicoMatch servico, CancellationToken ct) =>
    ExecutarAsync(async () => Results.Ok(await servico.RecomendarAsync(dias, somenteNovas == true, ct))));

// ---------------- Conjunto de avaliação ----------------

app.MapGet("/avaliacao/rotulos", async (IHttpClientFactory http, ILoggerFactory logs, CancellationToken ct) =>
{
    await using var mcp = await McpVagasCliente.ConectarAsync(http, logs, ct);
    var rotulos = await mcp.ListarRotulosAsync(ct);
    return Results.Ok(new { total = rotulos.Count, relevantes = rotulos.Count(r => r.Relevante), rotulos });
});

app.MapPut("/avaliacao/rotulos/{vagaId:int}", async (int vagaId, RotuloEntrada entrada, IHttpClientFactory http, ILoggerFactory logs, CancellationToken ct) =>
{
    await using var mcp = await McpVagasCliente.ConectarAsync(http, logs, ct);
    try
    {
        return Results.Ok(await mcp.RotularAsync(vagaId, entrada.Relevante, entrada.Observacao, ct));
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
    }
});

app.MapDelete("/avaliacao/rotulos/{vagaId:int}", async (int vagaId, IHttpClientFactory http, ILoggerFactory logs, CancellationToken ct) =>
{
    await using var mcp = await McpVagasCliente.ConectarAsync(http, logs, ct);
    return await mcp.RemoverRotuloAsync(vagaId, ct) ? Results.NoContent() : Results.NotFound();
});

app.MapGet("/avaliacao/pendentes", (int? limite, int? dias, ServicoMatch servico, CancellationToken ct) =>
    ExecutarAsync(async () => Results.Ok(await servico.PendentesAsync(limite, dias, ct))));

// usarLlm=false é grátis.
app.MapPost("/avaliacao/executar", (bool? usarLlm, ServicoMatch servico, CancellationToken ct) =>
    ExecutarAsync(async () => Results.Ok(await servico.AvaliarAsync(usarLlm != false, ct))));

app.Run();

static async Task<IResult> ExecutarAsync(Func<Task<IResult>> acao)
{
    try
    {
        return await acao();
    }
    catch (ErroDeUsoException ex)
    {
        return Results.Problem(ex.Message, statusCode: ex.Status);
    }
    catch (PerfilIncompletoException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
    }
    catch (Anthropic.Exceptions.AnthropicUnauthorizedException)
    {
        return Results.Problem("Chave da Anthropic ausente ou inválida. Defina o parâmetro anthropic-api-key no AppHost.",
            statusCode: StatusCodes.Status502BadGateway);
    }
}

public sealed record RotuloEntrada(bool Relevante, string? Observacao);
