using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectPlanner.Servidor.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CodigosDeEmail",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "text", nullable: false),
                    Hash = table.Column<string>(type: "text", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Tentativas = table.Column<int>(type: "integer", nullable: false),
                    SenhaHash = table.Column<string>(type: "text", nullable: true),
                    Nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodigosDeEmail", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TokensDeRenovacao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Hash = table.Column<string>(type: "text", nullable: false),
                    Familia = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevogadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokensDeRenovacao", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsosDeIa",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quando = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Modelo = table.Column<string>(type: "text", nullable: false),
                    TokensDeEntrada = table.Column<int>(type: "integer", nullable: false),
                    TokensDeSaida = table.Column<int>(type: "integer", nullable: false),
                    TokensTotais = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsosDeIa", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    EmailNormalizado = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SenhaHash = table.Column<string>(type: "text", nullable: true),
                    GoogleId = table.Column<string>(type: "text", nullable: true),
                    EmailConfirmado = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Plano = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TentativasFalhas = table.Column<int>(type: "integer", nullable: false),
                    BloqueadoAte = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CodigosDeEmail_UsuarioId_Tipo",
                table: "CodigosDeEmail",
                columns: new[] { "UsuarioId", "Tipo" });

            migrationBuilder.CreateIndex(
                name: "IX_TokensDeRenovacao_Familia",
                table: "TokensDeRenovacao",
                column: "Familia");

            migrationBuilder.CreateIndex(
                name: "IX_TokensDeRenovacao_Hash",
                table: "TokensDeRenovacao",
                column: "Hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsosDeIa_UsuarioId_Quando",
                table: "UsosDeIa",
                columns: new[] { "UsuarioId", "Quando" });

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_EmailNormalizado",
                table: "Usuarios",
                column: "EmailNormalizado",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_GoogleId",
                table: "Usuarios",
                column: "GoogleId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CodigosDeEmail");

            migrationBuilder.DropTable(
                name: "TokensDeRenovacao");

            migrationBuilder.DropTable(
                name: "UsosDeIa");

            migrationBuilder.DropTable(
                name: "Usuarios");
        }
    }
}
