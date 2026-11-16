using A2A;
using A2A.AspNetCore;
using Anthropic;
using AssistenteBuscaVagas.AgentePerfil;
using AssistenteBuscaVagas.AgentePerfil.Perfil;
using AssistenteBuscaVagas.Comum.A2A;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

const long TamanhoMaximoPdf = 5 * 1024 * 1024;
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = TamanhoMaximoPdf);

builder.Services.Configure<ExtracaoOptions>(builder.Configuration.GetSection("Extracao"));
builder.Services.AddHttpClient(McpVagasCliente.NomeHttpClient);

// Claude via SDK oficial. A chave vem de ANTHROPIC_API_KEY, preenchida pelo AppHost a partir de um parâmetro secreto.
builder.Services.AddSingleton(_ => new AnthropicClient());
builder.Services.AddSingleton<ExtratorCurriculo>();

// A2A (fase 4.5): Agent Card + endpoint JSON-RPC. A URL é lógica; o service discovery do Aspire a resolve.
builder.Services.AddA2AAgent<AgentePerfilA2A>(CartaoA2A.Criar(
    "agente_perfil",
    "Guarda e mostra o perfil do usuário (currículo estruturado e preferências de busca de emprego).",
    builder.Configuration["A2A:Url"] ?? "http://agente-perfil/a2a",
    AgentePerfilA2A.Lista));

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => "Agente de perfil. POST /perfil/curriculo (PDF), PUT /perfil/preferencias, GET /perfil. A2A em /a2a.");

app.MapA2A("/a2a");
app.MapWellKnownAgentCard(app.Services.GetRequiredService<AgentCard>());

// Recebe o PDF, extrai o texto localmente, pede ao Claude o JSON estruturado e grava via salvar_perfil.
// O PDF e o texto ficam só em memória durante a requisição.
app.MapPost("/perfil/curriculo", async (
    IFormFile arquivo, ExtratorCurriculo extrator, IHttpClientFactory http, ILoggerFactory logs, CancellationToken ct) =>
{
    if (arquivo.Length == 0 || arquivo.Length > TamanhoMaximoPdf)
        return Results.Problem($"Envie um PDF de até {TamanhoMaximoPdf / 1024 / 1024} MB.", statusCode: StatusCodes.Status422UnprocessableEntity);

    try
    {
        string texto;
        await using (var pdf = arquivo.OpenReadStream())
        {
            // PdfPig precisa de um stream com seek; o do upload pode não ter.
            using var memoria = new MemoryStream();
            await pdf.CopyToAsync(memoria, ct);
            memoria.Position = 0;
            texto = LeitorPdf.ExtrairTexto(memoria);
        }

        var curriculo = await extrator.ExtrairAsync(texto, ct);

        await using var mcp = await McpVagasCliente.ConectarAsync(http, logs, ct);
        return Results.Ok(await mcp.SalvarAsync(curriculo, null, ct));
    }
    catch (CurriculoInvalidoException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
    }
    catch (PerfilRejeitadoException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
    }
    catch (Anthropic.Exceptions.AnthropicUnauthorizedException)
    {
        return Results.Problem("Chave da Anthropic ausente ou inválida. Defina o parâmetro anthropic-api-key no AppHost.",
            statusCode: StatusCodes.Status502BadGateway);
    }
})
// Endpoint de API chamado por ferramentas (curl, Scalar, frontend da fase 6), não por um formulário com token antiforgery.
.DisableAntiforgery();

app.MapPut("/perfil/preferencias", async (Preferencias preferencias, IHttpClientFactory http, ILoggerFactory logs, CancellationToken ct) =>
{
    try
    {
        await using var mcp = await McpVagasCliente.ConectarAsync(http, logs, ct);
        return Results.Ok(await mcp.SalvarAsync(null, preferencias, ct));
    }
    catch (PerfilRejeitadoException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
    }
});

app.MapGet("/perfil", async (IHttpClientFactory http, ILoggerFactory logs, CancellationToken ct) =>
{
    await using var mcp = await McpVagasCliente.ConectarAsync(http, logs, ct);
    return await mcp.ObterAsync(ct) is { } perfil ? Results.Ok(perfil) : Results.NotFound();
});

app.Run();
