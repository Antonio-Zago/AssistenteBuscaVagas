namespace AssistenteBuscaVagas.AgentePerfil.Perfil;

/// <summary>Uma skill a acrescentar ou corrigir. Campos vazios mantêm o valor atual (ou o padrão, se a skill é nova).</summary>
public sealed record SkillAjuste(string Nome, string? Categoria = null, int? Anos = null, string? Evidencia = null);

/// <summary>
/// Edição parcial do currículo: só o que vier preenchido muda. Feita para ajustes pequenos sem reenviar o PDF
/// ("acrescente Docker com 1 ano", "tire Delphi", "meu cargo agora é Tech Lead"). Experiências e formação ficam
/// de fora: para elas, reenvie o PDF ou grave o currículo inteiro (salvar_perfil).
/// A validação final é a do mcp_vagas (ValidacaoPerfil), a mesma do currículo extraído do PDF.
/// </summary>
public sealed record AjusteCurriculo(
    string? Resumo = null,
    string? CargoAtual = null,
    string? Senioridade = null,
    int? AnosExperiencia = null,
    IReadOnlyList<SkillAjuste>? AdicionarSkills = null,
    IReadOnlyList<string>? RemoverSkills = null,
    IReadOnlyList<Idioma>? AdicionarIdiomas = null,
    IReadOnlyList<string>? RemoverIdiomas = null,
    IReadOnlyList<string>? AdicionarCertificacoes = null,
    IReadOnlyList<string>? RemoverCertificacoes = null)
{
    /// <summary>Evidência de uma skill acrescentada à mão: deixa claro que não veio do texto do currículo.</summary>
    public const string EvidenciaManual = "Informado pelo usuário";

    public bool Vazio =>
        Resumo is null && CargoAtual is null && Senioridade is null && AnosExperiencia is null
        && Nenhum(AdicionarSkills) && Nenhum(RemoverSkills) && Nenhum(AdicionarIdiomas) && Nenhum(RemoverIdiomas)
        && Nenhum(AdicionarCertificacoes) && Nenhum(RemoverCertificacoes);

    /// <summary>
    /// Aplica o ajuste. Remoções acontecem antes das adições (remover e acrescentar a mesma skill = substituí-la).
    /// Devolve avisos para pedidos que não mudaram nada (ex.: remover uma skill que não existe).
    /// </summary>
    public (Curriculo Curriculo, IReadOnlyList<string> Avisos) AplicarSobre(Curriculo atual)
    {
        var avisos = new List<string>();
        var comparador = StringComparer.OrdinalIgnoreCase;

        // ---- Skills ----
        var skills = atual.Skills.ToList();
        foreach (var nome in Limpos(RemoverSkills))
            if (skills.RemoveAll(s => comparador.Equals(s.Nome, nome)) == 0)
                avisos.Add($"Skill \"{nome}\" não estava no currículo.");

        foreach (var ajuste in AdicionarSkills ?? [])
        {
            if (string.IsNullOrWhiteSpace(ajuste.Nome)) continue;
            var nome = ajuste.Nome.Trim();
            var i = skills.FindIndex(s => comparador.Equals(s.Nome, nome));
            if (i >= 0)
            {
                var s = skills[i];
                skills[i] = s with
                {
                    Categoria = Texto(ajuste.Categoria) ?? s.Categoria,
                    Anos = ajuste.Anos ?? s.Anos,
                    Evidencia = Texto(ajuste.Evidencia) ?? s.Evidencia,
                };
            }
            else
            {
                skills.Add(new Skill(nome, Texto(ajuste.Categoria) ?? "Outra", ajuste.Anos, Texto(ajuste.Evidencia) ?? EvidenciaManual));
            }
        }

        // ---- Idiomas ----
        var idiomas = atual.Idiomas.ToList();
        foreach (var nome in Limpos(RemoverIdiomas))
            if (idiomas.RemoveAll(x => comparador.Equals(x.Nome, nome)) == 0)
                avisos.Add($"Idioma \"{nome}\" não estava no currículo.");
        foreach (var idioma in AdicionarIdiomas ?? [])
        {
            if (string.IsNullOrWhiteSpace(idioma.Nome)) continue;
            idiomas.RemoveAll(x => comparador.Equals(x.Nome, idioma.Nome.Trim()));
            idiomas.Add(new Idioma(idioma.Nome.Trim(), idioma.Nivel));
        }

        // ---- Certificações ----
        var certificacoes = atual.Certificacoes.ToList();
        foreach (var nome in Limpos(RemoverCertificacoes))
            if (certificacoes.RemoveAll(c => comparador.Equals(c, nome)) == 0)
                avisos.Add($"Certificação \"{nome}\" não estava no currículo.");
        foreach (var c in Limpos(AdicionarCertificacoes))
            if (!certificacoes.Contains(c, comparador)) certificacoes.Add(c);

        var novo = atual with
        {
            Resumo = Texto(Resumo) ?? atual.Resumo,
            // Cargo atual vazio ("") apaga o cargo: é como dizer "estou sem emprego no momento".
            CargoAtual = CargoAtual is null ? atual.CargoAtual : Texto(CargoAtual),
            Senioridade = Texto(Senioridade) ?? atual.Senioridade,
            AnosExperiencia = AnosExperiencia ?? atual.AnosExperiencia,
            Skills = skills,
            Idiomas = idiomas,
            Certificacoes = certificacoes,
        };
        return (novo, avisos);
    }

    private static bool Nenhum<T>(IReadOnlyList<T>? itens) => itens is null || itens.Count == 0;

    private static IEnumerable<string> Limpos(IReadOnlyList<string>? itens) =>
        (itens ?? []).Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim());

    private static string? Texto(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public sealed record ResultadoAjuste(PerfilCompleto Perfil, IReadOnlyList<string> Avisos);

public static class EdicaoCurriculo
{
    /// <summary>
    /// Lê o currículo gravado, aplica o ajuste e grava de volta (salvar_perfil substitui o currículo inteiro, por isso
    /// o ajuste é aplicado sobre o atual). Erros que o usuário corrige viram CurriculoInvalidoException ou
    /// PerfilRejeitadoException (validação do mcp_vagas); os dois viram HTTP 422 ou "erro" no A2A.
    /// </summary>
    public static async Task<ResultadoAjuste> AplicarAsync(McpVagasCliente mcp, AjusteCurriculo? ajuste, CancellationToken ct)
    {
        if (ajuste is null || ajuste.Vazio)
            throw new CurriculoInvalidoException("Informe pelo menos uma alteração (ex.: adicionarSkills, removerSkills, senioridade, cargoAtual).");

        var atual = (await mcp.ObterAsync(ct))?.Curriculo
            ?? throw new CurriculoInvalidoException("Ainda não há currículo cadastrado. Envie o PDF em POST /perfil/curriculo primeiro.");

        var (novo, avisos) = ajuste.AplicarSobre(atual);
        return new ResultadoAjuste(await mcp.SalvarAsync(novo, null, ct), avisos);
    }
}
