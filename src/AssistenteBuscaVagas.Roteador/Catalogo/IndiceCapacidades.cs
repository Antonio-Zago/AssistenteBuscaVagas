using System.Text.Json;
using AssistenteBuscaVagas.Comum.Busca;

namespace AssistenteBuscaVagas.Roteador.Catalogo;

/// <summary>
/// Uma coisa que o sistema sabe fazer: uma habilidade de agente A2A ou uma ferramenta MCP.
/// É o que o supervisor recebe para decidir quem chamar.
/// </summary>
public sealed record Capacidade(
    string Id,
    [property: System.ComponentModel.Description("\"agente\" (A2A) ou \"mcp\" (ferramenta de um servidor MCP).")] string Tipo,
    string Origem,
    string Habilidade,
    string Nome,
    string Descricao,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Exemplos,
    string Url,
    bool Paga,
    bool SomenteLeitura,
    JsonElement? Esquema);

public sealed record CapacidadeEncontrada(Capacidade Capacidade, double Pontuacao, IReadOnlyList<TermoPontuado> TermosQuePontuaram);

/// <summary>
/// Índice BM25 sobre as capacidades: o mesmo algoritmo do funil de vagas, aplicado a "quem sabe fazer isto?".
/// Com dezenas de agentes e ferramentas, o supervisor não pode receber tudo no prompt; recebe só as k mais
/// parecidas com o pedido.
/// </summary>
public sealed class IndiceCapacidades
{
    private const double PesoNome = 2, PesoTags = 2, PesoExemplos = 1.5, PesoDescricao = 1;

    private readonly IReadOnlyList<Capacidade> _capacidades;
    private readonly IndiceBm25 _bm25;

    public IndiceCapacidades(IReadOnlyList<Capacidade> capacidades)
    {
        _capacidades = capacidades;
        _bm25 = new IndiceBm25(capacidades.Select(Documento).ToList());
    }

    public IReadOnlyList<Capacidade> Todas => _capacidades;

    public IReadOnlyList<CapacidadeEncontrada> Buscar(string consulta, int k)
    {
        var termos = Termos(consulta).Distinct().ToDictionary(t => t, _ => 1.0);
        if (termos.Count == 0) return [];

        return _capacidades
            .Select((c, i) =>
            {
                var contribuicoes = _bm25.Contribuicoes(i, termos);
                return new CapacidadeEncontrada(c, Math.Round(contribuicoes.Sum(x => x.Pontos), 3),
                    contribuicoes.Select(x => x with { Pontos = Math.Round(x.Pontos, 3) }).ToList());
            })
            .Where(x => x.Pontuacao > 0)
            .OrderByDescending(x => x.Pontuacao)
            .Take(Math.Clamp(k, 1, 50))
            .ToList();
    }

    private static Dictionary<string, double> Documento(Capacidade c)
    {
        var tf = new Dictionary<string, double>();
        void Contar(string? texto, double peso)
        {
            foreach (var t in Termos(texto)) tf[t] = tf.GetValueOrDefault(t) + peso;
        }

        // O id ("recomendar_vagas") também diz o que a capacidade faz.
        Contar($"{c.Nome} {c.Habilidade.Replace('_', ' ')}", PesoNome);
        foreach (var tag in c.Tags) Contar(tag, PesoTags);
        foreach (var ex in c.Exemplos) Contar(ex, PesoExemplos);
        Contar(c.Descricao, PesoDescricao);
        return tf;
    }

    /// <summary>
    /// Tokenizador do funil + um radical bem simples: tira o "s" final ("vagas" → "vaga", "skills" → "skill").
    /// Pedidos chegam no plural e descrições no singular (ou o contrário); sem isso, não se encontram.
    /// </summary>
    public static List<string> Termos(string? texto) =>
        Tokenizador.Termos(texto).Select(Radical).ToList();

    internal static string Radical(string termo) =>
        termo.Length > 3 && termo.EndsWith('s') && !termo.EndsWith("ss") && char.IsLetter(termo[^2]) ? termo[..^1] : termo;
}
