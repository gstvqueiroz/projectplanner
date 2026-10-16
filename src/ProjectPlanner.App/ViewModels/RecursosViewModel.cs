using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Planejamento;
using ProjectPlanner.Core.Recursos;
using ProjectPlanner.Relatorios;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Um recurso na lista: os dados e o uso no projeto.</summary>
public sealed record RecursoNaLista(Recurso Recurso, string Uso, string CustoNoProjeto)
{
    public string Nome => Recurso.Nome;

    public string Tipo => TiposDeRecurso.Texto(Recurso.Tipo);

    public string Custo => $"{Moeda.Formatar(Recurso.CustoUnitario)}/{Recurso.Unidade}";

    public string Disponivel => Recurso.Disponivel is { } d ? $"{RecursosDoProjetoTexto.Numero(d)} por dia" : "";

    public string Fornecedor => Recurso.Fornecedor ?? "";
}

/// <summary>Formatação curta de quantidades de recursos (inteiros sem casas).</summary>
public static class RecursosDoProjetoTexto
{
    public static string Numero(decimal valor) => valor == Math.Floor(valor)
        ? valor.ToString("#,##0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))
        : Moeda.FormatarQuantidade(valor);
}

/// <summary>
/// Bloco 32: Projeto > Recursos…: os profissionais, materiais e equipamentos do projeto (nome, tipo, unidade, custo,
/// disponível por dia e fornecedor), quanto cada um é usado e quanto custa no projeto; importar de uma planilha, exportar
/// em CSV, e o catálogo pessoal (trazer de lá e guardar lá). Cada mudança no projeto é um passo do Desfazer.
/// </summary>
public sealed partial class RecursosViewModel : ViewModelBase
{
    private readonly Cronograma _cronograma;
    private readonly IDialogosService _dialogos;
    private readonly ICatalogoDeRecursos _catalogo;
    private readonly Action<string> _mudou;
    private bool _carregando;

    /// <param name="mudou">Os recursos do projeto mudaram, com o nome da mudança para o Desfazer.</param>
    public RecursosViewModel(Cronograma cronograma, IDialogosService dialogos, ICatalogoDeRecursos catalogo, Action<string> mudou)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(dialogos);
        ArgumentNullException.ThrowIfNull(catalogo);
        ArgumentNullException.ThrowIfNull(mudou);
        (_cronograma, _dialogos, _catalogo, _mudou) = (cronograma, dialogos, catalogo, mudou);
        Tipos = [.. TiposDeRecurso.Todos.Select(t => new Escolha<TipoDeRecurso>(t, TiposDeRecurso.Texto(t)))];
        Novo();
        Atualizar();
        AtualizarCatalogo();
    }

    private RecursosDoProjeto Recursos => _cronograma.Recursos;

    public string Titulo => $"Recursos — {_cronograma.Nome}";

    [ObservableProperty] public partial string Mensagem { get; private set; } = "";

    // ---- A lista do projeto ----

    public ObservableCollection<RecursoNaLista> Lista { get; } = [];

    public bool SemRecursos => Lista.Count == 0;

    public const string TextoSemRecursos =
        "Nenhum recurso ainda. Cadastre os profissionais, os materiais e os equipamentos da obra (ou traga do seu catálogo, ou importe uma planilha) e depois atribua-os às tarefas em Tarefa > Recursos da tarefa….";

    public string CustoTotal => $"Custo orçado do projeto: {Moeda.ComSimbolo(Recursos.CustoTotal)}";

    [ObservableProperty] public partial RecursoNaLista? Selecionado { get; set; }

    private void Atualizar()
    {
        var numero = Selecionado?.Recurso.Id;
        _carregando = true;
        try
        {
            Lista.Clear();
            foreach (var recurso in Recursos.Lista)
            {
                var (quantidade, custo) = Recursos.Uso(recurso.Id);
                var tarefas = Recursos.TarefasCom(recurso.Id).Count;
                var uso = tarefas == 0 ? "não usado" :
                    $"{RecursosDoProjetoTexto.Numero(quantidade)} {recurso.Unidade}{(recurso.PorDia && quantidade != 1 ? "s" : "")} · " +
                    (tarefas == 1 ? "1 tarefa" : $"{tarefas} tarefas");
                Lista.Add(new RecursoNaLista(recurso, uso, Moeda.Formatar(custo)));
            }
            Selecionado = Lista.FirstOrDefault(r => r.Recurso.Id == numero);
        }
        finally
        {
            _carregando = false;
        }
        OnPropertyChanged(nameof(SemRecursos));
        OnPropertyChanged(nameof(CustoTotal));
        RemoverCommand.NotifyCanExecuteChanged();
        GuardarNoCatalogoCommand.NotifyCanExecuteChanged();
    }

    // ---- O formulário ----

    public IReadOnlyList<Escolha<TipoDeRecurso>> Tipos { get; }

    [ObservableProperty] public partial string Nome { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PorDia), nameof(RotuloDoCusto))]
    public partial Escolha<TipoDeRecurso> TipoEscolhido { get; set; } = null!;

    [ObservableProperty] public partial string Unidade { get; set; } = "";

    [ObservableProperty] public partial string Custo { get; set; } = "";

    [ObservableProperty] public partial string Disponivel { get; set; } = "";

    [ObservableProperty] public partial string Fornecedor { get; set; } = "";

    /// <summary>Profissional ou equipamento: custo por dia e disponível por dia; material: unidade livre.</summary>
    public bool PorDia => TiposDeRecurso.PorDia(TipoEscolhido.Valor);

    public string RotuloDoCusto => PorDia ? "Custo por dia (R$)" : "Custo unitário (R$)";

    public string TextoDoSalvar => Selecionado is null ? "Adicionar" : "Salvar alteração";

    partial void OnSelecionadoChanged(RecursoNaLista? value)
    {
        OnPropertyChanged(nameof(TextoDoSalvar));
        RemoverCommand.NotifyCanExecuteChanged();
        GuardarNoCatalogoCommand.NotifyCanExecuteChanged();
        if (_carregando || value is not { Recurso: var r })
            return;
        (Nome, TipoEscolhido, Unidade, Custo) = (r.Nome, Tipos.First(t => t.Valor == r.Tipo), r.PorDia ? "" : r.Unidade, Moeda.Formatar(r.CustoUnitario));
        (Disponivel, Fornecedor) = (r.Disponivel is { } d ? RecursosDoProjetoTexto.Numero(d) : "", r.Fornecedor ?? "");
    }

    [RelayCommand]
    private void Novo()
    {
        Selecionado = null;
        TipoEscolhido ??= Tipos[0];
        (Nome, Unidade, Custo, Disponivel, Fornecedor) = ("", "", "", "", "");
    }

    [RelayCommand]
    private void Salvar()
    {
        Recurso recurso;
        try
        {
            recurso = new Recurso(Selecionado?.Recurso.Id ?? Guid.NewGuid(), Nome, TipoEscolhido.Valor, Unidade, Moeda.Ler(Custo) ?? 0,
                PorDia ? Moeda.LerNumero(Disponivel) : null, Fornecedor);
            if (Selecionado is null)
                Recursos.Adicionar(recurso);
            else
                Recursos.Substituir(recurso);
        }
        catch (Exception ex) when (ex is DomainValidationException or FormatException)
        {
            Mensagem = ex.Message;
            return;
        }
        var alterando = Selecionado is not null;
        _mudou(alterando ? $"Alterar o recurso \"{recurso.Nome}\"" : $"Recurso \"{recurso.Nome}\"");
        Atualizar();
        Mensagem = alterando ? $"Alterado: {recurso.Nome}." : $"Cadastrado: {recurso.Nome}. Atribua-o às tarefas em Tarefa > Recursos da tarefa….";
        Novo();
    }

    private bool TemSelecionado() => Selecionado is not null;

    [RelayCommand(CanExecute = nameof(TemSelecionado))]
    private async Task Remover()
    {
        var recurso = Selecionado!.Recurso;
        if (!await _dialogos.ConfirmarAsync("Remover recurso", $"Remover \"{recurso.Nome}\" do projeto? Dá para desfazer com Editar > Desfazer.", "Remover"))
            return;
        try
        {
            Recursos.Remover(recurso.Id);
        }
        catch (DomainValidationException ex)
        {
            Atualizar(); // o uso pode ter mudado
            Mensagem = ex.Message;
            return;
        }
        _mudou($"Remover o recurso \"{recurso.Nome}\"");
        Novo();
        Atualizar();
        Mensagem = $"Removido: {recurso.Nome}.";
    }

    // ---- Planilha ----

    /// <summary>Recursos de uma planilha (.xlsx ou .csv): os novos entram, os de mesmo nome e tipo são atualizados.</summary>
    [RelayCommand]
    private async Task ImportarPlanilha()
    {
        if (await _dialogos.EscolherPlanilhaAsync() is not { } caminho)
            return;
        try
        {
            var linhas = await Task.Run(() => LeitorDePlanilha.Ler(caminho));
            var resultado = ImportacaoDeRecursos.Ler(linhas, Recursos.Lista);
            foreach (var novo in resultado.Novos)
                Recursos.Adicionar(novo);
            foreach (var atualizado in resultado.Atualizados)
                Recursos.Substituir(atualizado);
            if (resultado.Novos.Count + resultado.Atualizados.Count > 0)
                _mudou($"Importar recursos de {Path.GetFileName(caminho)}");
            Atualizar();
            Mensagem = resultado.Resumo;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Mensagem = $"Não foi possível ler a planilha {Path.GetFileName(caminho)}: {ex.Message}";
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ExportarCsv()
    {
        if (await _dialogos.SalvarComoAsync("Exportar os recursos em CSV", $"Recursos - {_cronograma.Nome}", "csv") is not { } caminho)
            return;
        var tabela = ImportacaoDeRecursos.Tabela(Recursos.Lista);
        try
        {
            await File.WriteAllTextAsync(caminho, TabelaCsv.Gerar(tabela[0], tabela.Skip(1).ToList()), TabelaCsv.Codificacao);
            Mensagem = $"Salvo em {Path.GetFileName(caminho)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Mensagem = $"Não foi possível salvar: {ex.Message}";
        }
    }

    // ---- Catálogo pessoal ----

    public ObservableCollection<RecursoNaLista> Catalogo { get; } = [];

    public bool CatalogoVazio => Catalogo.Count == 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TrazerDoCatalogoCommand))]
    public partial RecursoNaLista? SelecionadoNoCatalogo { get; set; }

    private void AtualizarCatalogo()
    {
        Catalogo.Clear();
        foreach (var recurso in _catalogo.Carregar().OrderBy(r => r.Tipo).ThenBy(r => r.Nome, StringComparer.CurrentCultureIgnoreCase))
            Catalogo.Add(new RecursoNaLista(recurso, "", ""));
        OnPropertyChanged(nameof(CatalogoVazio));
    }

    private bool TemNoCatalogo() => SelecionadoNoCatalogo is not null;

    /// <summary>Traz o recurso do catálogo para o projeto (se já há um de mesmo nome e tipo, atualiza o custo dele).</summary>
    [RelayCommand(CanExecute = nameof(TemNoCatalogo))]
    private void TrazerDoCatalogo()
    {
        var doCatalogo = SelecionadoNoCatalogo!.Recurso;
        var existente = Recursos.Lista.FirstOrDefault(r => r.Tipo == doCatalogo.Tipo && string.Equals(r.Nome, doCatalogo.Nome, StringComparison.CurrentCultureIgnoreCase));
        if (existente is null)
            Recursos.Adicionar(doCatalogo.ComId(Guid.NewGuid()));
        else
            Recursos.Substituir(doCatalogo.ComId(existente.Id));
        _mudou($"Trazer \"{doCatalogo.Nome}\" do catálogo");
        Atualizar();
        Mensagem = existente is null ? $"{doCatalogo.Nome} veio do catálogo." : $"{doCatalogo.Nome} já estava no projeto: o custo foi atualizado pelo do catálogo.";
    }

    /// <summary>Guarda o recurso escolhido no catálogo pessoal (substitui o de mesmo nome e tipo).</summary>
    [RelayCommand(CanExecute = nameof(TemSelecionado))]
    private void GuardarNoCatalogo() => Guardar([Selecionado!.Recurso]);

    /// <summary>Guarda todos os recursos do projeto no catálogo pessoal.</summary>
    [RelayCommand]
    private void GuardarTodosNoCatalogo() => Guardar(Recursos.Lista);

    private void Guardar(IReadOnlyList<Recurso> recursos)
    {
        if (recursos.Count == 0)
        {
            Mensagem = "Não há recursos no projeto para guardar.";
            return;
        }
        var catalogo = _catalogo.Carregar().ToList();
        foreach (var recurso in recursos)
        {
            var indice = catalogo.FindIndex(r => r.Tipo == recurso.Tipo && string.Equals(r.Nome, recurso.Nome, StringComparison.CurrentCultureIgnoreCase));
            if (indice >= 0)
                catalogo[indice] = recurso.ComId(catalogo[indice].Id);
            else
                catalogo.Add(recurso.ComId(Guid.NewGuid()));
        }
        try
        {
            _catalogo.Salvar(catalogo);
        }
        catch (IOException ex)
        {
            Mensagem = ex.Message;
            return;
        }
        AtualizarCatalogo();
        Mensagem = recursos.Count == 1 ? $"{recursos[0].Nome} guardado no seu catálogo." : $"{recursos.Count} recursos guardados no seu catálogo.";
    }
}
