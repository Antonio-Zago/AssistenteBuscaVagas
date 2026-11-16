using System.Text.Json;
using AssistenteBuscaVagas.Comum.A2A;

namespace AssistenteBuscaVagas.AgenteMatch;

/// <summary>O agente_match como agente A2A. As descrições das habilidades são o que o roteador indexa e o supervisor lê.</summary>
public sealed class AgenteMatchA2A(ServicoMatch servico, ILogger<AgenteMatchA2A> logger) : AgenteA2A(logger)
{
    public static readonly IReadOnlyList<Habilidade> Lista =
    [
        new("recomendar_vagas", "Recomendar vagas",
            "Recomenda as 5 vagas que mais combinam com o perfil do usuário: filtra, ranqueia com BM25 e o Claude avalia as melhores, " +
            "com nota, justificativa, pontos fortes, lacunas e impeditivos. Traz também as lacunas de skills mais frequentes. " +
            "Dados opcionais: {\"dias\": número de dias da janela de publicação, padrão 30}.",
            ["vagas", "recomendação", "match", "combinam", "melhores", "lacunas", "skills"],
            ["quais vagas combinam comigo?", "me mostre as melhores vagas", "quais skills me faltam?"],
            Paga: true),
        new("ver_funil", "Ver funil sem LLM",
            "Ranqueia as vagas sem IA (filtros rígidos + BM25) e mostra quantas vagas passaram em cada etapa e por que foram descartadas. " +
            "Grátis e rápido; não dá nota nem justificativa detalhada. Dados opcionais: {\"limite\": 1 a 100, \"dias\": número}.",
            ["vagas", "funil", "bm25", "ranking", "descartadas", "filtros"],
            ["quantas vagas passaram nos filtros?", "ranking das vagas sem IA"]),
        new("medir_precisao", "Medir precisão do ranking",
            "Mede a precision@5 do funil sobre as vagas rotuladas à mão: sem LLM e, se pedido, com LLM. " +
            "Dados opcionais: {\"usarLlm\": true|false, padrão false}. Com usarLlm=true gasta API paga para vagas ainda não avaliadas.",
            ["avaliação", "precisão", "precision", "métrica", "rótulos", "qualidade"],
            ["qual a precisão do ranking?", "o LLM melhora as recomendações?"]),
    ];

    protected override IReadOnlyList<Habilidade> Habilidades => Lista;

    private sealed record DadosJanela(int? Dias, int? Limite);
    private sealed record DadosAvaliacao(bool? UsarLlm);

    protected override async Task<RespostaHabilidade> ExecutarAsync(PedidoHabilidade pedido, CancellationToken ct)
    {
        switch (pedido.Habilidade)
        {
            case "recomendar_vagas":
            {
                var d = LerDados<DadosJanela>(pedido);
                var r = await servico.RecomendarAsync(d?.Dias, ct);
                return RespostaHabilidade.Ok(
                    $"{r.Recomendacoes.Count} vagas recomendadas (custo estimado US$ {r.Custo.CustoEstimadoUsd}).", r);
            }
            case "ver_funil":
            {
                var d = LerDados<DadosJanela>(pedido);
                var r = await servico.FunilAsync(d?.Limite, d?.Dias, ct);
                return RespostaHabilidade.Ok(string.Join(" → ", r.Etapas.Select(e => $"{e.Etapa}: {e.Vagas}")), r);
            }
            case "medir_precisao":
            {
                var d = LerDados<DadosAvaliacao>(pedido);
                var r = await servico.AvaliarAsync(d?.UsarLlm ?? false, ct);
                return RespostaHabilidade.Ok(
                    $"{r.VagasRotuladas} vagas rotuladas; precision@5 sem LLM {r.SemLlm.PrecisaoEm5}" +
                    (r.ComLlm is null ? "." : $", com LLM {r.ComLlm.PrecisaoEm5}."), r);
            }
            default:
                throw new JsonException($"Habilidade não tratada: {pedido.Habilidade}");
        }
    }
}
