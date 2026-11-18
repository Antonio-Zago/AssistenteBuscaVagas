using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AssistenteBuscaVagas.Comum.A2A;
using Microsoft.Extensions.Options;

namespace AssistenteBuscaVagas.WorkerDiario;

public sealed record PassoExecucao(string Nome, bool Sucesso, string Resumo, long DuracaoMs);

public sealed record VagaDoDia(int VagaId, string Titulo, string Empresa, string? Local, int Nota, string Justificativa,
    IReadOnlyList<string> Lacunas, IReadOnlyList<string> Impeditivos, string Url);

/// <summary>O resumo de um dia. Vai para o banco (registrar_execucao_diaria) e para o notificador.</summary>
public sealed record ResumoDiario(
    string Data,
    IReadOnlyList<PassoExecucao> Passos,
    IReadOnlyList<VagaDoDia> VagasRecomendadas,
    int AbaixoDaNotaMinima,
    decimal CustoMatchUsd,
    string Texto);

public sealed record ResultadoExecucao(DateOnly Dia, string Status, int Tentativa, ResumoDiario Resumo);

/// <summary>
/// Um dia de trabalho: coleta (Remotive, grátis; web, paga), recomendação só de vagas novas, resumo e notificação.
/// Cada passo falha sozinho: se a coleta na web cair, a recomendação ainda roda com o que já está no banco.
/// </summary>
public sealed class ExecucaoDiariaServico(
    ClienteAgentes agentes,
    IHttpClientFactory http,
    ILoggerFactory logs,
    INotificador notificador,
    IOptions<AgendaOptions> options,
    TimeProvider relogio)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ILogger _log = logs.CreateLogger<ExecucaoDiariaServico>();

    public async Task<ResultadoExecucao> ExecutarAsync(DateOnly dia, int tentativa, IReadOnlyList<VagaDoDia> jaMostradasHoje, CancellationToken ct)
    {
        var o = options.Value;
        var inicio = relogio.GetUtcNow();
        var passos = new List<PassoExecucao>();
        _log.LogInformation("Execução diária {Dia} (tentativa {Tentativa}) iniciada.", dia, tentativa);

        await PassoAsync(passos, "coleta Remotive", () => agentes.ChamarAsync("agente-coleta", "coletar_remotive", "coleta diária", null, ct));
        if (o.ColetaWeb)
            await PassoAsync(passos, "coleta web", () => agentes.ChamarAsync("agente-coleta", "coletar_vagas",
                o.PedidoColetaWeb ?? "faça a coleta diária", o.PedidoColetaWeb is null ? null : new { pedido = o.PedidoColetaWeb }, ct));

        var recomendacao = await PassoAsync(passos, "recomendação", () => agentes.ChamarAsync("agente-match", "recomendar_vagas",
            "recomendações do dia, só vagas ainda não recomendadas", new { dias = o.DiasJanela, somenteNovas = true }, ct));

        var (novas, abaixo, custo) = LerRecomendacoes(recomendacao, o.NotaMinima);
        var vagas = jaMostradasHoje.Concat(novas.Where(n => jaMostradasHoje.All(j => j.VagaId != n.VagaId))).ToList();

        // A recomendação é o objetivo do dia: sem ela, o dia falhou. Coleta caída deixa o dia "parcial".
        var status = recomendacao?.Erro is null && recomendacao is not null
            ? passos.All(p => p.Sucesso) ? "sucesso" : "parcial"
            : "falha";

        var resumo = new ResumoDiario(dia.ToString("yyyy-MM-dd"), passos, vagas, abaixo, custo, "");
        resumo = resumo with { Texto = Formatar(resumo, status, o.NotaMinima) };

        await using (var mcp = await McpVagasCliente.ConectarAsync(http, logs, ct))
            await mcp.RegistrarAsync(dia, status, tentativa, inicio, relogio.GetUtcNow(), resumo, ct);

        try
        {
            await notificador.NotificarAsync(resumo, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Falha ao notificar o resumo de {Dia}", dia);
        }

        _log.LogInformation("Execução diária {Dia}: {Status}, {Vagas} vagas no resumo.", dia, status, vagas.Count);
        return new(dia, status, tentativa, resumo);
    }

    private async Task<RespostaHabilidade?> PassoAsync(List<PassoExecucao> passos, string nome, Func<Task<RespostaHabilidade>> acao)
    {
        var relogioPasso = Stopwatch.StartNew();
        try
        {
            var r = await acao();
            passos.Add(new(nome, r.Erro is null, r.Erro ?? r.Resumo, relogioPasso.ElapsedMilliseconds));
            return r;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Passo {Passo} falhou", nome);
            passos.Add(new(nome, false, ex.Message, relogioPasso.ElapsedMilliseconds));
            return null;
        }
    }

    // Cópia parcial do ResultadoMatch do agente_match (só o que o resumo usa).
    private sealed record RecomendacaoMatch(int VagaId, string Titulo, string Empresa, string? Local, string Url, int Nota,
        string Justificativa, List<string>? Lacunas, List<string>? Impeditivos);
    private sealed record CustoMatch(decimal CustoEstimadoUsd);
    private sealed record ResultadoMatch(List<RecomendacaoMatch> Recomendacoes, CustoMatch? Custo);

    internal static (List<VagaDoDia> Vagas, int Abaixo, decimal Custo) LerRecomendacoes(RespostaHabilidade? resposta, int notaMinima)
    {
        if (resposta?.Resultado is not { } json || resposta.Erro is not null)
            return ([], 0, 0);

        var r = json.Deserialize<ResultadoMatch>(Json);
        var todas = r?.Recomendacoes ?? [];
        var boas = todas
            .Where(x => x.Nota >= notaMinima)
            .Select(x => new VagaDoDia(x.VagaId, x.Titulo, x.Empresa, x.Local, x.Nota, x.Justificativa, x.Lacunas ?? [], x.Impeditivos ?? [], x.Url))
            .ToList();
        return (boas, todas.Count - boas.Count, r?.Custo?.CustoEstimadoUsd ?? 0);
    }

    internal static string Formatar(ResumoDiario r, string status, int notaMinima)
    {
        var sb = new StringBuilder();
        var data = DateOnly.ParseExact(r.Data, "yyyy-MM-dd").ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        sb.AppendLine($"Vagas do dia {data}");
        sb.AppendLine();

        if (r.VagasRecomendadas.Count == 0)
            sb.AppendLine(r.AbaixoDaNotaMinima > 0
                ? $"Nenhuma vaga nova com nota {notaMinima} ou mais hoje ({r.AbaixoDaNotaMinima} avaliadas abaixo disso)."
                : "Nenhuma vaga nova para recomendar hoje.");

        foreach (var (v, i) in r.VagasRecomendadas.Select((v, i) => (v, i + 1)))
        {
            sb.AppendLine($"{i}. {v.Titulo} — {v.Empresa}{(v.Local is null ? "" : $" ({v.Local})")} · nota {v.Nota}");
            sb.AppendLine($"   {v.Justificativa}");
            if (v.Lacunas.Count > 0) sb.AppendLine($"   Falta: {string.Join(", ", v.Lacunas)}");
            sb.AppendLine($"   {v.Url}");
        }

        sb.AppendLine();
        sb.AppendLine($"Status: {status}. " + string.Join("; ", r.Passos.Select(p => $"{p.Nome}: {(p.Sucesso ? "ok" : "falhou")} ({p.Resumo})")));
        if (r.CustoMatchUsd > 0) sb.AppendLine($"Custo estimado do match: US$ {r.CustoMatchUsd.ToString(CultureInfo.InvariantCulture)}");
        return sb.ToString().TrimEnd();
    }
}

/// <summary>Para onde vai o resumo do dia. Hoje só existe o log; Telegram ou e-mail entram implementando isto.</summary>
public interface INotificador
{
    Task NotificarAsync(ResumoDiario resumo, CancellationToken ct);
}

/// <summary>Sem canal configurado: o resumo fica no banco (GET /execucoes) e aparece no log do worker no dashboard do Aspire.</summary>
public sealed class NotificadorLog(ILogger<NotificadorLog> log) : INotificador
{
    public Task NotificarAsync(ResumoDiario resumo, CancellationToken ct)
    {
        log.LogInformation("Resumo do dia:\n{Texto}", resumo.Texto);
        return Task.CompletedTask;
    }
}
