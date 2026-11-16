using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AssistenteBuscaVagas.McpVagas.Dados.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarMatchesERotulos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "matches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VagaId = table.Column<int>(type: "integer", nullable: false),
                    ChaveAvaliacao = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Modelo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VersaoPrompt = table.Column<int>(type: "integer", nullable: false),
                    Nota = table.Column<int>(type: "integer", nullable: false),
                    Justificativa = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    PontosFortes = table.Column<List<string>>(type: "text[]", nullable: false),
                    Lacunas = table.Column<List<string>>(type: "text[]", nullable: false),
                    Impeditivos = table.Column<List<string>>(type: "text[]", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_matches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_matches_vagas_VagaId",
                        column: x => x.VagaId,
                        principalTable: "vagas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rotulos",
                columns: table => new
                {
                    VagaId = table.Column<int>(type: "integer", nullable: false),
                    Relevante = table.Column<bool>(type: "boolean", nullable: false),
                    Observacao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rotulos", x => x.VagaId);
                    table.ForeignKey(
                        name: "FK_rotulos_vagas_VagaId",
                        column: x => x.VagaId,
                        principalTable: "vagas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_matches_ChaveAvaliacao_VagaId",
                table: "matches",
                columns: new[] { "ChaveAvaliacao", "VagaId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_matches_VagaId",
                table: "matches",
                column: "VagaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "matches");

            migrationBuilder.DropTable(
                name: "rotulos");
        }
    }
}
