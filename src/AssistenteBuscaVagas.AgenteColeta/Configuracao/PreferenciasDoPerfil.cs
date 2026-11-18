namespace AssistenteBuscaVagas.AgenteColeta.Configuracao;

// Cópia parcial do contrato do mcp_vagas (obter_perfil), só com o que a coleta usa. O nome das empresas,
// o resumo e as experiências não são declarados: não são lidos, então não vão para o prompt da coleta.
public sealed record PerfilColeta(CurriculoColeta? Curriculo, PreferenciasColeta? Preferencias);
public sealed record CurriculoColeta(string? CargoAtual, string? Senioridade, IReadOnlyList<SkillColeta>? Skills);
public sealed record SkillColeta(string Nome, int? Anos);
public sealed record PreferenciasColeta(
    IReadOnlyList<string>? Cargos, IReadOnlyList<string>? Modalidades, IReadOnlyList<string>? Cidades,
    string? Senioridade, IReadOnlyList<string>? Idiomas);

/// <summary>De onde veio cada parte das preferências usadas numa coleta: "perfil" ou "appsettings".</summary>
public sealed record FontePreferencias(string Cargos, string Skills, string Senioridade, string Locais, string Idiomas)
{
    public bool TudoDoPerfil => new[] { Cargos, Skills, Senioridade, Locais, Idiomas }.All(f => f == "perfil");
}

/// <summary>
/// Monta o "o que procurar" da coleta a partir do perfil gravado no banco (o mesmo que o agente_match usa para
/// avaliar). Campo a campo: o que o perfil tiver vence; o que faltar vem do appsettings (Coleta:Preferencias),
/// que fica só como reserva para quando o perfil ainda não foi cadastrado.
/// </summary>
public static class PreferenciasDoPerfil
{
    /// <summary>Quantas skills entram na busca. As com mais anos de uso primeiro: são as que o candidato domina.</summary>
    public const int MaximoSkills = 8;

    public static (PreferenciasBusca Preferencias, FontePreferencias Fonte) Montar(PerfilColeta? perfil, PreferenciasBusca reserva, string pais)
    {
        var c = perfil?.Curriculo;
        var p = perfil?.Preferencias;

        var cargos = Lista(p?.Cargos);
        if (cargos.Count == 0 && !string.IsNullOrWhiteSpace(c?.CargoAtual)) cargos = [c.CargoAtual.Trim()];

        var skills = (c?.Skills ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Nome))
            .OrderByDescending(s => s.Anos ?? -1)
            .Select(s => s.Nome.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximoSkills)
            .ToList();

        // A desejada (preferências) vence a do currículo: quem é Pleno pode estar buscando vaga Sênior.
        var senioridade = Texto(p?.Senioridade) ?? Texto(c?.Senioridade);

        var locais = new List<string>();
        var modalidades = Lista(p?.Modalidades);
        if (modalidades.Contains("Remoto", StringComparer.OrdinalIgnoreCase)) locais.Add("Remoto");
        if (modalidades.Any(m => !m.Equals("Remoto", StringComparison.OrdinalIgnoreCase))) locais.AddRange(Lista(p?.Cidades));
        if (locais.Count > 0 && !string.IsNullOrWhiteSpace(pais)) locais.Add(pais);

        var idiomas = Lista(p?.Idiomas);

        var fonte = new FontePreferencias(
            cargos.Count > 0 ? "perfil" : "appsettings",
            skills.Count > 0 ? "perfil" : "appsettings",
            senioridade is not null ? "perfil" : "appsettings",
            locais.Count > 0 ? "perfil" : "appsettings",
            idiomas.Count > 0 ? "perfil" : "appsettings");

        return (new PreferenciasBusca
        {
            Cargos = cargos.Count > 0 ? cargos : [.. reserva.Cargos],
            Skills = skills.Count > 0 ? skills : [.. reserva.Skills],
            Senioridade = senioridade ?? reserva.Senioridade,
            Locais = locais.Count > 0 ? locais.Distinct(StringComparer.OrdinalIgnoreCase).ToList() : [.. reserva.Locais],
            Idiomas = idiomas.Count > 0 ? idiomas : [.. reserva.Idiomas],
        }, fonte);
    }

    private static List<string> Lista(IReadOnlyList<string>? itens) =>
        (itens ?? []).Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static string? Texto(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
