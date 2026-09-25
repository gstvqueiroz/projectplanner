using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectPlanner.Data.Migrations
{
    /// <inheritdoc />
    public partial class HierarquiaEMarcos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Tarefas_Duracao",
                table: "Tarefas");

            migrationBuilder.AddColumn<Guid>(
                name: "TarefaPaiId",
                table: "Tarefas",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Tipo",
                table: "Tarefas",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Tarefas_TarefaPaiId",
                table: "Tarefas",
                column: "TarefaPaiId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tarefas_Duracao",
                table: "Tarefas",
                sql: "(Tipo = 0 AND Duracao > 0) OR (Tipo = 1 AND Duracao = 0) OR (Tipo = 2 AND Duracao >= 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tarefas_PaiDiferenteDelaMesma",
                table: "Tarefas",
                sql: "TarefaPaiId IS NULL OR TarefaPaiId <> Id");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tarefas_Tipo",
                table: "Tarefas",
                sql: "Tipo BETWEEN 0 AND 2");

            migrationBuilder.AddForeignKey(
                name: "FK_Tarefas_Tarefas_TarefaPaiId",
                table: "Tarefas",
                column: "TarefaPaiId",
                principalTable: "Tarefas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tarefas_Tarefas_TarefaPaiId",
                table: "Tarefas");

            migrationBuilder.DropIndex(
                name: "IX_Tarefas_TarefaPaiId",
                table: "Tarefas");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tarefas_Duracao",
                table: "Tarefas");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tarefas_PaiDiferenteDelaMesma",
                table: "Tarefas");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tarefas_Tipo",
                table: "Tarefas");

            migrationBuilder.DropColumn(
                name: "TarefaPaiId",
                table: "Tarefas");

            migrationBuilder.DropColumn(
                name: "Tipo",
                table: "Tarefas");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tarefas_Duracao",
                table: "Tarefas",
                sql: "Duracao > 0");
        }
    }
}
