using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectPlanner.Data.Migrations
{
    /// <inheritdoc />
    public partial class FotosReais : Migration
    {
        /// <inheritdoc />
        // Bloco 22: fotos de verdade. DataRegistro passa a DataFoto; CaminhoArquivo (só "exemplo://…") sai; entram os
        // metadados novos e as tabelas das imagens e miniaturas. Os registros antigos (de exemplo) ficam sem imagem e
        // são descartados ao abrir (somem no próximo salvamento).
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(name: "CK_FotosTarefa_CaminhoArquivo", table: "FotosTarefa");
            migrationBuilder.DropColumn(name: "CaminhoArquivo", table: "FotosTarefa");
            migrationBuilder.RenameColumn(name: "DataRegistro", table: "FotosTarefa", newName: "DataFoto");

            migrationBuilder.AddColumn<DateTime>(name: "DataCadastro", table: "FotosTarefa", type: "TEXT", nullable: false,
                defaultValue: new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
            migrationBuilder.AddColumn<string>(name: "Titulo", table: "FotosTarefa", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>(name: "Autor", table: "FotosTarefa", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>(name: "Local", table: "FotosTarefa", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<int>(name: "PercentualConcluidoNaData", table: "FotosTarefa", type: "INTEGER", nullable: true);
            migrationBuilder.AddColumn<string>(name: "NomeArquivoOriginal", table: "FotosTarefa", type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<int>(name: "Largura", table: "FotosTarefa", type: "INTEGER", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "Altura", table: "FotosTarefa", type: "INTEGER", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<long>(name: "TamanhoBytes", table: "FotosTarefa", type: "INTEGER", nullable: false, defaultValue: 0L);
            migrationBuilder.AddColumn<string>(name: "HashOriginal", table: "FotosTarefa", type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "Tags", table: "FotosTarefa", type: "TEXT", nullable: true);

            migrationBuilder.CreateTable(
                name: "FotosImagens",
                columns: table => new
                {
                    FotoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Dados = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FotosImagens", x => x.FotoId);
                    table.ForeignKey(
                        name: "FK_FotosImagens_FotosTarefa_FotoId",
                        column: x => x.FotoId,
                        principalTable: "FotosTarefa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FotosMiniaturas",
                columns: table => new
                {
                    FotoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Dados = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FotosMiniaturas", x => x.FotoId);
                    table.ForeignKey(
                        name: "FK_FotosMiniaturas_FotosTarefa_FotoId",
                        column: x => x.FotoId,
                        principalTable: "FotosTarefa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_FotosTarefa_Percentual",
                table: "FotosTarefa",
                sql: "PercentualConcluidoNaData IS NULL OR PercentualConcluidoNaData BETWEEN 0 AND 100");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FotosImagens");
            migrationBuilder.DropTable(name: "FotosMiniaturas");
            migrationBuilder.DropCheckConstraint(name: "CK_FotosTarefa_Percentual", table: "FotosTarefa");
            foreach (var coluna in new[] { "DataCadastro", "Titulo", "Autor", "Local", "PercentualConcluidoNaData",
                         "NomeArquivoOriginal", "Largura", "Altura", "TamanhoBytes", "HashOriginal", "Tags" })
                migrationBuilder.DropColumn(name: coluna, table: "FotosTarefa");
            migrationBuilder.RenameColumn(name: "DataFoto", table: "FotosTarefa", newName: "DataRegistro");
            migrationBuilder.AddColumn<string>(name: "CaminhoArquivo", table: "FotosTarefa", type: "TEXT", nullable: false,
                defaultValue: "exemplo://");
            migrationBuilder.AddCheckConstraint(name: "CK_FotosTarefa_CaminhoArquivo", table: "FotosTarefa",
                sql: "length(trim(CaminhoArquivo)) > 0");
        }
    }
}
