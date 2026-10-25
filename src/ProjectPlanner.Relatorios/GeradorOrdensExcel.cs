using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ProjectPlanner.Core.OrdensDeServico;
using ProjectPlanner.Core.Relatorios;
using static ProjectPlanner.Relatorios.GeradorBoletimExcel;

namespace ProjectPlanner.Relatorios;

/// <summary>
/// Bloco 65: as ordens de serviço em Excel (.xlsx), com números de verdade (para somar e filtrar): o boletim de medição de uma OS
/// e o relatório das OS em abas (Resumo, OS, Itens, Medições e Por mês), com os mesmos estilos do boletim do contrato.
/// </summary>
public sealed class GeradorOrdensExcel
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Uma aba sendo escrita: as linhas, a partir da coluna A.</summary>
    private sealed class Aba(string nome, double[] larguras, uint? congelarAte = null)
    {
        private readonly List<Row> _linhas = [];
        private uint _numero = 1;

        public string Nome { get; } = nome;

        public uint Proxima => _numero;

        public uint? CongelarAte { get; set; } = congelarAte;

        public void Linha(params Cell?[] celulas)
        {
            var linha = new Row { RowIndex = _numero };
            var coluna = 0;
            foreach (var celula in celulas)
            {
                var escrita = celula ?? Texto("");
                escrita.CellReference = $"{Coluna(coluna)}{_numero}";
                linha.Append(escrita);
                coluna++;
            }
            _linhas.Add(linha);
            _numero++;
        }

        public Worksheet Folha()
        {
            var folha = new Worksheet();
            if (CongelarAte is { } ate)
                folha.Append(new SheetViews(new SheetView(new Pane
                {
                    VerticalSplit = ate, TopLeftCell = $"A{ate + 1}", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen,
                }) { WorkbookViewId = 0 }));
            folha.Append(new Columns(larguras.Select((largura, i) => new Column { Min = (uint)(i + 1), Max = (uint)(i + 1), Width = largura, CustomWidth = true })));
            var dados = new SheetData();
            dados.Append(_linhas);
            folha.Append(dados);
            return folha;
        }
    }

    private static void Salvar(Stream destino, IEnumerable<Aba> abas)
    {
        using var documento = SpreadsheetDocument.Create(destino, SpreadsheetDocumentType.Workbook);
        var livro = documento.AddWorkbookPart();
        livro.Workbook = new Workbook();
        livro.AddNewPart<WorkbookStylesPart>().Stylesheet = FolhaDeEstilos();
        var planilhas = new Sheets();
        uint id = 1;
        foreach (var aba in abas)
        {
            var parte = livro.AddNewPart<WorksheetPart>();
            parte.Worksheet = aba.Folha();
            planilhas.Append(new Sheet { Id = livro.GetIdOfPart(parte), SheetId = id++, Name = aba.Nome });
        }
        livro.Workbook.AppendChild(planilhas);
        livro.Workbook.Save();
    }

    private static string Edt(Guid? tarefaId, IReadOnlyDictionary<Guid, string> tarefas) =>
        tarefaId is { } id && tarefas.TryGetValue(id, out var t) ? t.Split(' ')[0] : "";

    private static Cell Data(DateOnly? data) => Texto(data?.ToString("dd/MM/yyyy", Brasil) ?? "", TextoComBorda);

    // ---- O boletim de uma medição ----

    public void Boletim(BoletimDaOrdem boletim, CabecalhoDoRelatorio cabecalho, IReadOnlyDictionary<Guid, string> tarefas, DateTime geradoEm, Stream destino)
    {
        ArgumentNullException.ThrowIfNull(boletim);
        ArgumentNullException.ThrowIfNull(cabecalho);
        ArgumentNullException.ThrowIfNull(destino);
        var o = boletim.Ordem;
        var aba = new Aba($"{o.Codigo} {boletim.Medicao.Codigo}", [6, 8, 44, 7, 13, 13, 13, 13, 13, 13, 9, 15, 16]);
        aba.Linha(Texto("BOLETIM DE MEDIÇÃO DA ORDEM DE SERVIÇO", Cinza));
        aba.Linha(Texto(cabecalho.Obra, Titulo));
        foreach (var (rotulo, valor) in new[]
                 {
                     ("OS", $"{o.Codigo} · {o.Dados.Titulo}"),
                     ("Executante", $"{o.Dados.Executante ?? "—"} ({TextosDaOrdem.TipoCurto(o.Dados.Tipo)})"),
                     ("Medição", $"{boletim.Medicao.Codigo} de {boletim.Medicao.Data.ToString("dd/MM/yyyy", Brasil)}"),
                     ("Pagamento", TextosDaOrdem.Texto(o.Dados.FormaDePagamento) + (o.Dados.Retencao > 0 ? $" · retenção {o.Dados.Retencao.ToString("0.##", Brasil)}%" : "")),
                     ("Gerado em", geradoEm.ToString("dd/MM/yyyy HH:mm", Brasil)),
                 })
            aba.Linha(null, Texto(rotulo, Negrito), Texto(valor));
        aba.Linha();
        aba.CongelarAte = aba.Proxima;
        aba.Linha([.. new[] { "Item", "Tarefa", "Descrição", "Un", "Contratado", "Preço unitário", "Anterior", "Nesta medição", "Acumulado", "Saldo", "%",
            "Valor nesta medição", "Valor acumulado" }.Select(t => Texto(t, CabecalhoDaTabela))]);
        foreach (var l in boletim.Linhas)
            aba.Linha(Numero(l.Numero, TextoComBorda), Texto(Edt(l.Item.TarefaId, tarefas), TextoComBorda), Texto(l.Item.Descricao, TextoComBorda),
                Texto(l.Item.Unidade, TextoComBorda), Numero(l.Item.Quantidade, Quantidade), Numero(l.Item.PrecoUnitario, Dinheiro),
                Numero(l.Anterior, Quantidade), Numero(l.NestaMedicao, Quantidade), Numero(l.Acumulado, Quantidade), Numero(l.Saldo, Quantidade),
                Numero(l.Percentual / 100, Percentual), Numero(l.ValorNestaMedicao, Dinheiro), Numero(l.ValorAcumulado, Dinheiro));
        aba.Linha(Texto("", GrupoTexto), Texto("", GrupoTexto), Texto("Total", GrupoTexto), Texto("", GrupoTexto), Texto("", GrupoTexto), Texto("", GrupoTexto),
            Texto("", GrupoTexto), Texto("", GrupoTexto), Texto("", GrupoTexto), Texto("", GrupoTexto), Numero(boletim.PercentualAcumulado / 100, GrupoPercentual),
            Numero(boletim.ValorNestaMedicao, GrupoDinheiro), Numero(boletim.MedidoAcumulado, GrupoDinheiro));
        aba.Linha();
        aba.Linha(null, null, Texto("Resumo", Titulo));
        var resumo = new List<(string, decimal, bool)>
        {
            ("Valor da OS", o.Total, false), ("Medido nas medições anteriores", boletim.MedidoAnterior, false),
            ($"Medido nesta medição ({boletim.Medicao.Codigo})", boletim.ValorNestaMedicao, true), ("Medido acumulado", boletim.MedidoAcumulado, false),
            ("Saldo da OS", boletim.Saldo, false),
        };
        if (boletim.Retencao > 0)
            resumo.Add(($"Retenção de garantia ({o.Dados.Retencao.ToString("0.##", Brasil)}%)", -boletim.Retencao, false));
        resumo.Add(("Líquido a pagar", boletim.Liquido, true));
        foreach (var (rotulo, valor, forte) in resumo)
            aba.Linha(null, null, Texto(rotulo, forte ? Negrito : Normal), null, null, Numero(valor, forte ? DinheiroForte : DinheiroSemBorda));
        Salvar(destino, [aba]);
    }

    // ---- O relatório das OS ----

    public void Relatorio(RelatorioDasOrdens r, CabecalhoDoRelatorio cabecalho, IReadOnlyDictionary<Guid, string> tarefas, DateTime geradoEm, Stream destino)
    {
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(cabecalho);
        ArgumentNullException.ThrowIfNull(destino);

        var resumo = new Aba("Resumo", [34, 18, 16, 16, 16, 16, 40]);
        resumo.Linha(Texto("RELATÓRIO DAS ORDENS DE SERVIÇO", Cinza));
        resumo.Linha(Texto(cabecalho.Obra, Titulo));
        resumo.Linha(Texto($"{r.Filtro.Texto} · gerado em {geradoEm.ToString("dd/MM/yyyy HH:mm", Brasil)}", Cinza));
        resumo.Linha();
        foreach (var (rotulo, valor) in new (string, decimal)[]
                 {
                     ("Contratado", r.Valor), ("Medido", r.Medido), ("Pago", r.Pago), ("A pagar", r.APagar), ("Retido", r.Retido), ("Por medir", r.PorMedir),
                 })
            resumo.Linha(Texto(rotulo, Negrito), Numero(valor, DinheiroSemBorda));
        resumo.Linha(Texto("Ordens de serviço", Negrito), Numero(r.Ordens.Count, Normal));
        foreach (var (situacao, quantidade) in r.PorSituacao)
            resumo.Linha(Texto("  " + TextosDaOrdem.Texto(situacao)), Numero(quantidade, Normal));
        if (r.Prazos is { } prazos)
            resumo.Linha(Texto(prazos));
        resumo.Linha();
        resumo.Linha(Texto("Por executante", Titulo));
        resumo.Linha([.. new[] { "Executante", "OS", "Contratado", "Medido", "Pago", "A pagar", "Prazos" }.Select(t => Texto(t, CabecalhoDaTabela))]);
        foreach (var e in r.PorExecutante)
            resumo.Linha(Texto(e.Nome, TextoComBorda), Numero(e.Ordens, TextoComBorda), Numero(e.Valor, Dinheiro), Numero(e.Medido, Dinheiro), Numero(e.Pago, Dinheiro),
                Numero(e.APagar, Dinheiro), Texto(e.Prazos is { } p ? p["Prazos: ".Length..].TrimEnd('.') : "", TextoComBorda));

        var ordens = new Aba("OS", [9, 40, 26, 18, 14, 12, 12, 12, 9, 14, 14, 14, 14, 12], 1);
        ordens.Linha([.. new[] { "OS", "Serviço", "Executante", "Tipo", "Situação", "Emissão", "Prazo", "Conclusão", "Atraso (dias)", "Valor", "Medido", "Pago",
            "A pagar", "Retenção (%)" }.Select(t => Texto(t, CabecalhoDaTabela))]);
        foreach (var o in r.Ordens)
            ordens.Linha(Texto(o.Codigo, TextoComBorda), Texto(o.Dados.Titulo, TextoComBorda), Texto(o.Dados.Executante ?? "", TextoComBorda),
                Texto(TextosDaOrdem.Texto(o.Dados.Tipo), TextoComBorda), Texto(TextosDaOrdem.Texto(o.Situacao), TextoComBorda), Data(o.Emissao), Data(o.Dados.Prazo),
                Data(o.Conclusao), r.DiasDeAtraso(o) is { } d ? Numero(d, TextoComBorda) : Texto("", TextoComBorda), Numero(o.Total, Dinheiro),
                Numero(o.TotalMedido, Dinheiro), Numero(o.TotalPago, Dinheiro), Numero(o.APagar, Dinheiro), Numero(o.Dados.Retencao, TextoComBorda));

        var itens = new Aba("Itens", [9, 8, 44, 7, 13, 13, 15, 13, 9], 1);
        itens.Linha([.. new[] { "OS", "Tarefa", "Descrição", "Un", "Quantidade", "Preço unitário", "Total", "Medido", "%" }.Select(t => Texto(t, CabecalhoDaTabela))]);
        foreach (var o in r.Ordens)
            for (var i = 0; i < o.Dados.Itens.Count; i++)
            {
                var item = o.Dados.Itens[i];
                var medido = o.MedidoDoItem(i);
                itens.Linha(Texto(o.Codigo, TextoComBorda), Texto(Edt(item.TarefaId, tarefas), TextoComBorda), Texto(item.Descricao, TextoComBorda),
                    Texto(item.Unidade, TextoComBorda), Numero(item.Quantidade, Quantidade), Numero(item.PrecoUnitario, Dinheiro), Numero(item.Total, Dinheiro),
                    Numero(medido, Quantidade), Numero(item.Quantidade == 0 ? 0 : medido / item.Quantidade, Percentual));
            }

        var medicoes = new Aba("Medições", [9, 26, 9, 12, 14, 14, 14, 12, 20], 1);
        medicoes.Linha([.. new[] { "OS", "Executante", "Medição", "Data", "Valor", "Retenção", "Líquido", "Paga em", "Documento" }.Select(t => Texto(t, CabecalhoDaTabela))]);
        foreach (var o in r.Ordens)
            foreach (var m in o.Medicoes)
                medicoes.Linha(Texto(o.Codigo, TextoComBorda), Texto(o.Dados.Executante ?? "", TextoComBorda), Texto(m.Codigo, TextoComBorda), Data(m.Data),
                    Numero(m.Valor, Dinheiro), Numero(o.RetencaoDe(m), Dinheiro), Numero(m.Valor - o.RetencaoDe(m), Dinheiro), Data(m.Pagamento?.Data),
                    Texto(m.Pagamento?.Documento ?? "", TextoComBorda));

        var porMes = new Aba("Por mês", [12, 16, 16, 16, 16], 1);
        porMes.Linha([.. new[] { "Mês", "Medido", "Pago", "Medido acumulado", "Pago acumulado" }.Select(t => Texto(t, CabecalhoDaTabela))]);
        var (medidoAcumulado, pagoAcumulado) = (0m, 0m);
        foreach (var m in r.PorMes)
        {
            (medidoAcumulado, pagoAcumulado) = (medidoAcumulado + m.Medido, pagoAcumulado + m.Pago);
            porMes.Linha(Texto(m.Mes.ToString("MMM/yyyy", Brasil).Replace(".", ""), TextoComBorda), Numero(m.Medido, Dinheiro), Numero(m.Pago, Dinheiro),
                Numero(medidoAcumulado, Dinheiro), Numero(pagoAcumulado, Dinheiro));
        }

        Salvar(destino, [resumo, ordens, itens, medicoes, porMes]);
    }
}
