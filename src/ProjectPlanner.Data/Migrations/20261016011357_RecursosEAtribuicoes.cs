using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectPlanner.Data.Migrations
{
    /// <inheritdoc />
    public partial class RecursosEAtribuicoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Recursos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjetoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordem = table.Column<int>(type: "INTEGER", nullable: false),
                    Nome = table.Column<string>(type: "TEXT", nullable: false),
                    Tipo = table.Column<int>(type: "INTEGER", nullable: false),
                    Unidade = table.Column<string>(type: "TEXT", nullable: false),
                    CustoUnitario = table.Column<decimal>(type: "TEXT", nullable: false),
                    Disponivel = table.Column<decimal>(type: "TEXT", nullable: true),
                    Fornecedor = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recursos", x => x.Id);
                    table.CheckConstraint("CK_Recursos_Custo", "CustoUnitario >= 0");
                    table.CheckConstraint("CK_Recursos_Tipo", "Tipo BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "FK_Recursos_Projetos_ProjetoId",
                        column: x => x.ProjetoId,
                        principalTable: "Projetos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AtribuicoesDeRecurso",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TarefaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecursoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordem = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantidade = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AtribuicoesDeRecurso", x => x.Id);
                    table.CheckConstraint("CK_Atribuicoes_Quantidade", "Quantidade > 0");
                    table.ForeignKey(
                        name: "FK_AtribuicoesDeRecurso_Recursos_RecursoId",
                        column: x => x.RecursoId,
                        principalTable: "Recursos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AtribuicoesDeRecurso_Tarefas_TarefaId",
                        column: x => x.TarefaId,
                        principalTable: "Tarefas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AtribuicoesDeRecurso_RecursoId",
                table: "AtribuicoesDeRecurso",
                column: "RecursoId");

            migrationBuilder.CreateIndex(
                name: "IX_AtribuicoesDeRecurso_TarefaId_RecursoId",
                table: "AtribuicoesDeRecurso",
                columns: new[] { "TarefaId", "RecursoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Recursos_ProjetoId_Ordem",
                table: "Recursos",
                columns: new[] { "ProjetoId", "Ordem" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AtribuicoesDeRecurso");

            migrationBuilder.DropTable(
                name: "Recursos");
        }
    }
}
