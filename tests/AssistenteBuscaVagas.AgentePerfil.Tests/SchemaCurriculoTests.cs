using System.Text.Json;
using AssistenteBuscaVagas.AgentePerfil.Perfil;
using AssistenteBuscaVagas.McpVagas.Armazenamento;

namespace AssistenteBuscaVagas.AgentePerfil.Tests;

/// <summary>
/// O schema pedido ao Claude e a validação do mcp_vagas são escritos em lugares diferentes.
/// Estes testes garantem que os dois aceitam os mesmos valores e que a saída do modelo desserializa.
/// </summary>
public class SchemaCurriculoTests
{
    private static readonly JsonElement Schema = JsonDocument.Parse(ExtratorCurriculo.SchemaJson).RootElement;

    private static string[] Enum(params string[] caminho)
    {
        var no = Schema;
        foreach (var parte in caminho)
            no = no.GetProperty(parte);
        return no.GetProperty("enum").EnumerateArray().Select(e => e.GetString()!).ToArray();
    }

    [Fact]
    public void Enums_do_schema_batem_com_a_validacao_do_mcp_vagas()
    {
        Assert.Equal(ValidacaoPerfil.Senioridades, Enum("properties", "senioridade"));
        Assert.Equal(ValidacaoPerfil.CategoriasSkill, Enum("properties", "skills", "items", "properties", "categoria"));
        Assert.Equal(ValidacaoPerfil.NiveisIdioma, Enum("properties", "idiomas", "items", "properties", "nivel"));
    }

    [Fact]
    public void Todo_objeto_do_schema_segue_as_regras_de_saida_estruturada()
    {
        // Saída estruturada exige additionalProperties=false e todos os campos em "required".
        foreach (var objeto in Objetos(Schema))
        {
            Assert.False(objeto.GetProperty("additionalProperties").GetBoolean());
            var campos = objeto.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order();
            var obrigatorios = objeto.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).Order();
            Assert.Equal(campos, obrigatorios);
        }
    }

    [Fact]
    public void Exemplo_de_saida_do_modelo_desserializa_no_contrato()
    {
        const string saida = """
            {
              "resumo": "Desenvolvedor backend com 8 anos em .NET.",
              "cargoAtual": "Desenvolvedor Sênior",
              "senioridade": "Sênior",
              "anosExperiencia": 8,
              "skills": [{ "nome": "C#", "categoria": "Linguagem", "anos": 8, "evidencia": "APIs em C#" }],
              "idiomas": [{ "nome": "Inglês", "nivel": "Avançado" }],
              "experiencias": [{ "empresa": "X", "cargo": "Dev", "inicio": "2020-03", "fim": null, "destaques": ["APIs no Azure"] }],
              "formacao": [{ "instituicao": "USP", "curso": "BCC", "conclusao": null }],
              "certificacoes": []
            }
            """;

        var c = JsonSerializer.Deserialize<Perfil.Curriculo>(saida, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal("Sênior", c.Senioridade);
        Assert.Null(c.Experiencias[0].Fim);
        Assert.Equal(8, c.Skills[0].Anos);
    }

    private static IEnumerable<JsonElement> Objetos(JsonElement no)
    {
        if (no.ValueKind == JsonValueKind.Object)
        {
            if (no.TryGetProperty("type", out var tipo) && tipo.ValueKind == JsonValueKind.String && tipo.GetString() == "object")
                yield return no;
            foreach (var p in no.EnumerateObject())
                foreach (var filho in Objetos(p.Value))
                    yield return filho;
        }
        else if (no.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in no.EnumerateArray())
                foreach (var filho in Objetos(item))
                    yield return filho;
        }
    }
}
