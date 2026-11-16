using System.Text.Json;
using A2A;
using Microsoft.Extensions.Logging;

namespace AssistenteBuscaVagas.Comum.A2A;

/// <summary>
/// Convenção deste projeto sobre o A2A. O protocolo só define mensagens com partes (texto, dados, arquivos);
/// não existe "chamar a habilidade X com os parâmetros Y". Então combinamos um envelope:
///
///   pedido   = parte de texto (o pedido em linguagem natural) + parte de dados {"habilidade": "...", "dados": {...}}
///   resposta = parte de texto (resumo para humanos)           + parte de dados {"resultado": ...} ou {"erro": "..."}
///
/// Qualquer cliente A2A consegue falar com os agentes seguindo isso; os Agent Cards descrevem as habilidades
/// e o formato de "dados" de cada uma.
/// </summary>
public sealed record PedidoHabilidade(string? Habilidade, JsonElement? Dados, string Texto)
{
    public const string CampoHabilidade = "habilidade";
    public const string CampoDados = "dados";

    public static PedidoHabilidade Ler(Message mensagem)
    {
        string? habilidade = null;
        JsonElement? dados = null;
        var textos = new List<string>();

        foreach (var parte in mensagem.Parts ?? [])
        {
            if (parte.ContentCase == PartContentCase.Text && !string.IsNullOrWhiteSpace(parte.Text))
                textos.Add(parte.Text);
            else if (parte.ContentCase == PartContentCase.Data && parte.Data is { ValueKind: JsonValueKind.Object } obj)
            {
                if (obj.TryGetProperty(CampoHabilidade, out var h) && h.ValueKind == JsonValueKind.String)
                    habilidade = h.GetString();
                if (obj.TryGetProperty(CampoDados, out var d) && d.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                    dados = d.Clone();
            }
        }

        return new(string.IsNullOrWhiteSpace(habilidade) ? null : habilidade.Trim(), dados, string.Join("\n", textos));
    }

    public Message ParaMensagem()
    {
        var envelope = new Dictionary<string, object?> { [CampoHabilidade] = Habilidade };
        if (Dados is { } d) envelope[CampoDados] = d;

        List<Part> partes = [Part.FromData(JsonSerializer.SerializeToElement(envelope))];
        if (!string.IsNullOrWhiteSpace(Texto)) partes.Insert(0, Part.FromText(Texto));

        return new Message
        {
            MessageId = Guid.NewGuid().ToString("N"),
            Role = Role.User,
            Parts = partes,
        };
    }
}

public sealed record RespostaHabilidade(string Resumo, JsonElement? Resultado, string? Erro)
{
    public const string CampoResultado = "resultado";
    public const string CampoErro = "erro";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static RespostaHabilidade Ok(string resumo, object? resultado) =>
        new(resumo, resultado is null ? null : JsonSerializer.SerializeToElement(resultado, Json), null);

    public static RespostaHabilidade Falha(string erro) => new($"Erro: {erro}", null, erro);

    public List<Part> ParaPartes()
    {
        var dados = Erro is not null
            ? new Dictionary<string, object?> { [CampoErro] = Erro }
            : new Dictionary<string, object?> { [CampoResultado] = Resultado };
        return [Part.FromText(Resumo), Part.FromData(JsonSerializer.SerializeToElement(dados))];
    }

    /// <summary>Lê a resposta de um agente. Aceita tanto mensagem direta quanto tarefa concluída (artefatos ou histórico).</summary>
    public static RespostaHabilidade Ler(SendMessageResponse resposta)
    {
        IEnumerable<Part> partes = resposta.PayloadCase switch
        {
            SendMessageResponseCase.Message => resposta.Message?.Parts ?? [],
            SendMessageResponseCase.Task => (resposta.Task?.Artifacts ?? []).SelectMany(a => a.Parts ?? [])
                .Concat(resposta.Task?.Status?.Message?.Parts ?? []),
            _ => [],
        };

        var textos = new List<string>();
        JsonElement? resultado = null;
        string? erro = null;
        foreach (var p in partes)
        {
            if (p.ContentCase == PartContentCase.Text && !string.IsNullOrWhiteSpace(p.Text))
                textos.Add(p.Text);
            else if (p.ContentCase == PartContentCase.Data && p.Data is { ValueKind: JsonValueKind.Object } obj)
            {
                if (obj.TryGetProperty(CampoErro, out var e) && e.ValueKind == JsonValueKind.String) erro = e.GetString();
                if (obj.TryGetProperty(CampoResultado, out var r)) resultado = r.Clone();
            }
        }

        if (textos.Count == 0 && resultado is null && erro is null)
            erro = "O agente não devolveu conteúdo.";
        return new(string.Join("\n", textos), resultado, erro);
    }
}

/// <summary>Uma habilidade publicada no Agent Card. "pago" nas tags avisa o supervisor de que a habilidade gasta API paga.</summary>
public sealed record Habilidade(string Id, string Nome, string Descricao, IReadOnlyList<string> Tags, IReadOnlyList<string> Exemplos, bool Paga = false);

public static class CartaoA2A
{
    public const string TagPaga = "pago";

    /// <summary>
    /// Monta o Agent Card. A URL é lógica ("http://agente-match/a2a"): dentro do Aspire o service discovery a resolve,
    /// então quem descobre o card consegue chamar o agente sem saber a porta.
    /// </summary>
    public static AgentCard Criar(string nome, string descricao, string url, IEnumerable<Habilidade> habilidades) => new()
    {
        Name = nome,
        Description = descricao,
        Version = "1.0.0",
        SupportedInterfaces = [new AgentInterface { Url = url, ProtocolBinding = "JSONRPC", ProtocolVersion = "1.0" }],
        DefaultInputModes = ["text/plain", "application/json"],
        DefaultOutputModes = ["text/plain", "application/json"],
        Capabilities = new AgentCapabilities { Streaming = false },
        Skills = habilidades.Select(h => new AgentSkill
        {
            Id = h.Id,
            Name = h.Nome,
            Description = h.Descricao,
            Tags = h.Paga ? [.. h.Tags, TagPaga] : [.. h.Tags],
            Examples = [.. h.Exemplos],
        }).ToList(),
    };
}

/// <summary>
/// Base dos agentes A2A deste projeto: lê o envelope, despacha para a habilidade e responde sem ciclo de tarefa
/// (MessageResponder). Erro de negócio vira resposta com "erro", para quem chamou (o supervisor) poder se corrigir.
/// </summary>
public abstract class AgenteA2A(ILogger logger) : IAgentHandler
{
    protected abstract IReadOnlyList<Habilidade> Habilidades { get; }

    protected abstract Task<RespostaHabilidade> ExecutarAsync(PedidoHabilidade pedido, CancellationToken ct);

    public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
    {
        var responder = new MessageResponder(eventQueue, context.ContextId);
        var pedido = PedidoHabilidade.Ler(context.Message);

        RespostaHabilidade resposta;
        if (pedido.Habilidade is null || Habilidades.All(h => h.Id != pedido.Habilidade))
        {
            var lista = string.Join(", ", Habilidades.Select(h => h.Id));
            resposta = RespostaHabilidade.Falha(pedido.Habilidade is null
                ? $"Informe a habilidade na parte de dados ({{\"habilidade\": \"...\"}}). Habilidades: {lista}."
                : $"Habilidade desconhecida: {pedido.Habilidade}. Habilidades: {lista}.");
        }
        else
        {
            try
            {
                resposta = await ExecutarAsync(pedido, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha na habilidade {Habilidade}", pedido.Habilidade);
                resposta = RespostaHabilidade.Falha(ex is ArgumentException or InvalidOperationException or JsonException
                    ? ex.Message
                    : "Erro interno do agente. Veja os logs.");
            }
        }

        await responder.ReplyAsync(resposta.ParaPartes(), null, cancellationToken);
    }

    public Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Lê "dados" como T. Dados ausentes viram o padrão de T.</summary>
    protected static T? LerDados<T>(PedidoHabilidade pedido) =>
        pedido.Dados is { } d ? d.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) : default;
}
