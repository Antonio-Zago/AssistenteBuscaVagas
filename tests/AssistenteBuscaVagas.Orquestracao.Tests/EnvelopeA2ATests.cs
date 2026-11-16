using System.Text.Json;
using A2A;
using AssistenteBuscaVagas.Comum.A2A;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssistenteBuscaVagas.Orquestracao.Tests;

public class EnvelopeA2ATests
{
    [Fact]
    public void PedidoIdaEVolta()
    {
        var dados = JsonSerializer.SerializeToElement(new { modalidades = new[] { "Híbrido" } });
        var mensagem = new PedidoHabilidade("atualizar_preferencias", dados, "aceito híbrido").ParaMensagem();

        var lido = PedidoHabilidade.Ler(mensagem);

        Assert.Equal("atualizar_preferencias", lido.Habilidade);
        Assert.Equal("aceito híbrido", lido.Texto);
        Assert.Equal("Híbrido", lido.Dados!.Value.GetProperty("modalidades")[0].GetString());
    }

    [Fact]
    public void MensagemSoComTextoNaoTemHabilidade()
    {
        var mensagem = new Message { MessageId = "1", Role = Role.User, Parts = [Part.FromText("oi")] };
        var lido = PedidoHabilidade.Ler(mensagem);
        Assert.Null(lido.Habilidade);
        Assert.Equal("oi", lido.Texto);
    }

    [Fact]
    public void RespostaIdaEVolta()
    {
        var ok = RespostaHabilidade.Ok("2 vagas", new { total = 2 });
        var lida = RespostaHabilidade.Ler(new SendMessageResponse { Message = Mensagem(ok.ParaPartes()) });

        Assert.Null(lida.Erro);
        Assert.Equal("2 vagas", lida.Resumo);
        Assert.Equal(2, lida.Resultado!.Value.GetProperty("total").GetInt32());

        var falha = RespostaHabilidade.Ler(new SendMessageResponse { Message = Mensagem(RespostaHabilidade.Falha("sem perfil").ParaPartes()) });
        Assert.Equal("sem perfil", falha.Erro);
    }

    [Fact]
    public void CartaoMarcaHabilidadePaga()
    {
        var card = CartaoA2A.Criar("a", "d", "http://a/a2a",
            [new Habilidade("x", "X", "faz x", ["t"], ["ex"], Paga: true), new Habilidade("y", "Y", "faz y", [], [])]);

        Assert.Contains(CartaoA2A.TagPaga, card.Skills![0].Tags!);
        Assert.DoesNotContain(CartaoA2A.TagPaga, card.Skills[1].Tags ?? []);
        Assert.Equal("http://a/a2a", card.SupportedInterfaces![0].Url);
    }

    private sealed class AgenteTeste() : AgenteA2A(NullLogger.Instance)
    {
        protected override IReadOnlyList<Habilidade> Habilidades { get; } = [new("eco", "Eco", "repete", [], [])];

        protected override Task<RespostaHabilidade> ExecutarAsync(PedidoHabilidade pedido, CancellationToken ct) =>
            pedido.Texto == "explode"
                ? throw new ArgumentException("dados inválidos")
                : Task.FromResult(RespostaHabilidade.Ok($"eco: {pedido.Texto}", null));
    }

    [Theory]
    [InlineData("eco", "olá", null, "eco: olá")]
    [InlineData("nao_existe", "olá", "Habilidade desconhecida", null)]
    [InlineData(null, "olá", "Informe a habilidade", null)]
    [InlineData("eco", "explode", "dados inválidos", null)]
    public async Task AgenteDespachaPelaHabilidade(string? habilidade, string texto, string? erroEsperado, string? resumoEsperado)
    {
        var mensagem = habilidade is null
            ? new Message { MessageId = "1", Role = Role.User, Parts = [Part.FromText(texto)] }
            : new PedidoHabilidade(habilidade, null, texto).ParaMensagem();
        var fila = new AgentEventQueue(10);

        await new AgenteTeste().ExecuteAsync(new RequestContext { Message = mensagem, ContextId = "c", TaskId = "t", StreamingResponse = false }, fila, default);
        fila.Complete(null);

        Message? resposta = null;
        await foreach (var evento in fila) resposta ??= evento.Message;
        var lida = RespostaHabilidade.Ler(new SendMessageResponse { Message = resposta });

        if (erroEsperado is null) Assert.Equal(resumoEsperado, lida.Resumo);
        else Assert.Contains(erroEsperado, lida.Erro);
    }

    private static Message Mensagem(List<Part> partes) => new() { MessageId = "r", Role = Role.Agent, Parts = partes };
}
