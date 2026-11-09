using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AssistenteBuscaVagas.McpVagas.Dados;

/// <summary>
/// Usada só pelo <c>dotnet ef</c> para gerar migrations. Gerar uma migration não conecta no banco,
/// então a connection string aqui é apenas um marcador; em execução quem fornece a real é o Aspire.
/// </summary>
public sealed class VagasDbContextFactory : IDesignTimeDbContextFactory<VagasDbContext>
{
    public VagasDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<VagasDbContext>()
            .UseNpgsql("Host=localhost;Database=vagasdb")
            .Options);
}
