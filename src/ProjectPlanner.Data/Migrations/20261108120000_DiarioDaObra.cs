using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectPlanner.Data.Migrations
{
    /// <inheritdoc />
    public partial class DiarioDaObra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DiarioDaObra",
                columns: table => new
                {
                    ProjetoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Versao = table.Column<int>(type: "INTEGER", nullable: false),
                    Dados = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiarioDaObra", x => x.ProjetoId);
                    table.ForeignKey(
                        name: "FK_DiarioDaObra_Projetos_ProjetoId",
                        column: x => x.ProjetoId,
                        principalTable: "Projetos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiarioDaObra");
        }
    }
}
