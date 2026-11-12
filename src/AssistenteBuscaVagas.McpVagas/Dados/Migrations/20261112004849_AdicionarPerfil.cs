using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssistenteBuscaVagas.McpVagas.Dados.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarPerfil : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "perfis",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Curriculo = table.Column<string>(type: "jsonb", nullable: true),
                    CurriculoAtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Preferencias = table.Column<string>(type: "jsonb", nullable: true),
                    PreferenciasAtualizadasEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_perfis", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "perfis");
        }
    }
}
