using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectPlanner.Servidor.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class CaixaComOrigem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ChatId",
                table: "CaixaDeEntrada",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MensagemId",
                table: "CaixaDeEntrada",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PessoaId",
                table: "CaixaDeEntrada",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChatId",
                table: "CaixaDeEntrada");

            migrationBuilder.DropColumn(
                name: "MensagemId",
                table: "CaixaDeEntrada");

            migrationBuilder.DropColumn(
                name: "PessoaId",
                table: "CaixaDeEntrada");
        }
    }
}
