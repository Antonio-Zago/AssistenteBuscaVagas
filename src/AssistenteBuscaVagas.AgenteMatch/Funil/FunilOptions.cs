namespace AssistenteBuscaVagas.AgenteMatch.Funil;

/// <summary>Parâmetros do funil (seção "Funil" do appsettings). Valores pensados para um candidato no Brasil.</summary>
public sealed class FunilOptions
{
    /// <summary>Só entram vagas publicadas (ou coletadas, se a fonte não deu data) nos últimos N dias.</summary>
    public int DiasMaximos { get; set; } = 30;

    /// <summary>Teto de vagas lidas do mcp_vagas por execução.</summary>
    public int MaximoVagasLidas { get; set; } = 2000;

    /// <summary>Quantas vagas saem do funil (a entrada da etapa com LLM, na fase 4).</summary>
    public int Recomendacoes { get; set; } = 20;

    /// <summary>Distância máxima aceita entre a senioridade do título e a desejada (1 = um nível acima ou abaixo).</summary>
    public int ToleranciaSenioridade { get; set; } = 1;

    /// <summary>
    /// Vaga remota só passa se o local exigido do candidato estiver vazio ou contiver um destes trechos.
    /// "USA only" ou "Europe" ficam de fora. Comparação sem acento e sem diferenciar maiúsculas.
    /// </summary>
    public List<string> LocaisRemotosAceitos { get; set; } =
        ["worldwide", "anywhere", "global", "remote", "remoto", "brazil", "brasil", "latam", "latin america", "americas", "south america"];

    /// <summary>Peso dos campos na frequência dos termos (BM25F simplificado).</summary>
    public double PesoTitulo { get; set; } = 3;
    public double PesoTags { get; set; } = 2;
    public double PesoDescricao { get; set; } = 1;

    /// <summary>Peso dos termos que vêm dos cargos desejados e do cargo atual, em relação a uma skill sem anos informados.</summary>
    public double PesoCargo { get; set; } = 1.5;
}
