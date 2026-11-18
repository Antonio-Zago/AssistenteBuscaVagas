using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AssistenteBuscaVagas.McpVagas.Dados.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarExecucoesDiarias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "execucoes_diarias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Tentativas = table.Column<int>(type: "integer", nullable: false),
                    IniciadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcluidaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Resumo = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_execucoes_diarias", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "recomendacoes_enviadas",
                columns: table => new
                {
                    VagaId = table.Column<int>(type: "integer", nullable: false),
                    ExecucaoId = table.Column<int>(type: "integer", nullable: false),
                    Nota = table.Column<int>(type: "integer", nullable: false),
                    RecomendadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recomendacoes_enviadas", x => x.VagaId);
                    table.ForeignKey(
                        name: "FK_recomendacoes_enviadas_execucoes_diarias_ExecucaoId",
                        column: x => x.ExecucaoId,
                        principalTable: "execucoes_diarias",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_recomendacoes_enviadas_vagas_VagaId",
                        column: x => x.VagaId,
                        principalTable: "vagas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_execucoes_diarias_Data",
                table: "execucoes_diarias",
                column: "Data",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recomendacoes_enviadas_ExecucaoId",
                table: "recomendacoes_enviadas",
                column: "ExecucaoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recomendacoes_enviadas");

            migrationBuilder.DropTable(
                name: "execucoes_diarias");
        }
    }
}
