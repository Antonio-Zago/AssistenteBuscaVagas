namespace AssistenteBuscaVagas.WorkerDiario;

public sealed class AgendaOptions
{
    /// <summary>Liga o disparo automático. Desligado, o worker só roda por POST /execucoes/agora.</summary>
    public bool Ativa { get; set; } = true;

    /// <summary>Horário local da execução (HH:mm).</summary>
    public string Horario { get; set; } = "09:00";

    /// <summary>
    /// Dias da semana em que roda (Monday, Tuesday...). Vazio = todos os dias. O padrão fica vazio de propósito:
    /// o binder de configuração soma os itens do appsettings aos de um array já preenchido em vez de substituí-los.
    /// </summary>
    public DayOfWeek[] DiasDaSemana { get; set; } = [];

    /// <summary>Fuso do usuário (IANA). "Já rodou hoje?" depende do dia local, não do dia em UTC.</summary>
    public string FusoHorario { get; set; } = "America/Sao_Paulo";

    /// <summary>De quanto em quanto tempo o worker confere se está na hora (e se precisa recuperar um dia perdido).</summary>
    public int VerificarACadaMinutos { get; set; } = 15;

    /// <summary>Quantas vezes tentar cada horário agendado se ele falhar. Limita gasto quando algo está quebrado.</summary>
    public int MaximoTentativas { get; set; } = 2;

    /// <summary>Roda também a coleta na web (Claude + Firecrawl, paga). A Remotive (grátis) roda sempre.</summary>
    public bool ColetaWeb { get; set; } = true;

    /// <summary>Foco opcional para a coleta na web (ex.: "priorize vagas de .NET").</summary>
    public string? PedidoColetaWeb { get; set; }

    /// <summary>Só entram no resumo vagas com nota do LLM a partir disto.</summary>
    public int NotaMinima { get; set; } = 60;

    /// <summary>Janela de publicação das vagas consideradas no match.</summary>
    public int DiasJanela { get; set; } = 30;
}

/// <summary>Uma execução gravada no banco, só com o que a agenda precisa.</summary>
public sealed record ExecucaoAnterior(DateOnly Data, string Status, int Tentativas);

public sealed record Decisao(bool Executar, string Motivo, DateTimeOffset? HorarioAtendido);

/// <summary>
/// A regra de quando rodar, sem relógio nem banco dentro: recebe o momento atual e as execuções recentes.
/// Como o worker vive dentro do AppHost na máquina do usuário, ele pode estar desligado no horário marcado.
/// Por isso a pergunta não é "é segunda às 09:00?", e sim "o último horário agendado que já passou foi atendido?".
/// Computador desligado na segunda → a rodada de segunda acontece quando ele ligar na terça; a de quinta segue normal.
/// </summary>
public static class Agenda
{
    public static TimeZoneInfo Fuso(string id) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(id, out var fuso) ? fuso : TimeZoneInfo.Local;

    public static DateOnly DiaLocal(DateTimeOffset agoraUtc, TimeZoneInfo fuso) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(agoraUtc, fuso).DateTime);

    private static bool Agendado(DateOnly dia, IReadOnlyCollection<DayOfWeek> dias) => dias.Count == 0 || dias.Contains(dia.DayOfWeek);

    private static DateTimeOffset NoFuso(DateOnly dia, TimeOnly horario, TimeZoneInfo fuso)
    {
        var alvo = dia.ToDateTime(horario);
        return new DateTimeOffset(alvo, fuso.GetUtcOffset(alvo));
    }

    /// <summary>O horário agendado mais recente que já passou (até 7 dias para trás), ou null se não houver dia agendado.</summary>
    public static DateTimeOffset? UltimoHorario(DateTimeOffset agoraUtc, TimeZoneInfo fuso, TimeOnly horario, IReadOnlyCollection<DayOfWeek> dias)
    {
        var hoje = DiaLocal(agoraUtc, fuso);
        for (var i = 0; i <= 7; i++)
        {
            var dia = hoje.AddDays(-i);
            var momento = NoFuso(dia, horario, fuso);
            if (Agendado(dia, dias) && momento <= agoraUtc) return momento;
        }
        return null;
    }

    /// <summary>O próximo horário agendado depois de agora.</summary>
    public static DateTimeOffset? ProximoHorario(DateTimeOffset agoraUtc, TimeZoneInfo fuso, TimeOnly horario, IReadOnlyCollection<DayOfWeek> dias)
    {
        var hoje = DiaLocal(agoraUtc, fuso);
        for (var i = 0; i <= 7; i++)
        {
            var dia = hoje.AddDays(i);
            var momento = NoFuso(dia, horario, fuso);
            if (Agendado(dia, dias) && momento > agoraUtc) return momento;
        }
        return null;
    }

    /// <param name="execucoes">Execuções recentes (as últimas ~8 bastam).</param>
    public static Decisao Decidir(DateTimeOffset agoraUtc, TimeZoneInfo fuso, TimeOnly horario, IReadOnlyCollection<DayOfWeek> dias,
        IReadOnlyList<ExecucaoAnterior> execucoes, int maximoTentativas)
    {
        if (UltimoHorario(agoraUtc, fuso, horario, dias) is not { } ultimo)
            return new(false, "nenhum horário agendado nos últimos 7 dias", null);

        var diaDoHorario = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(ultimo, fuso).DateTime);
        var quando = $"{diaDoHorario:dd/MM} {horario:HH\\:mm}";
        // Atende o horário qualquer execução feita a partir do dia dele (inclusive uma recuperação no dia seguinte).
        var desde = execucoes.Where(e => e.Data >= diaDoHorario).ToList();

        if (desde.FirstOrDefault(e => e.Status != "falha") is { } feita)
            return new(false, $"horário de {quando} já atendido ({feita.Status} em {feita.Data:dd/MM})", ultimo);

        var tentativas = desde.Sum(e => e.Tentativas);
        if (tentativas >= maximoTentativas)
            return new(false, $"horário de {quando} falhou {tentativas} vezes; espera o próximo", ultimo);

        return new(true, tentativas == 0
            ? $"horário de {quando} ainda não rodou"
            : $"nova tentativa do horário de {quando} ({tentativas + 1} de {maximoTentativas})", ultimo);
    }
}
