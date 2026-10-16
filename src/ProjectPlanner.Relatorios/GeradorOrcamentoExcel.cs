using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ProjectPlanner.Core.Orcamento;
using static ProjectPlanner.Relatorios.GeradorBoletimExcel;

namespace ProjectPlanner.Relatorios;

/// <summary>
/// Bloco 33: o orçamento em Excel (.xlsx), com números de verdade: a planilha "Sintética" (itens, quantidades, custos, preços com
/// BDI e totais) e a "Analítica" (os insumos de cada composição, com coeficiente, preço e parcial).
/// </summary>
public sealed class GeradorOrcamentoExcel
{
    private static readonly double[] Larguras = [8, 10, 10, 60, 7, 12, 13, 13, 16];
    private static readonly double[] LargurasDaAnalitica = [10, 60, 14, 7, 12, 13, 14];

    public void Gerar(PlanilhaOrcamentaria planilha, IReadOnlyDictionary<Guid, ComposicaoAberta> composicoes, string subtitulo, Stream destino)
    {
        ArgumentNullException.ThrowIfNull(planilha);
        ArgumentNullException.ThrowIfNull(composicoes);
        ArgumentNullException.ThrowIfNull(destino);
        using var documento = SpreadsheetDocument.Create(destino, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook);
        var livro = documento.AddWorkbookPart();
        livro.Workbook = new Workbook();
        livro.AddNewPart<WorkbookStylesPart>().Stylesheet = FolhaDeEstilos();
        var folhas = new Sheets();
        livro.Workbook.AppendChild(folhas);

        // ---- Sintética ----
        var s = new Folha();
        s.Linha(Texto("ORÇAMENTO · PLANILHA SINTÉTICA", Cinza));
        s.Linha(Texto(subtitulo, Titulo));
        s.Linha();
        var primeira = s.Numero;
        s.Linha([.. TextosDoOrcamento.Colunas.Select(c => Texto(c, CabecalhoDaTabela))]);
        foreach (var l in planilha.Linhas)
        {
            var (texto, dinheiro, quantidade) = l.EhGrupo ? (GrupoTexto, GrupoDinheiro, GrupoTexto) : (TextoComBorda, Dinheiro, Quantidade);
            s.Linha(Texto(l.Edt, texto), Texto(l.SemComposicao ? "" : l.Codigo, texto), Texto(l.SemComposicao ? "informado" : l.Fonte, texto),
                Texto(new string(' ', Math.Min(l.Nivel, 6) * 2) + l.Descricao, texto), Texto(l.Unidade, texto),
                l.Quantidade is { } q ? Numero(q, quantidade) : Texto("", texto), l.CustoUnitario is { } c ? Numero(c, dinheiro) : Texto("", texto),
                l.PrecoUnitario is { } p ? Numero(p, dinheiro) : Texto("", texto), Numero(l.Total, dinheiro));
        }
        s.Linha(Texto("", GrupoTexto), Texto("", GrupoTexto), Texto("", GrupoTexto), Texto("Total", GrupoTexto), Texto("", GrupoTexto), Texto("", GrupoTexto),
            Texto("", GrupoTexto), Texto("", GrupoTexto), Numero(planilha.Total, GrupoDinheiro));
        s.Linha();
        s.Linha(null, null, null, Texto("Custo direto (composições)"), null, null, null, null, Numero(planilha.CustoDireto, DinheiroSemBorda));
        s.Linha(null, null, null, Texto(TextosDoOrcamento.Bdi(planilha.Configuracao)), null, null, null, null, Numero(planilha.ValorDoBdi, DinheiroSemBorda));
        if (planilha.Informado > 0)
            s.Linha(null, null, null, Texto("Tarefas sem composição (valor informado)"), null, null, null, null, Numero(planilha.Informado, DinheiroSemBorda));
        s.Linha(null, null, null, Texto("Total do orçamento", Negrito), null, null, null, null, Numero(planilha.Total, DinheiroForte));
        s.Linha();
        s.Linha(Texto(TextosDoOrcamento.Rodape(planilha), Cinza));
        Adicionar(livro, folhas, s, "Sintética", Larguras, primeira);

        // ---- Analítica ----
        var a = new Folha();
        a.Linha(Texto("ORÇAMENTO · COMPOSIÇÕES (ANALÍTICA)", Cinza));
        a.Linha(Texto(subtitulo, Titulo));
        foreach (var (linha, composicao) in TextosDoOrcamento.Analitica(planilha, composicoes))
        {
            a.Linha();
            a.Linha(Texto(TextosDoOrcamento.TituloDaComposicao(linha, composicao), Negrito));
            a.Linha([.. TextosDoOrcamento.ColunasDaAnalitica.Select(c => Texto(c, CabecalhoDaTabela))]);
            foreach (var i in composicao.Insumos)
                a.Linha(Texto(i.Codigo, TextoComBorda), Texto(i.Descricao, TextoComBorda), Texto(TiposDeInsumo.Texto(i.Tipo), TextoComBorda),
                    Texto(i.Unidade, TextoComBorda), Numero(i.Coeficiente, Quantidade), Numero(i.Preco, Dinheiro),
                    Numero(Math.Round(i.Parcial, 2, MidpointRounding.AwayFromZero), Dinheiro));
            a.Linha(Texto("", GrupoTexto), Texto("Custo unitário", GrupoTexto), Texto("", GrupoTexto), Texto(composicao.Unidade, GrupoTexto), Texto("", GrupoTexto),
                Texto("", GrupoTexto), Numero(composicao.CustoUnitario, GrupoDinheiro));
        }
        Adicionar(livro, folhas, a, "Analítica", LargurasDaAnalitica, null);
        livro.Workbook.Save();
    }

    private sealed class Folha
    {
        public uint Numero { get; private set; } = 1;
        public List<Row> Linhas { get; } = [];

        // Uma célula em cada coluna desde a A (vazia onde não há nada), como no boletim.
        public void Linha(params Cell?[] celulas)
        {
            var linha = new Row { RowIndex = Numero };
            for (var i = 0; i < celulas.Length; i++)
            {
                var escrita = celulas[i] ?? Texto("");
                escrita.CellReference = $"{Coluna(i)}{Numero}";
                linha.Append(escrita);
            }
            Linhas.Add(linha);
            Numero++;
        }
    }

    private static void Adicionar(WorkbookPart livro, Sheets folhas, Folha folha, string nome, double[] larguras, uint? congelarAte)
    {
        var parte = livro.AddNewPart<WorksheetPart>();
        var planilha = new Worksheet();
        if (congelarAte is { } linha)
            planilha.Append(new SheetViews(new SheetView(new Pane
            {
                VerticalSplit = linha, TopLeftCell = $"A{linha + 1}", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen,
            }) { WorkbookViewId = 0 }));
        planilha.Append(new Columns(larguras.Select((largura, i) => new Column { Min = (uint)(i + 1), Max = (uint)(i + 1), Width = largura, CustomWidth = true })));
        planilha.Append(new SheetData(folha.Linhas));
        parte.Worksheet = planilha;
        folhas.Append(new Sheet { Id = livro.GetIdOfPart(parte), SheetId = (uint)folhas.Count() + 1, Name = nome });
    }
}
