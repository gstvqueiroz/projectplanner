using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ProjectPlanner.Core.Medicoes;
using ProjectPlanner.Core.Relatorios;

namespace ProjectPlanner.Relatorios;

/// <summary>
/// Bloco 36: o boletim de medição em Excel (.xlsx): uma planilha "BM 01" com o cabeçalho, a tabela de itens com números de
/// verdade (quantidades, valores e o % como fração, formatados), o total e o resumo financeiro, para conferir e somar.
/// </summary>
public sealed class GeradorBoletimExcel
{
    // Os estilos (a ordem dos CellFormats da folha de estilos).
    internal const uint Normal = 0, Negrito = 1, Titulo = 2, CabecalhoDaTabela = 3, TextoComBorda = 4, Dinheiro = 5, Quantidade = 6,
        Percentual = 7, GrupoTexto = 8, GrupoDinheiro = 9, GrupoPercentual = 10, DinheiroForte = 11, DinheiroSemBorda = 12, Cinza = 13;

    private static readonly double[] Larguras = [8, 46, 7, 14, 14, 16, 13, 13, 13, 13, 16, 16, 16, 9];

    public void Gerar(BoletimDeMedicao boletim, CabecalhoDoRelatorio cabecalho, DateTime geradoEm, Stream destino)
    {
        ArgumentNullException.ThrowIfNull(boletim);
        ArgumentNullException.ThrowIfNull(cabecalho);
        ArgumentNullException.ThrowIfNull(destino);
        using var documento = SpreadsheetDocument.Create(destino, SpreadsheetDocumentType.Workbook);
        var livro = documento.AddWorkbookPart();
        livro.Workbook = new Workbook();
        var estilos = livro.AddNewPart<WorkbookStylesPart>();
        estilos.Stylesheet = FolhaDeEstilos();
        var parte = livro.AddNewPart<WorksheetPart>();
        var dados = new SheetData();
        var linhas = new List<Row>();
        uint numero = 1;

        void Linha(params Cell?[] celulas)
        {
            var linha = new Row { RowIndex = numero };
            var coluna = 0;
            // Uma célula em cada coluna, desde a A (vazia onde não há nada): leitores simples (como a pré-visualização do
            // macOS) param de ler na primeira linha que não começa na coluna A.
            foreach (var celula in celulas)
            {
                var escrita = celula ?? Texto("");
                escrita.CellReference = $"{Coluna(coluna)}{numero}";
                linha.Append(escrita);
                coluna++;
            }
            linhas.Add(linha);
            numero++;
        }

        Linha(Texto(TextosDoBoletim.Titulo(boletim.Medicao), Cinza));
        Linha(Texto(cabecalho.Obra, Titulo));
        foreach (var (rotulo, valor) in TextosDoBoletim.Dados(boletim, cabecalho, geradoEm))
            Linha(null, Texto(rotulo, Negrito), Texto(valor));
        Linha();

        var primeiraDaTabela = numero;
        Linha([.. TextosDoBoletim.Colunas.Select(c => Texto(c, CabecalhoDaTabela))]);
        foreach (var l in boletim.Linhas)
        {
            if (l.EhGrupo)
                Linha(Texto(l.Edt, GrupoTexto), Texto(l.Descricao, GrupoTexto), Texto("", GrupoTexto), Texto("", GrupoTexto), Texto("", GrupoTexto),
                    Numero(l.ValorContratado, GrupoDinheiro), Texto("", GrupoTexto), Texto("", GrupoTexto), Texto("", GrupoTexto), Texto("", GrupoTexto),
                    Numero(l.ValorNoPeriodo, GrupoDinheiro), Numero(l.ValorAcumulado, GrupoDinheiro), Numero(l.ValorSaldo, GrupoDinheiro),
                    Numero((decimal)l.PercentualAcumulado / 100, GrupoPercentual));
            else
                Linha(Texto(l.Edt, TextoComBorda), Texto(new string(' ', Math.Min(l.Nivel, 6) * 2) + l.Descricao, TextoComBorda), Texto(l.Unidade, TextoComBorda),
                    Numero(l.QuantidadeContratada, Quantidade), Numero(l.PrecoUnitario, Dinheiro), Numero(l.ValorContratado, Dinheiro),
                    Numero(l.QuantidadeAnterior, Quantidade), Numero(l.QuantidadeNoPeriodo, Quantidade), Numero(l.QuantidadeAcumulada, Quantidade),
                    Numero(l.QuantidadeSaldo, Quantidade), Numero(l.ValorNoPeriodo, Dinheiro), Numero(l.ValorAcumulado, Dinheiro), Numero(l.ValorSaldo, Dinheiro),
                    Numero((decimal)l.PercentualAcumulado / 100, Percentual));
        }
        var r = boletim.Resumo;
        Linha(Texto("", GrupoTexto), Texto("Total", GrupoTexto), Texto("", GrupoTexto), Texto("", GrupoTexto), Texto("", GrupoTexto),
            Numero(r.ValorContratado, GrupoDinheiro), Texto("", GrupoTexto), Texto("", GrupoTexto), Texto("", GrupoTexto), Texto("", GrupoTexto),
            Numero(r.MedidoNoPeriodo, GrupoDinheiro), Numero(r.MedidoAcumulado, GrupoDinheiro), Numero(r.Saldo, GrupoDinheiro),
            Numero((decimal)r.PercentualMedido / 100, GrupoPercentual));
        Linha();

        Linha(null, Texto("Resumo financeiro", Titulo));
        foreach (var (rotulo, valor, forte) in Resumo(r))
            Linha(null, Texto(rotulo, forte ? Negrito : Normal), null, null, null, valor is { } v ? Numero(v, forte ? DinheiroForte : DinheiroSemBorda) : null);
        if (boletim.Medicao.Glosas.Count > 0)
        {
            Linha();
            Linha(null, Texto("Glosas", Negrito));
            foreach (var glosa in boletim.Medicao.Glosas)
                Linha(null, Texto(glosa.Descricao), null, null, null, Numero(glosa.Valor, DinheiroSemBorda));
        }
        if (boletim.Medicao.Observacoes is { } observacoes)
        {
            Linha();
            Linha(null, Texto("Observações", Negrito));
            Linha(null, Texto(observacoes));
        }

        dados.Append(linhas);
        parte.Worksheet = new Worksheet(
            new SheetViews(new SheetView(new Pane
            {
                VerticalSplit = primeiraDaTabela, TopLeftCell = $"A{primeiraDaTabela + 1}", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen,
            }) { WorkbookViewId = 0 }),
            new Columns(Larguras.Select((largura, i) => new Column { Min = (uint)(i + 1), Max = (uint)(i + 1), Width = largura, CustomWidth = true })),
            dados);
        livro.Workbook.AppendChild(new Sheets(new Sheet { Id = livro.GetIdOfPart(parte), SheetId = 1, Name = $"BM {boletim.Medicao.Numero:00}" }));
        livro.Workbook.Save();
    }

    // O resumo com os números (os textos de TextosDoBoletim, mas o valor como número para a planilha somar).
    private static IEnumerable<(string Rotulo, decimal? Valor, bool Forte)> Resumo(ResumoDoBoletim r)
    {
        yield return ("Valor original do contrato", r.ValorOriginal, false);
        if (r.QuantidadeDeAditivos > 0)
        {
            yield return ($"Aditivos ({r.QuantidadeDeAditivos})", r.ValorDosAditivos, false);
            yield return ("Valor atual do contrato", r.ValorContratado, false);
        }
        yield return ("Medido nas medições anteriores", r.MedidoAnterior, false);
        yield return ("Medido nesta medição", r.MedidoNoPeriodo, true);
        yield return ("Medido acumulado", r.MedidoAcumulado, false);
        yield return ("Saldo do contrato", r.Saldo, false);
        if (r.Fisico is { } fisico)
            yield return ($"Avanço físico do cronograma: {TextosDoBoletim.Percentual(fisico)}", null, false);
        if (r.DescricaoDoReajuste is not null || r.Reajuste != 0)
            yield return ($"Reajuste{(r.DescricaoDoReajuste is { } d ? $" ({d})" : "")}", r.Reajuste, false);
        if (r.Glosas > 0)
            yield return ("Glosas", -r.Glosas, false);
        if (r.Reajuste != 0 || r.Glosas > 0)
            yield return ("Valor bruto", r.Bruto, false);
        if (r.PercentualDeRetencao > 0 || r.Retencao > 0)
            yield return ($"Retenção ({r.PercentualDeRetencao.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))}%)", -r.Retencao, false);
        if (r.Devolucao > 0)
            yield return ("Devolução da retenção", r.Devolucao, false);
        yield return ("Valor líquido a faturar", r.Liquido, true);
        if (r.RetidoAcumulado > 0)
            yield return ("Retido acumulado", r.RetidoAcumulado, false);
    }

    internal static Cell Texto(string texto, uint estilo = Normal) =>
        new() { DataType = CellValues.InlineString, StyleIndex = estilo, InlineString = new InlineString(new Text(texto) { Space = SpaceProcessingModeValues.Preserve }) };

    internal static Cell Numero(decimal valor, uint estilo) => new() { CellValue = new CellValue(valor), DataType = CellValues.Number, StyleIndex = estilo };

    internal static string Coluna(int indice) => indice < 26 ? ((char)('A' + indice)).ToString() : $"{(char)('A' + indice / 26 - 1)}{(char)('A' + indice % 26)}";

    internal static Stylesheet FolhaDeEstilos()
    {
        var borda = new Border(
            new LeftBorder(new Color { Rgb = "FFCFD8DC" }) { Style = BorderStyleValues.Thin },
            new RightBorder(new Color { Rgb = "FFCFD8DC" }) { Style = BorderStyleValues.Thin },
            new TopBorder(new Color { Rgb = "FFCFD8DC" }) { Style = BorderStyleValues.Thin },
            new BottomBorder(new Color { Rgb = "FFCFD8DC" }) { Style = BorderStyleValues.Thin },
            new DiagonalBorder());
        CellFormat Formato(uint fonte, uint preenchimento, uint borda, uint numero = 0, bool quebra = false, bool direita = false)
        {
            var formato = new CellFormat
            {
                FontId = fonte, FillId = preenchimento, BorderId = borda, NumberFormatId = numero,
                ApplyFont = true, ApplyFill = preenchimento > 0, ApplyBorder = borda > 0, ApplyNumberFormat = numero > 0,
            };
            if (quebra || direita)
            {
                formato.Append(new Alignment
                {
                    WrapText = quebra, Vertical = VerticalAlignmentValues.Center,
                    Horizontal = direita ? HorizontalAlignmentValues.Right : HorizontalAlignmentValues.General,
                });
                formato.ApplyAlignment = true;
            }
            return formato;
        }
        return new Stylesheet(
            new NumberingFormats(
                new NumberingFormat { NumberFormatId = 164, FormatCode = "#,##0.00" },
                new NumberingFormat { NumberFormatId = 165, FormatCode = "#,##0.00##" },
                new NumberingFormat { NumberFormatId = 166, FormatCode = "0.0%" }) { Count = 3 },
            new Fonts(
                new Font(new FontSize { Val = 10 }, new FontName { Val = "Calibri" }),
                new Font(new Bold(), new FontSize { Val = 10 }, new FontName { Val = "Calibri" }),
                new Font(new Bold(), new FontSize { Val = 14 }, new FontName { Val = "Calibri" }),
                new Font(new FontSize { Val = 9 }, new Color { Rgb = "FF6B6B6B" }, new FontName { Val = "Calibri" })) { Count = 4 },
            new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
                new Fill(new PatternFill(new ForegroundColor { Rgb = "FFF4F6F8" }) { PatternType = PatternValues.Solid })) { Count = 3 },
            new Borders(new Border(new LeftBorder(), new RightBorder(), new TopBorder(), new BottomBorder(), new DiagonalBorder()), borda) { Count = 2 },
            new CellFormats(
                Formato(0, 0, 0),              // Normal
                Formato(1, 0, 0),              // Negrito
                Formato(2, 0, 0),              // Titulo
                Formato(1, 2, 1, quebra: true), // CabecalhoDaTabela
                Formato(0, 0, 1),              // TextoComBorda
                Formato(0, 0, 1, 164),         // Dinheiro
                Formato(0, 0, 1, 165),         // Quantidade
                Formato(0, 0, 1, 166),         // Percentual
                Formato(1, 2, 1),              // GrupoTexto
                Formato(1, 2, 1, 164),         // GrupoDinheiro
                Formato(1, 2, 1, 166),         // GrupoPercentual
                Formato(1, 0, 0, 164),         // DinheiroForte
                Formato(0, 0, 0, 164),         // DinheiroSemBorda
                Formato(3, 0, 0)) { Count = 14 }); // Cinza
    }
}
