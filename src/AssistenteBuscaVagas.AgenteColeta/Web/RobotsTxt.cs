using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;

namespace AssistenteBuscaVagas.AgenteColeta.Web;

/// <summary>
/// Confere o robots.txt do site antes de pedir a página ao Firecrawl.
/// Implementa o essencial da RFC 9309: grupo do nosso user-agent (ou "*"), regras Allow/Disallow,
/// curingas "*" e "$" e "a regra mais longa vence". Sem robots.txt (404) = permitido;
/// erro de rede ou 5xx = bloqueado (na dúvida, não acessa).
/// </summary>
public sealed class RobotsTxt(HttpClient http, TimeProvider relogio, ILogger<RobotsTxt> logger)
{
    public const string UserAgent = "AssistenteBuscaVagas";
    private static readonly TimeSpan Validade = TimeSpan.FromHours(12);

    private readonly ConcurrentDictionary<string, (DateTimeOffset LidoEm, Regras Regras)> _cache = new();

    public async Task<bool> PermiteAsync(Uri url, CancellationToken ct)
    {
        var origem = url.GetLeftPart(UriPartial.Authority);
        var agora = relogio.GetUtcNow();
        if (!_cache.TryGetValue(origem, out var item) || agora - item.LidoEm > Validade)
        {
            item = (agora, await BaixarAsync(origem, ct));
            _cache[origem] = item;
        }
        return item.Regras.Permite(url.PathAndQuery);
    }

    private async Task<Regras> BaixarAsync(string origem, CancellationToken ct)
    {
        try
        {
            using var resposta = await http.GetAsync($"{origem}/robots.txt", ct);
            if (resposta.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) return Regras.TudoPermitido;
            if (!resposta.IsSuccessStatusCode) return Regras.TudoBloqueado;
            return Regras.Ler(await resposta.Content.ReadAsStringAsync(ct), UserAgent);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Não foi possível ler {Origem}/robots.txt ({Erro}); o site será ignorado.", origem, ex.Message);
            return Regras.TudoBloqueado;
        }
    }

    public sealed class Regras
    {
        private readonly List<(bool Permitir, string Padrao, Regex Regex)> _regras;

        private Regras(List<(bool, string, Regex)> regras) => _regras = regras;

        public static Regras TudoPermitido { get; } = new([]);
        public static Regras TudoBloqueado { get; } = new([(false, "/", Compilar("/"))]);

        public bool Permite(string caminho)
        {
            // A regra de padrão mais longo vence; em empate, Allow vence.
            var vencedora = _regras
                .Where(r => r.Regex.IsMatch(caminho))
                .OrderByDescending(r => r.Padrao.Length)
                .ThenByDescending(r => r.Permitir)
                .FirstOrDefault();
            return vencedora.Regex is null || vencedora.Permitir;
        }

        public static Regras Ler(string conteudo, string userAgent)
        {
            var grupos = new List<(List<string> Agentes, List<(bool, string)> Regras)>();
            (List<string> Agentes, List<(bool, string)> Regras)? atual = null;
            var lendoAgentes = false;

            foreach (var linhaBruta in conteudo.Split('\n'))
            {
                var linha = linhaBruta.Split('#')[0].Trim();
                var sep = linha.IndexOf(':');
                if (sep <= 0) continue;
                var campo = linha[..sep].Trim().ToLowerInvariant();
                var valor = linha[(sep + 1)..].Trim();

                if (campo == "user-agent")
                {
                    if (!lendoAgentes || atual is null)
                    {
                        atual = ([], []);
                        grupos.Add(atual.Value);
                    }
                    atual.Value.Agentes.Add(valor.ToLowerInvariant());
                    lendoAgentes = true;
                }
                else if (campo is "allow" or "disallow" && atual is not null)
                {
                    lendoAgentes = false;
                    if (valor.Length > 0) atual.Value.Regras.Add((campo == "allow", valor));
                }
            }

            var ua = userAgent.ToLowerInvariant();
            var grupo = grupos.Where(g => g.Agentes.Any(a => a != "*" && ua.Contains(a))).ToList();
            if (grupo.Count == 0) grupo = grupos.Where(g => g.Agentes.Contains("*")).ToList();

            return new(grupo.SelectMany(g => g.Regras).Select(r => (r.Item1, r.Item2, Compilar(r.Item2))).ToList());
        }

        private static Regex Compilar(string padrao)
        {
            var ancorado = padrao.EndsWith('$');
            var corpo = Regex.Escape(ancorado ? padrao[..^1] : padrao).Replace(@"\*", ".*");
            return new Regex("^" + corpo + (ancorado ? "$" : ""), RegexOptions.CultureInvariant);
        }
    }
}
