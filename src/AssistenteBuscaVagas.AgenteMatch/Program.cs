using Anthropic;
using AssistenteBuscaVagas.AgenteMatch.Funil;
using AssistenteBuscaVagas.AgenteMatch.Llm;
using Microsoft.Extensions.Options;
using Rotulo = AssistenteBuscaVagas.AgenteMatch.Llm.Rotulo;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.Configure<FunilOptions>(builder.Configuration.GetSection("Funil"));
builder.Services.Configure<AvaliacaoOptions>(builder.Configuration.GetSection("Avaliacao"));
builder.Services.AddHttpClient(McpVagasCliente.NomeHttpClient);

// Claude via SDK oficial. A chave vem de ANTHROPIC_API_KEY, preenchida pelo AppHost a partir de um parâmetro secreto.
builder.Services.AddSingleton(_ => new AnthropicClient());
builder.Services.AddSingleton<AvaliadorClaude>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => "Agente de match. GET /match/funil (sem LLM), POST /match/recomendacoes (com LLM), " +
                      "/avaliacao/* (rótulos e precision@5).");

// ---------------- Funil ----------------

// Fase 3: só filtros rígidos + BM25. Grátis.
app.MapGet("/match/funil", async (int? limite, int? dias, IOptions<FunilOptions> opcoes, IHttpClientFactory http, ILoggerFactory logs, CancellationToken ct) =>
{
    var o = opcoes.Value;
    await using var mcp = await McpVagasCliente.ConectarAsync(http, logs, ct);
    if (await mcp.ObterPerfilAsync(ct) is not { } perfil)
        return SemPerfil();

    var funil = new FunilMatch(o);
    var vagas = await LerJanelaAsync(mcp, funil, perfil, dias, o, ct);
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

// Fase 4: o funil completo, com o Claude avaliando o top do BM25. PAGO, mas avaliações guardadas não são refeitas.
app.MapPost("/match/recomendacoes", async (
    int? dias, IOptions<FunilOptions> funilOpcoes, IOptions<AvaliacaoOptions> avaliacaoOpcoes, AvaliadorClaude avaliador,
    IHttpClientFactory http, ILoggerFactory logs, CancellationToken ct) =>
{
    await using var mcp = await McpVagasCliente.ConectarAsync(http, logs, ct);
    if (await mcp.ObterPerfilAsync(ct) is not { } perfil)
        return SemPerfil();

    var funil = new FunilMatch(funilOpcoes.Value);
    var vagas = await LerJanelaAsync(mcp, funil, perfil, dias, funilOpcoes.Value, ct);
    var recomendador = new Recomendador(funil, avaliador, mcp, avaliacaoOpcoes.Value, app.Logger);

    return await ComTratamentoAsync(async () =>
    {
        var r = await recomendador.ExecutarAsync(perfil, vagas, ct);
        app.Logger.LogInformation("Recomendações: {Etapas}; custo estimado US$ {Custo}",
            string.Join(" → ", r.Etapas.Select(e => $"{e.Etapa}: {e.Vagas}")), r.Custo.CustoEstimadoUsd);
        return Results.Ok(r);
    });
});

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

// Vagas ainda sem rótulo, na ordem em que vale a pena rotular: primeiro as que o BM25 ranqueia (são as que
// disputam o top 5), depois as que passam nos filtros sem pontuar, por fim as descartadas pelos filtros.
app.MapGet("/avaliacao/pendentes", async (int? limite, int? dias, IOptions<FunilOptions> opcoes, IHttpClientFactory http, ILoggerFactory logs, CancellationToken ct) =>
{
    var o = opcoes.Value;
    await using var mcp = await McpVagasCliente.ConectarAsync(http, logs, ct);
    if (await mcp.ObterPerfilAsync(ct) is not { } perfil)
        return SemPerfil();

    var funil = new FunilMatch(o);
    var rotuladas = (await mcp.ListarRotulosAsync(ct)).Select(r => r.VagaId).ToHashSet();
    var vagas = (await mcp.ListarVagasAsync(null, Math.Clamp(dias ?? o.DiasMaximos, 1, 365), o.MaximoVagasLidas, ct))
        .Where(v => !rotuladas.Contains(v.Id)).ToList();

    try
    {
        var ranking = funil.Executar(perfil, vagas, 100).Recomendacoes.ToDictionary(r => r.VagaId, r => r.Posicao);
        var filtros = funil.Filtros(perfil);
        var pendentes = vagas
            .Select(v => new
            {
                vagaId = v.Id,
                v.Titulo,
                v.Empresa,
                v.Local,
                v.Remoto,
                v.Url,
                posicaoBm25 = ranking.TryGetValue(v.Id, out var p) ? p : (int?)null,
                descartadaPor = filtros.Avaliar(v)?.Detalhe,
            })
            .OrderBy(x => x.posicaoBm25 ?? int.MaxValue)
            .ThenBy(x => x.descartadaPor is null ? 0 : 1)
            .ThenByDescending(x => x.vagaId)
            .Take(Math.Clamp(limite ?? 20, 1, 200))
            .ToList();
        return Results.Ok(new { jaRotuladas = rotuladas.Count, pendentes });
    }
    catch (PerfilIncompletoException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
    }
});

// Mede precision@5 sem LLM e com LLM sobre as vagas rotuladas. usarLlm=false é grátis.
app.MapPost("/avaliacao/executar", async (
    bool? usarLlm, IOptions<FunilOptions> funilOpcoes, IOptions<AvaliacaoOptions> avaliacaoOpcoes, AvaliadorClaude avaliador,
    IHttpClientFactory http, ILoggerFactory logs, CancellationToken ct) =>
{
    await using var mcp = await McpVagasCliente.ConectarAsync(http, logs, ct);
    if (await mcp.ObterPerfilAsync(ct) is not { } perfil)
        return SemPerfil();

    var rotulos = (await mcp.ListarRotulosAsync(ct)).Select(r => new Rotulo(r.VagaId, r.Relevante, r.Observacao)).ToList();
    if (rotulos.Count == 0)
        return Results.Problem("Nenhuma vaga rotulada. Use GET /avaliacao/pendentes e PUT /avaliacao/rotulos/{vagaId}.",
            statusCode: StatusCodes.Status422UnprocessableEntity);

    var vagas = await mcp.ListarVagasPorIdsAsync(rotulos.Select(r => r.VagaId).ToList(), ct);
    var funil = new FunilMatch(funilOpcoes.Value);
    var recomendador = usarLlm == false ? null : new Recomendador(funil, avaliador, mcp, avaliacaoOpcoes.Value, app.Logger);

    return await ComTratamentoAsync(async () =>
    {
        var r = await AvaliacaoFunil.ExecutarAsync(funil, recomendador, avaliacaoOpcoes.Value, perfil, vagas, rotulos, ct);
        app.Logger.LogInformation("Avaliação: {Rotuladas} rotuladas, P@5 sem LLM {Sem}, com LLM {Com}",
            r.VagasRotuladas, r.SemLlm.PrecisaoEm5, r.ComLlm?.PrecisaoEm5);
        return Results.Ok(r);
    });
});

app.Run();

// ---------------- Auxiliares ----------------

static IResult SemPerfil() =>
    Results.Problem("Perfil não cadastrado. Use o agente_perfil primeiro.", statusCode: StatusCodes.Status404NotFound);

// Quando a preferência é só remoto, o filtro já vai para a consulta ao banco.
static Task<IReadOnlyList<VagaParaMatch>> LerJanelaAsync(
    McpVagasCliente mcp, FunilMatch funil, PerfilCompleto perfil, int? dias, FunilOptions o, CancellationToken ct) =>
    mcp.ListarVagasAsync(funil.Filtros(perfil).SoRemotas ? true : null, Math.Clamp(dias ?? o.DiasMaximos, 1, 365), o.MaximoVagasLidas, ct);

static async Task<IResult> ComTratamentoAsync(Func<Task<IResult>> acao)
{
    try
    {
        return await acao();
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
