using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.Core;
using ProjectPlanner.Core.OrdensDeServico;
using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.App.ViewModels;

// Bloco 70 (saiu de OrdensDeServicoViewModel.cs): a ficha da OS (os dados, os itens, a verificação) e as ações do rascunho.
public sealed partial class OrdensDeServicoViewModel
{
    // ---- A ficha ----

    public IReadOnlyList<Escolha<TipoDeExecutante>> Tipos { get; } =
        [.. Enum.GetValues<TipoDeExecutante>().Select(t => new Escolha<TipoDeExecutante>(t, TextosDaOrdem.Texto(t)))];

    public IReadOnlyList<Escolha<FormaDePagamentoDaOrdem>> Formas { get; } =
        [.. Enum.GetValues<FormaDePagamentoDaOrdem>().Select(f => new Escolha<FormaDePagamentoDaOrdem>(f, TextosDaOrdem.Texto(f)))];

    public IReadOnlyList<Escolha<PrecoDosItens>> Precos { get; } =
    [
        new(PrecoDosItens.MaoDeObra, "Só mão de obra (o material é da obra)"),
        new(PrecoDosItens.ServicoCompleto, "Serviço completo (com material)"),
    ];

    /// <summary>Os profissionais, os fornecedores e os executantes de outras OS, para o executante.</summary>
    public IReadOnlyList<string> Sugestoes { get; }

    public IReadOnlyList<TarefaParaEscolher> Tarefas { get; }

    public bool TemSelecionada => Selecionada is not null;

    public OrdemDeServico? Ordem => Selecionada?.Ordem;

    /// <summary>"OS-003 · Em execução".</summary>
    public string Cabecalho => Ordem is { } o ? $"{o.Codigo} · {TextosDaOrdem.Texto(o.Situacao)}" : "";

    public string AvisoDeVencida => Ordem is { } o && o.Vencida(Hoje) ? $"Passou do prazo ({o.Dados.Prazo!.Value.ToString("dd/MM/yyyy", Brasil)})." : "";

    public bool PodeEditar => Ordem?.PodeEditar == true;

    /// <summary>A lista de verificação aparece no rascunho (para montar) e quando tem algum ponto.</summary>
    public bool MostraVerificacao => PodeEditar || Verificacao.Count > 0;

    /// <summary>A verificação se marca no rascunho (a lista) e durante a execução e a conferência.</summary>
    public bool PodeMarcarVerificacao => PodeEditar || Ordem?.Situacao is SituacaoDaOrdem.Aceita or SituacaoDaOrdem.EmExecucao or SituacaoDaOrdem.Concluida;

    [ObservableProperty] public partial string TituloDaOrdem { get; set; } = "";
    [ObservableProperty] public partial Escolha<TipoDeExecutante> TipoEscolhido { get; set; } = null!;
    [ObservableProperty] public partial string Executante { get; set; } = "";
    [ObservableProperty] public partial string Contato { get; set; } = "";
    [ObservableProperty] public partial DateTime? InicioPrevisto { get; set; }
    [ObservableProperty] public partial DateTime? Prazo { get; set; }
    [ObservableProperty] public partial Escolha<FormaDePagamentoDaOrdem> FormaEscolhida { get; set; } = null!;
    [ObservableProperty] public partial string Observacoes { get; set; } = "";

    /// <summary>Bloco 62: a retenção de garantia (%), como digitada.</summary>
    [ObservableProperty] public partial string Retencao { get; set; } = "";
    [ObservableProperty] public partial Escolha<PrecoDosItens> PrecoEscolhido { get; set; } = null!;
    [ObservableProperty] public partial string NovaVerificacao { get; set; } = "";

    public ObservableCollection<ItemNaFicha> Itens { get; } = [];

    public ObservableCollection<VerificacaoNaFicha> Verificacao { get; } = [];

    /// <summary>"25/10/2026 14:00 · Aceita (João, pelo Telegram)", do mais novo ao mais antigo.</summary>
    public IReadOnlyList<string> Historico => Ordem is { } o
        ? [.. o.Historico.Reverse().Select(e => $"{e.Quando.ToString("dd/MM/yyyy HH:mm", Brasil)} · {e.Descricao}{(e.Autor is null ? "" : $" ({e.Autor})")}")]
        : [];

    public string TotalDaFicha
    {
        get
        {
            try
            {
                return Moeda.ComSimbolo(Itens.Sum(i => (Moeda.LerNumero(i.Quantidade) ?? 0) * (Moeda.Ler(i.Preco) ?? 0)));
            }
            catch (FormatException)
            {
                return "—";
            }
        }
    }

    private static readonly HashSet<string> CamposDaFicha =
    [
        nameof(TituloDaOrdem), nameof(TipoEscolhido), nameof(Executante), nameof(Contato), nameof(InicioPrevisto), nameof(Prazo),
        nameof(FormaEscolhida), nameof(Observacoes), nameof(Retencao),
    ];

    private void FichaMudou(object? sender, PropertyChangedEventArgs e)
    {
        if (!_carregando && CamposDaFicha.Contains(e.PropertyName ?? ""))
            _fichaMudou = true;
    }

    private void ItemMudou(object? sender, PropertyChangedEventArgs e)
    {
        if (_carregando)
            return;
        _fichaMudou = true;
        OnPropertyChanged(nameof(TotalDaFicha));
    }

    // A verificação: no rascunho é só a lista; depois, marcar um ponto muda a OS na hora.
    private void VerificacaoMudou(object? sender, PropertyChangedEventArgs e)
    {
        if (_carregando || sender is not VerificacaoNaFicha v || e.PropertyName != nameof(VerificacaoNaFicha.Feito))
            return;
        if (PodeEditar)
        {
            _fichaMudou = true;
            return;
        }
        if (Ordem is not { } o)
            return;
        if (!Executar(() => o.MarcarVerificacao(Verificacao.IndexOf(v), v.Feito),
                $"{o.Codigo}: {(v.Feito ? "feito" : "desmarcado")} \"{v.Texto}\"", $"{(v.Feito ? "Marcado" : "Desmarcado")}: {v.Texto}."))
            Carregar(o); // a caixa volta a mostrar o que vale
    }

    partial void OnSelecionadaChanged(OrdemNaLista? oldValue, OrdemNaLista? newValue)
    {
        if (_carregando)
            return;
        if (oldValue is { Ordem.PodeEditar: true } anterior && _fichaMudou && !Gravar(anterior.Ordem, out var erro))
            Mensagem = $"As alterações da {anterior.Ordem.Codigo} não foram salvas: {erro}";
        Carregar(newValue?.Ordem);
    }

    // Põe a OS na ficha (ou limpa).
    private void Carregar(OrdemDeServico? o)
    {
        _carregando = true;
        try
        {
            foreach (var item in Itens)
                item.PropertyChanged -= ItemMudou;
            foreach (var v in Verificacao)
                v.PropertyChanged -= VerificacaoMudou;
            Itens.Clear();
            Verificacao.Clear();
            var d = o?.Dados;
            TituloDaOrdem = d?.Titulo ?? "";
            TipoEscolhido = Tipos.First(t => t.Valor == (d?.Tipo ?? TipoDeExecutante.Terceiro));
            Executante = d?.Executante ?? "";
            Contato = d?.Contato ?? "";
            InicioPrevisto = d?.InicioPrevisto?.ToDateTime(TimeOnly.MinValue);
            Prazo = d?.Prazo?.ToDateTime(TimeOnly.MinValue);
            FormaEscolhida = Formas.First(f => f.Valor == (d?.FormaDePagamento ?? FormaDePagamentoDaOrdem.Empreitada));
            PrecoEscolhido ??= Precos[0];
            Observacoes = d?.Observacoes ?? "";
            Retencao = d is { Retencao: > 0 } ? d.Retencao.ToString("0.##", Brasil) : "";
            NovaVerificacao = "";
            foreach (var item in d?.Itens ?? [])
                Itens.Add(ItemNaFicha.De(item, EdtDe(item.TarefaId)));
            foreach (var v in d?.Verificacao ?? [])
                Verificacao.Add(new VerificacaoNaFicha(v.Texto, v.Feito));
            foreach (var item in Itens)
                item.PropertyChanged += ItemMudou;
            foreach (var v in Verificacao)
                v.PropertyChanged += VerificacaoMudou;
            var tarefasDaOrdem = d?.Tarefas.ToHashSet() ?? [];
            foreach (var t in Tarefas)
                t.Marcada = tarefasDaOrdem.Contains(t.Id);
        }
        finally
        {
            _carregando = false;
        }
        _fichaMudou = false;
        foreach (var nome in new[] { nameof(TemSelecionada), nameof(Ordem), nameof(Cabecalho), nameof(AvisoDeVencida), nameof(PodeEditar),
                     nameof(PodeMarcarVerificacao), nameof(MostraVerificacao), nameof(Historico), nameof(TotalDaFicha), nameof(TemRelatorioFotografico) })
            OnPropertyChanged(nome);
        CarregarMedicoes(o);
        AtualizarBotoes();
    }

    private string EdtDe(Guid? tarefaId) => tarefaId is { } id && _edt.TryGetValue(id, out var edt) ? edt : "";

    /// <summary>Os dados como estão na ficha.</summary>
    /// <exception cref="DomainValidationException">Algum campo não vale.</exception>
    /// <exception cref="FormatException">Uma quantidade ou um preço que não é número.</exception>
    private DadosDaOrdem DadosDaFicha()
    {
        var itens = Itens.Select(i => new ItemDaOrdem(i.TarefaId, i.Descricao, i.Unidade,
            Moeda.LerNumero(i.Quantidade) ?? throw new DomainValidationException(nameof(Itens), $"Informe a quantidade de \"{i.Descricao}\"."),
            Moeda.Ler(i.Preco) ?? 0)).ToList();
        var recurso = _cronograma.Recursos.Lista.FirstOrDefault(r => string.Equals(r.Nome, Executante.Trim(), StringComparison.OrdinalIgnoreCase));
        return new DadosDaOrdem(TituloDaOrdem, TipoEscolhido.Valor, Executante, recurso?.Id, Contato,
            Tarefas.Where(t => t.Marcada).Select(t => t.Id), itens,
            InicioPrevisto is { } inicio ? DateOnly.FromDateTime(inicio) : null, Prazo is { } prazo ? DateOnly.FromDateTime(prazo) : null,
            FormaEscolhida.Valor, Observacoes, Verificacao.Select(v => new ItemDeVerificacao(v.Texto, v.Feito)),
            Moeda.LerNumero(Retencao.Replace("%", "")) ?? 0);
    }

    // Grava a ficha na OS em rascunho (sem mensagem); falso com o motivo.
    private bool Gravar(OrdemDeServico ordem, out string erro)
    {
        try
        {
            var nova = ordem.Editar(DadosDaFicha());
            _cronograma.OrdensDeServico.Substituir(nova);
            _mudou($"Alterar a {nova.Codigo}", false);
            _fichaMudou = false;
            erro = "";
            return true;
        }
        catch (Exception ex) when (ex is DomainValidationException or FormatException)
        {
            erro = ex.Message;
            return false;
        }
    }

    // ---- Ações da ficha (rascunho) ----

    /// <summary>Uma OS nova em rascunho, com a tarefa selecionada na grid (e as de dentro, se for uma etapa), ou em branco.</summary>
    [RelayCommand]
    private void NovaOrdem() => NovaOuCopia(null);

    /// <summary>Bloco 62: um rascunho novo com os dados da OS escolhida (um serviço que se repete, de pavimento em pavimento).</summary>
    [RelayCommand(CanExecute = nameof(TemSelecionadaParaPdf))]
    private void Duplicar() => NovaOuCopia(Ordem);

    private void NovaOuCopia(OrdemDeServico? original)
    {
        if (original is null)
        {
            NovaDoZero();
            return;
        }
        if (!SalvarSePreciso())
            return;
        var copia = original.Duplicar(Guid.NewGuid(), _cronograma.OrdensDeServico.ProximoNumero, Agora);
        _cronograma.OrdensDeServico.Adicionar(copia);
        _mudou($"Duplicar a {original.Codigo}", false);
        if (FiltroEscolhido.Valor is FiltroDasOrdens.Vencidas or FiltroDasOrdens.Encerradas)
            FiltroEscolhido = Filtros[0];
        Selecionada = null;
        Atualizar();
        Selecionada = Lista.First(l => l.Ordem.Id == copia.Id);
        Mensagem = $"{copia.Codigo} criada como cópia da {original.Codigo}, em rascunho: ajuste o serviço, as tarefas e as datas.";
    }

    private void NovaDoZero()
    {
        if (!SalvarSePreciso())
            return;
        DadosDaOrdem dados;
        try
        {
            dados = TarefaSelecionadaNaGrid is { } id && _cronograma.Tarefas.Any(t => t.Id == id)
                ? MontagemDaOrdem.Rascunho(_cronograma, [id], PrecoEscolhido?.Valor ?? PrecoDosItens.MaoDeObra)
                : new DadosDaOrdem("Nova ordem de serviço", TipoDeExecutante.Terceiro, null, null, null, null, null, Hoje, null,
                    FormaDePagamentoDaOrdem.Empreitada, null, null);
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return;
        }
        var ordem = OrdemDeServico.Nova(Guid.NewGuid(), _cronograma.OrdensDeServico.ProximoNumero, dados, Agora);
        _cronograma.OrdensDeServico.Adicionar(ordem);
        _mudou($"Nova {ordem.Codigo}", false);
        if (FiltroEscolhido.Valor is FiltroDasOrdens.Vencidas or FiltroDasOrdens.Encerradas)
            FiltroEscolhido = Filtros[0];
        Selecionada = null;
        Atualizar();
        Selecionada = Lista.First(l => l.Ordem.Id == ordem.Id);
        Mensagem = TarefaSelecionadaNaGrid is null
            ? $"{ordem.Codigo} criada. Marque as tarefas e clique em Trazer itens das tarefas, ou adicione os itens à mão."
            : $"{ordem.Codigo} criada com {Plural(dados.Itens.Count, "item", "itens")} da tarefa selecionada. Confira os preços e escolha o executante.";
    }

    private bool SalvarSePreciso()
    {
        if (Ordem is not { PodeEditar: true } o || !_fichaMudou)
            return true;
        if (Gravar(o, out var erro))
        {
            Atualizar();
            return true;
        }
        Mensagem = erro;
        return false;
    }

    [RelayCommand(CanExecute = nameof(PodeEditar))]
    private void Salvar()
    {
        if (Ordem is not { } o)
            return;
        if (!Gravar(o, out var erro))
        {
            Mensagem = erro;
            return;
        }
        Atualizar();
        Mensagem = $"{o.Codigo} salva.";
    }

    /// <summary>Troca os itens que vieram de tarefas pelos das tarefas marcadas (os avulsos ficam), com o preço escolhido.</summary>
    [RelayCommand(CanExecute = nameof(PodeEditar))]
    private void TrazerItensDasTarefas()
    {
        var marcadas = Tarefas.Where(t => t.Marcada).Select(t => t.Id).ToList();
        if (marcadas.Count == 0)
        {
            Mensagem = "Marque as tarefas da OS (na lista de tarefas) e clique de novo.";
            return;
        }
        DadosDaOrdem montada;
        try
        {
            montada = MontagemDaOrdem.Rascunho(_cronograma, marcadas, PrecoEscolhido.Valor);
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return;
        }
        var avulsos = Itens.Where(i => i.TarefaId is null).ToList();
        foreach (var item in Itens)
            item.PropertyChanged -= ItemMudou;
        Itens.Clear();
        foreach (var item in montada.Itens.Select(i => ItemNaFicha.De(i, EdtDe(i.TarefaId))).Concat(avulsos))
        {
            item.PropertyChanged += ItemMudou;
            Itens.Add(item);
        }
        (InicioPrevisto, Prazo) = (montada.InicioPrevisto?.ToDateTime(TimeOnly.MinValue), montada.Prazo?.ToDateTime(TimeOnly.MinValue));
        _fichaMudou = true;
        OnPropertyChanged(nameof(TotalDaFicha));
        Mensagem = $"{Plural(montada.Itens.Count, "item veio", "itens vieram")} das tarefas, com as datas do cronograma. Confira e salve.";
    }

    [RelayCommand(CanExecute = nameof(PodeEditar))]
    private void AdicionarItem()
    {
        var item = new ItemNaFicha(null, "") { Unidade = MontagemDaOrdem.UnidadeSemQuantidade, Quantidade = "1" };
        item.PropertyChanged += ItemMudou;
        Itens.Add(item);
        _fichaMudou = true;
    }

    [RelayCommand]
    private void RemoverItem(ItemNaFicha? item)
    {
        if (!PodeEditar || item is null)
            return;
        item.PropertyChanged -= ItemMudou;
        Itens.Remove(item);
        _fichaMudou = true;
        OnPropertyChanged(nameof(TotalDaFicha));
    }

    [RelayCommand(CanExecute = nameof(PodeEditar))]
    private void AdicionarVerificacao()
    {
        try
        {
            var v = new ItemDeVerificacao(NovaVerificacao);
            var naFicha = new VerificacaoNaFicha(v.Texto, false);
            naFicha.PropertyChanged += VerificacaoMudou;
            Verificacao.Add(naFicha);
            NovaVerificacao = "";
            _fichaMudou = true;
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
        }
    }

    [RelayCommand]
    private void RemoverVerificacao(VerificacaoNaFicha? v)
    {
        if (!PodeEditar || v is null)
            return;
        v.PropertyChanged -= VerificacaoMudou;
        Verificacao.Remove(v);
        _fichaMudou = true;
    }

    [RelayCommand(CanExecute = nameof(PodeEditar))]
    private async Task ExcluirRascunho()
    {
        var o = Ordem!;
        if (!await _dialogos.ConfirmarAsync("Excluir rascunho", $"Excluir a {o.Codigo} (\"{o.Dados.Titulo}\")? Dá para desfazer com Editar > Desfazer.", "Excluir"))
            return;
        _cronograma.OrdensDeServico.Remover(o.Id);
        _mudou($"Excluir a {o.Codigo}", false);
        _fichaMudou = false;
        Selecionada = null;
        Atualizar();
        Selecionada = Lista.FirstOrDefault();
        Mensagem = $"{o.Codigo} excluída.";
    }
}
