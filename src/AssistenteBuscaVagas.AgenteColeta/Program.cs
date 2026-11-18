using A2A;
using A2A.AspNetCore;
using Anthropic;
using AssistenteBuscaVagas.Comum.A2A;
using AssistenteBuscaVagas.AgenteColeta.Agente;
using AssistenteBuscaVagas.AgenteColeta.Armazenamento;
using AssistenteBuscaVagas.AgenteColeta.Configuracao;
using AssistenteBuscaVagas.AgenteColeta.Fontes;
using AssistenteBuscaVagas.AgenteColeta.Web;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<ColetaOptions>(builder.Configuration.GetSection("Coleta"));
builder.Services.Configure<RemotiveOptions>(builder.Configuration.GetSection("Fontes:Remotive"));
builder.Services.Configure<FirecrawlOptions>(builder.Configuration.GetSection("Firecrawl"));
builder.Services.AddSingleton<OrcamentoDiario>();

// Fontes e web. A resiliência (retries, timeout) vem do ServiceDefaults para todo HttpClient.
builder.Services.AddHttpClient<RemotiveFonte>(c => c.BaseAddress = new Uri("https://remotive.com/"));
builder.Services.AddHttpClient<RobotsTxt>(c =>
{
    c.DefaultRequestHeaders.UserAgent.ParseAdd($"{RobotsTxt.UserAgent}/0.1");
    c.Timeout = TimeSpan.FromSeconds(10);
});
// Firecrawl sem a resiliência padrão: ela corta em 30 s (a busca às vezes demora mais) e repete o POST quando
// falha, e cada repetição é outra busca ou outro scrape cobrado. Aqui vale um timeout maior e nenhuma repetição
// automática; quem decide tentar de novo (com outra consulta) é o agente.
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers ainda é marcada como experimental no pacote.
builder.Services.AddHttpClient<FirecrawlCliente>((sp, c) =>
{
    var fc = sp.GetRequiredService<IOptions<FirecrawlOptions>>().Value;
    c.BaseAddress = new Uri(fc.UrlBase);
    c.Timeout = TimeSpan.FromSeconds(Math.Max(10, fc.TimeoutSegundos));
    if (!string.IsNullOrWhiteSpace(fc.ApiKey))
        c.DefaultRequestHeaders.Authorization = new("Bearer", fc.ApiKey);
}).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
builder.Services.AddHttpClient(McpVagasCliente.NomeHttpClient);

// LLM: Claude via SDK oficial da Anthropic, exposto como IChatClient (Microsoft.Extensions.AI).
// A chave vem da variável ANTHROPIC_API_KEY, que o AppHost preenche a partir de um parâmetro secreto.
// O cliente beta permite pedir fallback no servidor caso o modelo recuse por política.
builder.Services.AddSingleton(_ => new AnthropicClient());
builder.Services
    .AddChatClient(sp => sp.GetRequiredService<AnthropicClient>().Beta
        .AsIChatClient(sp.GetRequiredService<IOptions<ColetaOptions>>().Value.Modelo))
    .UseFunctionInvocation(configure: f =>
        f.MaximumIterationsPerRequest = builder.Configuration.GetValue("Coleta:MaximoIteracoes", 15))
    .UseOpenTelemetry(configure: o => o.EnableSensitiveData = false)
    .UseLogging();

builder.Services.AddScoped<AgenteColetaServico>();

// A2A (fase 4.5): Agent Card + endpoint JSON-RPC. A URL é lógica; o service discovery do Aspire a resolve.
builder.Services.AddA2AAgent<AgenteColetaA2A>(CartaoA2A.Criar(
    "agente_coleta",
    "Busca vagas de emprego novas em fontes públicas (Remotive e sites de empresas via web) e grava no banco.",
    builder.Configuration["A2A:Url"] ?? "http://agente-coleta/a2a",
    AgenteColetaA2A.Lista));

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => "Agente de coleta (Claude + Firecrawl + Remotive). POST /coletar para executar. A2A em /a2a.");

app.MapA2A("/a2a");
app.MapWellKnownAgentCard(app.Services.GetRequiredService<AgentCard>());

// Dispara uma execução. O worker diário (fase 5) e o supervisor (fase 4.5) chamarão este endpoint.
app.MapPost("/coletar", async (PedidoColeta? pedido, AgenteColetaServico agente, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await agente.ExecutarAsync(pedido?.Pedido, ct));
    }
    catch (Anthropic.Exceptions.AnthropicUnauthorizedException)
    {
        return Results.Problem("Chave da Anthropic ausente ou inválida. Defina o parâmetro anthropic-api-key no AppHost.",
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (HttpRequestException ex) when (ex.Message.StartsWith("Firecrawl"))
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
});

// Fluxo fixo sem LLM (não gasta tokens nem créditos): só a Remotive.
app.MapPost("/coletar/remotive", async (AgenteColetaServico agente, CancellationToken ct) =>
    Results.Ok(await agente.ColetarRemotiveAsync(ct)));

app.Run();

public sealed record PedidoColeta(string? Pedido);
