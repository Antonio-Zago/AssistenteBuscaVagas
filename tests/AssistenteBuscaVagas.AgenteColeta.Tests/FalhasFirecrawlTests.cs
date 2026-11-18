using System.Net;
using AssistenteBuscaVagas.AgenteColeta.Agente;
using AssistenteBuscaVagas.AgenteColeta.Web;

namespace AssistenteBuscaVagas.AgenteColeta.Tests;

public class FalhasFirecrawlTests
{
    /// <summary>Firecrawl falso: demora (ou responde um status) e conta quantas requisições recebeu.</summary>
    private sealed class FirecrawlFalso(TimeSpan demora, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Requisicoes;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Requisicoes);
            await Task.Delay(demora, ct);
            return new HttpResponseMessage(status) { Content = new StringContent("""{"data":{"web":[]}}""") };
        }
    }

    private static (FirecrawlCliente Cliente, FirecrawlFalso Falso) Criar(TimeSpan demora, TimeSpan timeout, HttpStatusCode status = HttpStatusCode.OK)
    {
        var falso = new FirecrawlFalso(demora, status);
        var http = new HttpClient(falso) { BaseAddress = new Uri("https://firecrawl.test/v2/"), Timeout = timeout };
        return (new FirecrawlCliente(http), falso);
    }

    [Fact]
    public async Task BuscaLentaViraTempoEsgotadoSemNovaTentativa()
    {
        var (cliente, falso) = Criar(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(100));

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => cliente.BuscarAsync("dev", 10, null, [], default));

        Assert.True(FerramentasColeta.FalhaDeRede(ex, default));
        Assert.Equal("tempo esgotado", FerramentasColeta.Motivo(ex));
        Assert.Equal(1, falso.Requisicoes); // nenhuma repetição automática: cada uma seria outra busca cobrada
    }

    [Fact]
    public async Task ErroDoFirecrawlTrazOStatus()
    {
        var (cliente, _) = Criar(TimeSpan.Zero, TimeSpan.FromSeconds(5), HttpStatusCode.PaymentRequired);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => cliente.BuscarAsync("dev", 10, null, [], default));

        Assert.True(FerramentasColeta.FalhaDeRede(ex, default));
        Assert.Equal("Firecrawl respondeu 402", FerramentasColeta.Motivo(ex));
    }

    [Fact]
    public async Task CancelamentoPedidoPorQuemChamouNaoEhFalhaDeRede()
    {
        var (cliente, _) = Criar(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cliente.BuscarAsync("dev", 10, null, [], cts.Token));

        Assert.False(FerramentasColeta.FalhaDeRede(ex, cts.Token));
    }
}
