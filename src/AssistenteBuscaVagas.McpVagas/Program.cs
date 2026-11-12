using AssistenteBuscaVagas.McpVagas.Armazenamento;
using AssistenteBuscaVagas.McpVagas.Dados;
using AssistenteBuscaVagas.McpVagas.Ferramentas;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Banco: a connection string "vagasdb" é injetada pelo AppHost (WithReference).
// A integração do Aspire também registra health check, retries e telemetria do Npgsql.
builder.AddNpgsqlDbContext<VagasDbContext>("vagasdb");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ArmazenamentoVagas>();
builder.Services.AddScoped<ArmazenamentoPerfil>();

// Servidor MCP via HTTP (Streamable HTTP), exposto em /mcp.
// O mcp_vagas só guarda e consulta dados; a coleta fica no agente_coleta.
builder.Services.AddMcpServer()
    .WithHttpTransport()
    // Coleção de tipos: WithTools(typeof(X)) cairia na sobrecarga WithTools<T>(T alvo) com T = System.Type
    // e não registraria nada. Classe estática não pode ser argumento genérico, então WithTools<VagasTools>() não serve.
    .WithTools([typeof(VagasTools), typeof(PerfilTools)]);

var app = builder.Build();

// Em desenvolvimento, aplica as migrations ao subir. Em produção isso seria um passo separado do deploy.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<VagasDbContext>().Database.MigrateAsync();
}

app.MapDefaultEndpoints();

app.MapGet("/", () => "Servidor MCP de vagas (armazenamento). Endpoint MCP: /mcp.");

app.MapMcp("/mcp");

app.Run();
