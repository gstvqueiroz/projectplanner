using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectPlanner.Data.Migrations
{
    /// <inheritdoc />
    public partial class FotosDasTarefas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FotosTarefa",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TarefaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordem = table.Column<int>(type: "INTEGER", nullable: false),
                    DataRegistro = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CaminhoArquivo = table.Column<string>(type: "TEXT", nullable: false),
                    Descricao = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FotosTarefa", x => x.Id);
                    table.CheckConstraint("CK_FotosTarefa_CaminhoArquivo", "length(trim(CaminhoArquivo)) > 0");
                    table.ForeignKey(
                        name: "FK_FotosTarefa_Tarefas_TarefaId",
                        column: x => x.TarefaId,
                        principalTable: "Tarefas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FotosTarefa_TarefaId",
                table: "FotosTarefa",
                column: "TarefaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FotosTarefa");
        }
    }
}
