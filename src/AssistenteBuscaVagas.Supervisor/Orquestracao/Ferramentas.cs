using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using A2A;
using AssistenteBuscaVagas.Comum.A2A;
using Microsoft.Extensions.AI;

namespace AssistenteBuscaVagas.Supervisor.Orquestracao;

/// <summary>Cópia do formato que o roteador devolve em /roteador/buscar.</summary>
public sealed record Capacidade(
    string Id, string Tipo, string Origem, string Habilidade, string Nome, string Descricao,
    IReadOnlyList<string> Tags, IReadOnlyList<string> Exemplos, string Url, bool Paga, bool SomenteLeitura, JsonElement? Esquema);

public sealed record TermoPontuado(string Termo, double Pontos);

public sealed record CapacidadeEncontrada(Capacidade Capacidade, double Pontuacao, IReadOnlyList<TermoPontuado> TermosQuePontuaram);

/// <summary>Uma chamada feita durante a conversa: o que o supervisor pediu a quem, e o que voltou.</summary>
public sealed record Passo(int Ordem, string Ferramenta, string Tipo, JsonElement Argumentos, bool Sucesso, string Resumo, long DuracaoMs);

/// <summary>Registro das chamadas de uma conversa. As ferramentas podem rodar em paralelo, então é protegido por trava.</summary>
public sealed class Execucao(int maximoCaracteresResultado)
{
    private readonly List<Passo> _passos = [];

    public int MaximoCaracteresResultado { get; } = maximoCaracteresResultado;

    public IReadOnlyList<Passo> Passos { get { lock (_passos) return _passos.ToList(); } }

    public void Registrar(string ferramenta, string tipo, JsonElement argumentos, bool sucesso, string resumo, long ms)
    {
        lock (_passos) _passos.Add(new(_passos.Count + 1, ferramenta, tipo, argumentos, sucesso, resumo, ms));
    }

    /// <summary>Resultados enormes (ex.: vagas com descrição completa) estouram o contexto: corta e avisa o modelo.</summary>
    public string Limitar(string texto) => texto.Length <= MaximoCaracteresResultado
        ? texto
        : texto[..MaximoCaracteresResultado] + $"\n[resultado cortado: {texto.Length} caracteres, mostrando {MaximoCaracteresResultado}]";
}

public static partial class NomesFerramenta
{
    /// <summary>"agente_match" + "recomendar_vagas" → "agente_match__recomendar_vagas". A API aceita só [a-zA-Z0-9_-], até 64.</summary>
    public static string De(string origem, string habilidade)
    {
        var nome = Invalido().Replace($"{origem}__{habilidade}", "_");
        return nome.Length <= 64 ? nome : nome[..64];
    }

    [GeneratedRegex("[^a-zA-Z0-9_-]")]
    private static partial Regex Invalido();
}

/// <summary>
/// Uma habilidade de agente A2A vista como ferramenta pelo Claude. O A2A não tem schema de parâmetros por habilidade:
/// o agente recebe uma mensagem. Então a ferramenta pede só "instrucao" (texto) e "dados" (JSON no formato que a
/// descrição da habilidade documenta), e o agente valida. Compare com as ferramentas MCP, que trazem o schema exato.
/// </summary>
public sealed class FerramentaA2A(Capacidade capacidade, HttpClient http, Execucao execucao) : AIFunction
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "instrucao": { "type": "string", "description": "O pedido para o agente, em português, com o contexto que ele precisa." },
            "dados": { "type": "object", "description": "Parâmetros no formato descrito na habilidade. Omita se a habilidade não pede dados." }
          },
          "required": ["instrucao"]
        }
        """).RootElement.Clone();

    public override string Name { get; } = NomesFerramenta.De(capacidade.Origem, capacidade.Habilidade);

    public override string Description { get; } =
        $"[agente {capacidade.Origem}] {capacidade.Descricao}" +
        (capacidade.Paga ? " ATENÇÃO: gasta API paga; use só quando o pedido exigir." : "");

    public override JsonElement JsonSchema => Schema;

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var instrucao = arguments.TryGetValue("instrucao", out var i) ? Texto(i) : "";
        JsonElement? dados = arguments.TryGetValue("dados", out var d) && d is not null ? Json(d) : null;
        var args = JsonSerializer.SerializeToElement(new { instrucao, dados });

        var relogio = Stopwatch.StartNew();
        try
        {
            using var cliente = new A2AClient(new Uri(capacidade.Url), http);
            var resposta = await cliente.SendMessageAsync(new SendMessageRequest
            {
                Message = new PedidoHabilidade(capacidade.Habilidade, dados, instrucao).ParaMensagem(),
            }, cancellationToken);
            var r = RespostaHabilidade.Ler(resposta);

            execucao.Registrar(Name, "agente", args, r.Erro is null, r.Erro ?? r.Resumo, relogio.ElapsedMilliseconds);
            return execucao.Limitar(JsonSerializer.Serialize(new { resumo = r.Resumo, resultado = r.Resultado, erro = r.Erro }));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // O modelo recebe o erro como resultado e pode tentar outro caminho ou explicar ao usuário.
            execucao.Registrar(Name, "agente", args, false, ex.Message, relogio.ElapsedMilliseconds);
            return JsonSerializer.Serialize(new { erro = $"Falha ao chamar o agente {capacidade.Origem}: {ex.Message}" });
        }
    }

    private static string Texto(object? valor) => valor switch
    {
        null => "",
        JsonElement { ValueKind: JsonValueKind.String } e => e.GetString() ?? "",
        JsonElement e => e.ToString(),
        _ => valor.ToString() ?? "",
    };

    private static JsonElement? Json(object valor) => valor switch
    {
        JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
        JsonElement e => e.Clone(),
        _ => JsonSerializer.SerializeToElement(valor),
    };
}

/// <summary>Uma ferramenta MCP de leitura, chamada direto pelo supervisor. Só embrulha para registrar o passo e limitar o tamanho.</summary>
public sealed class FerramentaMcp(AIFunction interna, Capacidade capacidade, Execucao execucao) : AIFunction
{
    public override string Name { get; } = NomesFerramenta.De(capacidade.Origem, capacidade.Habilidade);
    public override string Description { get; } = $"[ferramenta MCP {capacidade.Origem}, só leitura] {interna.Description}";
    public override JsonElement JsonSchema => interna.JsonSchema;

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var args = JsonSerializer.SerializeToElement(arguments.ToDictionary(kv => kv.Key, kv => kv.Value));
        var relogio = Stopwatch.StartNew();
        try
        {
            var resultado = await interna.InvokeAsync(arguments, cancellationToken);
            var texto = resultado is JsonElement e ? e.ToString() : JsonSerializer.Serialize(resultado);
            execucao.Registrar(Name, "mcp", args, true, $"{texto.Length} caracteres", relogio.ElapsedMilliseconds);
            return execucao.Limitar(texto);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            execucao.Registrar(Name, "mcp", args, false, ex.Message, relogio.ElapsedMilliseconds);
            return JsonSerializer.Serialize(new { erro = ex.Message });
        }
    }
}
