using A2A;
using AssistenteBuscaVagas.Comum.A2A;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace AssistenteBuscaVagas.Roteador.Catalogo;

public sealed class RoteadorOptions
{
    /// <summary>Nomes dos agentes no Aspire. O card fica em http://{nome}/.well-known/agent-card.json.</summary>
    public string[] Agentes { get; set; } = [];

    /// <summary>Nomes dos servidores MCP no Aspire. O endpoint fica em http://{nome}/mcp.</summary>
    public string[] ServidoresMcp { get; set; } = [];

    /// <summary>Intervalo para redescobrir as capacidades sozinho (um agente pode ter subido depois, ou mudado o card).</summary>
    public int MinutosEntreAtualizacoes { get; set; } = 10;
}

public sealed record FalhaDescoberta(string Origem, string Motivo);

public sealed record EstadoCatalogo(DateTimeOffset AtualizadoEm, int Agentes, int FerramentasMcp, IReadOnlyList<FalhaDescoberta> Falhas);

/// <summary>
/// Monta o catálogo: lê o Agent Card de cada agente (A2A) e a lista de ferramentas de cada servidor MCP.
/// Um agente fora do ar não derruba o catálogo: vira uma falha listada e o resto segue.
/// </summary>
public sealed class Descoberta(IHttpClientFactory http, ILoggerFactory logs, IOptions<RoteadorOptions> opcoes, TimeProvider relogio)
{
    public const string NomeHttpClient = "descoberta";
    private readonly ILogger _log = logs.CreateLogger<Descoberta>();

    public async Task<(IReadOnlyList<Capacidade> Capacidades, EstadoCatalogo Estado)> DescobrirAsync(CancellationToken ct)
    {
        var capacidades = new List<Capacidade>();
        var falhas = new List<FalhaDescoberta>();
        int agentes = 0, ferramentas = 0;

        foreach (var nome in opcoes.Value.Agentes)
        {
            try
            {
                var card = await new A2ACardResolver(new Uri($"http://{nome}/"), http.CreateClient(NomeHttpClient)).GetAgentCardAsync(ct);
                var url = card.SupportedInterfaces?.FirstOrDefault()?.Url ?? $"http://{nome}/a2a";
                foreach (var s in card.Skills ?? [])
                {
                    var tags = (s.Tags ?? []).ToList();
                    capacidades.Add(new Capacidade(
                        $"{card.Name}.{s.Id}", "agente", card.Name, s.Id, s.Name, s.Description,
                        tags.Where(t => t != CartaoA2A.TagPaga).ToList(), (s.Examples ?? []).ToList(), url,
                        Paga: tags.Contains(CartaoA2A.TagPaga), SomenteLeitura: false, Esquema: null));
                }
                agentes++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _log.LogWarning("Agent Card de {Agente} indisponível: {Erro}", nome, ex.Message);
                falhas.Add(new(nome, ex.Message));
            }
        }

        foreach (var nome in opcoes.Value.ServidoresMcp)
        {
            try
            {
                var url = $"http://{nome}/mcp";
                var transporte = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(url), Name = nome },
                    http.CreateClient(NomeHttpClient), logs, ownsHttpClient: true);
                await using var mcp = await McpClient.CreateAsync(transporte, loggerFactory: logs, cancellationToken: ct);
                foreach (var t in await mcp.ListToolsAsync(cancellationToken: ct))
                {
                    capacidades.Add(new Capacidade(
                        $"{nome}.{t.Name}", "mcp", nome, t.Name, t.ProtocolTool.Title ?? t.Name, t.Description,
                        [], [], url, Paga: false,
                        SomenteLeitura: t.ProtocolTool.Annotations?.ReadOnlyHint == true,
                        Esquema: t.JsonSchema));
                    ferramentas++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _log.LogWarning("Servidor MCP {Servidor} indisponível: {Erro}", nome, ex.Message);
                falhas.Add(new(nome, ex.Message));
            }
        }

        _log.LogInformation("Catálogo: {Capacidades} capacidades ({Agentes} agentes, {Ferramentas} ferramentas MCP), {Falhas} falhas",
            capacidades.Count, agentes, ferramentas, falhas.Count);
        return (capacidades, new EstadoCatalogo(relogio.GetUtcNow(), agentes, ferramentas, falhas));
    }
}

/// <summary>O índice atual. Trocado por inteiro a cada atualização, então leituras nunca veem um índice pela metade.</summary>
public sealed class Catalogo
{
    private volatile Instantaneo _atual = new(new IndiceCapacidades([]), null);

    public sealed record Instantaneo(IndiceCapacidades Indice, EstadoCatalogo? Estado);

    public Instantaneo Atual => _atual;

    public void Substituir(IReadOnlyList<Capacidade> capacidades, EstadoCatalogo estado) =>
        _atual = new(new IndiceCapacidades(capacidades), estado);
}

/// <summary>Descobre ao subir (com novas tentativas, já que os agentes podem ainda estar iniciando) e depois periodicamente.</summary>
public sealed class AtualizadorCatalogo(Descoberta descoberta, Catalogo catalogo, IOptions<RoteadorOptions> opcoes, ILogger<AtualizadorCatalogo> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var tentativa = 1; tentativa <= 5 && !stoppingToken.IsCancellationRequested; tentativa++)
        {
            if (await AtualizarAsync(stoppingToken) is { Falhas.Count: 0 }) break;
            await Task.Delay(TimeSpan.FromSeconds(5 * tentativa), stoppingToken);
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, opcoes.Value.MinutosEntreAtualizacoes)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await AtualizarAsync(stoppingToken);
    }

    public async Task<EstadoCatalogo?> AtualizarAsync(CancellationToken ct)
    {
        try
        {
            var (capacidades, estado) = await descoberta.DescobrirAsync(ct);
            // Se tudo falhou, mantém o catálogo anterior em vez de ficar vazio.
            if (capacidades.Count > 0 || catalogo.Atual.Estado is null)
                catalogo.Substituir(capacidades, estado);
            return estado;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogError(ex, "Falha ao atualizar o catálogo");
            return null;
        }
    }
}
