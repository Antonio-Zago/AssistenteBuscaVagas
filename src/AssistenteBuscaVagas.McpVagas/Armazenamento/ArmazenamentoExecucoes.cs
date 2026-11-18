using System.ComponentModel;
using System.Text.Json;
using AssistenteBuscaVagas.McpVagas.Dados;
using Microsoft.EntityFrameworkCore;

namespace AssistenteBuscaVagas.McpVagas.Armazenamento;

public sealed record VagaRecomendadaEntrada(
    [property: Description("Id da vaga mostrada no resumo.")] int VagaId,
    [property: Description("Nota que ela recebeu (0 a 100).")] int Nota);

public sealed record ExecucaoResumo(
    string Data, string Status, int Tentativas, DateTimeOffset IniciadaEm, DateTimeOffset ConcluidaEm, JsonElement Resumo);

public sealed record VagaJaRecomendada(int VagaId, int Nota, string Data, DateTimeOffset RecomendadaEm);

/// <summary>Grava e lê as execuções do worker diário e as vagas já mostradas ao usuário.</summary>
public sealed class ArmazenamentoExecucoes(VagasDbContext db, TimeProvider relogio)
{
    public static readonly string[] StatusValidos = ["sucesso", "parcial", "falha"];
    private const int TamanhoMaximoResumo = 200_000;

    public async Task<ExecucaoResumo> RegistrarAsync(
        string data, string status, int tentativas, DateTimeOffset iniciadaEm, DateTimeOffset concluidaEm,
        JsonElement resumo, IReadOnlyList<VagaRecomendadaEntrada>? recomendadas, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(data, "yyyy-MM-dd", out var dia))
            throw new ArgumentException($"Data inválida: '{data}'. Use AAAA-MM-DD.");
        if (!StatusValidos.Contains(status))
            throw new ArgumentException($"Status inválido: '{status}'. Use um de: {string.Join(", ", StatusValidos)}.");
        if (tentativas < 1)
            throw new ArgumentException("Tentativas deve ser 1 ou maior.");
        if (concluidaEm < iniciadaEm)
            throw new ArgumentException("concluidaEm não pode ser anterior a iniciadaEm.");
        if (resumo.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("O resumo deve ser um objeto JSON.");
        var resumoTexto = resumo.GetRawText();
        if (resumoTexto.Length > TamanhoMaximoResumo)
            throw new ArgumentException($"Resumo com mais de {TamanhoMaximoResumo} caracteres.");

        var execucao = await db.ExecucoesDiarias.SingleOrDefaultAsync(e => e.Data == dia, ct);
        if (execucao is null)
        {
            execucao = new ExecucaoDiaria { Data = dia, Status = "", Resumo = "" };
            db.ExecucoesDiarias.Add(execucao);
        }
        execucao.Status = status;
        execucao.Tentativas = tentativas;
        execucao.IniciadaEm = iniciadaEm;
        execucao.ConcluidaEm = concluidaEm;
        execucao.Resumo = resumoTexto;

        var ids = (recomendadas ?? []).Select(r => r.VagaId).Distinct().ToList();
        if (ids.Count > 0)
        {
            var vagasExistentes = await db.Vagas.Where(v => ids.Contains(v.Id)).Select(v => v.Id).ToListAsync(ct);
            if (ids.Except(vagasExistentes).FirstOrDefault() is var faltando and > 0)
                throw new ArgumentException($"Vaga {faltando} não existe.");

            // Uma vaga é recomendada uma vez só: se já estava registrada (por outro dia), fica o primeiro registro.
            var jaRegistradas = await db.RecomendacoesEnviadas.Where(r => ids.Contains(r.VagaId)).Select(r => r.VagaId).ToListAsync(ct);
            var agora = relogio.GetUtcNow();
            foreach (var r in (recomendadas ?? []).DistinctBy(r => r.VagaId).Where(r => !jaRegistradas.Contains(r.VagaId)))
            {
                if (r.Nota is < 0 or > 100)
                    throw new ArgumentException($"Nota inválida para a vaga {r.VagaId}: {r.Nota}.");
                db.RecomendacoesEnviadas.Add(new RecomendacaoEnviada { VagaId = r.VagaId, Execucao = execucao, Nota = r.Nota, RecomendadaEm = agora });
            }
        }

        await db.SaveChangesAsync(ct);
        return Converter(execucao);
    }

    public async Task<IReadOnlyList<ExecucaoResumo>> ListarAsync(int limite, CancellationToken ct)
    {
        var execucoes = await db.ExecucoesDiarias.AsNoTracking()
            .OrderByDescending(e => e.Data)
            .Take(Math.Clamp(limite, 1, 60))
            .ToListAsync(ct);
        return execucoes.Select(Converter).ToList();
    }

    public async Task<IReadOnlyList<VagaJaRecomendada>> ListarRecomendadasAsync(CancellationToken ct)
    {
        var lista = await db.RecomendacoesEnviadas.AsNoTracking()
            .OrderByDescending(r => r.RecomendadaEm)
            .Select(r => new { r.VagaId, r.Nota, r.Execucao.Data, r.RecomendadaEm })
            .ToListAsync(ct);
        return lista.Select(r => new VagaJaRecomendada(r.VagaId, r.Nota, r.Data.ToString("yyyy-MM-dd"), r.RecomendadaEm)).ToList();
    }

    private static ExecucaoResumo Converter(ExecucaoDiaria e)
    {
        using var doc = JsonDocument.Parse(e.Resumo);
        return new(e.Data.ToString("yyyy-MM-dd"), e.Status, e.Tentativas, e.IniciadaEm, e.ConcluidaEm, doc.RootElement.Clone());
    }
}
