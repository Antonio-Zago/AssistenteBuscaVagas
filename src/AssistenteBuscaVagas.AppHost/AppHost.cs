var builder = DistributedApplication.CreateBuilder(args);

// Servidor MCP: dono dos dados (vagas, perfil, candidaturas).
// Fase 1: adicionar Postgres + pgvector aqui (requer Docker).
var mcpVagas = builder.AddProject<Projects.AssistenteBuscaVagas_McpVagas>("mcp-vagas")
    .WithHttpHealthCheck("/health");

// Agentes A2A: todos são clientes MCP do mcp-vagas.
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
    .WithReference(agentePerfil).WaitFor(agentePerfil)
    .WithReference(agenteMatch).WaitFor(agenteMatch)
    .WithReference(agenteCandidatura).WaitFor(agenteCandidatura)
    .WithHttpHealthCheck("/health");

// Supervisor: recebe a mensagem do usuário, consulta o roteador e chama o agente certo.
var supervisor = builder.AddProject<Projects.AssistenteBuscaVagas_Supervisor>("supervisor")
    .WithReference(roteador).WaitFor(roteador)
    .WithReference(agentePerfil)
    .WithReference(agenteMatch)
    .WithReference(agenteCandidatura)
    .WithHttpHealthCheck("/health");

// Worker diário: coleta + match + notificação.
builder.AddProject<Projects.AssistenteBuscaVagas_WorkerDiario>("worker-diario")
    .WithReference(mcpVagas).WaitFor(mcpVagas)
    .WithReference(agenteMatch).WaitFor(agenteMatch)
    .WithHttpHealthCheck("/health");

// Frontend Blazor: fala só com o supervisor.
builder.AddProject<Projects.AssistenteBuscaVagas_Frontend>("frontend")
    .WithReference(supervisor).WaitFor(supervisor)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health");

builder.Build().Run();
