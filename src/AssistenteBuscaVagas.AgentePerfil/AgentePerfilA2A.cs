using System.Text.Json;
using AssistenteBuscaVagas.AgentePerfil.Perfil;
using AssistenteBuscaVagas.Comum.A2A;

namespace AssistenteBuscaVagas.AgentePerfil;

/// <summary>
/// Preferências parciais: só os campos informados mudam. Pelo A2A o pedido vem de uma conversa
/// ("agora também aceito híbrido em Campinas"), então substituir tudo apagaria o que o usuário não citou.
/// </summary>
public sealed record PreferenciasParciais(
    IReadOnlyList<string>? Cargos = null,
    IReadOnlyList<string>? Modalidades = null,
    IReadOnlyList<string>? Cidades = null,
    string? Senioridade = null,
    decimal? SalarioMinimoMensal = null,
    string? Moeda = null,
    IReadOnlyList<string>? Idiomas = null)
{
    public Preferencias AplicarSobre(Preferencias? atual) => new(
        Cargos ?? atual?.Cargos ?? [],
        Modalidades ?? atual?.Modalidades ?? [],
        Cidades ?? atual?.Cidades ?? [],
        Senioridade ?? atual?.Senioridade,
        SalarioMinimoMensal ?? atual?.SalarioMinimoMensal,
        Moeda ?? atual?.Moeda,
        Idiomas ?? atual?.Idiomas ?? []);

    public bool Vazia => Cargos is null && Modalidades is null && Cidades is null && Senioridade is null
                         && SalarioMinimoMensal is null && Moeda is null && Idiomas is null;
}

/// <summary>
/// O agente_perfil como agente A2A. O envio do currículo (PDF) continua só no HTTP: o pedido de conversa que chega
/// pelo supervisor é texto.
/// </summary>
public sealed class AgentePerfilA2A(IHttpClientFactory http, ILoggerFactory logs) : AgenteA2A(logs.CreateLogger<AgentePerfilA2A>())
{
    public static readonly IReadOnlyList<Habilidade> Lista =
    [
        new("obter_perfil", "Ver perfil",
            "Mostra o perfil do usuário: currículo estruturado (skills, senioridade, experiências) e preferências de busca " +
            "(cargos, modalidades, cidades, senioridade, salário mínimo, idiomas). Sem dados.",
            ["perfil", "currículo", "skills", "preferências", "senioridade", "meu"],
            ["qual é o meu perfil?", "quais skills estão no meu currículo?", "quais são minhas preferências?"]),
        new("atualizar_preferencias", "Atualizar preferências",
            "Atualiza as preferências de busca. Só os campos enviados mudam; os outros ficam como estão. " +
            "Dados: {\"cargos\": [texto], \"modalidades\": [\"Remoto\"|\"Híbrido\"|\"Presencial\"], \"cidades\": [texto], " +
            "\"senioridade\": \"Estágio\"|\"Júnior\"|\"Pleno\"|\"Sênior\"|\"Especialista\"|\"Liderança\", " +
            "\"salarioMinimoMensal\": número, \"moeda\": \"BRL\"|\"USD\"..., \"idiomas\": [\"pt\", \"en\"...]}. " +
            "Listas substituem a lista anterior inteira: para acrescentar uma cidade, envie a lista completa.",
            ["perfil", "preferências", "atualizar", "mudar", "modalidade", "remoto", "híbrido", "presencial", "cidade", "salário", "cargo"],
            ["atualize meu perfil para aceitar híbrido em Campinas", "agora busco vagas de Tech Lead", "meu salário mínimo é 18 mil"]),
    ];

    protected override IReadOnlyList<Habilidade> Habilidades => Lista;

    protected override async Task<RespostaHabilidade> ExecutarAsync(PedidoHabilidade pedido, CancellationToken ct)
    {
        await using var mcp = await McpVagasCliente.ConectarAsync(http, logs, ct);

        switch (pedido.Habilidade)
        {
            case "obter_perfil":
            {
                var perfil = await mcp.ObterAsync(ct);
                return perfil is null
                    ? RespostaHabilidade.Falha("Perfil ainda não cadastrado. Envie o currículo em POST /perfil/curriculo no agente_perfil.")
                    : RespostaHabilidade.Ok($"Perfil {perfil.Curriculo?.Senioridade ?? "sem currículo"}, " +
                                            $"{perfil.Curriculo?.Skills.Count ?? 0} skills.", perfil);
            }
            case "atualizar_preferencias":
            {
                var parciais = LerDados<PreferenciasParciais>(pedido);
                if (parciais is null || parciais.Vazia)
                    return RespostaHabilidade.Falha("Envie em \"dados\" pelo menos um campo de preferência a alterar.");

                var atual = await mcp.ObterAsync(ct);
                try
                {
                    var salvo = await mcp.SalvarAsync(null, parciais.AplicarSobre(atual?.Preferencias), ct);
                    return RespostaHabilidade.Ok("Preferências atualizadas.", salvo.Preferencias);
                }
                catch (PerfilRejeitadoException ex)
                {
                    return RespostaHabilidade.Falha(ex.Message);
                }
            }
            default:
                throw new JsonException($"Habilidade não tratada: {pedido.Habilidade}");
        }
    }
}
