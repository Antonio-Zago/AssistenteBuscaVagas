using System.ComponentModel;
using System.Text.RegularExpressions;
using AssistenteBuscaVagas.McpVagas.Dados;
using Match = AssistenteBuscaVagas.McpVagas.Dados.Match;
using Microsoft.EntityFrameworkCore;

namespace AssistenteBuscaVagas.McpVagas.Armazenamento;

public sealed record MatchEntrada(
    [property: Description("Id da vaga avaliada.")] int VagaId,
    [property: Description("Nota de 0 a 100.")] int Nota,
    [property: Description("Justificativa curta da nota.")] string Justificativa,
    [property: Description("Requisitos da vaga que o perfil atende.")] IReadOnlyList<string>? PontosFortes = null,
    [property: Description("Requisitos da vaga que o perfil não atende.")] IReadOnlyList<string>? Lacunas = null,
    [property: Description("Condições que tornam a vaga inviável (ex.: exige residência em outro país).")] IReadOnlyList<string>? Impeditivos = null);

public sealed record MatchSalvo(
    int VagaId,
    int Nota,
    string Justificativa,
    IReadOnlyList<string> PontosFortes,
    IReadOnlyList<string> Lacunas,
    IReadOnlyList<string> Impeditivos,
    string Modelo,
    int VersaoPrompt,
    DateTimeOffset CriadoEm);

public sealed record RotuloResumo(int VagaId, bool Relevante, string? Observacao, DateTimeOffset AtualizadoEm, string Titulo, string Empresa);

/// <summary>Grava e lê as avaliações do LLM (cache) e os rótulos manuais (conjunto de avaliação). Valida tudo na entrada.</summary>
public sealed partial class ArmazenamentoMatches(VagasDbContext db, TimeProvider relogio)
{
    public const int MaximoPorChamada = 100;
    private const int MaximoItens = 15;

    public async Task<int> SalvarAsync(string chave, string modelo, int versaoPrompt, IReadOnlyList<MatchEntrada> matches, CancellationToken ct)
    {
        ValidarChave(chave);
        if (string.IsNullOrWhiteSpace(modelo) || modelo.Length > 100)
            throw new ArgumentException("Informe o modelo (até 100 caracteres).");
        if (versaoPrompt < 1)
            throw new ArgumentException("A versão do prompt deve ser 1 ou maior.");
        if (matches.Count > MaximoPorChamada)
            throw new ArgumentException($"Envie no máximo {MaximoPorChamada} avaliações por chamada.");

        foreach (var m in matches)
        {
            if (m.Nota is < 0 or > 100)
                throw new ArgumentException($"Nota inválida para a vaga {m.VagaId}: {m.Nota}. Use 0 a 100.");
            if (string.IsNullOrWhiteSpace(m.Justificativa))
                throw new ArgumentException($"Justificativa vazia para a vaga {m.VagaId}.");
        }

        var ids = matches.Select(m => m.VagaId).Distinct().ToList();
        var existentesVagas = await db.Vagas.Where(v => ids.Contains(v.Id)).Select(v => v.Id).ToListAsync(ct);
        if (ids.Except(existentesVagas).FirstOrDefault() is var faltando and > 0)
            throw new ArgumentException($"Vaga {faltando} não existe.");

        var atuais = await db.Matches.Where(m => m.ChaveAvaliacao == chave && ids.Contains(m.VagaId)).ToDictionaryAsync(m => m.VagaId, ct);
        var agora = relogio.GetUtcNow();

        foreach (var m in matches.DistinctBy(m => m.VagaId))
        {
            if (!atuais.TryGetValue(m.VagaId, out var match))
            {
                match = new Match { VagaId = m.VagaId, ChaveAvaliacao = chave, Modelo = "", Justificativa = "" };
                db.Matches.Add(match);
            }
            match.Modelo = modelo.Trim();
            match.VersaoPrompt = versaoPrompt;
            match.Nota = m.Nota;
            match.Justificativa = Limitar(m.Justificativa.Trim(), 2000);
            match.PontosFortes = Lista(m.PontosFortes);
            match.Lacunas = Lista(m.Lacunas);
            match.Impeditivos = Lista(m.Impeditivos);
            match.CriadoEm = agora;
        }

        await db.SaveChangesAsync(ct);
        return ids.Count;
    }

    public async Task<IReadOnlyList<MatchSalvo>> ObterAsync(string chave, IReadOnlyList<int> vagaIds, CancellationToken ct)
    {
        ValidarChave(chave);
        var ids = vagaIds.Distinct().ToList();
        return await db.Matches.AsNoTracking()
            .Where(m => m.ChaveAvaliacao == chave && ids.Contains(m.VagaId))
            .Select(m => new MatchSalvo(m.VagaId, m.Nota, m.Justificativa, m.PontosFortes, m.Lacunas, m.Impeditivos, m.Modelo, m.VersaoPrompt, m.CriadoEm))
            .ToListAsync(ct);
    }

    public async Task<RotuloResumo> RotularAsync(int vagaId, bool relevante, string? observacao, CancellationToken ct)
    {
        var vaga = await db.Vagas.AsNoTracking().Where(v => v.Id == vagaId).Select(v => new { v.Titulo, v.Empresa }).SingleOrDefaultAsync(ct)
            ?? throw new ArgumentException($"Vaga {vagaId} não existe.");

        var rotulo = await db.Rotulos.FindAsync([vagaId], ct);
        if (rotulo is null)
        {
            rotulo = new Rotulo { VagaId = vagaId };
            db.Rotulos.Add(rotulo);
        }
        rotulo.Relevante = relevante;
        rotulo.Observacao = string.IsNullOrWhiteSpace(observacao) ? null : Limitar(observacao.Trim(), 500);
        rotulo.AtualizadoEm = relogio.GetUtcNow();
        await db.SaveChangesAsync(ct);

        return new(vagaId, rotulo.Relevante, rotulo.Observacao, rotulo.AtualizadoEm, vaga.Titulo, vaga.Empresa);
    }

    public async Task<bool> RemoverRotuloAsync(int vagaId, CancellationToken ct) =>
        await db.Rotulos.Where(r => r.VagaId == vagaId).ExecuteDeleteAsync(ct) > 0;

    public async Task<IReadOnlyList<RotuloResumo>> ListarRotulosAsync(CancellationToken ct) =>
        await db.Rotulos.AsNoTracking()
            .OrderBy(r => r.VagaId)
            .Select(r => new RotuloResumo(r.VagaId, r.Relevante, r.Observacao, r.AtualizadoEm, r.Vaga.Titulo, r.Vaga.Empresa))
            .ToListAsync(ct);

    private static void ValidarChave(string chave)
    {
        if (string.IsNullOrEmpty(chave) || !Hex64().IsMatch(chave))
            throw new ArgumentException("chaveAvaliacao deve ser um hash hexadecimal de 64 caracteres (minúsculo).");
    }

    private static List<string> Lista(IReadOnlyList<string>? itens) =>
        (itens ?? [])
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Select(i => Limitar(i.Trim(), 300))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximoItens)
            .ToList();

    private static string Limitar(string valor, int max) => valor.Length <= max ? valor : valor[..max];

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Hex64();
}
