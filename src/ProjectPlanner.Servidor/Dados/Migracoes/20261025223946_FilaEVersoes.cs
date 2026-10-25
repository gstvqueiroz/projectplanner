using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectPlanner.Servidor.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class FilaEVersoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VersaoDoApp",
                table: "Usuarios",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VersaoVistaEm",
                table: "Usuarios",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AvisosDoTelegram",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UpdateId = table.Column<long>(type: "bigint", nullable: true),
                    Json = table.Column<string>(type: "text", nullable: false),
                    Recebido = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Tentativas = table.Column<int>(type: "integer", nullable: false),
                    Atendido = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvisosDoTelegram", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FalhasDoApp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Quando = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Versao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Sistema = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Origem = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Mensagem = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Pilha = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FalhasDoApp", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AvisosDoTelegram_Atendido",
                table: "AvisosDoTelegram",
                column: "Atendido");

            migrationBuilder.CreateIndex(
                name: "IX_AvisosDoTelegram_UpdateId",
                table: "AvisosDoTelegram",
                column: "UpdateId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FalhasDoApp_Quando",
                table: "FalhasDoApp",
                column: "Quando");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AvisosDoTelegram");

            migrationBuilder.DropTable(
                name: "FalhasDoApp");

            migrationBuilder.DropColumn(
                name: "VersaoDoApp",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "VersaoVistaEm",
                table: "Usuarios");
        }
    }
}
