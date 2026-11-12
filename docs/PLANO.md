# Plano: Assistente de Busca de Vagas

Sistema multiagente (A2A + MCP) em .NET que coleta vagas de fontes públicas, compara com o meu perfil, recomenda as melhores todos os dias e ajuda a preparar as candidaturas.

Objetivo de aprendizado: praticar, em .NET, os conceitos do curso de agentes da Alura (A2A, MCP, roteamento, supervisor) e ir além deles: funil de ranqueamento, agentes disparados por eventos, avaliação e human-in-the-loop.

---

## 1. O que o usuário consegue fazer

1. Enviar o currículo e definir preferências: remoto ou presencial, cidade, senioridade, faixa salarial.
2. Receber todo dia as **5 vagas que mais combinam**, cada uma com a justificativa. Exemplo: "combina porque pede C# e Azure; falta experiência com Kubernetes".
3. Ver as **lacunas de skills**. Exemplo: "40% das vagas que combinam com você pedem Docker".
4. Para uma vaga escolhida, gerar os pontos do currículo que devem ser destacados, uma carta de apresentação e perguntas prováveis de entrevista.
5. Acompanhar as candidaturas em um kanban: *salva → aplicada → entrevista → oferta/recusada*.

---

## 2. Arquitetura

```
                ┌──────────────────┐
                │ Worker diário    │  (BackgroundService)
                │ coleta + match   │
                └────────┬─────────┘
                         │
┌──────────┐     ┌───────▼──────┐     ┌────────────┐
│ frontend │────▶│  supervisor  │────▶│  roteador  │
│ (Blazor) │     └───────┬──────┘     └────────────┘
└──────────┘             │ A2A
   ┌──────────────┬──────┴───────┬──────────────────┐
   ▼              ▼              ▼                  ▼
┌─────────────┐┌─────────────┐┌─────────────┐┌──────────────────┐
│agente_coleta││agente_perfil││agente_match ││agente_candidatura│
└──┬───────┬──┘└──────┬──────┘└──────┬──────┘└────────┬─────────┘
   │       └──────────┴──────┬───────┴────────────────┘
   │ MCP / HTTP              │ MCP
   ▼                ┌────────▼────────┐
Firecrawl (web)     │   mcp_vagas     │──▶ Postgres (+ pgvector)
Remotive (API)      └─────────────────┘
```

Os próprios agentes são **clientes MCP**: eles chamam as ferramentas do `mcp_vagas` sozinhos. O `mcp_vagas` só guarda e consulta dados; quem vai buscar vagas fora é o `agente_coleta`.

---

## 3. Serviços

### `mcp_vagas` (servidor MCP)

| Ferramenta | O que faz |
|---|---|
| `salvar_vagas` | Valida, deduplica e grava vagas trazidas pelo `agente_coleta` |
| `listar_fontes` | Lista as fontes cadastradas e quando cada uma foi coletada pela última vez |
| `listar_vagas` | Lista vagas com filtros (remoto, cidade, data) |
| `obter_vaga` | Traz os detalhes completos de uma vaga |
| `salvar_perfil` / `obter_perfil` | Guarda e lê o perfil estruturado |
| `atualizar_status` | Move a vaga no kanban |
| `registrar_feedback` | Registra quando o usuário diz "essa vaga não tem a ver comigo" |
| `estatisticas_skills` | Mostra quais skills mais aparecem nas vagas compatíveis |

### `agente_coleta` (A2A)
Decide o que buscar (a partir do perfil), procura vagas na web pelo Firecrawl e na API da Remotive, extrai os dados estruturados (JSON-LD `JobPosting`) em código e grava tudo via `salvar_vagas`. Respeita `robots.txt`, os termos de cada fonte e um orçamento diário de créditos.

### `agente_perfil` (A2A)
Lê o currículo (PDF) e o transforma em dados estruturados: skills, anos de experiência, senioridade, idiomas.

### `agente_match` (A2A)
Dá uma nota para cada vaga em relação ao perfil, explica o motivo e aponta as lacunas.

### `agente_candidatura` (A2A)
Adapta o material para uma vaga específica: pontos de destaque, carta de apresentação, perguntas de entrevista.

### `roteador`
Mesma ideia do `bfa` do curso: descobre os Agent Cards e as ferramentas MCP e indexa tudo com BM25.

### `supervisor`
Recebe a mensagem do usuário, consulta o roteador e chama o agente certo, encadeando agentes quando o pedido precisar.

### Worker diário
Roda sozinho: aciona o `agente_coleta`, executa o match e avisa por e-mail ou Telegram.

---

## 4. Conceito central: o funil de match

Mandar mil vagas para o LLM é caro e lento. Cada etapa do funil é mais cara e mais inteligente que a anterior:

```
1000 vagas coletadas
   │  1. Filtros rígidos em código (remoto? senioridade? já descartada?)
   ▼
 200 vagas
   │  2. Busca barata: BM25/embeddings comparando skills com o perfil
   ▼
  20 vagas
   │  3. LLM avalia e justifica só essas
   ▼
   5 vagas recomendadas
```

---

## 5. Modelo de dados (rascunho)

```
perfis(id, skills_json, senioridade, preferencias_json, atualizado_em)
fontes(id, nome, tipo, url_base, ativa)
vagas(id, fonte_id, id_externo, empresa, titulo, descricao, local, remoto,
      senioridade, publicada_em, coletada_em, hash_dedup, embedding)
matches(id, vaga_id, perfil_id, nota, justificativa, lacunas_json, criado_em)
candidaturas(id, vaga_id, status, notas, atualizado_em)
feedbacks(id, vaga_id, tipo, motivo, criado_em)
```

---

## 6. Desafios para resolver

- **Vagas repetidas:** a mesma vaga aparece em várias fontes com títulos um pouco diferentes. Normalizar o texto? Gerar um hash de empresa + título? Comparar por similaridade?
- **Avaliar o ranqueamento:** marcar manualmente 50 vagas como "boa" ou "ruim" e medir quantas das 5 recomendadas eram boas (*precision@5*). Toda mudança no prompt ou no funil precisa melhorar esse número.
- **Aprender com o feedback:** um descarte deve afetar as próximas recomendações. Começar com uma lista de empresas e palavras a evitar.
- **Não inventar nada:** o `agente_candidatura` nunca pode inventar experiências que não estão no currículo. Validar com *LLM-as-judge*.

---

## 7. Cuidados

- **Usar só fontes que permitem acesso automatizado.** Nada de scraping de sites cujos termos de uso proíbem, como o LinkedIn. Opções a avaliar: agregadores com API (Adzuna, Remotive, Arbeitnow) e os portais públicos Greenhouse e Lever. Conferir os termos de cada fonte antes de usar.
- **Não automatizar o envio da candidatura.** O sistema prepara tudo, mas quem clica em "candidatar" sou eu (*human-in-the-loop*).
- **O currículo é um dado pessoal.** Manter tudo local e não registrar o conteúdo dele nos logs.

---

## 8. Stack .NET

| Necessidade | Ferramenta |
|---|---|
| Orquestração e observabilidade | .NET Aspire |
| Servidor/cliente MCP | SDK oficial em C# (`ModelContextProtocol`) |
| Agentes A2A | SDK A2A para .NET (`A2A`) |
| Framework de agentes | Microsoft Agent Framework (avaliar) |
| LLM | `Microsoft.Extensions.AI` |
| APIs | ASP.NET Core Minimal APIs |
| Worker diário | `BackgroundService` (ou Hangfire, se quiser painel de jobs) |
| Banco + busca vetorial | Postgres + pgvector com EF Core |
| Chamadas às APIs de vagas | `HttpClient` + Polly (tentativas e limite de requisições) |
| Busca BM25 | Lucene.NET |
| PDF do currículo | PdfPig |
| Frontend | Blazor |

Essas bibliotecas evoluem rápido: conferir as versões atuais no NuGet antes de começar.

---

## 9. Roteiro em fases

Cada fase termina com algo funcionando. Não avançar sem fechar a anterior.

- [x] **Fase 0: esqueleto.** Solução .NET com Aspire e todos os serviços respondendo um health check.
- [x] **Fase 1a: armazenamento.** O `mcp_vagas` vira só a camada de dados: `salvar_vagas` (valida, deduplica e grava), `listar_vagas`, `obter_vaga` e `listar_fontes`, sobre o Postgres. Testar com o MCP Inspector (`npx @modelcontextprotocol/inspector`), sem LLM.
- [x] **Fase 1b: agente de coleta.** O `agente_coleta` (Claude via `IChatClient`) decide o que buscar e grava via `salvar_vagas`. Ferramentas: `buscar_web` (API REST v2 do Firecrawl; o LLM lê só título e trecho), `extrair_vagas` em código (confere o `robots.txt`, faz scrape em `rawHtml` e extrai o JSON-LD `JobPosting`, sem LLM), `coletar_remotive` e, do `mcp_vagas`, `listar_fontes` e `listar_vagas`. Orçamento diário de créditos no código e domínios bloqueados (LinkedIn, Indeed, Glassdoor). O Firecrawl entra pela API REST e não pelo MCP hospedado porque o MCP não expõe `rawHtml`, `tbs` nem `excludeDomains`. `POST /coletar` roda o agente; `POST /coletar/remotive` é o fluxo fixo sem LLM. *Status:* concluída. Remotive → `salvar_vagas` testado de ponta a ponta; o laço com Claude + Firecrawl ainda não rodou de verdade. A publicação como A2A (Agent Card) fica para a fase 4.5.
- [x] **Fase 2: perfil.** O `agente_perfil` transforma o currículo em JSON validado. `POST /perfil/curriculo` recebe o PDF, extrai o texto localmente (PdfPig), faz UMA chamada ao Claude com saída estruturada (JSON Schema, sem ferramentas nem laço: não é agente) e grava via `salvar_perfil`. `PUT /perfil/preferencias` grava as preferências de busca; `GET /perfil` lê via `obter_perfil`. O `mcp_vagas` ganhou a tabela `perfis` (linha única, jsonb) e valida tudo de novo na fronteira (`ValidacaoPerfil`). Privacidade: o PDF e o texto ficam só em memória, nada do conteúdo vai para log, e o schema não tem campos para nome, e-mail, telefone ou endereço; o texto do currículo é enviado à API da Anthropic só nessa chamada (decisão tomada). *Status:* concluída. Fluxo sem LLM testado de ponta a ponta; a extração com um currículo real ainda não rodou.
- [ ] **Fase 3: funil sem LLM.** Só filtros e BM25. Ver quão longe dá para ir sem IA.
- [ ] **Fase 4: match com LLM.** Adicionar a última etapa do funil, com justificativas. Montar o conjunto de avaliação e medir a *precision@5*.
- [ ] **Fase 4.5: roteador + supervisor.** Os agentes publicam seus Agent Cards. O `roteador` descobre os cards e as ferramentas MCP e indexa tudo com BM25. O `supervisor` recebe uma mensagem em linguagem natural, consulta o roteador e chama o agente certo, encadeando agentes quando preciso (ex.: "atualize meu perfil e me mostre as vagas" → `agente_perfil` → `agente_match`). Entra aqui porque é a primeira vez que existe mais de um agente para escolher, e o frontend da fase 6 só fala com o supervisor.
- [ ] **Fase 5: worker + notificação.** Recomendações diárias automáticas. O worker chama o `mcp_vagas` e o `agente_match` diretamente, sem passar pelo supervisor: fluxo fixo não precisa de roteamento.
- [ ] **Fase 6: frontend.** Lista ranqueada, kanban e painel de lacunas.
- [ ] **Fase 7: candidatura e feedback.** O `agente_candidatura` e o aprendizado com os descartes.
- [ ] **Fase 8: mais fontes.** A deduplicação passa a ser obrigatória.

A fase 3 é a mais didática: comparar o funil sem LLM e com LLM mostra exatamente quanto valor a IA agrega e quanto custa.

---

## 10. O que cada fase ensina

| Fase | Conceito praticado |
|---|---|
| 1a | Desenho de ferramentas MCP, validação na fronteira de dados |
| 1b | Agente com ferramentas, cliente de vários servidores MCP, integração com APIs externas, custo e orçamento |
| 2 | Saída estruturada e validação |
| 3 | Busca e ranqueamento clássicos |
| 4 | Funil de custo, avaliação (*precision@k*) |
| 4.5 | A2A (Agent Cards), descoberta e roteamento, padrão supervisor |
| 5 | Agentes disparados por eventos |
| 7 | Human-in-the-loop, LLM-as-judge, personalização |
| 8 | Qualidade de dados, deduplicação |
