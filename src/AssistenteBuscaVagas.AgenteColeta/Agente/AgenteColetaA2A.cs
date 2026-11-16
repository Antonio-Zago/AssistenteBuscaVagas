using System.Text.Json;
using AssistenteBuscaVagas.Comum.A2A;

namespace AssistenteBuscaVagas.AgenteColeta.Agente;

/// <summary>
/// O agente_coleta como agente A2A. O serviço de coleta é scoped (orçamento, relatório da execução),
/// então cada pedido abre o seu escopo.
/// </summary>
public sealed class AgenteColetaA2A(IServiceScopeFactory escopos, ILogger<AgenteColetaA2A> logger) : AgenteA2A(logger)
{
    public static readonly IReadOnlyList<Habilidade> Lista =
    [
        new("coletar_remotive", "Coletar vagas da Remotive",
            "Busca vagas novas na API pública da Remotive (vagas remotas de tecnologia) e grava no banco. " +
            "Fluxo fixo, sem IA e grátis; respeita o intervalo mínimo de 6 horas entre consultas. Sem dados.",
            ["coletar", "buscar", "vagas", "novas", "remotive", "atualizar", "remotas"],
            ["busque vagas novas", "atualize as vagas", "colete vagas remotas"]),
        new("coletar_vagas", "Coletar vagas na web",
            "Roda o agente de coleta: o Claude planeja buscas na web (Firecrawl) e na Remotive, abre as páginas de vaga, " +
            "extrai os dados estruturados e grava no banco. Demora alguns minutos e tem orçamento diário de créditos. " +
            "Dados opcionais: {\"pedido\": texto com o foco da busca, ex.: \"priorize vagas de .NET em São Paulo\"}.",
            ["coletar", "buscar", "vagas", "web", "internet", "sites", "empresas", "carreiras", "novas"],
            ["procure vagas na internet", "busque vagas de .NET em sites de empresas"],
            Paga: true),
    ];

    protected override IReadOnlyList<Habilidade> Habilidades => Lista;

    private sealed record DadosColeta(string? Pedido);

    protected override async Task<RespostaHabilidade> ExecutarAsync(PedidoHabilidade pedido, CancellationToken ct)
    {
        await using var escopo = escopos.CreateAsyncScope();
        var agente = escopo.ServiceProvider.GetRequiredService<AgenteColetaServico>();

        switch (pedido.Habilidade)
        {
            case "coletar_remotive":
            {
                var r = await agente.ColetarRemotiveAsync(ct);
                return RespostaHabilidade.Ok($"Remotive: {r.Status}; {r.Novas} novas, {r.Atualizadas} atualizadas.", r);
            }
            case "coletar_vagas":
            {
                var d = LerDados<DadosColeta>(pedido);
                var r = await agente.ExecutarAsync(d?.Pedido ?? (string.IsNullOrWhiteSpace(pedido.Texto) ? null : pedido.Texto), ct);
                return RespostaHabilidade.Ok($"{r.Novas} vagas novas, {r.Atualizadas} atualizadas. {r.ResumoDoAgente}", r);
            }
            default:
                throw new JsonException($"Habilidade não tratada: {pedido.Habilidade}");
        }
    }
}
