var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Fase 1b: registrar o LLM, os clientes MCP (Firecrawl e mcp-vagas) e as fontes
// (RemotiveFonte já está em Fontes/, trazida do mcp_vagas).

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => "Agente A2A de coleta (Firecrawl + Remotive)");

app.Run();
