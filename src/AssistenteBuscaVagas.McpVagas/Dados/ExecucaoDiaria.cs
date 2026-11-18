namespace AssistenteBuscaVagas.McpVagas.Dados;

/// <summary>
/// Uma execução do worker diário (coleta + match + resumo). Uma linha por dia: é por ela que o worker sabe
/// se o dia já rodou, inclusive depois de o computador ficar desligado no horário marcado.
/// </summary>
public sealed class ExecucaoDiaria
{
    public int Id { get; set; }

    /// <summary>O dia no fuso do usuário (não em UTC): "rodou hoje?" é uma pergunta sobre o dia local.</summary>
    public DateOnly Data { get; set; }

    /// <summary>sucesso, parcial (algum passo falhou) ou falha.</summary>
    public required string Status { get; set; }

    public int Tentativas { get; set; }
    public DateTimeOffset IniciadaEm { get; set; }
    public DateTimeOffset ConcluidaEm { get; set; }

    /// <summary>Resumo do dia em JSON (passos, contagens, vagas recomendadas, custo). Formato definido pelo worker.</summary>
    public required string Resumo { get; set; }
}

/// <summary>Vaga já mostrada ao usuário num resumo diário. Com "só vagas novas", ela não volta nos dias seguintes.</summary>
public sealed class RecomendacaoEnviada
{
    public int VagaId { get; set; }
    public Vaga Vaga { get; set; } = null!;

    public int ExecucaoId { get; set; }
    public ExecucaoDiaria Execucao { get; set; } = null!;

    public int Nota { get; set; }
    public DateTimeOffset RecomendadaEm { get; set; }
}
