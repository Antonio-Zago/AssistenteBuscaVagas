var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => "Roteador (Agent Cards + ferramentas MCP, BM25)");

app.Run();
