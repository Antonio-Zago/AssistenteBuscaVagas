var builder = DistributedApplication.CreateBuilder(args);

// Postgres em contêiner (requer Docker). A imagem pgvector já traz a extensão de busca vetorial,
// que será usada nas fases de match. O volume mantém os dados entre execuções.
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "pg17")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithPgAdmin();

var vagasDb = postgres.AddDatabase("vagasdb");

// Servidor MCP: só a camada de dados (vagas, perfil, candidaturas). Não busca nada fora.
var mcpVagas = builder.AddProject<Projects.AssistenteBuscaVagas_McpVagas>("mcp-vagas")
    .WithReference(vagasDb).WaitFor(vagasDb)
    .WithHttpHealthCheck("/health");

// Chaves de API como parâmetros secretos. Defina com user-secrets no projeto do AppHost:
//   dotnet user-secrets set "Parameters:anthropic-api-key" "sk-ant-..." --project src/AssistenteBuscaVagas.AppHost
//   dotnet user-secrets set "Parameters:firecrawl-api-key" "fc-..."     --project src/AssistenteBuscaVagas.AppHost
// Se faltarem, o dashboard do Aspire pede os valores ao subir.
var anthropicApiKey = builder.AddParameter("anthropic-api-key", secret: true);
var firecrawlApiKey = builder.AddParameter("firecrawl-api-key", secret: true);

// Agentes A2A: todos são clientes MCP do mcp-vagas.
// agente-coleta: busca vagas na web (Firecrawl) e na Remotive e grava via salvar_vagas.
var agenteColeta = builder.AddProject<Projects.AssistenteBuscaVagas_AgenteColeta>("agente-coleta")
    .WithReference(mcpVagas).WaitFor(mcpVagas)
    .WithEnvironment("ANTHROPIC_API_KEY", anthropicApiKey)
    .WithEnvironment("Firecrawl__ApiKey", firecrawlApiKey)
    .WithHttpHealthCheck("/health");

var agentePerfil = builder.AddProject<Projects.AssistenteBuscaVagas_AgentePerfil>("agente-perfil")
    .WithReference(mcpVagas).WaitFor(mcpVagas)
    .WithHttpHealthCheck("/health");

var agenteMatch = builder.AddProject<Projects.AssistenteBuscaVagas_AgenteMatch>("agente-match")
    .WithReference(mcpVagas).WaitFor(mcpVagas)
    .WithHttpHealthCheck("/health");

var agenteCandidatura = builder.AddProject<Projects.AssistenteBuscaVagas_AgenteCandidatura>("agente-candidatura")
    .WithReference(mcpVagas).WaitFor(mcpVagas)
    .WithHttpHealthCheck("/health");

// Roteador: descobre Agent Cards e ferramentas MCP e indexa com BM25.
var roteador = builder.AddProject<Projects.AssistenteBuscaVagas_Roteador>("roteador")
    .WithReference(mcpVagas).WaitFor(mcpVagas)
    .WithReference(agenteColeta).WaitFor(agenteColeta)
    .WithReference(agentePerfil).WaitFor(agentePerfil)
    .WithReference(agenteMatch).WaitFor(agenteMatch)
    .WithReference(agenteCandidatura).WaitFor(agenteCandidatura)
    .WithHttpHealthCheck("/health");

// Supervisor: recebe a mensagem do usuário, consulta o roteador e chama o agente certo.
var supervisor = builder.AddProject<Projects.AssistenteBuscaVagas_Supervisor>("supervisor")
    .WithReference(roteador).WaitFor(roteador)
    .WithReference(agenteColeta)
    .WithReference(agentePerfil)
    .WithReference(agenteMatch)
    .WithReference(agenteCandidatura)
    .WithHttpHealthCheck("/health");

// Worker diário: aciona a coleta e o match e notifica.
builder.AddProject<Projects.AssistenteBuscaVagas_WorkerDiario>("worker-diario")
    .WithReference(mcpVagas).WaitFor(mcpVagas)
    .WithReference(agenteColeta).WaitFor(agenteColeta)
    .WithReference(agenteMatch).WaitFor(agenteMatch)
    .WithHttpHealthCheck("/health");

// Frontend Blazor: fala só com o supervisor.
builder.AddProject<Projects.AssistenteBuscaVagas_Frontend>("frontend")
    .WithReference(supervisor).WaitFor(supervisor)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health");

builder.Build().Run();
