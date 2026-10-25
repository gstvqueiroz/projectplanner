using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectPlanner.Servidor.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class FalhasDoServidor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FalhasDoServidor",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Codigo = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Quando = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Rota = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Mensagem = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Pilha = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FalhasDoServidor", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FalhasDoServidor_Quando",
                table: "FalhasDoServidor",
                column: "Quando");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FalhasDoServidor");
        }
    }
}
