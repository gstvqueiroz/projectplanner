using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectPlanner.Servidor.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class Telegram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CaixaDeEntrada",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObraId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Texto = table.Column<string>(type: "text", nullable: false),
                    Conteudo = table.Column<string>(type: "text", nullable: true),
                    Tarefas = table.Column<string>(type: "text", nullable: true),
                    Foto = table.Column<byte[]>(type: "bytea", nullable: true),
                    NomeDaFoto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaixaDeEntrada", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CodigosDoTelegram",
                columns: table => new
                {
                    Hash = table.Column<string>(type: "text", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodigosDoTelegram", x => x.Hash);
                });

            migrationBuilder.CreateTable(
                name: "Obras",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Projeto = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Retrato = table.Column<string>(type: "text", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Obras", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VinculosDoTelegram",
                columns: table => new
                {
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChatId = table.Column<long>(type: "bigint", nullable: false),
                    Nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ObraAtiva = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VinculosDoTelegram", x => x.UsuarioId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaixaDeEntrada_UsuarioId_ObraId",
                table: "CaixaDeEntrada",
                columns: new[] { "UsuarioId", "ObraId" });

            migrationBuilder.CreateIndex(
                name: "IX_Obras_UsuarioId_Projeto",
                table: "Obras",
                columns: new[] { "UsuarioId", "Projeto" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VinculosDoTelegram_ChatId",
                table: "VinculosDoTelegram",
                column: "ChatId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaixaDeEntrada");

            migrationBuilder.DropTable(
                name: "CodigosDoTelegram");

            migrationBuilder.DropTable(
                name: "Obras");

            migrationBuilder.DropTable(
                name: "VinculosDoTelegram");
        }
    }
}
