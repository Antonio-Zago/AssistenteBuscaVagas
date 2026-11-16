using AssistenteBuscaVagas.Comum.Busca;

namespace AssistenteBuscaVagas.AgenteMatch.Funil;

/// <summary>
/// O funil de match sem LLM (fase 3). Código puro: recebe o perfil e as vagas e devolve o ranking explicado,
/// sem acessar rede nem banco, para poder ser testado e comparado depois com a etapa de LLM (fase 4).
///
///   vagas lidas ──▶ 1. filtros rígidos ──▶ 2. BM25 (skills + cargos contra título/tags/descrição) ──▶ top N
/// </summary>
public sealed class FunilMatch(FunilOptions opcoes)
{
    private const int MaximoTermosExplicados = 8;
    private const int MaximoLacunasFrequentes = 10;

    private sealed record SkillConsulta(string Nome, IReadOnlyList<string> Termos);

    private sealed record ConsultaPerfil(
        IReadOnlyDictionary<string, double> Pesos,
        IReadOnlyList<SkillConsulta> Skills,
        IReadOnlySet<string> TermosCargo,
        IReadOnlySet<string> TodosTermos);

    private sealed record Documento(IReadOnlyDictionary<string, double> Tf, IReadOnlySet<string> Termos, IReadOnlySet<string> TermosTitulo);

    public FiltrosRigidos Filtros(PerfilCompleto perfil) => new FiltrosRigidos(perfil, opcoes);

    public ResultadoFunil Executar(PerfilCompleto perfil, IReadOnlyList<VagaParaMatch> vagas, int? limite = null)
    {
        var consulta = MontarConsulta(perfil);
        var n = Math.Clamp(limite ?? opcoes.Recomendacoes, 1, 100);

        // Etapa 1: filtros rígidos.
        var filtros = Filtros(perfil);
        var aprovadas = new List<VagaParaMatch>();
        var descartes = new SortedDictionary<string, int>();
        foreach (var vaga in vagas)
        {
            if (filtros.Avaliar(vaga) is { } d)
                descartes[d.Codigo] = descartes.GetValueOrDefault(d.Codigo) + 1;
            else
                aprovadas.Add(vaga);
        }

        // Etapa 2: BM25. O índice é montado só com as aprovadas: o idf mede o quanto um termo é raro entre as vagas que ainda concorrem.
        var docs = aprovadas.Select(MontarDocumento).ToList();
        var indice = new IndiceBm25(docs.Select(d => d.Tf).ToList());

        var pontuadas = aprovadas
            .Select((vaga, i) =>
            {
                var contribuicoes = indice.Contribuicoes(i, consulta.Pesos);
                return (Vaga: vaga, Doc: docs[i], Contribuicoes: contribuicoes, Total: contribuicoes.Sum(c => c.Pontos));
            })
            .Where(x => x.Total > 0)
            .OrderByDescending(x => x.Total)
            .ThenByDescending(x => x.Vaga.PublicadaEm)
            .ToList();

        var top = pontuadas.Take(n).ToList();
        var maior = top.Count > 0 ? top[0].Total : 1;

        var recomendacoes = top
            .Select((x, i) => Explicar(i + 1, x.Vaga, x.Doc, x.Contribuicoes, x.Total, maior, consulta))
            .ToList();

        var lacunasFrequentes = recomendacoes
            .SelectMany(r => r.Lacunas)
            .GroupBy(l => l)
            .Select(g => new LacunaFrequente(g.Key, g.Count(), (int)Math.Round(100.0 * g.Count() / recomendacoes.Count)))
            .OrderByDescending(l => l.Vagas).ThenBy(l => l.Skill, StringComparer.Ordinal)
            .Take(MaximoLacunasFrequentes)
            .ToList();

        return new ResultadoFunil(
            [
                new("vagas lidas", vagas.Count),
                new("após filtros rígidos", aprovadas.Count, descartes),
                new("com algum termo do perfil (BM25 > 0)", pontuadas.Count),
                new("recomendadas", recomendacoes.Count),
            ],
            recomendacoes,
            lacunasFrequentes,
            consulta.Pesos.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new TermoPontuado(p.Key, Math.Round(p.Value, 2))).ToList());
    }

    /// <summary>
    /// A consulta é o próprio perfil: cada skill vira termos com peso 1 a 2 (mais anos de uso, mais peso),
    /// e os cargos desejados e o atual entram com <see cref="FunilOptions.PesoCargo"/>.
    /// </summary>
    private ConsultaPerfil MontarConsulta(PerfilCompleto perfil)
    {
        var pesos = new Dictionary<string, double>();
        void Somar(string termo, double peso) => pesos[termo] = Math.Max(pesos.GetValueOrDefault(termo), peso);

        var skills = new List<SkillConsulta>();
        foreach (var skill in perfil.Curriculo?.Skills ?? [])
        {
            var termos = Tokenizador.Termos(skill.Nome).Distinct().ToList();
            if (termos.Count == 0) continue;
            skills.Add(new(skill.Nome, termos));
            // Skill de vários termos divide o peso entre eles: em "ASP.NET Core", "core" sozinho não pode valer uma skill inteira.
            // Se o termo também é uma skill própria (".NET" e ".NET Framework"), fica o maior peso.
            var peso = (1 + Math.Min(skill.Anos ?? 0, 10) / 10.0) / termos.Count;
            foreach (var t in termos) Somar(t, peso);
        }

        // Senioridade já foi tratada no filtro; no cargo ("Desenvolvedor pleno") ela só faria ruído na busca.
        var cargos = (perfil.Preferencias?.Cargos ?? []).Append(perfil.Curriculo?.CargoAtual);
        var termosCargo = cargos.SelectMany(c => Tokenizador.Termos(c)).Where(t => !Senioridade.EhMarcador(t)).ToHashSet();
        foreach (var t in termosCargo) Somar(t, opcoes.PesoCargo);

        if (pesos.Count == 0)
            throw new PerfilIncompletoException(
                "O perfil não tem skills nem cargos desejados. Envie o currículo (POST /perfil/curriculo no agente_perfil) " +
                "ou defina os cargos nas preferências (PUT /perfil/preferencias).");

        var todos = skills.SelectMany(s => s.Termos).Concat(termosCargo).ToHashSet();
        return new(pesos, skills, termosCargo, todos);
    }

    private Documento MontarDocumento(VagaParaMatch v)
    {
        var tf = new Dictionary<string, double>();
        void Contar(IEnumerable<string> termos, double peso)
        {
            foreach (var t in termos) tf[t] = tf.GetValueOrDefault(t) + peso;
        }

        var titulo = Tokenizador.Termos(v.Titulo);
        Contar(titulo, opcoes.PesoTitulo);
        Contar(v.Tags.SelectMany(t => Tokenizador.Termos(t)), opcoes.PesoTags);
        Contar(Tokenizador.Termos(v.Descricao), opcoes.PesoDescricao);

        return new(tf, tf.Keys.ToHashSet(), titulo.ToHashSet());
    }

    private static Recomendacao Explicar(
        int posicao, VagaParaMatch v, Documento doc, IReadOnlyList<TermoPontuado> contribuicoes,
        double total, double maior, ConsultaPerfil consulta)
    {
        var pontosPorTermo = contribuicoes.ToDictionary(c => c.Termo, c => c.Pontos);

        // Uma skill "aparece" quando todos os seus termos estão na vaga ("ASP.NET Core" precisa de asp.net e core).
        var skills = consulta.Skills
            .Where(s => s.Termos.All(doc.Termos.Contains))
            .OrderByDescending(s => s.Termos.Sum(t => pontosPorTermo.GetValueOrDefault(t)))
            .Select(s => s.Nome)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var cargoNoTitulo = consulta.TermosCargo.Where(doc.TermosTitulo.Contains).Order(StringComparer.Ordinal).ToList();

        // Lacunas: tags da vaga que o perfil não cobre. Sem LLM, só dá para ver o que a fonte rotulou;
        // tag de senioridade ("senior") não é skill e fica de fora.
        var lacunas = v.Tags
            .Select(tag => (Tag: tag.Trim().ToLowerInvariant(), Termos: Tokenizador.Termos(tag)))
            .Where(x => x.Termos.Count > 0
                        && Senioridade.InferirDoTitulo(x.Tag) is null
                        && !x.Termos.All(consulta.TodosTermos.Contains))
            .Select(x => x.Tag)
            .Distinct()
            .ToList();

        return new Recomendacao(
            posicao, v.Id, v.Titulo, v.Empresa, v.Local, v.Remoto,
            Senioridade.InferirDoTitulo(v.Titulo) is { } nivel ? Senioridade.Niveis[nivel] : null,
            v.Fonte, v.Url,
            Math.Round(total, 2),
            (int)Math.Round(100 * total / maior),
            skills, cargoNoTitulo, lacunas,
            Justificar(skills, cargoNoTitulo, lacunas, contribuicoes),
            contribuicoes.Take(MaximoTermosExplicados).Select(c => c with { Pontos = Math.Round(c.Pontos, 2) }).ToList());
    }

    private static string Justificar(
        IReadOnlyList<string> skills, IReadOnlyList<string> cargo, IReadOnlyList<string> lacunas, IReadOnlyList<TermoPontuado> contribuicoes)
    {
        var partes = new List<string>();
        partes.Add(skills.Count > 0
            ? $"Pede {Listar(skills)}, que estão no perfil."
            : $"Nenhuma skill completa do perfil aparece; pontuou por termos soltos ({Listar(contribuicoes.Take(3).Select(c => c.Termo).ToList())}).");
        if (cargo.Count > 0)
            partes.Add($"O título bate com o cargo desejado ({Listar(cargo)}).");
        if (lacunas.Count > 0)
            partes.Add($"Falta no perfil: {Listar(lacunas)}.");
        return string.Join(" ", partes);
    }

    private static string Listar(IReadOnlyList<string> itens) =>
        itens.Count <= 5 ? string.Join(", ", itens) : $"{string.Join(", ", itens.Take(5))} e mais {itens.Count - 5}";
}
