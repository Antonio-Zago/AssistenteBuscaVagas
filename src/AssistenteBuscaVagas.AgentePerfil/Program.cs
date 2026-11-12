using Anthropic;
using AssistenteBuscaVagas.AgentePerfil.Perfil;
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

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => "Agente de perfil. POST /perfil/curriculo (PDF), PUT /perfil/preferencias, GET /perfil.");

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
