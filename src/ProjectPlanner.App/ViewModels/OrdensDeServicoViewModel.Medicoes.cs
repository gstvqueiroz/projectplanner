using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Indicadores;
using ProjectPlanner.Core.OrdensDeServico;
using ProjectPlanner.Core.Planejamento;
using ProjectPlanner.Relatorios;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Bloco 60: um item na medição nova: o contratado, o já medido e o que entra nesta medição.</summary>
public sealed partial class ItemParaMedir(int indice, ItemDaOrdem item, decimal medido) : ObservableObject
{
    public int Indice { get; } = indice;

    public string Descricao { get; } = item.Descricao;

    public string Contratado { get; } = $"{Moeda.FormatarQuantidade(item.Quantidade)} {item.Unidade}";

    public string JaMedido { get; } = $"{Moeda.FormatarQuantidade(medido)} {item.Unidade}";

    /// <summary>O que falta medir.</summary>
    public decimal Falta { get; } = Math.Max(0, item.Quantidade - medido);

    [ObservableProperty] public partial string NestaMedicao { get; set; } = "";
}

/// <summary>Bloco 60: uma medição na ficha: "M1 · 01/10/2026 · R$ 1.400,00" e se foi paga.</summary>
/// <param name="Retencao">Bloco 62: a retenção de garantia desta medição (zero sem retenção).</param>
public sealed record MedicaoNaTela(MedicaoDaOrdem Medicao, decimal Retencao = 0)
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    public string Texto => $"{Medicao.Codigo} · {Medicao.Data.ToString("dd/MM/yyyy", Brasil)} · {Moeda.ComSimbolo(Medicao.Valor)}";

    public string Situacao => (Medicao.Pagamento is { } p
        ? $"paga em {p.Data.ToString("dd/MM/yyyy", Brasil)}" + (p.Documento is { } doc ? $" ({doc})" : "")
        : "a pagar") + (Retencao > 0 ? $" · retenção {Moeda.ComSimbolo(Retencao)}" : "");

    public bool Paga => Medicao.Paga;
}

// Bloco 60: as medições e os pagamentos da OS. A medição pode ser parcial (por item, durante a execução) ou final (o que
// falta, na conferência); o pagamento lança os custos realizados (um por tarefa, na categoria do executante) e o medido leva o
// % das tarefas. Tudo no Desfazer.
public sealed partial class OrdensDeServicoViewModel
{
    public ObservableCollection<MedicaoNaTela> MedicoesNaTela { get; } = [];

    public ObservableCollection<ItemParaMedir> ItensParaMedir { get; } = [];

    /// <summary>"Por executante": o total, o medido e o pago de cada um.</summary>
    public IReadOnlyList<LinhaDoResumoNaTela> PorExecutante { get; private set; } = [];

    public bool PodeMedir => Ordem?.PodeMedir == true && Ordem.PorMedir > 0;

    public bool MostraMedicoes => Ordem is { } o && (o.PodeMedir || o.Medicoes.Count > 0);

    /// <summary>"Medido R$ 1.400,00 de R$ 3.800,00 (36%) · pago R$ 0,00 · a pagar R$ 1.400,00".</summary>
    public string ResumoDasMedicoes => Ordem is { Total: > 0 } o
        ? $"Medido {Moeda.ComSimbolo(o.TotalMedido)} de {Moeda.ComSimbolo(o.Total)} ({Math.Floor(o.TotalMedido / o.Total * 100):0}%) · " +
          $"pago {Moeda.ComSimbolo(o.TotalPago)} · a pagar {Moeda.ComSimbolo(o.APagar)}" +
          (o.Retido > 0 ? $" (retido {Moeda.ComSimbolo(o.Retido)})" : "")
        : "";

    // ---- Bloco 62: retenção de garantia ----

    public bool PodeLiberarRetencao => Ordem?.PodeLiberarRetencao == true;

    public bool RetencaoLiberada => Ordem?.Liberacao is not null;

    /// <summary>"Liberar a retenção (R$ 157,67)…" ou "Retenção liberada em 01/12/2026 (RPA 9)".</summary>
    public string TextoDaLiberacao => Ordem switch
    {
        { Liberacao: { } l } => $"Retenção de {Moeda.ComSimbolo(l.Valor)} liberada em {l.Data.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR"))}" +
                                (l.Documento is { } doc ? $" ({doc})" : ""),
        { PodeLiberarRetencao: true } o => $"Liberar a retenção ({Moeda.ComSimbolo(o.Retido)})…",
        _ => "",
    };

    /// <summary>Paga a retenção ao executante (lança nos custos realizados, por tarefa) e a OS fica paga.</summary>
    [RelayCommand(CanExecute = nameof(PodeLiberarRetencao))]
    private async Task LiberarRetencao()
    {
        var o = Ordem!;
        if (await _dialogos.PedirTextoAsync("Liberar a retenção",
                $"Documento do pagamento da retenção da {o.Codigo} ({Moeda.ComSimbolo(o.Retido)}): NF, recibo, PIX… Em branco, fica \"{o.Codigo}/retenção\".") is not { } documento)
            return;
        var lancamentos = PagamentoDasOrdens.LancamentosDaRetencao(o, Hoje, documento);
        var nova = o.LiberarRetencao(Hoje, documento, [.. lancamentos.Select(l => l.Id)], Agora);
        foreach (var l in lancamentos)
            _cronograma.Custos.Adicionar(l);
        _cronograma.OrdensDeServico.Substituir(nova);
        _mudou($"Liberar a retenção da {o.Codigo}", false);
        Atualizar();
        Mensagem = $"Retenção de {Moeda.ComSimbolo(nova.Liberacao!.Valor)} liberada nos custos realizados. A {o.Codigo} está paga.";
        if (nova.NoTelegram && _publicar is not null)
            _ = AtualizarNoTelegramAsync(nova);
    }

    [RelayCommand(CanExecute = nameof(RetencaoLiberada))]
    private void DesfazerLiberacao()
    {
        var o = Ordem!;
        var liberacao = o.Liberacao!;
        var nova = o.DesfazerLiberacao(Agora);
        foreach (var id in liberacao.Lancamentos.Where(id => _cronograma.Custos.Lancamentos.Any(l => l.Id == id)))
            _cronograma.Custos.Remover(id);
        _cronograma.OrdensDeServico.Substituir(nova);
        _mudou($"Desfazer a liberação da retenção da {o.Codigo}", false);
        Atualizar();
        Mensagem = "Liberação da retenção desfeita; os custos dela saíram dos custos realizados.";
    }

    [ObservableProperty] public partial DateTime? DataDaMedicao { get; set; }

    /// <summary>O medido leva o % concluído das tarefas da OS (só aumenta).</summary>
    [ObservableProperty] public partial bool AtualizarTarefas { get; set; } = true;

    private void CarregarMedicoes(OrdemDeServico? o)
    {
        MedicoesNaTela.Clear();
        ItensParaMedir.Clear();
        foreach (var m in o?.Medicoes ?? [])
            MedicoesNaTela.Add(new MedicaoNaTela(m, o!.RetencaoDe(m)));
        if (o is not null && o.PodeMedir)
            for (var i = 0; i < o.Dados.Itens.Count; i++)
                ItensParaMedir.Add(new ItemParaMedir(i, o.Dados.Itens[i], o.MedidoDoItem(i)));
        DataDaMedicao = Agora.Date;
        foreach (var nome in new[] { nameof(PodeMedir), nameof(MostraMedicoes), nameof(ResumoDasMedicoes), nameof(PodeLiberarRetencao),
                     nameof(TextoDaLiberacao), nameof(RetencaoLiberada) })
            OnPropertyChanged(nome);
    }

    /// <summary>Põe em cada item o que falta medir.</summary>
    [RelayCommand(CanExecute = nameof(PodeMedir))]
    private void MedirTudoOQueFalta()
    {
        foreach (var item in ItensParaMedir)
            item.NestaMedicao = item.Falta == 0 ? "" : Moeda.FormatarQuantidade(item.Falta);
    }

    [RelayCommand(CanExecute = nameof(PodeMedir))]
    private void RegistrarMedicao()
    {
        var o = Ordem!;
        List<decimal> quantidades;
        try
        {
            quantidades = [.. ItensParaMedir.Select(i => Moeda.LerNumero(i.NestaMedicao) ?? 0)];
        }
        catch (FormatException ex)
        {
            Mensagem = ex.Message;
            return;
        }
        var data = DateOnly.FromDateTime(DataDaMedicao ?? Agora);
        Executar(() => o.Medir(Guid.NewGuid(), data, quantidades, Agora), $"Medição na {o.Codigo}",
            $"Medição registrada na {o.Codigo}. Para pagar, clique em Pagar… na medição.", atualizarTarefas: true);
    }

    [RelayCommand]
    private async Task PagarMedicao(MedicaoNaTela? linha)
    {
        if (Ordem is not { } o || linha is null || linha.Paga)
            return;
        var m = linha.Medicao;
        if (await _dialogos.PedirTextoAsync($"Pagamento da {m.Codigo}",
                $"Documento do pagamento da {o.Codigo} {m.Codigo} ({Moeda.ComSimbolo(m.Valor)}): NF, recibo, PIX… Em branco, fica \"{o.Codigo}/{m.Codigo}\".") is not { } documento)
            return;
        var lancamentos = PagamentoDasOrdens.Lancamentos(o, m, Hoje, documento);
        OrdemDeServico nova;
        try
        {
            nova = o.RegistrarPagamento(m.Id, new PagamentoDaMedicao(Hoje, string.IsNullOrWhiteSpace(documento) ? null : documento.Trim(),
                [.. lancamentos.Select(l => l.Id)]), Agora);
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return;
        }
        foreach (var l in lancamentos)
            _cronograma.Custos.Adicionar(l);
        _cronograma.OrdensDeServico.Substituir(nova);
        _mudou($"Pagamento da {o.Codigo} {m.Codigo}", false);
        Atualizar();
        var categoria = Categorias.Texto(o.Dados.Tipo == TipoDeExecutante.EquipePropria ? CategoriaDeCusto.MaoDeObra : CategoriaDeCusto.Terceiros);
        var retido = o.RetencaoDe(m);
        Mensagem = $"Pagamento da {m.Codigo} registrado: {Moeda.ComSimbolo(m.Valor - retido)} nos custos realizados ({categoria.ToLowerInvariant()})." +
                   (retido > 0 ? $" {Moeda.ComSimbolo(retido)} ficam retidos até a liberação." : "") +
                   (nova.Situacao == SituacaoDaOrdem.Paga ? $" A {o.Codigo} está paga." : "");
        if (nova.NoTelegram && _publicar is not null)
            _ = AtualizarNoTelegramAsync(nova);
    }

    [RelayCommand]
    private async Task DesfazerPagamento(MedicaoNaTela? linha)
    {
        if (Ordem is not { } o || linha?.Medicao.Pagamento is not { } pagamento)
            return;
        var m = linha.Medicao;
        if (!await _dialogos.ConfirmarAsync("Desfazer pagamento",
                $"Desfazer o pagamento da {o.Codigo} {m.Codigo} ({Moeda.ComSimbolo(m.Valor)})? Os custos que ele lançou saem dos custos realizados.", "Desfazer pagamento"))
            return;
        var nova = o.DesfazerPagamento(m.Id, Agora);
        foreach (var id in pagamento.Lancamentos.Where(id => _cronograma.Custos.Lancamentos.Any(l => l.Id == id)))
            _cronograma.Custos.Remover(id);
        _cronograma.OrdensDeServico.Substituir(nova);
        _mudou($"Desfazer o pagamento da {o.Codigo} {m.Codigo}", false);
        Atualizar();
        Mensagem = $"Pagamento da {m.Codigo} desfeito; os custos dele saíram dos custos realizados.";
        if (nova.NoTelegram && _publicar is not null)
            _ = AtualizarNoTelegramAsync(nova);
    }

    // ---- Bloco 65: o boletim de medição da OS ----

    [RelayCommand]
    private Task BoletimPdf(MedicaoNaTela? linha) => ExportarBoletim(linha, "pdf", "PDF",
        (b, cabecalho, destino) => new GeradorBoletimDaOrdemPdf().Gerar(b, cabecalho, _nomes, Agora.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("pt-BR")), destino));

    [RelayCommand]
    private Task BoletimExcel(MedicaoNaTela? linha) => ExportarBoletim(linha, "xlsx", "Excel",
        (b, cabecalho, destino) => new GeradorOrdensExcel().Boletim(b, cabecalho, _nomes, Agora, destino));

    private async Task ExportarBoletim(MedicaoNaTela? linha, string extensao, string formato,
        Action<BoletimDaOrdem, Core.Relatorios.CabecalhoDoRelatorio, Stream> gerar)
    {
        if (Ordem is not { } o || linha is null)
            return;
        var boletim = BoletimDaOrdem.Montar(o, linha.Medicao.Id);
        var nome = string.Concat($"{o.Codigo} {linha.Medicao.Codigo} - Boletim de medição".Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (await _dialogos.SalvarComoAsync($"Boletim de medição da {o.Codigo} {linha.Medicao.Codigo} em {formato}", nome, extensao) is not { } caminho)
            return;
        var cabecalho = _cabecalho();
        var temporario = caminho + ".tmp";
        try
        {
            await Task.Run(() =>
            {
                using (var destino = File.Create(temporario))
                    gerar(boletim, cabecalho, destino);
                File.Move(temporario, caminho, overwrite: true);
            });
            Mensagem = $"Salvo em {Path.GetFileName(caminho)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporario); } catch (Exception) { /* nada a fazer */ }
            Mensagem = $"Não foi possível salvar: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RemoverMedicao(MedicaoNaTela? linha)
    {
        if (Ordem is not { } o || linha is null)
            return;
        Executar(() => o.RemoverMedicao(linha.Medicao.Id, Agora), $"Tirar a {linha.Medicao.Codigo} da {o.Codigo}", $"{linha.Medicao.Codigo} retirada da {o.Codigo}.");
    }

    // O medido leva o % das tarefas da OS (só para cima; um resumo ou uma tarefa que saiu fica de fora).
    private bool AtualizarTarefasCom(OrdemDeServico ordem)
    {
        var mudou = false;
        foreach (var (id, percentual) in PagamentoDasOrdens.PercentualDasTarefas(ordem))
            if (_cronograma.Tarefas.FirstOrDefault(t => t.Id == id) is { EhResumo: false } tarefa && percentual > tarefa.PercentualConcluido)
            {
                tarefa.AtualizarPercentualConcluido(percentual);
                mudou = true;
            }
        return mudou;
    }

    // ---- Relatório ----

    /// <summary>A lista das OS em CSV (para o Excel): número, serviço, executante, situação, datas e os valores.</summary>
    [RelayCommand]
    private async Task ExportarCsv()
    {
        if (await _dialogos.SalvarComoAsync("Exportar as ordens de serviço em CSV", $"Ordens de serviço - {_cronograma.Nome}", "csv") is not { } caminho)
            return;
        var texto = Csv(_cronograma.OrdensDeServico.Todas, Hoje);
        var temporario = caminho + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporario, texto, TabelaCsv.Codificacao);
            File.Move(temporario, caminho, overwrite: true);
            Mensagem = $"Salvo em {Path.GetFileName(caminho)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporario); } catch (Exception) { /* nada a fazer */ }
            Mensagem = $"Não foi possível salvar: {ex.Message}";
        }
    }

    internal static string Csv(IEnumerable<OrdemDeServico> ordens, DateOnly hoje)
    {
        var brasil = CultureInfo.GetCultureInfo("pt-BR");
        static string Numero(decimal v) => v.ToString("0.00", CultureInfo.GetCultureInfo("pt-BR"));
        string Data(DateOnly? d) => d?.ToString("dd/MM/yyyy", brasil) ?? "";
        return TabelaCsv.Gerar(
            ["OS", "Serviço", "Executante", "Tipo", "Situação", "Emissão", "Prazo", "Conclusão", "Dias de atraso", "Valor (R$)", "Medido (R$)", "Pago (R$)",
             "A pagar (R$)", "Retido (R$)"],
            ordens.Select(o => (IReadOnlyList<string>)[o.Codigo, TabelaCsv.TextoLivre(o.Dados.Titulo), TabelaCsv.TextoLivre(o.Dados.Executante ?? ""),
                TextosDaOrdem.Texto(o.Dados.Tipo), TextosDaOrdem.Texto(o.Situacao), Data(o.Emissao), Data(o.Dados.Prazo), Data(o.Conclusao),
                TextosDaOrdem.DiasDeAtraso(o, hoje)?.ToString(brasil) ?? "", Numero(o.Total), Numero(o.TotalMedido), Numero(o.TotalPago), Numero(o.APagar),
                Numero(o.Retido)]));
    }
}
