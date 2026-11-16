using System.Text.Json;
using AssistenteBuscaVagas.Roteador.Catalogo;
using AssistenteBuscaVagas.Supervisor.Orquestracao;
using CapacidadeRoteador = AssistenteBuscaVagas.Roteador.Catalogo.Capacidade;
using CapacidadeSupervisor = AssistenteBuscaVagas.Supervisor.Orquestracao.Capacidade;

namespace AssistenteBuscaVagas.Orquestracao.Tests;

public class RoteamentoTests
{
    // Um catálogo parecido com o real: as mesmas descrições curtas, tags e exemplos dos Agent Cards.
    private static readonly IReadOnlyList<CapacidadeRoteador> Catalogo =
    [
        Agente("agente_match", "recomendar_vagas", "Recomendar vagas", "Recomenda as 5 vagas que mais combinam com o perfil, com nota e lacunas de skills.",
            ["vagas", "recomendação", "combinam", "melhores", "lacunas", "skills"], ["me mostre as melhores vagas", "quais skills me faltam?"], paga: true),
        Agente("agente_match", "ver_funil", "Ver funil sem LLM", "Ranqueia as vagas sem IA e mostra quantas passaram em cada etapa.",
            ["vagas", "funil", "ranking", "filtros"], ["quantas vagas passaram nos filtros?"]),
        Agente("agente_perfil", "obter_perfil", "Ver perfil", "Mostra o perfil do usuário: currículo e preferências.",
            ["perfil", "currículo", "skills", "preferências"], ["qual é o meu perfil?"]),
        Agente("agente_perfil", "atualizar_preferencias", "Atualizar preferências", "Atualiza as preferências de busca: modalidades, cidades, salário.",
            ["perfil", "preferências", "atualizar", "híbrido", "remoto", "cidade"], ["atualize meu perfil para aceitar híbrido em Campinas"]),
        Agente("agente_coleta", "coletar_remotive", "Coletar vagas da Remotive", "Busca vagas novas na Remotive e grava no banco.",
            ["coletar", "buscar", "vagas", "novas", "remotive"], ["busque vagas novas"]),
        new("mcp-vagas.listar_fontes", "mcp", "mcp-vagas", "listar_fontes", "listar_fontes",
            "Lista as fontes de vagas cadastradas e quando cada uma recebeu vagas pela última vez.", [], [], "http://mcp-vagas/mcp", false, true, null),
        new("mcp-vagas.salvar_vagas", "mcp", "mcp-vagas", "salvar_vagas", "salvar_vagas",
            "Grava vagas coletadas de uma fonte.", [], [], "http://mcp-vagas/mcp", false, false, null),
    ];

    private static CapacidadeRoteador Agente(string origem, string id, string nome, string descricao, string[] tags, string[] exemplos, bool paga = false) =>
        new($"{origem}.{id}", "agente", origem, id, nome, descricao, tags, exemplos, $"http://{origem}/a2a", paga, false, null);

    [Theory]
    [InlineData("me mostre as vagas que mais combinam comigo", "agente_match.recomendar_vagas")]
    [InlineData("quero aceitar trabalho híbrido em Campinas", "agente_perfil.atualizar_preferencias")]
    [InlineData("busque vagas novas", "agente_coleta.coletar_remotive")]
    [InlineData("quais fontes de vagas estão cadastradas?", "mcp-vagas.listar_fontes")]
    [InlineData("qual é o meu perfil?", "agente_perfil.obter_perfil")]
    public void PrimeiroResultadoEhACapacidadeCerta(string pedido, string esperado)
    {
        var achados = new IndiceCapacidades(Catalogo).Buscar(pedido, 3);
        Assert.Equal(esperado, achados[0].Capacidade.Id);
    }

    [Fact]
    public void PedidoComposto_TrazAsDuasCapacidades()
    {
        var ids = new IndiceCapacidades(Catalogo).Buscar("atualize meu perfil para aceitar híbrido e me mostre as melhores vagas", 4)
            .Select(a => a.Capacidade.Id).ToList();

        Assert.Contains("agente_perfil.atualizar_preferencias", ids);
        Assert.Contains("agente_match.recomendar_vagas", ids);
    }

    [Fact]
    public void PedidoSemTermoUtilNaoTrazNada()
    {
        Assert.Empty(new IndiceCapacidades(Catalogo).Buscar("o a de", 5));
        Assert.Empty(new IndiceCapacidades(Catalogo).Buscar("xyzzy", 5));
    }

    [Theory]
    [InlineData("vagas", "vaga")]
    [InlineData("skills", "skill")]
    [InlineData("class", "class")]
    [InlineData("aws", "aws")]
    [InlineData(".net", ".net")]
    public void RadicalTiraSoOPlural(string termo, string esperado) =>
        Assert.Equal(esperado, IndiceCapacidades.Radical(termo));

    [Fact]
    public void SupervisorNaoOfereceFerramentaMcpQueGrava()
    {
        var encontradas = Catalogo
            .Select(c => new AssistenteBuscaVagas.Supervisor.Orquestracao.CapacidadeEncontrada(
                JsonSerializer.Deserialize<CapacidadeSupervisor>(JsonSerializer.Serialize(c))!, 1, []))
            .ToList();

        var oferecidas = ServicoSupervisor.Oferecer(encontradas, 10).Select(x => x.Oferecida.Ferramenta).ToList();

        Assert.Contains("mcp-vagas__listar_fontes", oferecidas);
        Assert.DoesNotContain("mcp-vagas__salvar_vagas", oferecidas);
        Assert.Equal(3, ServicoSupervisor.Oferecer(encontradas, 3).Count);
    }

    [Fact]
    public void NomeDeFerramentaValidoParaAApi()
    {
        Assert.Equal("agente_match__recomendar_vagas", NomesFerramenta.De("agente_match", "recomendar_vagas"));
        Assert.Equal("a_b__c_d", NomesFerramenta.De("a.b", "c d"));
        Assert.Equal(64, NomesFerramenta.De(new string('x', 50), new string('y', 50)).Length);
    }

    [Fact]
    public void ResultadoGrandeEhCortadoComAviso()
    {
        var execucao = new Execucao(10);
        Assert.Equal("curto", execucao.Limitar("curto"));
        Assert.StartsWith("0123456789\n[resultado cortado", execucao.Limitar("0123456789ABC"));
    }
}
