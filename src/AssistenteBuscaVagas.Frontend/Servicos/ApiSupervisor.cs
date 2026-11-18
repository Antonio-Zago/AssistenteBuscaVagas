using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AssistenteBuscaVagas.Frontend.Servicos;

/// <summary>Erro que a tela mostra ao usuário (vem do ProblemDetails do supervisor).</summary>
public sealed class ErroApiException(string mensagem) : Exception(mensagem);

/// <summary>
/// O único cliente do frontend: tudo passa pelo supervisor ("http://supervisor", resolvido pelo Aspire).
/// As leituras do /api são grátis; só AvaliarAgora e Conversar chamam o Claude.
/// </summary>
public sealed class ApiSupervisor(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<List<ExecucaoDia>> ExecucoesAsync(int limite = 14, CancellationToken ct = default) =>
        LerAsync<List<ExecucaoDia>>(http.GetAsync($"api/execucoes?limite={limite}", ct), ct, padrao: []);

    public Task<ResultadoMatch> AvaliarAgoraAsync(bool somenteNovas, CancellationToken ct = default) =>
        LerAsync<ResultadoMatch>(http.PostAsync($"api/recomendacoes/avaliar?somenteNovas={(somenteNovas ? "true" : "false")}", null, ct), ct);

    public Task<ResultadoAvaliacoes> AvaliacoesAsync(int notaMinima, bool soPerfilAtual, bool soRecomendadas, string? busca, int limite = 100, CancellationToken ct = default) =>
        LerAsync<ResultadoAvaliacoes>(http.GetAsync(
            $"api/avaliacoes?notaMinima={notaMinima}&soPerfilAtual={(soPerfilAtual ? "true" : "false")}&soRecomendadas={(soRecomendadas ? "true" : "false")}" +
            $"&limite={limite}{(string.IsNullOrWhiteSpace(busca) ? "" : "&busca=" + Uri.EscapeDataString(busca.Trim()))}", ct), ct);

    public Task<EstatisticasSkills> LacunasAsync(int notaMinima, CancellationToken ct = default) =>
        LerAsync<EstatisticasSkills>(http.GetAsync($"api/lacunas?notaMinima={notaMinima}", ct), ct);

    public Task<List<Candidatura>> CandidaturasAsync(CancellationToken ct = default) =>
        LerAsync<List<Candidatura>>(http.GetAsync("api/candidaturas", ct), ct, padrao: []);

    public Task<Candidatura> AtualizarCandidaturaAsync(int vagaId, string status, string? notas, CancellationToken ct = default) =>
        LerAsync<Candidatura>(http.PutAsJsonAsync($"api/candidaturas/{vagaId}", new { status, notas }, ct), ct);

    public async Task RemoverCandidaturaAsync(int vagaId, CancellationToken ct = default)
    {
        using var r = await http.DeleteAsync($"api/candidaturas/{vagaId}", ct);
        if (r.StatusCode is not (HttpStatusCode.NoContent or HttpStatusCode.NotFound))
            throw new ErroApiException(await DetalheAsync(r, ct));
    }

    public Task<RespostaConversa> ConversarAsync(string mensagem, CancellationToken ct = default) =>
        LerAsync<RespostaConversa>(http.PostAsJsonAsync("conversa", new { mensagem }, ct), ct);

    public Task<RotaPrevista> RotaAsync(string mensagem, CancellationToken ct = default) =>
        LerAsync<RotaPrevista>(http.GetAsync($"conversa/rota?mensagem={Uri.EscapeDataString(mensagem)}", ct), ct);

    private static async Task<T> LerAsync<T>(Task<HttpResponseMessage> chamada, CancellationToken ct, T? padrao = default)
    {
        HttpResponseMessage r;
        try
        {
            r = await chamada;
        }
        catch (HttpRequestException ex)
        {
            throw new ErroApiException($"Supervisor indisponível: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ErroApiException("O supervisor demorou demais para responder.");
        }

        using (r)
        {
            if (r.StatusCode == HttpStatusCode.NotFound && padrao is not null) return padrao;
            if (!r.IsSuccessStatusCode) throw new ErroApiException(await DetalheAsync(r, ct));
            return await r.Content.ReadFromJsonAsync<T>(Json, ct) ?? padrao ?? throw new ErroApiException("Resposta vazia do supervisor.");
        }
    }

    private static async Task<string> DetalheAsync(HttpResponseMessage r, CancellationToken ct)
    {
        var corpo = await r.Content.ReadAsStringAsync(ct);
        try
        {
            using var doc = JsonDocument.Parse(corpo);
            if (doc.RootElement.TryGetProperty("detail", out var d) && d.GetString() is { Length: > 0 } detalhe) return detalhe;
        }
        catch (JsonException) { }
        return $"O supervisor respondeu {(int)r.StatusCode}.";
    }
}
