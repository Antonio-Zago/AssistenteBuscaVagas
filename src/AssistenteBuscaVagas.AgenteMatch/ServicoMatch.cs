using AssistenteBuscaVagas.AgenteMatch.Funil;
using AssistenteBuscaVagas.AgenteMatch.Llm;
using Microsoft.Extensions.Options;
using Rotulo = AssistenteBuscaVagas.AgenteMatch.Llm.Rotulo;

namespace AssistenteBuscaVagas.AgenteMatch;

/// <summary>Erro que o chamador consegue corrigir (perfil ausente, nenhum rótulo...). Vira HTTP 4xx ou "erro" no A2A.</summary>
public sealed class ErroDeUsoException(string mensagem, int status) : InvalidOperationException(mensagem)
{
    public int Status { get; } = status;
}

public sealed record VagaPendente(int VagaId, string Titulo, string Empresa, string? Local, bool Remoto, string Url, int? PosicaoBm25, string? DescartadaPor);

public sealed record ResultadoPendentes(int JaRotuladas, IReadOnlyList<VagaPendente> Pendentes);

/// <summary>
/// As operações do agente_match, usadas pelos endpoints HTTP e pelas habilidades A2A. Cada chamada abre a própria
/// conexão MCP com o mcp_vagas (o servidor é stateless e as chamadas são poucas).
/// </summary>
public sealed class ServicoMatch(
    IHttpClientFactory http,
    ILoggerFactory logs,
    IOptions<FunilOptions> funilOpcoes,
    IOptions<AvaliacaoOptions> avaliacaoOpcoes,
    AvaliadorClaude avaliador)
{
    private readonly ILogger _log = logs.CreateLogger<ServicoMatch>();

    /// <summary>Fase 3: filtros rígidos + BM25. Grátis.</summary>
    public async Task<ResultadoFunil> FunilAsync(int? limite, int? dias, CancellationToken ct)
    {
        await using var mcp = await McpVagasCliente.ConectarAsync(http, logs, ct);
        var perfil = await PerfilAsync(mcp, ct);
        var funil = new FunilMatch(funilOpcoes.Value);
        var resultado = funil.Executar(perfil, await LerJanelaAsync(mcp, funil, perfil, dias, ct), limite);
        // Só contagens no log: o perfil é dado pessoal.
        _log.LogInformation("Funil: {Etapas}", string.Join(" → ", resultado.Etapas.Select(e => $"{e.Etapa}: {e.Vagas}")));
        return resultado;
    }

    /// <summary>Fase 4: funil completo com o Claude no top do BM25. Pago, mas avaliações guardadas não são refeitas.</summary>
    public async Task<ResultadoMatch> RecomendarAsync(int? dias, CancellationToken ct)
    {
        await using var mcp = await McpVagasCliente.ConectarAsync(http, logs, ct);
        var perfil = await PerfilAsync(mcp, ct);
        var funil = new FunilMatch(funilOpcoes.Value);
        var vagas = await LerJanelaAsync(mcp, funil, perfil, dias, ct);
        var r = await new Recomendador(funil, avaliador, mcp, avaliacaoOpcoes.Value, _log).ExecutarAsync(perfil, vagas, ct);
        _log.LogInformation("Recomendações: {Etapas}; custo estimado US$ {Custo}",
            string.Join(" → ", r.Etapas.Select(e => $"{e.Etapa}: {e.Vagas}")), r.Custo.CustoEstimadoUsd);
        return r;
    }

    /// <summary>Vagas sem rótulo, na ordem em que vale a pena rotular.</summary>
    public async Task<ResultadoPendentes> PendentesAsync(int? limite, int? dias, CancellationToken ct)
    {
        var o = funilOpcoes.Value;
        await using var mcp = await McpVagasCliente.ConectarAsync(http, logs, ct);
        var perfil = await PerfilAsync(mcp, ct);
        var funil = new FunilMatch(o);
        var rotuladas = (await mcp.ListarRotulosAsync(ct)).Select(r => r.VagaId).ToHashSet();
        var vagas = (await mcp.ListarVagasAsync(null, Math.Clamp(dias ?? o.DiasMaximos, 1, 365), o.MaximoVagasLidas, ct))
            .Where(v => !rotuladas.Contains(v.Id)).ToList();

        var ranking = funil.Executar(perfil, vagas, 100).Recomendacoes.ToDictionary(r => r.VagaId, r => r.Posicao);
        var filtros = funil.Filtros(perfil);
        var pendentes = vagas
            .Select(v => new VagaPendente(v.Id, v.Titulo, v.Empresa, v.Local, v.Remoto, v.Url,
                ranking.TryGetValue(v.Id, out var p) ? p : null, filtros.Avaliar(v)?.Detalhe))
            .OrderBy(x => x.PosicaoBm25 ?? int.MaxValue)
            .ThenBy(x => x.DescartadaPor is null ? 0 : 1)
            .ThenByDescending(x => x.VagaId)
            .Take(Math.Clamp(limite ?? 20, 1, 200))
            .ToList();
        return new(rotuladas.Count, pendentes);
    }

    /// <summary>Precision@5 sem e com LLM sobre as vagas rotuladas.</summary>
    public async Task<ResultadoAvaliacao> AvaliarAsync(bool usarLlm, CancellationToken ct)
    {
        await using var mcp = await McpVagasCliente.ConectarAsync(http, logs, ct);
        var perfil = await PerfilAsync(mcp, ct);

        var rotulos = (await mcp.ListarRotulosAsync(ct)).Select(r => new Rotulo(r.VagaId, r.Relevante, r.Observacao)).ToList();
        if (rotulos.Count == 0)
            throw new ErroDeUsoException("Nenhuma vaga rotulada. Use GET /avaliacao/pendentes e PUT /avaliacao/rotulos/{vagaId}.",
                StatusCodes.Status422UnprocessableEntity);

        var vagas = await mcp.ListarVagasPorIdsAsync(rotulos.Select(r => r.VagaId).ToList(), ct);
        var funil = new FunilMatch(funilOpcoes.Value);
        var recomendador = usarLlm ? new Recomendador(funil, avaliador, mcp, avaliacaoOpcoes.Value, _log) : null;

        var r = await AvaliacaoFunil.ExecutarAsync(funil, recomendador, avaliacaoOpcoes.Value, perfil, vagas, rotulos, ct);
        _log.LogInformation("Avaliação: {Rotuladas} rotuladas, P@5 sem LLM {Sem}, com LLM {Com}",
            r.VagasRotuladas, r.SemLlm.PrecisaoEm5, r.ComLlm?.PrecisaoEm5);
        return r;
    }

    private static async Task<PerfilCompleto> PerfilAsync(McpVagasCliente mcp, CancellationToken ct) =>
        await mcp.ObterPerfilAsync(ct)
        ?? throw new ErroDeUsoException("Perfil não cadastrado. Use o agente_perfil primeiro.", StatusCodes.Status404NotFound);

    // Quando a preferência é só remoto, o filtro já vai para a consulta ao banco.
    private Task<IReadOnlyList<VagaParaMatch>> LerJanelaAsync(McpVagasCliente mcp, FunilMatch funil, PerfilCompleto perfil, int? dias, CancellationToken ct)
    {
        var o = funilOpcoes.Value;
        return mcp.ListarVagasAsync(funil.Filtros(perfil).SoRemotas ? true : null, Math.Clamp(dias ?? o.DiasMaximos, 1, 365), o.MaximoVagasLidas, ct);
    }
}
