using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectPlanner.Data.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Projetos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Nome = table.Column<string>(type: "TEXT", nullable: false),
                    DataInicio = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DataCriacao = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DataUltimaModificacao = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projetos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tarefas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjetoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordem = table.Column<int>(type: "INTEGER", nullable: false),
                    Nome = table.Column<string>(type: "TEXT", nullable: false),
                    Duracao = table.Column<int>(type: "INTEGER", nullable: false),
                    DataInicio = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DataTermino = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PercentualConcluido = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tarefas", x => x.Id);
                    table.CheckConstraint("CK_Tarefas_Duracao", "Duracao > 0");
                    table.CheckConstraint("CK_Tarefas_PercentualConcluido", "PercentualConcluido BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_Tarefas_Projetos_ProjetoId",
                        column: x => x.ProjetoId,
                        principalTable: "Projetos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Predecessoras",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Ordem = table.Column<int>(type: "INTEGER", nullable: false),
                    TarefaOrigemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TarefaDestinoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tipo = table.Column<int>(type: "INTEGER", nullable: false),
                    Lag = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Predecessoras", x => x.Id);
                    table.CheckConstraint("CK_Predecessoras_SemAutodependencia", "TarefaOrigemId <> TarefaDestinoId");
                    table.CheckConstraint("CK_Predecessoras_Tipo", "Tipo BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "FK_Predecessoras_Tarefas_TarefaDestinoId",
                        column: x => x.TarefaDestinoId,
                        principalTable: "Tarefas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Predecessoras_Tarefas_TarefaOrigemId",
                        column: x => x.TarefaOrigemId,
                        principalTable: "Tarefas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Predecessoras_TarefaDestinoId",
                table: "Predecessoras",
                column: "TarefaDestinoId");

            migrationBuilder.CreateIndex(
                name: "IX_Predecessoras_TarefaOrigemId_TarefaDestinoId",
                table: "Predecessoras",
                columns: new[] { "TarefaOrigemId", "TarefaDestinoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tarefas_ProjetoId_Ordem",
                table: "Tarefas",
                columns: new[] { "ProjetoId", "Ordem" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Predecessoras");

            migrationBuilder.DropTable(
                name: "Tarefas");

            migrationBuilder.DropTable(
                name: "Projetos");
        }
    }
}
