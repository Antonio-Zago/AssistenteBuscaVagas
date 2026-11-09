using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AssistenteBuscaVagas.McpVagas.Dados.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fontes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UrlBase = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Ativa = table.Column<bool>(type: "boolean", nullable: false),
                    UltimaColetaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fontes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "vagas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FonteId = table.Column<int>(type: "integer", nullable: false),
                    IdExterno = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Empresa = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Titulo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Descricao = table.Column<string>(type: "text", nullable: false),
                    DescricaoOriginal = table.Column<string>(type: "text", nullable: false),
                    Local = table.Column<string>(type: "text", nullable: true),
                    Remoto = table.Column<bool>(type: "boolean", nullable: false),
                    Senioridade = table.Column<string>(type: "text", nullable: true),
                    Categoria = table.Column<string>(type: "text", nullable: true),
                    TipoContrato = table.Column<string>(type: "text", nullable: true),
                    Salario = table.Column<string>(type: "text", nullable: true),
                    Tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PublicadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ColetadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    HashDedup = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vagas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_vagas_fontes_FonteId",
                        column: x => x.FonteId,
                        principalTable: "fontes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "fontes",
                columns: new[] { "Id", "Ativa", "Nome", "Tipo", "UltimaColetaEm", "UrlBase" },
                values: new object[,]
                {
                    { 1, true, "Remotive", "api", null, "https://remotive.com/" },
                    { 2, true, "Web", "web-jsonld", null, "https://www.firecrawl.dev/" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_fontes_Nome",
                table: "fontes",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vagas_FonteId_IdExterno",
                table: "vagas",
                columns: new[] { "FonteId", "IdExterno" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vagas_HashDedup",
                table: "vagas",
                column: "HashDedup");

            migrationBuilder.CreateIndex(
                name: "IX_vagas_PublicadaEm",
                table: "vagas",
                column: "PublicadaEm");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vagas");

            migrationBuilder.DropTable(
                name: "fontes");
        }
    }
}
