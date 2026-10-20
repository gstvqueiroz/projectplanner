using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectPlanner.Servidor.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class AlertasDeCusto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AlertasDeCusto",
                columns: table => new
                {
                    Mes = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    EnviadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CustoEmMicrodolares = table.Column<long>(type: "bigint", nullable: false),
                    EnviadoPara = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertasDeCusto", x => x.Mes);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsosDeIa_Quando",
                table: "UsosDeIa",
                column: "Quando");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertasDeCusto");

            migrationBuilder.DropIndex(
                name: "IX_UsosDeIa_Quando",
                table: "UsosDeIa");
        }
    }
}
