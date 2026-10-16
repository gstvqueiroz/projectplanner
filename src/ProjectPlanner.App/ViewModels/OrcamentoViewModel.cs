using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Orcamento;
using ProjectPlanner.Core.Planejamento;
using ProjectPlanner.Relatorios;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Uma linha da planilha orçamentária na tela.</summary>
public sealed record LinhaDoOrcamentoNaTela(LinhaDoOrcamento Linha)
{
    public bool EhGrupo => Linha.EhGrupo;
    public string Edt => Linha.Edt;
    public string Descricao => new string(' ', Math.Min(Linha.Nivel, 6) * 3) + Linha.Descricao;
    public string Codigo => Linha.SemComposicao ? "—" : Linha.Codigo;
    public string Fonte => Linha.SemComposicao ? "valor informado" : Linha.Fonte;
    public string Unidade => Linha.Unidade;
    public string Quantidade => Linha.Quantidade is { } q ? (Linha.QuantidadeEstimada ? "≈ " : "") + Moeda.FormatarQuantidade(q) : "";
    public string CustoUnitario => Linha.CustoUnitario is { } c ? Moeda.Formatar(c) : "";
    public string PrecoUnitario => Linha.PrecoUnitario is { } p ? Moeda.Formatar(p) : "";
    public string Total => Moeda.Formatar(Linha.Total);
    public bool SemQuantidade => !Linha.EhGrupo && !Linha.SemComposicao && Linha.Quantidade is null;
}

/// <summary>Uma composição achada na busca, com o custo unitário na base.</summary>
public sealed record ComposicaoNaBusca(Composicao Composicao, string Custo)
{
    public string Codigo => Composicao.Codigo;
    public string Descricao => Composicao.Descricao;
    public string Unidade => Composicao.Unidade;
}

/// <summary>Um insumo da composição da tarefa, na tela.</summary>
public sealed record InsumoNaTela(InsumoNaComposicao Insumo)
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");
    public string Codigo => Insumo.Codigo;
    public string Descricao => Insumo.Descricao;
    public string Tipo => TiposDeInsumo.Texto(Insumo.Tipo);
    public string Unidade => Insumo.Unidade;
    public string Coeficiente => Insumo.Coeficiente.ToString("0.0000###", Brasil);
    public string Preco => Moeda.Formatar(Insumo.Preco);
    public string Parcial => Moeda.Formatar(Math.Round(Insumo.Parcial, 2, MidpointRounding.AwayFromZero));
}

/// <summary>
/// Bloco 33: Projeto > Orçamento…: a planilha orçamentária (sintética, com o BDI) das tarefas com composição, a escolha das
/// composições (buscadas nas bases importadas) e as bases de preços (importar a SINAPI de um estado e mês, remover,
/// atualizar os preços do orçamento por outra base). Aplicar põe os valores e os recursos nas tarefas. Exporta em PDF,
/// Word e Excel (sintética e analítica). Cada mudança no projeto é um passo do Desfazer.
/// </summary>
public sealed partial class OrcamentoViewModel : ViewModelBase
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    private readonly Cronograma _cronograma;
    private readonly IDialogosService _dialogos;
    private readonly IBibliotecaDeReferencias _biblioteca;
    private readonly TimeProvider _relogio;
    private readonly Action<string> _mudou;
    private readonly Dictionary<string, TabelaDeReferencia> _carregadas = [];
    private bool _carregando;

    /// <param name="mudou">Uma mudança no projeto, com o nome para o Desfazer.</param>
    /// <param name="consultor">Bloco 38: a IA das sugestões; sem ela, a aba avisa que é preciso configurar o assistente.</param>
    public OrcamentoViewModel(Cronograma cronograma, IDialogosService dialogos, IBibliotecaDeReferencias biblioteca, TimeProvider relogio, Action<string> mudou,
        IA.IConsultorDeOrcamento? consultor = null)
    {
        _consultor = consultor ?? new IA.ConsultorIndisponivel();
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(dialogos);
        ArgumentNullException.ThrowIfNull(biblioteca);
        ArgumentNullException.ThrowIfNull(relogio);
        ArgumentNullException.ThrowIfNull(mudou);
        (_cronograma, _dialogos, _biblioteca, _relogio, _mudou) = (cronograma, dialogos, biblioteca, relogio, mudou);
        _carregando = true;
        Bdi = Numero(cronograma.Orcamento.Configuracao.Bdi);
        BdiDeMateriais = cronograma.Orcamento.Configuracao.BdiDeMateriais is { } m ? Numero(m) : "";
        Fonte = "SINAPI";
        DesoneradoEscolhido = OpcoesDeDesoneracao[1];
        _carregando = false;
        AtualizarBases();
        AtualizarTarefas();
        AtualizarPlanilha();
    }

    private OrcamentoDoProjeto Orcamento => _cronograma.Orcamento;

    public string Titulo => $"Orçamento — {_cronograma.Nome}";

    [ObservableProperty] public partial string Mensagem { get; private set; } = "";

    [ObservableProperty] public partial bool Ocupado { get; private set; }

    private static string Numero(decimal valor) => valor.ToString("0.##", Brasil);

    // ================= Planilha =================

    [ObservableProperty] public partial string Bdi { get; set; } = "";

    [ObservableProperty] public partial string BdiDeMateriais { get; set; } = "";

    public IReadOnlyList<LinhaDoOrcamentoNaTela> Linhas { get; private set; } = [];

    public bool SemServicos => !Linhas.Any(l => !l.EhGrupo && !l.Linha.SemComposicao);

    public const string TextoSemServicos =
        "Nenhuma tarefa tem composição ainda. Importe uma base (aba Bases) e escolha a composição de cada tarefa (aba Composições); a quantidade vem da coluna Quantidade da grid.";

    public IReadOnlyList<LinhaDoResumoNaTela> Totais { get; private set; } = [];

    public string BasesUsadas { get; private set; } = "";

    private PlanilhaOrcamentaria Planilha => Orcamento.Planilha();

    private void AtualizarPlanilha()
    {
        var p = Planilha;
        Linhas = [.. p.Linhas.Select(l => new LinhaDoOrcamentoNaTela(l))];
        Totais =
        [
            new("Custo direto (composições)", Moeda.ComSimbolo(p.CustoDireto)),
            new($"BDI ({Numero(p.Configuracao.Bdi)}%{(p.Configuracao.BdiDeMateriais is { } m ? $"; materiais {Numero(m)}%" : "")})", Moeda.ComSimbolo(p.ValorDoBdi)),
            new("Tarefas sem composição (valor informado)", Moeda.ComSimbolo(p.Informado)),
            new("Total do orçamento", Moeda.ComSimbolo(p.Total), true),
        ];
        BasesUsadas = string.Join(" · ", new[]
        {
            p.Bases.Count == 0 ? null : "Preços: " + string.Join(", ", p.Bases),
            p.Servicos.Any(l => l.Fonte == ComposicaoAberta.FonteDaEstimativa) ? "Estimativa IA: preço estimado pela IA, sem composição de base" : null,
            p.Servicos.Any(l => l.QuantidadeEstimada) ? "≈ quantidade estimada pela IA" : null,
        }.OfType<string>());
        foreach (var nome in new[] { nameof(Linhas), nameof(Totais), nameof(BasesUsadas), nameof(SemServicos) })
            OnPropertyChanged(nome);
        AplicarCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SalvarBdi()
    {
        try
        {
            Orcamento.DefinirConfiguracao(new ConfiguracaoDoOrcamento
            {
                Bdi = Moeda.LerNumero(Bdi.Replace("%", "")) ?? 0,
                BdiDeMateriais = Moeda.LerNumero(BdiDeMateriais.Replace("%", "")),
            });
        }
        catch (Exception ex) when (ex is DomainValidationException or FormatException)
        {
            Mensagem = ex.Message;
            return;
        }
        _mudou("BDI do orçamento");
        AtualizarPlanilha();
        AtualizarComposicaoDaTarefa();
        Mensagem = "BDI guardado: os preços foram recalculados.";
    }

    private bool PodeAplicar() => !SemServicos;

    /// <summary>Põe o total de cada serviço no Valor da tarefa e os insumos como recursos atribuídos.</summary>
    [RelayCommand(CanExecute = nameof(PodeAplicar))]
    private async Task Aplicar()
    {
        if (!await _dialogos.ConfirmarAsync("Aplicar o orçamento",
                "As tarefas com composição recebem no Valor o total do orçamento (com o BDI), e os insumos viram recursos atribuídos a elas " +
                "(os de mesmo nome são reaproveitados, com o custo da base). Dá para desfazer com Editar > Desfazer.", "Aplicar"))
            return;
        ResultadoDaAplicacaoDoOrcamento resultado;
        try
        {
            resultado = Orcamento.Aplicar();
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return;
        }
        _mudou("Aplicar o orçamento");
        AtualizarPlanilha();
        AtualizarTarefas();
        Mensagem = resultado.Resumo;
    }

    // ---- Exportar ----

    private string NomeDoArquivo => $"Orçamento - {_cronograma.Nome}";

    [RelayCommand]
    private Task ExportarPdf() => Exportar("pdf", "PDF", (p, a, c, destino) => new GeradorOrcamentoPdf().Gerar(p, a, c, destino));

    [RelayCommand]
    private Task ExportarWord() => Exportar("docx", "Word", (p, a, c, destino) => new GeradorOrcamentoWord().Gerar(p, a, c, destino));

    [RelayCommand]
    private Task ExportarExcel() => Exportar("xlsx", "Excel", (p, a, c, destino) => new GeradorOrcamentoExcel().Gerar(p, a, c, destino));

    private async Task Exportar(string extensao, string formato, Action<PlanilhaOrcamentaria, IReadOnlyDictionary<Guid, ComposicaoAberta>, string, Stream> gerar)
    {
        var planilha = Planilha;
        var composicoes = Orcamento.Servicos.Where(s => Orcamento.ComposicaoDe(s.Key) is not null).ToDictionary(s => s.Key, s => s.Value);
        if (await _dialogos.SalvarComoAsync($"Exportar o orçamento em {formato}", NomeDoArquivo, extensao) is not { } caminho)
            return;
        var cabecalho = $"{_cronograma.Nome} · gerado em {_relogio.GetLocalNow().DateTime.ToString("dd/MM/yyyy HH:mm", Brasil)}";
        var temporario = caminho + ".tmp";
        try
        {
            await Task.Run(() =>
            {
                using (var destino = File.Create(temporario))
                    gerar(planilha, composicoes, cabecalho, destino);
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

    // ================= Composições =================

    public IReadOnlyList<Escolha<BaseDeReferencia>> Bases { get; private set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UsarCommand))]
    public partial Escolha<BaseDeReferencia>? BaseEscolhida { get; set; }

    [ObservableProperty] public partial string Busca { get; set; } = "";

    public IReadOnlyList<ComposicaoNaBusca> Resultados { get; private set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UsarCommand))]
    public partial ComposicaoNaBusca? ResultadoEscolhido { get; set; }

    public IReadOnlyList<Escolha<Guid>> Tarefas { get; private set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UsarCommand), nameof(TirarCommand))]
    public partial Escolha<Guid>? TarefaEscolhida { get; set; }

    public string TextoDaComposicao { get; private set; } = "";

    public IReadOnlyList<InsumoNaTela> InsumosDaTarefa { get; private set; } = [];

    partial void OnBaseEscolhidaChanged(Escolha<BaseDeReferencia>? value) => Buscar();

    partial void OnBuscaChanged(string value) => Buscar();

    partial void OnTarefaEscolhidaChanged(Escolha<Guid>? value) => AtualizarComposicaoDaTarefa();

    private TabelaDeReferencia? TabelaEscolhida
    {
        get
        {
            if (BaseEscolhida is not { } b)
                return null;
            if (!_carregadas.TryGetValue(b.Valor.Chave, out var tabela) && _biblioteca.Carregar(b.Valor) is { } lida)
                _carregadas[b.Valor.Chave] = tabela = lida;
            return tabela;
        }
    }

    private void Buscar()
    {
        if (_carregando)
            return;
        var tabela = TabelaEscolhida;
        Resultados = tabela is null ? [] : [.. tabela.Buscar(Busca).Select(c =>
        {
            string custo;
            try
            {
                custo = Moeda.Formatar(tabela.Abrir(c.Codigo).CustoUnitario);
            }
            catch (DomainValidationException)
            {
                custo = "—";
            }
            return new ComposicaoNaBusca(c, custo);
        })];
        OnPropertyChanged(nameof(Resultados)); // antes da escolha: a grid limpa a seleção quando a lista muda
        ResultadoEscolhido = Resultados.FirstOrDefault();
    }

    private void AtualizarTarefas()
    {
        var anterior = TarefaEscolhida?.Valor;
        var edt = _cronograma.NumeracaoEdt();
        Tarefas = [.. _cronograma.Tarefas.Where(t => !t.EhResumo).Select(t => new Escolha<Guid>(t.Id,
            $"{edt[t.Id]} {t.Nome}" + (t.Quantidade is { } q ? $" · {Moeda.FormatarQuantidade(q)} {t.Unidade}" : " · sem quantidade") +
            (Orcamento.ComposicaoDe(t.Id) is { } c ? $" · {c.Codigo}" : "")))];
        OnPropertyChanged(nameof(Tarefas));
        TarefaEscolhida = Tarefas.FirstOrDefault(t => t.Valor == anterior) ?? Tarefas.FirstOrDefault();
    }

    private void AtualizarComposicaoDaTarefa()
    {
        var c = TarefaEscolhida is { } t ? Orcamento.ComposicaoDe(t.Valor) : null;
        TextoDaComposicao = c is null ? "Sem composição." :
            (c.EhEstimativa ? $"Estimativa da IA — {c.Descricao} ({c.Unidade}) · " : $"{c.Base.Fonte} {c.Codigo} — {c.Descricao} ({c.Unidade}) · {c.Base.Descricao} · ") +
            $"custo {Moeda.ComSimbolo(c.CustoUnitario)}/{c.Unidade} · preço com BDI {Moeda.ComSimbolo(Orcamento.PrecoUnitario(c))}/{c.Unidade}";
        InsumosDaTarefa = c is null ? [] : [.. c.Insumos.Select(i => new InsumoNaTela(i))];
        OnPropertyChanged(nameof(TextoDaComposicao));
        OnPropertyChanged(nameof(InsumosDaTarefa));
        TirarCommand.NotifyCanExecuteChanged();
    }

    private bool PodeUsar() => ResultadoEscolhido is not null && TarefaEscolhida is not null && TabelaEscolhida is not null;

    [RelayCommand(CanExecute = nameof(PodeUsar))]
    private void Usar()
    {
        var (tarefa, codigo) = (_cronograma.Tarefas.First(t => t.Id == TarefaEscolhida!.Valor), ResultadoEscolhido!.Codigo);
        try
        {
            Orcamento.Associar(tarefa, TabelaEscolhida!.Abrir(codigo));
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return;
        }
        _mudou($"Composição de \"{tarefa.Nome}\"");
        AtualizarTarefas();
        AtualizarComposicaoDaTarefa();
        AtualizarPlanilha();
        Mensagem = $"\"{tarefa.Nome}\" usa a composição {codigo}." + (tarefa.Quantidade is null ? " Falta a quantidade da tarefa (coluna Quantidade)." : "");
    }

    private bool PodeTirar() => TarefaEscolhida is { } t && Orcamento.ComposicaoDe(t.Valor) is not null;

    [RelayCommand(CanExecute = nameof(PodeTirar))]
    private void Tirar()
    {
        var tarefa = _cronograma.Tarefas.First(t => t.Id == TarefaEscolhida!.Valor);
        Orcamento.Desassociar(tarefa.Id);
        _mudou($"Tirar a composição de \"{tarefa.Nome}\"");
        AtualizarTarefas();
        AtualizarComposicaoDaTarefa();
        AtualizarPlanilha();
    }

    // ================= Bases =================

    public IReadOnlyList<BaseNaBiblioteca> BasesNaBiblioteca { get; private set; } = [];

    public bool SemBases => BasesNaBiblioteca.Count == 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoverBaseCommand), nameof(AtualizarPrecosCommand))]
    public partial BaseNaBiblioteca? BaseNaLista { get; set; }

    [ObservableProperty] public partial string Fonte { get; set; } = "";

    [ObservableProperty] public partial string Uf { get; set; } = "";

    [ObservableProperty] public partial string Referencia { get; set; } = "";

    public IReadOnlyList<Escolha<bool?>> OpcoesDeDesoneracao { get; } = [new(true, "Desonerado"), new(false, "Não desonerado"), new(null, "Não se aplica")];

    [ObservableProperty] public partial Escolha<bool?> DesoneradoEscolhido { get; set; } = null!;

    private void AtualizarBases()
    {
        var anterior = BaseEscolhida?.Valor.Chave;
        BasesNaBiblioteca = _biblioteca.Listar();
        Bases = [.. BasesNaBiblioteca.Select(b => new Escolha<BaseDeReferencia>(b.Base, b.Base.Descricao))];
        foreach (var nome in new[] { nameof(BasesNaBiblioteca), nameof(Bases), nameof(SemBases) })
            OnPropertyChanged(nome);
        BaseEscolhida = Bases.FirstOrDefault(b => b.Valor.Chave == anterior) ?? Bases.LastOrDefault();
    }

    /// <summary>
    /// Importa uma planilha (a de insumos ou a de composições analíticas) para a base dos campos Fonte, UF, Referência e
    /// Desoneração (preenchidos pelo nome do arquivo quando ele os traz); a mesma base pode receber as duas planilhas.
    /// </summary>
    [RelayCommand]
    private async Task ImportarPlanilha()
    {
        if (await _dialogos.EscolherPlanilhaAsync() is not { } caminho)
            return;
        var doNome = BaseDeReferencia.DoNomeDoArquivo(caminho);
        _carregando = true;
        // O nome do arquivo da Caixa diz o estado, o mês e a desoneração: vale mais que os campos (que ficam do último arquivo).
        Uf = doNome.Uf ?? Uf;
        Referencia = doNome.Referencia ?? Referencia;
        if (doNome.Desonerado is { } d) DesoneradoEscolhido = OpcoesDeDesoneracao.First(o => o.Valor == d);
        _carregando = false;
        Ocupado = true;
        try
        {
            var @base = new BaseDeReferencia(Fonte, Uf, Referencia, DesoneradoEscolhido.Valor);
            var resultado = await Task.Run(() => ImportacaoDeReferencia.Ler(LeitorDePlanilha.Ler(caminho)));
            var existente = _biblioteca.Carregar(@base);
            var insumos = (existente?.Insumos ?? []).ToDictionary(i => i.Codigo);
            foreach (var i in resultado.Insumos)
                if (resultado.Composicoes.Count == 0 || !insumos.ContainsKey(i.Codigo)) // a planilha de insumos (com a classe) vale mais
                    insumos[i.Codigo] = i;
            var composicoes = (existente?.Composicoes ?? []).ToDictionary(c => c.Codigo);
            foreach (var c in resultado.Composicoes)
                composicoes[c.Codigo] = c;
            var tabela = new TabelaDeReferencia(@base, insumos.Values, composicoes.Values);
            await Task.Run(() => _biblioteca.Salvar(tabela));
            _carregadas[@base.Chave] = tabela;
            AtualizarBases();
            BaseEscolhida = Bases.First(b => b.Valor.Chave == @base.Chave);
            Mensagem = $"{Path.GetFileName(caminho)} → {@base.Descricao}: {resultado.Resumo} A base tem agora {tabela.Insumos.Count.ToString("#,##0", Brasil)} insumos e " +
                       $"{tabela.Composicoes.Count.ToString("#,##0", Brasil)} composições.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Mensagem = $"Não foi possível ler {Path.GetFileName(caminho)}: {ex.Message}";
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
        }
        finally
        {
            Ocupado = false;
        }
    }

    private bool TemBaseNaLista() => BaseNaLista is not null;

    [RelayCommand(CanExecute = nameof(TemBaseNaLista))]
    private async Task RemoverBase()
    {
        var b = BaseNaLista!.Base;
        if (!await _dialogos.ConfirmarAsync("Remover base", $"Remover a base {b.Descricao} da biblioteca? Os orçamentos que já usam composições dela continuam iguais.", "Remover"))
            return;
        _biblioteca.Remover(b);
        _carregadas.Remove(b.Chave);
        AtualizarBases();
        Mensagem = $"Base {b.Descricao} removida.";
    }

    /// <summary>Troca as composições do orçamento pelas de mesmo código na base escolhida (preços de outro mês, por exemplo).</summary>
    [RelayCommand(CanExecute = nameof(TemBaseNaLista))]
    private async Task AtualizarPrecos()
    {
        var b = BaseNaLista!.Base;
        if (_biblioteca.Carregar(b) is not { } tabela)
        {
            Mensagem = $"A base {b.Descricao} não pôde ser lida.";
            return;
        }
        if (!await _dialogos.ConfirmarAsync("Atualizar os preços", $"Trocar as composições do orçamento pelas de mesmo código na base {b.Descricao}?", "Atualizar"))
            return;
        var (atualizadas, faltando) = Orcamento.AtualizarPrecos(tabela);
        if (atualizadas > 0)
            _mudou($"Atualizar os preços pela {b.Descricao}");
        AtualizarPlanilha();
        AtualizarComposicaoDaTarefa();
        Mensagem = $"{(atualizadas == 1 ? "1 composição atualizada" : $"{atualizadas} composições atualizadas")} pela {b.Descricao}." +
                   (faltando.Count == 0 ? "" : $" Não estão na base (ficaram como estavam): {string.Join(", ", faltando)}.");
    }
}
