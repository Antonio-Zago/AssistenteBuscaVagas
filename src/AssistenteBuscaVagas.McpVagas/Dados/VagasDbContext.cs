using Microsoft.EntityFrameworkCore;

namespace AssistenteBuscaVagas.McpVagas.Dados;

public sealed class VagasDbContext(DbContextOptions<VagasDbContext> options) : DbContext(options)
{
    public const int FonteRemotiveId = 1;
    public const int FonteWebId = 2;

    public DbSet<Fonte> Fontes => Set<Fonte>();
    public DbSet<Vaga> Vagas => Set<Vaga>();
    public DbSet<Perfil> Perfis => Set<Perfil>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<Rotulo> Rotulos => Set<Rotulo>();
    public DbSet<ExecucaoDiaria> ExecucoesDiarias => Set<ExecucaoDiaria>();
    public DbSet<RecomendacaoEnviada> RecomendacoesEnviadas => Set<RecomendacaoEnviada>();
    public DbSet<Candidatura> Candidaturas => Set<Candidatura>();

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

        modelBuilder.Entity<Match>(e =>
        {
            e.ToTable("matches");
            e.Property(m => m.ChaveAvaliacao).HasMaxLength(64);
            e.Property(m => m.Modelo).HasMaxLength(100);
            e.Property(m => m.Justificativa).HasMaxLength(2000);
            e.HasOne(m => m.Vaga).WithMany().HasForeignKey(m => m.VagaId).OnDelete(DeleteBehavior.Cascade);
            // Uma avaliação por vaga e por chave (perfil + modelo + prompt): é a chave do cache.
            e.HasIndex(m => new { m.ChaveAvaliacao, m.VagaId }).IsUnique();
        });

        modelBuilder.Entity<Rotulo>(e =>
        {
            e.ToTable("rotulos");
            e.HasKey(r => r.VagaId);
            e.Property(r => r.Observacao).HasMaxLength(500);
            e.HasOne(r => r.Vaga).WithMany().HasForeignKey(r => r.VagaId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExecucaoDiaria>(e =>
        {
            e.ToTable("execucoes_diarias");
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Resumo).HasColumnType("jsonb");
            e.HasIndex(x => x.Data).IsUnique();
        });

        modelBuilder.Entity<RecomendacaoEnviada>(e =>
        {
            e.ToTable("recomendacoes_enviadas");
            e.HasKey(x => x.VagaId);
            e.HasOne(x => x.Vaga).WithMany().HasForeignKey(x => x.VagaId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Execucao).WithMany().HasForeignKey(x => x.ExecucaoId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Candidatura>(e =>
        {
            e.ToTable("candidaturas");
            e.HasKey(x => x.VagaId);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Notas).HasMaxLength(2000);
            e.HasOne(x => x.Vaga).WithMany().HasForeignKey(x => x.VagaId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.Status);
        });
    }
}
