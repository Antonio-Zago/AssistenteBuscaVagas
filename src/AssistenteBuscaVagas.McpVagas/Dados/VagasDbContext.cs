using Microsoft.EntityFrameworkCore;

namespace AssistenteBuscaVagas.McpVagas.Dados;

public sealed class VagasDbContext(DbContextOptions<VagasDbContext> options) : DbContext(options)
{
    public const int FonteRemotiveId = 1;
    public const int FonteWebId = 2;

    public DbSet<Fonte> Fontes => Set<Fonte>();
    public DbSet<Vaga> Vagas => Set<Vaga>();
    public DbSet<Perfil> Perfis => Set<Perfil>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Fonte>(e =>
        {
            e.ToTable("fontes");
            e.Property(f => f.Nome).HasMaxLength(100);
            e.Property(f => f.Tipo).HasMaxLength(50);
            e.Property(f => f.UrlBase).HasMaxLength(500);
            e.HasIndex(f => f.Nome).IsUnique();

            // Fontes conhecidas. O agente_coleta informa o nome da fonte ao chamar salvar_vagas.
            e.HasData(
            new Fonte
            {
                Id = FonteRemotiveId,
                Nome = "Remotive",
                Tipo = "api",
                UrlBase = "https://remotive.com/",
                Ativa = true,
            },
            new Fonte
            {
                Id = FonteWebId,
                Nome = "Web",
                Tipo = "web-jsonld",
                UrlBase = "https://www.firecrawl.dev/",
                Ativa = true,
            });
        });

        modelBuilder.Entity<Vaga>(e =>
        {
            e.ToTable("vagas");
            e.Property(v => v.IdExterno).HasMaxLength(200);
            e.Property(v => v.Empresa).HasMaxLength(300);
            e.Property(v => v.Titulo).HasMaxLength(500);
            e.Property(v => v.Url).HasMaxLength(1000);
            e.Property(v => v.HashDedup).HasMaxLength(64);

            e.HasIndex(v => new { v.FonteId, v.IdExterno }).IsUnique();
            e.HasIndex(v => v.HashDedup);
            e.HasIndex(v => v.PublicadaEm);
        });

        modelBuilder.Entity<Perfil>(e =>
        {
            e.ToTable("perfis");
            e.Property(p => p.Id).ValueGeneratedNever();
            e.Property(p => p.Curriculo).HasColumnType("jsonb");
            e.Property(p => p.Preferencias).HasColumnType("jsonb");
        });
    }
}
