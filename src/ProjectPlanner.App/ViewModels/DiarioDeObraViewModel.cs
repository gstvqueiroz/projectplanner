using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Diario;
using ProjectPlanner.Core.Relatorios;
using ProjectPlanner.Relatorios;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Bloco 81: uma linha do efetivo na tela (a quantidade em texto, conferida ao salvar).</summary>
public sealed partial class LinhaDoEfetivo(DiarioDeObraViewModel tela) : ObservableObject
{
    [ObservableProperty] public partial string Funcao { get; set; } = "";
    [ObservableProperty] public partial string Quantidade { get; set; } = "1";
    [ObservableProperty] public partial string Empresa { get; set; } = "";

    [RelayCommand]
    private void Remover() => tela.Remover(this);

    partial void OnFuncaoChanged(string value) => tela.Mudou();
    partial void OnQuantidadeChanged(string value) => tela.Mudou();
    partial void OnEmpresaChanged(string value) => tela.Mudou();
}

/// <summary>Bloco 81: uma linha dos equipamentos na tela.</summary>
public sealed partial class LinhaDoEquipamento(DiarioDeObraViewModel tela) : ObservableObject
{
    [ObservableProperty] public partial string Nome { get; set; } = "";
    [ObservableProperty] public partial string Quantidade { get; set; } = "1";

    [RelayCommand]
    private void Remover() => tela.Remover(this);

    partial void OnNomeChanged(string value) => tela.Mudou();
    partial void OnQuantidadeChanged(string value) => tela.Mudou();
}

/// <summary>Bloco 81: um serviço executado na tela (a tarefa do cronograma, se veio dele; o % em texto).</summary>
public sealed partial class LinhaDaAtividade(DiarioDeObraViewModel tela, Guid? tarefaId) : ObservableObject
{
    public Guid? TarefaId { get; } = tarefaId;

    /// <summary>Veio do cronograma: o % pode ir para a tarefa.</summary>
    public bool DoCronograma => TarefaId is not null;

    [ObservableProperty] public partial string Descricao { get; set; } = "";
    [ObservableProperty] public partial string Percentual { get; set; } = "";

    [RelayCommand]
    private void Remover() => tela.Remover(this);

    partial void OnDescricaoChanged(string value) => tela.Mudou();
    partial void OnPercentualChanged(string value) => tela.Mudou();
}

/// <summary>Bloco 81: um dia na lista: "RDO 012 · 28/09/2026 · seg" e o resumo ("9 pessoas · chuva à tarde · fechado").</summary>
public sealed record DiaNaLista(DateOnly Data, string Texto, string Resumo, bool Fechado);

/// <summary>
/// Bloco 81: Projeto > Diário de obra… (o RDO). A lista dos dias à esquerda; à direita, o dia escolhido: o clima de manhã e de
/// tarde, o efetivo, os equipamentos, os serviços executados e as ocorrências. Um dia novo já vem preenchido pelo cronograma (os
/// serviços previstos, o efetivo e os equipamentos deles, e o clima pelo calendário); o usuário ajusta o que mudou. Fechar o dia o
/// assina (não muda mais, a não ser reaberto). O % dos serviços vai ao cronograma e um dia impraticável vai ao calendário quando o
/// usuário pede. Cada mudança é um passo do Desfazer; o que está no formulário é salvo ao trocar de dia e ao fechar a tela.
/// </summary>
public sealed partial class DiarioDeObraViewModel : ViewModelBase
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    private readonly Cronograma _cronograma;
    private readonly IDialogosService _dialogos;
    private readonly TimeProvider _relogio;
    private readonly Action<string> _mudou;
    private readonly Func<CabecalhoDoRelatorio> _cabecalho;
    private readonly Func<IEnumerable<Guid>, IReadOnlyDictionary<Guid, byte[]>> _miniaturas;
    private bool _carregando;
    private bool _sujo;

    /// <param name="mudou">O projeto mudou, com o nome da mudança para o Desfazer (o programa recalcula).</param>
    /// <param name="miniaturas">As miniaturas das fotos, para o PDF.</param>
    public DiarioDeObraViewModel(Cronograma cronograma, IDialogosService dialogos, TimeProvider relogio, Action<string> mudou,
        Func<CabecalhoDoRelatorio> cabecalho, Func<IEnumerable<Guid>, IReadOnlyDictionary<Guid, byte[]>> miniaturas)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(dialogos);
        ArgumentNullException.ThrowIfNull(relogio);
        ArgumentNullException.ThrowIfNull(mudou);
        ArgumentNullException.ThrowIfNull(cabecalho);
        ArgumentNullException.ThrowIfNull(miniaturas);
        (_cronograma, _dialogos, _relogio, _mudou, _cabecalho, _miniaturas) = (cronograma, dialogos, relogio, mudou, cabecalho, miniaturas);
        DataDoNovo = ProximaData().ToDateTime(TimeOnly.MinValue);
        AtualizarLista();
        Selecionado = Dias.LastOrDefault();
    }

    public string Titulo => $"Diário de obra — {_cronograma.Nome}";

    private DateOnly Hoje => DateOnly.FromDateTime(_relogio.GetLocalNow().DateTime);

    private DateTime Agora => _relogio.GetLocalNow().DateTime;

    public ObservableCollection<DiaNaLista> Dias { get; } = [];

    public bool SemDias => Dias.Count == 0;

    public const string TextoSemDias =
        "Nenhum dia ainda. Escolha a data e use Novo dia: ele já vem com os serviços, o efetivo e os equipamentos que o cronograma prevê.";

    [ObservableProperty] public partial DiaNaLista? Selecionado { get; set; }

    [ObservableProperty] public partial DateTime? DataDoNovo { get; set; }

    [ObservableProperty] public partial string Mensagem { get; private set; } = "";

    // ---- O dia escolhido ----

    public IReadOnlyList<Escolha<TempoNoPeriodo>> Tempos { get; } =
        [.. Enum.GetValues<TempoNoPeriodo>().Select(t => new Escolha<TempoNoPeriodo>(t, ClimaDoPeriodo.NomeDoTempo(t)))];

    [ObservableProperty] public partial Escolha<TempoNoPeriodo>? TempoDaManha { get; set; }
    [ObservableProperty] public partial bool ManhaPraticavel { get; set; } = true;
    [ObservableProperty] public partial Escolha<TempoNoPeriodo>? TempoDaTarde { get; set; }
    [ObservableProperty] public partial bool TardePraticavel { get; set; } = true;
    [ObservableProperty] public partial string Ocorrencias { get; set; } = "";

    public ObservableCollection<LinhaDoEfetivo> Efetivo { get; } = [];
    public ObservableCollection<LinhaDoEquipamento> Equipamentos { get; } = [];
    public ObservableCollection<LinhaDaAtividade> Atividades { get; } = [];

    /// <summary>"RDO nº 012 · 28/09/2026 · segunda-feira".</summary>
    [ObservableProperty] public partial string Cabecalho { get; private set; } = "";

    /// <summary>"Aberto" ou "Fechado em 28/09/2026 18:00 por Eng. Ana".</summary>
    [ObservableProperty] public partial string Situacao { get; private set; } = "";

    /// <summary>"3 fotos neste dia".</summary>
    [ObservableProperty] public partial string FotosDoDia { get; private set; } = "";

    [ObservableProperty] public partial bool TemDia { get; private set; }

    /// <summary>O dia está aberto: o formulário edita.</summary>
    [ObservableProperty] public partial bool PodeEditar { get; private set; }

    [ObservableProperty] public partial bool Fechado { get; private set; }

    /// <summary>Manhã e tarde impraticáveis: dá para marcar o dia sem trabalho no calendário.</summary>
    public bool Impraticavel => TemDia && !ManhaPraticavel && !TardePraticavel;

    partial void OnSelecionadoChanged(DiaNaLista? oldValue, DiaNaLista? newValue)
    {
        if (_carregando)
            return;
        if (oldValue is not null && _sujo && !Salvar(oldValue.Data))
        {
            // O que estava no formulário não vale: volta para ele, com o motivo na mensagem.
            _carregando = true;
            Selecionado = oldValue;
            _carregando = false;
            return;
        }
        Carregar(newValue?.Data);
    }

    partial void OnTempoDaManhaChanged(Escolha<TempoNoPeriodo>? value) => Mudou();
    partial void OnTempoDaTardeChanged(Escolha<TempoNoPeriodo>? value) => Mudou();
    partial void OnOcorrenciasChanged(string value) => Mudou();

    partial void OnManhaPraticavelChanged(bool value)
    {
        Mudou();
        OnPropertyChanged(nameof(Impraticavel));
    }

    partial void OnTardePraticavelChanged(bool value)
    {
        Mudou();
        OnPropertyChanged(nameof(Impraticavel));
    }

    internal void Mudou()
    {
        if (!_carregando)
            _sujo = true;
    }

    internal void Remover(LinhaDoEfetivo linha) => MudarLista(() => Efetivo.Remove(linha));
    internal void Remover(LinhaDoEquipamento linha) => MudarLista(() => Equipamentos.Remove(linha));
    internal void Remover(LinhaDaAtividade linha) => MudarLista(() => Atividades.Remove(linha));

    private void MudarLista(Action mudanca)
    {
        if (!PodeEditar)
            return;
        mudanca();
        Mudou();
    }

    [RelayCommand]
    private void AdicionarEfetivo() => MudarLista(() => Efetivo.Add(new LinhaDoEfetivo(this)));

    [RelayCommand]
    private void AdicionarEquipamento() => MudarLista(() => Equipamentos.Add(new LinhaDoEquipamento(this)));

    [RelayCommand]
    private void AdicionarAtividade() => MudarLista(() => Atividades.Add(new LinhaDaAtividade(this, null)));

    // ---- Os dias ----

    /// <summary>Um dia novo na data escolhida, já preenchido pelo cronograma.</summary>
    [RelayCommand]
    private void NovoDia()
    {
        if (DataDoNovo is not { } escolhida)
        {
            Mensagem = "Escolha a data do dia novo.";
            return;
        }
        if (Selecionado is { } atual && _sujo && !Salvar(atual.Data))
            return;
        var data = DateOnly.FromDateTime(escolhida);
        try
        {
            _cronograma.Diario.Abrir(RegrasDoDiario.Sugerir(_cronograma, data));
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return;
        }
        _mudou($"Diário: novo dia {data:dd/MM/yyyy}");
        AtualizarLista();
        _carregando = true;
        Selecionado = Dias.First(d => d.Data == data);
        _carregando = false;
        Carregar(data);
        DataDoNovo = ProximaData().ToDateTime(TimeOnly.MinValue);
        Mensagem = $"RDO nº {_cronograma.Diario.NumeroDe(data):000} criado com o que o cronograma prevê para {data:dd/MM/yyyy}: ajuste o que mudou.";
    }

    [RelayCommand]
    private async Task ExcluirDia()
    {
        if (Selecionado is not { } dia)
            return;
        if (!await _dialogos.ConfirmarAsync("Excluir dia do diário",
                $"Excluir o RDO de {dia.Data:dd/MM/yyyy}? Dá para desfazer (Editar > Desfazer) depois de fechar o diário.", "Excluir"))
            return;
        try
        {
            _cronograma.Diario.Remover(dia.Data);
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return;
        }
        _sujo = false;
        _mudou($"Diário: excluir {dia.Data:dd/MM/yyyy}");
        AtualizarLista();
        _carregando = true;
        Selecionado = Dias.LastOrDefault();
        _carregando = false;
        Carregar(Selecionado?.Data);
        Mensagem = $"O dia {dia.Data:dd/MM/yyyy} saiu do diário.";
    }

    [RelayCommand]
    private void SalvarDia()
    {
        if (Selecionado is { } dia && Salvar(dia.Data, sempre: true))
            Mensagem = $"RDO de {dia.Data:dd/MM/yyyy} salvo.";
    }

    /// <summary>Salva o que está no formulário (ao trocar de dia, ao fechar a tela e no botão Salvar).</summary>
    /// <returns>Se deu (ou se não havia o que salvar).</returns>
    public bool SalvarSePreciso() => Selecionado is not { } dia || !_sujo || Salvar(dia.Data);

    /// <summary>Ao fechar a tela com um número que não vale: pergunta se fecha sem salvar as mudanças do dia.</summary>
    /// <returns>Se pode fechar (o dia fica como estava).</returns>
    public async Task<bool> FecharSemSalvarAsync()
    {
        if (!await _dialogos.ConfirmarAsync("Fechar o diário", $"{Mensagem} Fechar sem salvar as mudanças deste dia?", "Fechar sem salvar"))
            return false;
        _sujo = false;
        return true;
    }

    private bool Salvar(DateOnly data, bool sempre = false)
    {
        if (!_sujo && !sempre)
            return true;
        if (_cronograma.Diario.Dia(data) is not { Fechado: false } atual)
        {
            _sujo = false;
            return true;
        }
        if (Ler(atual) is not { } dia)
            return false;
        try
        {
            _cronograma.Diario.Salvar(dia);
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return false;
        }
        _sujo = false;
        _mudou($"Diário de {data:dd/MM/yyyy}");
        AtualizarLista();
        return true;
    }

    // O formulário como um dia; nulo (com o motivo na mensagem) se um número não vale.
    private DiaDaObra? Ler(DiaDaObra atual)
    {
        static int? Inteiro(string texto) => int.TryParse(texto.Trim(), NumberStyles.Integer, Brasil, out var n) ? n : null;
        var efetivo = new List<EfetivoNoDia>();
        foreach (var linha in Efetivo.Where(l => !string.IsNullOrWhiteSpace(l.Funcao)))
        {
            if (Inteiro(linha.Quantidade) is not { } n)
                return Recusar($"A quantidade de \"{linha.Funcao.Trim()}\" não é um número.");
            efetivo.Add(new EfetivoNoDia(linha.Funcao, n, linha.Empresa));
        }
        var equipamentos = new List<EquipamentoNoDia>();
        foreach (var linha in Equipamentos.Where(l => !string.IsNullOrWhiteSpace(l.Nome)))
        {
            if (Inteiro(linha.Quantidade) is not { } n)
                return Recusar($"A quantidade de \"{linha.Nome.Trim()}\" não é um número.");
            equipamentos.Add(new EquipamentoNoDia(linha.Nome, n));
        }
        var atividades = new List<AtividadeNoDia>();
        foreach (var linha in Atividades.Where(l => !string.IsNullOrWhiteSpace(l.Descricao)))
        {
            var texto = linha.Percentual.Trim().TrimEnd('%').Trim();
            int? percentual = null;
            if (texto.Length > 0)
            {
                if (Inteiro(texto) is not { } p)
                    return Recusar($"O % de \"{linha.Descricao.Trim()}\" não é um número.");
                percentual = p;
            }
            atividades.Add(new AtividadeNoDia(linha.TarefaId, linha.Descricao, percentual));
        }
        return atual with
        {
            Manha = new ClimaDoPeriodo(TempoDaManha?.Valor ?? TempoNoPeriodo.Bom, ManhaPraticavel),
            Tarde = new ClimaDoPeriodo(TempoDaTarde?.Valor ?? TempoNoPeriodo.Bom, TardePraticavel),
            Efetivo = efetivo,
            Equipamentos = equipamentos,
            Atividades = atividades,
            Ocorrencias = Ocorrencias,
        };
    }

    private DiaDaObra? Recusar(string motivo)
    {
        Mensagem = motivo;
        return null;
    }

    /// <summary>Repõe no dia o que o cronograma prevê (os serviços, o efetivo e os equipamentos); o clima e as ocorrências ficam.</summary>
    [RelayCommand]
    private void PreencherPeloCronograma()
    {
        if (Selecionado is not { } dia || !PodeEditar)
            return;
        var sugerido = RegrasDoDiario.Sugerir(_cronograma, dia.Data);
        _carregando = true;
        Preencher(sugerido, soAsListas: true);
        _carregando = false;
        _sujo = true;
        Mensagem = $"Serviços, efetivo e equipamentos de {dia.Data:dd/MM/yyyy} pelo cronograma: {sugerido.Atividades.Count} serviço(s), " +
                   $"{sugerido.TotalDoEfetivo} pessoa(s). Salve para gravar.";
    }

    /// <summary>Fecha (assina) o dia: salvo, ele não muda mais, a não ser reaberto.</summary>
    [RelayCommand]
    private void FecharDia()
    {
        if (Selecionado is not { } dia || !Salvar(dia.Data))
            return;
        var responsavel = _cabecalho().Responsavel;
        try
        {
            _cronograma.Diario.Fechar(dia.Data, Agora, responsavel);
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return;
        }
        _mudou($"Diário: fechar {dia.Data:dd/MM/yyyy}");
        AtualizarLista();
        Carregar(dia.Data);
        Mensagem = $"RDO de {dia.Data:dd/MM/yyyy} fechado{(responsavel is null ? "" : $" por {responsavel}")}: para mudar, reabra.";
    }

    [RelayCommand]
    private void ReabrirDia()
    {
        if (Selecionado is not { } dia)
            return;
        try
        {
            _cronograma.Diario.Reabrir(dia.Data);
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return;
        }
        _mudou($"Diário: reabrir {dia.Data:dd/MM/yyyy}");
        AtualizarLista();
        Carregar(dia.Data);
        Mensagem = $"RDO de {dia.Data:dd/MM/yyyy} reaberto.";
    }

    /// <summary>Leva o % dos serviços que são tarefas ao cronograma (um passo do Desfazer).</summary>
    [RelayCommand]
    private void LevarOPercentual()
    {
        if (Selecionado is not { } dia || !SalvarSePreciso())
            return;
        IReadOnlyList<string> mudaram;
        try
        {
            mudaram = RegrasDoDiario.LevarOPercentual(_cronograma, dia.Data);
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return;
        }
        if (mudaram.Count == 0)
        {
            Mensagem = "O cronograma já está com o % dos serviços deste dia.";
            return;
        }
        _mudou($"Diário: % de {dia.Data:dd/MM/yyyy} no cronograma");
        Mensagem = $"No cronograma: {string.Join("; ", mudaram)}.";
    }

    /// <summary>Um dia impraticável vira um dia sem trabalho no calendário (o cronograma anda um dia útil).</summary>
    [RelayCommand]
    private void MarcarSemTrabalho()
    {
        if (Selecionado is not { } dia || !SalvarSePreciso())
            return;
        try
        {
            var copia = _cronograma.Copiar(); // primeiro numa cópia: se o cronograma não fecha com o dia parado, nada muda
            RegrasDoDiario.MarcarSemTrabalho(copia, dia.Data);
            CalculadoraCronograma.Calcular(copia);
            RegrasDoDiario.MarcarSemTrabalho(_cronograma, dia.Data);
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return;
        }
        _mudou($"Diário: {dia.Data:dd/MM/yyyy} sem trabalho no calendário");
        Mensagem = $"{dia.Data:dd/MM/yyyy} ficou sem trabalho no calendário: as tarefas andaram um dia útil.";
    }

    // ---- O PDF ----

    [RelayCommand]
    private Task PdfDoDia() => Selecionado is { } dia ? Pdf([dia.Data], $"RDO {_cronograma.Diario.NumeroDe(dia.Data):000} - {dia.Data:yyyy-MM-dd}") : Task.CompletedTask;

    /// <summary>Os dias do mês do dia escolhido, num PDF só.</summary>
    [RelayCommand]
    private Task PdfDoMes()
    {
        if (Selecionado is not { } dia)
            return Task.CompletedTask;
        var doMes = _cronograma.Diario.Dias.Where(d => d.Data.Year == dia.Data.Year && d.Data.Month == dia.Data.Month).Select(d => d.Data).ToList();
        return Pdf(doMes, $"Diário de obra - {dia.Data:yyyy-MM}");
    }

    private async Task Pdf(IReadOnlyList<DateOnly> datas, string nome)
    {
        if (!SalvarSePreciso())
            return;
        if (await _dialogos.SalvarComoAsync("Salvar o diário de obra em PDF", Limpo(nome), "pdf") is not { } caminho)
            return;
        var fotosDosDias = datas.ToDictionary(d => d, FotosDe);
        var miniaturas = _miniaturas(fotosDosDias.Values.SelectMany(f => f.Take(GeradorDiarioPdf.FotosPorDia)).Select(f => f.Foto.Id));
        var dias = datas.Select(d => new DiaNoPdf(_cronograma.Diario.Dia(d)!, _cronograma.Diario.NumeroDe(d),
            [.. fotosDosDias[d].Where(f => miniaturas.ContainsKey(f.Foto.Id)).Select(f => new FotoDoDiaNoPdf(miniaturas[f.Foto.Id], f.Legenda))])).ToList();
        var termino = _cronograma.Tarefas.Select(t => t.TerminoMaisCedo ?? t.DataTermino).DefaultIfEmpty(_cronograma.DataInicio).Max();
        try
        {
            await using var destino = File.Create(caminho);
            new GeradorDiarioPdf().Gerar(dias, _cabecalho(), new PrazoNoDiario(DateOnly.FromDateTime(_cronograma.DataInicio), DateOnly.FromDateTime(termino)),
                Agora.ToString("dd/MM/yyyy HH:mm", Brasil), destino);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Mensagem = $"Não foi possível salvar o PDF: {ex.Message}";
            return;
        }
        Mensagem = $"Salvo em {Path.GetFileName(caminho)} ({(dias.Count == 1 ? "1 dia" : $"{dias.Count} dias")}).";
    }

    private static string Limpo(string nome) => string.Concat(nome.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    // As fotos tiradas no dia, em ordem de hora, com a legenda "09:15 · 2.1 Alvenaria".
    private List<(FotoTarefa Foto, string Legenda)> FotosDe(DateOnly data)
    {
        var edt = _cronograma.NumeracaoEdt();
        return [.. _cronograma.Tarefas.SelectMany(t => t.Fotos.Where(f => DateOnly.FromDateTime(f.DataFoto) == data)
                .Select(f => (Foto: f, Legenda: $"{f.DataFoto:HH:mm} · {edt[t.Id]} {t.Nome}")))
            .OrderBy(x => x.Foto.DataFoto)];
    }

    // ---- Carregar e listar ----

    private void AtualizarLista()
    {
        var escolhido = Selecionado?.Data;
        var lista = _cronograma.Diario.Dias.Select(d => new DiaNaLista(d.Data,
            $"RDO {_cronograma.Diario.NumeroDe(d.Data):000} · {d.Data.ToString("dd/MM/yyyy", Brasil)} · {CalendarioDoProjeto.NomeDoDia(d.Data.DayOfWeek)[..3]}",
            Resumo(d), d.Fechado)).ToList();
        _carregando = true;
        try
        {
            Dias.Clear();
            foreach (var d in lista)
                Dias.Add(d);
            Selecionado = escolhido is { } e ? Dias.FirstOrDefault(d => d.Data == e) : null;
        }
        finally
        {
            _carregando = false;
        }
        OnPropertyChanged(nameof(SemDias));
    }

    private static string Resumo(DiaDaObra d)
    {
        var partes = new List<string> { d.TotalDoEfetivo == 1 ? "1 pessoa" : $"{d.TotalDoEfetivo} pessoas" };
        if (d.Impraticavel)
            partes.Add("impraticável");
        else if (d.Manha.Tempo == TempoNoPeriodo.Chuva || d.Tarde.Tempo == TempoNoPeriodo.Chuva)
            partes.Add(d.Manha.Tempo == TempoNoPeriodo.Chuva && d.Tarde.Tempo == TempoNoPeriodo.Chuva ? "chuva" : d.Tarde.Tempo == TempoNoPeriodo.Chuva ? "chuva à tarde" : "chuva de manhã");
        partes.Add(d.Fechado ? "fechado" : "aberto");
        return string.Join(" · ", partes);
    }

    private void Carregar(DateOnly? data)
    {
        _carregando = true;
        try
        {
            var dia = data is { } d ? _cronograma.Diario.Dia(d) : null;
            TemDia = dia is not null;
            Fechado = dia?.Fechado ?? false;
            PodeEditar = dia is { Fechado: false };
            Preencher(dia ?? new DiaDaObra(Hoje), soAsListas: false);
            Cabecalho = dia is null ? "" : $"RDO nº {_cronograma.Diario.NumeroDe(dia.Data):000} · {dia.Data.ToString("dd/MM/yyyy", Brasil)} · {DiaDaSemana(dia.Data)}";
            Situacao = dia?.Fechamento is { } f
                ? $"Fechado em {f.Quando.ToString("dd/MM/yyyy HH:mm", Brasil)}{(f.Responsavel is { } r ? $" por {r}" : "")}: para mudar, reabra."
                : dia is null ? "" : "Aberto: as mudanças são salvas ao trocar de dia e ao fechar a tela.";
            var fotos = dia is null ? 0 : FotosDe(dia.Data).Count;
            FotosDoDia = fotos switch { 0 => "Nenhuma foto neste dia.", 1 => "1 foto neste dia (vai no PDF).", _ => $"{fotos} fotos neste dia (até {GeradorDiarioPdf.FotosPorDia} vão no PDF)." };
        }
        finally
        {
            _carregando = false;
        }
        _sujo = false;
        OnPropertyChanged(nameof(Impraticavel));
    }

    // "segunda-feira", "sábado" (sem depender dos nomes do sistema).
    private static string DiaDaSemana(DateOnly data) =>
        CalendarioDoProjeto.NomeDoDia(data.DayOfWeek) + (data.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "" : "-feira");

    private void Preencher(DiaDaObra dia, bool soAsListas)
    {
        if (!soAsListas)
        {
            TempoDaManha = Tempos.First(t => t.Valor == dia.Manha.Tempo);
            ManhaPraticavel = dia.Manha.Praticavel;
            TempoDaTarde = Tempos.First(t => t.Valor == dia.Tarde.Tempo);
            TardePraticavel = dia.Tarde.Praticavel;
            Ocorrencias = dia.Ocorrencias ?? "";
        }
        Efetivo.Clear();
        foreach (var e in dia.Efetivo)
            Efetivo.Add(new LinhaDoEfetivo(this) { Funcao = e.Funcao, Quantidade = e.Quantidade.ToString(Brasil), Empresa = e.Empresa ?? "" });
        Equipamentos.Clear();
        foreach (var e in dia.Equipamentos)
            Equipamentos.Add(new LinhaDoEquipamento(this) { Nome = e.Nome, Quantidade = e.Quantidade.ToString(Brasil) });
        Atividades.Clear();
        foreach (var a in dia.Atividades)
            Atividades.Add(new LinhaDaAtividade(this, a.TarefaId) { Descricao = a.Descricao, Percentual = a.Percentual?.ToString(Brasil) ?? "" });
    }

    // Hoje, se ainda não está no diário; senão, o dia depois do último.
    private DateOnly ProximaData() =>
        _cronograma.Diario.Dia(Hoje) is null && (_cronograma.Diario.UltimoFechado is not { } f || f.Data <= Hoje)
            ? Hoje
            : (_cronograma.Diario.Dias.LastOrDefault()?.Data ?? Hoje).AddDays(1);
}
