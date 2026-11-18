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
| `listar_vagas_para_match` | Lê vagas em lote, com descrição, para o funil (paginação por cursor ou lista de ids) |
| `salvar_matches` / `obter_matches` | Cache das avaliações do LLM, por chave de perfil + modelo + prompt |
| `rotular_vaga` / `remover_rotulo` / `listar_rotulos` | Conjunto de avaliação: vagas marcadas à mão como boas ou ruins |
| `registrar_execucao_diaria` / `listar_execucoes_diarias` | Histórico do worker diário: status, passos e vagas recomendadas de cada dia |
| `listar_vagas_recomendadas` | Vagas já mostradas em resumos diários (para recomendar só novas) |
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
| Agentes A2A | SDK A2A para .NET (`A2A` e `A2A.AspNetCore` 1.0.0-preview4, protocolo v1) |
| Framework de agentes | Microsoft Agent Framework (avaliar) |
| LLM | `Microsoft.Extensions.AI` |
| APIs | ASP.NET Core Minimal APIs |
| Worker diário | `BackgroundService` (ou Hangfire, se quiser painel de jobs) |
| Banco + busca vetorial | Postgres + pgvector com EF Core |
| Chamadas às APIs de vagas | `HttpClient` + Polly (tentativas e limite de requisições) |
| Busca BM25 | Implementação própria (biblioteca `Comum`), no lugar do Lucene.NET |
| PDF do currículo | PdfPig |
| Frontend | Blazor |

Essas bibliotecas evoluem rápido: conferir as versões atuais no NuGet antes de começar.

---

## 9. Roteiro em fases

Cada fase termina com algo funcionando. Não avançar sem fechar a anterior.

- [x] **Fase 0: esqueleto.** Solução .NET com Aspire e todos os serviços respondendo um health check.
- [x] **Fase 1a: armazenamento.** O `mcp_vagas` vira só a camada de dados: `salvar_vagas` (valida, deduplica e grava), `listar_vagas`, `obter_vaga` e `listar_fontes`, sobre o Postgres. Testar com o MCP Inspector (`npx @modelcontextprotocol/inspector`), sem LLM.
- [x] **Fase 1b: agente de coleta.** O `agente_coleta` (Claude via `IChatClient`) decide o que buscar e grava via `salvar_vagas`. Ferramentas: `buscar_web` (API REST v2 do Firecrawl; o LLM lê só título e trecho), `extrair_vagas` em código (confere o `robots.txt`, faz scrape em `rawHtml` e extrai o JSON-LD `JobPosting`, sem LLM), `coletar_remotive` e, do `mcp_vagas`, `listar_fontes` e `listar_vagas`. Orçamento diário de créditos no código e domínios bloqueados (LinkedIn, Indeed, Glassdoor). O Firecrawl entra pela API REST e não pelo MCP hospedado porque o MCP não expõe `rawHtml`, `tbs` nem `excludeDomains`. `POST /coletar` roda o agente; `POST /coletar/remotive` é o fluxo fixo sem LLM. *Status:* concluída. Remotive → `salvar_vagas` testado de ponta a ponta; o laço com Claude + Firecrawl ainda não rodou de verdade. A publicação como A2A (Agent Card) fica para a fase 4.5. **Atualização (depois da fase 5):** o "o que procurar" da coleta na web passou a vir do perfil gravado (`obter_perfil`): cargos desejados (ou o cargo atual), as 8 skills com mais anos de uso, a senioridade desejada (ou a do currículo), locais montados a partir das modalidades e cidades mais o país (`Coleta:PaisCandidato`) e os idiomas. Antes ela usava valores fixos do appsettings ("Sênior", ASP.NET Core, Azure), que não batiam com o perfil real (Pleno) usado pelo `agente_match` para avaliar. O appsettings (`Coleta:Preferencias`) virou reserva, usada campo a campo só no que faltar no perfil, e o resultado da coleta informa a fonte de cada campo (`fontePreferencias`).
- [x] **Fase 2: perfil.** O `agente_perfil` transforma o currículo em JSON validado. `POST /perfil/curriculo` recebe o PDF, extrai o texto localmente (PdfPig), faz UMA chamada ao Claude com saída estruturada (JSON Schema, sem ferramentas nem laço: não é agente) e grava via `salvar_perfil`. `PUT /perfil/preferencias` grava as preferências de busca; `GET /perfil` lê via `obter_perfil`. O `mcp_vagas` ganhou a tabela `perfis` (linha única, jsonb) e valida tudo de novo na fronteira (`ValidacaoPerfil`). Privacidade: o PDF e o texto ficam só em memória, nada do conteúdo vai para log, e o schema não tem campos para nome, e-mail, telefone ou endereço; o texto do currículo é enviado à API da Anthropic só nessa chamada (decisão tomada). **Atualização (depois da fase 5):** o currículo também pode ser editado sem reenviar o PDF, sem LLM: `PATCH /perfil/curriculo` e a habilidade A2A `ajustar_curriculo` (o supervisor entende "acrescente Docker com 1 ano às minhas skills"). Só o que vier preenchido muda: resumo, cargo atual, senioridade, anos de experiência, adicionar/remover skills (skill existente é atualizada; skill nova recebe a evidência "Informado pelo usuário"), idiomas e certificações. Experiências e formação continuam só pelo PDF. O ajuste é aplicado sobre o currículo gravado e validado de novo pelo `mcp_vagas`. *Status:* concluída. Fluxo sem LLM testado de ponta a ponta; a extração com um currículo real ainda não rodou.
- [x] **Fase 3: funil sem LLM.** Só filtros e BM25. Ver quão longe dá para ir sem IA. O `mcp_vagas` ganhou `listar_vagas_para_match` (lote com descrição, paginado por cursor). O `agente_match` roda o funil em código puro (`FunilMatch`), exposto em `GET /match/funil?limite=20&dias=30`: (1) filtros rígidos: senioridade inferida do título (tolerância de um nível), modalidade, vaga remota restrita a outra região (`LocaisRemotosAceitos`) e cidade para presencial/híbrido; dado ausente não descarta; (2) BM25 escrito à mão no lugar do Lucene.NET, para devolver a contribuição de cada termo, que vira a justificativa; a consulta é o perfil (skills com peso pelos anos de uso, cargos desejados com peso 1,5, sem termos de senioridade) e os campos pesam título 3, tags 2, descrição 1; (3) top N com skills encontradas, lacunas (tags da vaga que o perfil não cobre) e lacunas mais frequentes. Tokenizador próprio mantém `c#`, `.net`, `node.js` inteiros e normaliza sinônimos pt/en. *Status:* concluída e rodada com o perfil real: 15 vagas lidas → 7 após filtros (7 descartadas por local, 1 por senioridade) → 4 com pontos. *O que ficou claro sem IA:* a vaga certa ("Senior .NET Developer") fica em 1º com folga, mas da 2ª em diante as vagas sobem por termos genéricos ("developer", "engineer"), e as lacunas tiradas das tags são ruidosas ("security", "documentation"). É isso que a fase 4 precisa resolver. Salário e idioma ainda não filtram (o salário vem como texto livre).
- [x] **Fase 4: match com LLM.** Adicionar a última etapa do funil, com justificativas. Montar o conjunto de avaliação e medir a *precision@5*. O `agente_match` manda o top 20 do BM25 para o Claude (`AvaliadorClaude`): **uma chamada por vaga**, para que a descrição de uma vaga (texto de terceiros) não influencie a nota das outras; instruções + perfil num prefixo fixo com `cache_control`, lido do cache a partir da segunda vaga; saída estruturada com nota 0–100 (rubrica no prompt), justificativa, pontos fortes, lacunas e impeditivos (com impeditivo, nota ≤ 29); `fallbacks` do servidor para recusas. O ranking final é a nota do LLM, com empate decidido pelo BM25, e o top 5 é recomendado. `POST /match/recomendacoes` roda o funil completo e devolve as etapas, as 5 vagas, as lacunas frequentes ("x% das vagas que combinam pedem Docker", entre as de nota ≥ 60) e o custo (tokens e US$ estimado). **Cache:** o `mcp_vagas` ganhou a tabela `matches` e as ferramentas `salvar_matches`/`obter_matches`; a chave é o hash de perfil + modelo + versão do prompt + esforço, então só vagas novas (ou um perfil/prompt alterado) custam chamadas. **Avaliação:** tabela `rotulos` e ferramentas `rotular_vaga`, `remover_rotulo` e `listar_rotulos`; `listar_vagas_para_match` aceita `ids`. Endpoints: `GET /avaliacao/pendentes` (vagas sem rótulo, na ordem em que vale rotular), `PUT`/`DELETE /avaliacao/rotulos/{vagaId}` e `POST /avaliacao/executar?usarLlm=true|false`, que roda o funil só sobre as vagas rotuladas e devolve a precision@5 sem e com LLM, o teto possível, o *recall* do BM25 (o LLM não recupera o que o BM25 cortou) e as relevantes que os filtros descartaram. **Privacidade (decisão nova):** o perfil estruturado vai para a API da Anthropic a cada avaliação, mas sem o resumo livre, sem o nome das empresas e sem os trechos do currículo (`PerfilParaLlm`). *Status:* código pronto e testado sem LLM (rótulos, avaliação sem LLM e cache contra o Postgres real). Ainda faltam: a primeira chamada real ao Claude e o conjunto de ~50 vagas rotuladas (hoje há 15 vagas nos últimos 30 dias: coletar mais antes de medir).
- [x] **Fase 4.5: roteador + supervisor.** Os agentes publicam seus Agent Cards. O `roteador` descobre os cards e as ferramentas MCP e indexa tudo com BM25. O `supervisor` recebe uma mensagem em linguagem natural, consulta o roteador e chama o agente certo, encadeando agentes quando preciso (ex.: "atualize meu perfil e me mostre as vagas" → `agente_perfil` → `agente_match`). Entra aqui porque é a primeira vez que existe mais de um agente para escolher, e o frontend da fase 6 só fala com o supervisor. **A2A** com o SDK `A2A` 1.0.0-preview4 (protocolo v1, JSON-RPC): `agente_coleta` (`coletar_remotive`; `coletar_vagas`, paga), `agente_perfil` (`obter_perfil`; `atualizar_preferencias`, que é **parcial**: só os campos enviados mudam) e `agente_match` (`recomendar_vagas`, paga; `ver_funil`; `medir_precisao`) servem o card em `/.well-known/agent-card.json` e atendem em `/a2a`, reaproveitando a lógica dos endpoints HTTP. A URL no card é lógica (`http://agente-match/a2a`) e o service discovery do Aspire a resolve. O `agente_candidatura` ainda não publica card (fase 7). **Envelope:** o A2A não tem "chamar habilidade X com parâmetros Y", então o projeto combinou pedido = texto + dados `{"habilidade", "dados"}` e resposta = texto (resumo) + dados `{"resultado"}` ou `{"erro"}` (biblioteca `Comum`, que também passou a abrigar o tokenizador e o BM25). Habilidade paga leva a tag `pago` no card. **Roteador:** lê os cards e o `tools/list` do `mcp_vagas` ao subir (com novas tentativas) e a cada 10 min; um agente fora do ar vira uma falha listada, não derruba o catálogo. Indexa nome, id, tags, exemplos e descrição com o mesmo BM25 do funil, mais um radical simples (tira o "s" final). `GET /roteador/capacidades`, `GET /roteador/buscar?q=&k=` e `POST /roteador/atualizar`. **Supervisor:** pede ao roteador 2k capacidades e oferece ao Claude as k=6 primeiras, só habilidades de agentes e ferramentas MCP de leitura (gravar passa pelos agentes). Habilidade A2A vira ferramenta com `instrucao` + `dados` (o A2A não publica schema por habilidade; o agente valida), e ferramenta MCP vai com o schema exato. O laço é o `UseFunctionInvocation` (até 8 iterações). Resultados acima de 20 mil caracteres são cortados com aviso. O cliente HTTP do A2A fica sem a resiliência padrão: ela corta em 30 s e repete POST, o que repetiria ações pagas. `POST /conversa` (pago) devolve resposta, passos e capacidades oferecidas; `GET /conversa/rota` mostra só o roteamento (grátis). Sem memória entre mensagens por enquanto. *Status:* concluída, sem a conversa com o Claude. Testado de verdade: cards, catálogo (3 agentes, 7 habilidades, 12 ferramentas MCP, sem falhas), roteamento ("atualize meu perfil para aceitar híbrido e me mostre as melhores vagas" → `recomendar_vagas` e `atualizar_preferencias` no topo; "quantas vagas tem de cada fonte?" → `listar_fontes`) e chamadas A2A diretas. Ainda não rodou: `POST /conversa`, que é a primeira vez que o `A2AClient` do supervisor chama um agente.
- [x] **Fase 5: worker + notificação.** Recomendações diárias automáticas. O worker chama o `mcp_vagas` e o `agente_match` diretamente, sem passar pelo supervisor: fluxo fixo não precisa de roteamento. **Decisões:** segunda e quinta às 09:00 (America/Sao_Paulo; `Agenda:DiasDaSemana`, vazio = todo dia); coleta da Remotive (grátis) e na web (Claude + Firecrawl, paga) em cada rodada; só vagas novas (cada vaga aparece em um resumo só); sem canal de notificação por enquanto. **Agenda:** o worker vive no AppHost, então só roda com o computador ligado. Em vez de "dormir até o horário", ele confere a cada 15 min se o **último horário agendado que já passou** foi atendido por alguma execução a partir daquele dia: computador desligado na segunda → a rodada acontece quando ele ligar na terça, e a de quinta segue normal. O estado fica no banco (tabela `execucoes_diarias`, uma linha por dia em que rodou), então reiniciar não repete a rodada. Horário que falha tenta de novo até 2 vezes no total (contadas por horário, não por dia, para um sistema quebrado não gastar todo dia). A regra é pura (`Agenda.Decidir`) e testada. `Agenda:Ativa = false` desliga o disparo automático. **Execução:** por A2A, `agente-coleta.coletar_remotive` → `agente-coleta.coletar_vagas` → `agente-match.recomendar_vagas` com `somenteNovas`. As vagas já recomendadas (tabela `recomendacoes_enviadas`) saem antes do BM25, para não ocuparem o top 20. Entram no resumo as vagas com nota ≥ 60. Cada passo falha sozinho: coleta caída deixa o dia "parcial"; sem recomendação o dia é "falha". Um segundo disparo no mesmo dia soma as vagas novas às já mostradas. O cliente A2A do worker também fica sem a resiliência padrão (timeout de 20 min, sem repetir POST). **Notificação:** interface `INotificador`; hoje só existe `NotificadorLog` (o resumo em texto vai para o log do worker no dashboard do Aspire). Telegram ou e-mail entram implementando a interface. **MCP:** `registrar_execucao_diaria`, `listar_execucoes_diarias` e `listar_vagas_recomendadas`; as duas de leitura são descobertas pelo roteador, então o supervisor já responde "quais vagas foram recomendadas hoje?". **Worker:** `GET /` (status e próximo horário), `GET /execucoes` (histórico) e `POST /execucoes/agora` (pago). `ver_funil` e `recomendar_vagas` aceitam `somenteNovas`. *Status:* concluída, sem uma execução diária completa de verdade. Testado de verdade com a agenda desligada: status, histórico, gravação e validação de execuções, `somenteNovas` no funil (a vaga recomendada some) e o roteador encontrando as ferramentas novas. **Atenção:** com a agenda ligada, subir o AppHost com um horário agendado ainda não atendido (ex.: na terça, se a segunda não rodou) dispara a execução (paga) em cerca de 20 s.
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
