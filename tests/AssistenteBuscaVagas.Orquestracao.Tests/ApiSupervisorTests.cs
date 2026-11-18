using System.Net;
using System.Text;
using AssistenteBuscaVagas.Frontend.Servicos;

namespace AssistenteBuscaVagas.Orquestracao.Tests;

/// <summary>O cliente do frontend contra um supervisor falso: formato do JSON e mensagens de erro.</summary>
public class ApiSupervisorTests
{
    private sealed class SupervisorFalso(HttpStatusCode status, string corpo, string tipo = "application/json") : HttpMessageHandler
    {
        public HttpRequestMessage? Ultima;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Ultima = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(corpo, Encoding.UTF8, tipo) });
        }
    }

    private static (ApiSupervisor Api, SupervisorFalso Falso) Criar(HttpStatusCode status, string corpo, string tipo = "application/json")
    {
        var falso = new SupervisorFalso(status, corpo, tipo);
        return (new ApiSupervisor(new HttpClient(falso) { BaseAddress = new Uri("http://supervisor/") }), falso);
    }

    [Fact]
    public async Task LeAsExecucoesNoFormatoDoWorker()
    {
        // Mesmo formato que o worker grava (ResumoDiario, JSON web/camelCase) e o mcp_vagas devolve.
        const string json = """
            [{"data":"2026-11-17","status":"sucesso","tentativas":1,"iniciadaEm":"2026-11-17T12:00:00+00:00","concluidaEm":"2026-11-17T12:03:27+00:00",
              "resumo":{"data":"2026-11-17","passos":[{"nome":"coleta web","sucesso":true,"resumo":"3 novas","duracaoMs":179000}],
                        "vagasRecomendadas":[{"vagaId":17,"titulo":"Senior .NET Developer","empresa":"SysMap","local":"Brazil","nota":82,
                          "justificativa":"Pede C# e .NET.","lacunas":["Docker"],"impeditivos":[],"url":"https://e.com/17"}],
                        "abaixoDaNotaMinima":5,"custoMatchUsd":0.1449,"texto":"Vagas do dia"}}]
            """;
        var (api, falso) = Criar(HttpStatusCode.OK, json);

        var execucoes = await api.ExecucoesAsync(7);

        Assert.Equal("api/execucoes?limite=7", falso.Ultima!.RequestUri!.PathAndQuery.TrimStart('/'));
        var e = Assert.Single(execucoes);
        Assert.Equal(5, e.Resumo!.AbaixoDaNotaMinima);
        var cartao = VagaCartao.De(Assert.Single(e.Resumo.VagasRecomendadas!));
        Assert.Equal(82, cartao.Nota);
        Assert.Equal(["Docker"], cartao.Lacunas);
        Assert.Empty(cartao.PontosFortes);
    }

    [Fact]
    public async Task LeOResultadoDoMatch()
    {
        const string json = """
            {"etapas":[{"etapa":"recomendadas","vagas":1,"descartes":null}],
             "recomendacoes":[{"posicao":1,"vagaId":8,"titulo":"Full-Stack","empresa":"F","local":"Worldwide","remoto":true,"fonte":"Remotive",
               "url":"https://e.com/8","nota":45,"justificativa":"Parcial.","pontosFortes":["C#"],"lacunas":["React"],"impeditivos":[],"posicaoBm25":2,"doCache":true}],
             "lacunasFrequentes":[],"custo":{"chamadasLlm":0,"reaproveitadasDoCache":1,"falhas":0,"tokens":{"entrada":0,"saida":0,"cacheLeitura":0,"cacheEscrita":0},"custoEstimadoUsd":0},
             "ordemFinal":[8]}
            """;
        var (api, falso) = Criar(HttpStatusCode.OK, json);

        var r = await api.AvaliarAgoraAsync(somenteNovas: true);

        Assert.Equal(HttpMethod.Post, falso.Ultima!.Method);
        Assert.EndsWith("somenteNovas=true", falso.Ultima.RequestUri!.Query);
        var cartao = VagaCartao.De(Assert.Single(r.Recomendacoes!));
        Assert.Equal(["C#"], cartao.PontosFortes);
        Assert.Equal(1, r.Custo!.ReaproveitadasDoCache);
    }

    [Fact]
    public async Task LeOHistoricoDeAvaliacoes()
    {
        const string json = """
            {"total":2,"soPerfilAtual":true,"vagas":[
              {"vagaId":8,"titulo":"Full-Stack","empresa":"F","local":"Worldwide","remoto":true,"url":"https://e.com/8","fonte":"Remotive",
               "publicadaEm":null,"nota":45,"justificativa":"Parcial.","pontosFortes":["C#"],"lacunas":["React"],"impeditivos":[],
               "avaliadaEm":"2026-11-17T12:03:00+00:00","modelo":"claude-opus-5-5","recomendadaEm":null}]}
            """;
        var (api, falso) = Criar(HttpStatusCode.OK, json);

        var r = await api.AvaliacoesAsync(notaMinima: 40, soPerfilAtual: false, soRecomendadas: true, busca: " .NET & co ");

        var q = falso.Ultima!.RequestUri!.Query;
        Assert.Contains("notaMinima=40", q);
        Assert.Contains("soPerfilAtual=false", q);
        Assert.Contains("soRecomendadas=true", q);
        Assert.Contains("busca=.NET%20%26%20co", q);
        Assert.Equal(2, r.Total);
        var v = Assert.Single(r.Vagas!);
        Assert.Null(v.RecomendadaEm);
        Assert.Equal(["React"], VagaCartao.De(v).Lacunas);
    }

    [Fact]
    public async Task ErroMostraODetalheDoProblemDetails()
    {
        var (api, _) = Criar(HttpStatusCode.UnprocessableEntity,
            """{"type":"x","title":"Unprocessable","status":422,"detail":"Etapa inválida: 'xpto'. Use uma de: salva, aplicada, entrevista, oferta, recusada."}""",
            "application/problem+json");

        var ex = await Assert.ThrowsAsync<ErroApiException>(() => api.AtualizarCandidaturaAsync(1, "xpto", null));

        Assert.StartsWith("Etapa inválida", ex.Message);
    }

    [Fact]
    public async Task ErroSemProblemDetailsMostraOStatus()
    {
        var (api, _) = Criar(HttpStatusCode.BadGateway, "<html>bad gateway</html>", "text/html");
        var ex = await Assert.ThrowsAsync<ErroApiException>(() => api.LacunasAsync(0));
        Assert.Equal("O supervisor respondeu 502.", ex.Message);
    }

    [Fact]
    public async Task ListaInexistenteViraListaVazia()
    {
        var (api, _) = Criar(HttpStatusCode.NotFound, "");
        Assert.Empty(await api.CandidaturasAsync());
    }

    [Fact]
    public async Task RemoverIgnoraQuemJaNaoEstava()
    {
        var (api, falso) = Criar(HttpStatusCode.NotFound, "");
        await api.RemoverCandidaturaAsync(3);
        Assert.Equal(HttpMethod.Delete, falso.Ultima!.Method);
    }

    [Fact]
    public void EtapasNaOrdemDoKanban()
    {
        Assert.Equal(["salva", "aplicada", "entrevista", "oferta", "recusada"], Etapas.Todas.Select(e => e.Id));
        Assert.Equal("Entrevista", Etapas.Nome("entrevista"));
    }
}
