using Anthropic;
using AssistenteBuscaVagas.Supervisor.Orquestracao;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.Configure<SupervisorOptions>(builder.Configuration.GetSection("Supervisor"));

builder.Services.AddHttpClient(ServicoSupervisor.ClienteRoteador);
builder.Services.AddHttpClient(ServicoSupervisor.ClienteMcp);

// Chamadas A2A podem levar minutos (o agente de coleta navega na web; o de match chama o Claude várias vezes).
// A resiliência padrão do ServiceDefaults corta em 30 s e repete pedidos que falham, inclusive POST: aqui isso
// repetiria ações pagas. Então este cliente fica sem ela, só com um timeout longo.
// (RemoveAllResilienceHandlers ainda é marcada como experimental no pacote; a alternativa seria reconfigurar
// tempo total, tentativas e circuit breaker do handler padrão só para este cliente.)
#pragma warning disable EXTEXP0001
builder.Services.AddHttpClient(ServicoSupervisor.ClienteA2A, c => c.Timeout = TimeSpan.FromMinutes(15))
    .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

// Claude via SDK oficial, exposto como IChatClient. A chave vem de ANTHROPIC_API_KEY (parâmetro secreto do AppHost).
// UseFunctionInvocation executa o laço: o Claude pede uma ferramenta, o código chama o agente e devolve o resultado.
builder.Services.AddSingleton(_ => new AnthropicClient());
builder.Services
    .AddChatClient(sp => sp.GetRequiredService<AnthropicClient>().Beta
        .AsIChatClient(sp.GetRequiredService<IOptions<SupervisorOptions>>().Value.Modelo))
    .UseFunctionInvocation(configure: f =>
    {
        f.MaximumIterationsPerRequest = builder.Configuration.GetValue("Supervisor:MaximoIteracoes", 8);
        // Ferramentas de agentes diferentes podem rodar juntas quando o Claude pede várias no mesmo turno.
        f.AllowConcurrentInvocation = true;
    })
    .UseOpenTelemetry(configure: o => o.EnableSensitiveData = false)
    .UseLogging();

builder.Services.AddScoped<ServicoSupervisor>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => "Supervisor: POST /conversa {\"mensagem\": \"...\"} (com Claude) e GET /conversa/rota?mensagem=... (só roteamento, grátis).");

// Mostra o roteamento sem chamar o Claude: o que o roteador achou e quais ferramentas seriam oferecidas.
app.MapGet("/conversa/rota", async (string? mensagem, ServicoSupervisor supervisor, CancellationToken ct) =>
    string.IsNullOrWhiteSpace(mensagem)
        ? Results.Problem("Informe a mensagem.", statusCode: StatusCodes.Status400BadRequest)
        : Results.Ok(await supervisor.PreverRotaAsync(mensagem, ct)));

// A conversa: um pedido em linguagem natural, uma resposta. Sem memória entre pedidos (fica para o frontend da fase 6).
app.MapPost("/conversa", async (PedidoConversa pedido, ServicoSupervisor supervisor, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(pedido.Mensagem) || pedido.Mensagem.Length > 4000)
        return Results.Problem("Informe a mensagem (até 4000 caracteres).", statusCode: StatusCodes.Status400BadRequest);
    try
    {
        return Results.Ok(await supervisor.ConversarAsync(pedido.Mensagem.Trim(), ct));
    }
    catch (Anthropic.Exceptions.AnthropicUnauthorizedException)
    {
        return Results.Problem("Chave da Anthropic ausente ou inválida. Defina o parâmetro anthropic-api-key no AppHost.",
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (HttpRequestException ex)
    {
        return Results.Problem($"Roteador indisponível: {ex.Message}", statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();

public sealed record PedidoConversa(string Mensagem);
