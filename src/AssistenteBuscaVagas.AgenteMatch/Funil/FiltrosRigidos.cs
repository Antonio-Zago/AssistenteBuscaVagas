namespace AssistenteBuscaVagas.AgenteMatch.Funil;

/// <summary>Motivo de descarte: um código curto (para contar no resumo do funil) e o detalhe da vaga.</summary>
public sealed record Descarte(string Codigo, string Detalhe);

/// <summary>
/// Etapa 1 do funil: regras de sim ou não, em código, baratas e previsíveis. Uma vaga só é descartada
/// quando a informação existe e contradiz o perfil; dado ausente (título sem senioridade, local vazio) não descarta.
/// </summary>
public sealed class FiltrosRigidos
{
    private readonly int? _nivelDesejado;
    private readonly int _tolerancia;
    private readonly bool _aceitaRemoto;
    private readonly bool _aceitaLocal;
    private readonly IReadOnlyList<string> _cidades;
    private readonly IReadOnlyList<string> _locaisRemotos;

    public FiltrosRigidos(PerfilCompleto perfil, FunilOptions opcoes)
    {
        _nivelDesejado = Senioridade.Nivel(perfil.Preferencias?.Senioridade) ?? Senioridade.Nivel(perfil.Curriculo?.Senioridade);
        _tolerancia = opcoes.ToleranciaSenioridade;

        var modalidades = perfil.Preferencias?.Modalidades ?? [];
        // Sem preferências de modalidade, nada é descartado por modalidade.
        _aceitaRemoto = modalidades.Count == 0 || modalidades.Any(m => Igual(m, "Remoto"));
        _aceitaLocal = modalidades.Count == 0 || modalidades.Any(m => Igual(m, "Híbrido") || Igual(m, "Presencial"));
        _cidades = (perfil.Preferencias?.Cidades ?? []).Select(Normalizar).Where(c => c.Length > 0).ToList();
        _locaisRemotos = opcoes.LocaisRemotosAceitos.Select(Normalizar).ToList();
        SoRemotas = modalidades.Count > 0 && _aceitaRemoto && !_aceitaLocal;
    }

    /// <summary>Quando só remoto interessa, o filtro já vai para a consulta ao banco.</summary>
    public bool SoRemotas { get; }

    public Descarte? Avaliar(VagaParaMatch vaga)
    {
        if (_nivelDesejado is { } desejado && Senioridade.InferirDoTitulo(vaga.Titulo) is { } nivel
            && Math.Abs(nivel - desejado) > _tolerancia)
            return new("senioridade", $"título indica {Senioridade.Niveis[nivel]}, perfil busca {Senioridade.Niveis[desejado]}");

        var local = Normalizar(vaga.Local ?? "");

        if (vaga.Remoto)
        {
            if (!_aceitaRemoto)
                return new("modalidade", "vaga remota; preferências não incluem Remoto");
            if (local.Length > 0 && !_locaisRemotos.Any(local.Contains))
                return new("local", $"remota, mas restrita a: {vaga.Local}");
            return null;
        }

        if (!_aceitaLocal)
            return new("modalidade", "vaga não é remota; preferências só incluem Remoto");
        // Híbrido/presencial com cidades definidas: a vaga precisa citar uma delas.
        if (_cidades.Count > 0 && !_cidades.Any(local.Contains))
            return new("local", $"presencial/híbrida em {vaga.Local ?? "local não informado"}, fora das cidades desejadas");
        return null;
    }

    private static string Normalizar(string s) => Tokenizador.SemAcento(s.Trim().ToLowerInvariant());

    private static bool Igual(string a, string b) =>
        string.Equals(Normalizar(a), Normalizar(b), StringComparison.Ordinal);
}
