using AssistenteBuscaVagas.McpVagas.Armazenamento;

namespace AssistenteBuscaVagas.McpVagas.Tests;

public class ContagemLacunasTests
{
    [Fact]
    public void ContaUmaVezPorVagaSemDiferenciarMaiusculas()
    {
        IReadOnlyList<IReadOnlyList<string>> vagas =
        [
            ["Docker", "docker", "Kubernetes"],
            ["DOCKER", "AWS"],
            ["Kubernetes"],
            [],
        ];

        var r = ArmazenamentoCandidaturas.ContarLacunas(vagas, 10);

        Assert.Equal(new LacunaContada("Docker", 2, 50), r[0]);
        Assert.Equal(new LacunaContada("Kubernetes", 2, 50), r[1]);
        Assert.Equal(new LacunaContada("AWS", 1, 25), r[2]);
    }

    [Fact]
    public void LimitaEIgnoraVazios()
    {
        IReadOnlyList<IReadOnlyList<string>> vagas = [["a", " ", "b", "c"]];
        Assert.Equal(2, ArmazenamentoCandidaturas.ContarLacunas(vagas, 2).Count);
        Assert.Empty(ArmazenamentoCandidaturas.ContarLacunas([], 10));
    }

    [Fact]
    public void EtapasDoKanban() =>
        Assert.Equal(["salva", "aplicada", "entrevista", "oferta", "recusada"], ArmazenamentoCandidaturas.Etapas);
}
