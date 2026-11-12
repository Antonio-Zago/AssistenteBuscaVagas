using System.Text.Json;
using AssistenteBuscaVagas.McpVagas.Dados;

namespace AssistenteBuscaVagas.McpVagas.Armazenamento;

/// <summary>Lê e grava o perfil único. Cada parte (currículo, preferências) é atualizada de forma independente.</summary>
public sealed class ArmazenamentoPerfil(VagasDbContext db, TimeProvider relogio)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PerfilCompleto> SalvarAsync(Curriculo? curriculo, Preferencias? preferencias, CancellationToken ct)
    {
        if (curriculo is null && preferencias is null)
            throw new ArgumentException("Informe o currículo, as preferências ou os dois.");

        // Valida tudo antes de tocar no banco: ou grava as duas partes, ou nenhuma.
        var curriculoValido = curriculo is null ? null : ValidacaoPerfil.Validar(curriculo);
        var preferenciasValidas = preferencias is null ? null : ValidacaoPerfil.Validar(preferencias);

        var perfil = await db.Perfis.FindAsync([Perfil.IdUnico], ct);
        if (perfil is null)
        {
            perfil = new Perfil { Id = Perfil.IdUnico };
            db.Perfis.Add(perfil);
        }

        var agora = relogio.GetUtcNow();
        if (curriculoValido is not null)
        {
            perfil.Curriculo = JsonSerializer.Serialize(curriculoValido, Json);
            perfil.CurriculoAtualizadoEm = agora;
        }
        if (preferenciasValidas is not null)
        {
            perfil.Preferencias = JsonSerializer.Serialize(preferenciasValidas, Json);
            perfil.PreferenciasAtualizadasEm = agora;
        }

        await db.SaveChangesAsync(ct);
        return Converter(perfil);
    }

    public async Task<PerfilCompleto?> ObterAsync(CancellationToken ct)
    {
        var perfil = await db.Perfis.FindAsync([Perfil.IdUnico], ct);
        return perfil is null ? null : Converter(perfil);
    }

    private static PerfilCompleto Converter(Perfil p) => new(
        p.Curriculo is null ? null : JsonSerializer.Deserialize<Curriculo>(p.Curriculo, Json),
        p.CurriculoAtualizadoEm,
        p.Preferencias is null ? null : JsonSerializer.Deserialize<Preferencias>(p.Preferencias, Json),
        p.PreferenciasAtualizadasEm);
}
