namespace AssistenteBuscaVagas.AgenteColeta.Configuracao;

/// <summary>Configuração do agente de coleta (seção "Coleta" do appsettings).</summary>
public sealed class ColetaOptions
{
    /// <summary>Modelo Claude usado pelo agente. Ex.: claude-opus-5-5 ou claude-haiku-5-5 (mais barato).</summary>
    public string Modelo { get; set; } = "claude-opus-5-5";

    /// <summary>Esforço de raciocínio: low, medium, high. Planejar buscas e triar títulos é tarefa simples.</summary>
    public string Esforco { get; set; } = "low";

    /// <summary>Máximo de chamadas de ferramenta numa execução (trava contra loops).</summary>
    public int MaximoIteracoes { get; set; } = 15;

    /// <summary>O que procurar. Na fase 2 isto vem do perfil (obter_perfil); até lá, da configuração.</summary>
    public PreferenciasBusca Preferencias { get; set; } = new();

    public OrcamentoOptions Orcamento { get; set; } = new();

    /// <summary>Domínios que nunca são buscados nem abertos (termos de uso proíbem acesso automatizado).</summary>
    public List<string> DominiosBloqueados { get; set; } = [];

    /// <summary>Intervalo mínimo entre consultas à Remotive (os termos pedem no máximo ~4 por dia).</summary>
    public int IntervaloMinimoRemotiveHoras { get; set; } = 6;
}

public sealed class PreferenciasBusca
{
    public List<string> Cargos { get; set; } = [];
    public List<string> Skills { get; set; } = [];
    public string? Senioridade { get; set; }
    public List<string> Locais { get; set; } = [];
    public List<string> Idiomas { get; set; } = [];
}

/// <summary>Orçamento diário de créditos do Firecrawl (plano gratuito: 1000 créditos/mês).</summary>
public sealed class OrcamentoOptions
{
    public int CreditosPorDia { get; set; } = 26;

    /// <summary>Custo estimado de uma busca de até 10 resultados.</summary>
    public int CustoBusca { get; set; } = 2;

    /// <summary>Custo de um scrape em rawHtml.</summary>
    public int CustoScrape { get; set; } = 1;

    /// <summary>Máximo de URLs abertas por chamada de extrair_vagas.</summary>
    public int MaximoUrlsPorChamada { get; set; } = 10;
}
