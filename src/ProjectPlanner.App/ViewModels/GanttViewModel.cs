using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Models;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Gantt;

namespace ProjectPlanner.App.ViewModels;

/// <summary>Quem cria os vínculos pedidos por arraste no Gantt: a janela principal, dona do cronograma.</summary>
public interface ILigacoesNoGantt
{
    /// <summary>Por que o vínculo não pode ser criado, ou nulo se pode. Não muda nada.</summary>
    string? MotivoParaNaoLigar(Guid predecessoraId, Guid sucessoraId, TipoDependencia tipo);

    /// <summary>Cria o vínculo, recalcula e avisa na barra de status (também quando recusa).</summary>
    /// <returns>Se criou o vínculo.</returns>
    bool Ligar(Guid predecessoraId, Guid sucessoraId, TipoDependencia tipo);
}

/// <summary>Como o destino sob o mouse aparece durante o arraste de um vínculo.</summary>
public enum EstadoDaLigacao
{
    /// <summary>A própria tarefa de origem: soltar ali só cancela.</summary>
    Neutra,
    Valida,
    Invalida,
}

/// <summary>O que acontece se o arraste for solto no destino atual: o estado e o texto do rótulo junto ao cursor.</summary>
public sealed record PrevisaoDeLigacao(EstadoDaLigacao Estado, string Texto);

/// <summary>
/// O Gantt ao lado da grid: guarda o último cronograma calculado e o layout dele na escala atual.
/// O layout é refeito quando o cronograma é recalculado, quando o zoom muda e quando a área visível
/// muda de tamanho (sem recalcular o CPM); a View só desenha o que está em <see cref="Layout"/>.
/// </summary>
public sealed partial class GanttViewModel : ViewModelBase
{
    public const double EscalaMinima = 4;
    public const double EscalaMaxima = 60;
    public const double EscalaInicial = OpcoesGantt.EscalaPadrao;

    /// <summary>Quanto os botões − e + do zoom mudam a escala, em px/dia.</summary>
    public const double PassoDoZoom = 5;

    /// <summary>Altura de cada linha; a grid usa a mesma, para que a linha N e a barra N fiquem lado a lado.</summary>
    public const double AlturaLinha = 30;

    /// <summary>
    /// O RowHeight a configurar no DataGrid: ele soma à altura da linha 1 px de linha de grade horizontal
    /// (espessura interna dele), então a linha ocupa exatamente <see cref="AlturaLinha"/>.
    /// </summary>
    public const double AlturaLinhaDaGrade = AlturaLinha - 1;

    /// <summary>Altura do cabeçalho de tempo; a grid usa a mesma no cabeçalho das colunas.</summary>
    public const double AlturaCabecalho = 48;

    private Cronograma? _cronograma;
    private ResultadoCronograma? _resultado;
    private IReadOnlySet<Guid> _ocultas = new HashSet<Guid>();
    private double _larguraVisivel;
    private double _alturaVisivel;

    /// <summary>Zoom: largura de um dia, entre <see cref="EscalaMinima"/> e <see cref="EscalaMaxima"/>.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AumentarZoomCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiminuirZoomCommand))]
    public partial double PixelsPorDia { get; set; } = EscalaInicial;

    /// <summary>Botão + do zoom: <see cref="PassoDoZoom"/> px/dia a mais, até <see cref="EscalaMaxima"/>.</summary>
    [RelayCommand(CanExecute = nameof(PodeAumentarZoom))]
    private void AumentarZoom() => PixelsPorDia = Math.Min(EscalaMaxima, PixelsPorDia + PassoDoZoom);

    /// <summary>Botão − do zoom: <see cref="PassoDoZoom"/> px/dia a menos, até <see cref="EscalaMinima"/>.</summary>
    [RelayCommand(CanExecute = nameof(PodeDiminuirZoom))]
    private void DiminuirZoom() => PixelsPorDia = Math.Max(EscalaMinima, PixelsPorDia - PassoDoZoom);

    private bool PodeAumentarZoom() => PixelsPorDia < EscalaMaxima;
    private bool PodeDiminuirZoom() => PixelsPorDia > EscalaMinima;

    /// <summary>
    /// Opção de exibição (só da sessão, desligada por padrão desde o Bloco 16): a linha cinza da folga
    /// total, logo abaixo da barra, até onde a tarefa pode atrasar sem atrasar o projeto.
    /// </summary>
    [ObservableProperty]
    public partial bool MostrarFolga { get; set; }

    /// <summary>
    /// Opção de exibição (só da sessão, ligada por padrão; Bloco 25, C1): a linha tracejada no dia de <see cref="Hoje"/>.
    /// </summary>
    [ObservableProperty]
    public partial bool MostrarHoje { get; set; } = true;

    partial void OnMostrarHojeChanged(bool value) => RefazerLayout();

    /// <summary>O dia de hoje para a linha "Hoje", repassado pela janela principal a cada recálculo.</summary>
    public DateTime? Hoje { get; set; }

    /// <summary>
    /// Opção de exibição (só da sessão): falso pinta as tarefas e setas críticas na cor comum. Não
    /// muda o cálculo; <see cref="BarraGantt.EhCritica"/> continua como o CPM calculou.
    /// </summary>
    [ObservableProperty]
    public partial bool MostrarCaminhoCritico { get; set; } = true;

    /// <summary>
    /// A tarefa cuja barra aparece destacada: a selecionada na grid, repassada pela janela principal
    /// (nula quando não há seleção ou quando o destaque de seleção está desligado).
    /// </summary>
    [ObservableProperty]
    public partial Guid? TarefaDestacada { get; set; }

    /// <summary>A dica rica de uma barra (Bloco 25, V4), dada pela janela principal; sem ela, a dica é só o nome.</summary>
    public Func<Guid, string>? DicaDaTarefa { get; set; }

    /// <summary>O usuário clicou na barra de uma tarefa; a janela principal a seleciona.</summary>
    public event Action<Guid>? BarraClicada;

    /// <summary>Pedido de rolagem horizontal até o X dado (Bloco 25: ir para a tarefa, ir para hoje, ajustar).</summary>
    public event Action<double>? RolagemPedida;

    /// <summary>Rola o Gantt até a barra da tarefa; falso se ela não está desenhada (oculta ou sem cálculo).</summary>
    public bool MostrarTarefa(Guid tarefaId)
    {
        if (Layout?.Barras.FirstOrDefault(b => b.TarefaId == tarefaId) is not { } barra)
            return false;
        RolagemPedida?.Invoke(barra.X);
        return true;
    }

    /// <summary>Dias a mais depois do fim do projeto ao ajustar o zoom, para a última barra não encostar na borda.</summary>
    public const int FolgaAoAjustar = 2;

    /// <summary>Botão do zoom (Bloco 25, P8): a escala em que o projeto inteiro cabe na largura visível.</summary>
    [RelayCommand(CanExecute = nameof(PodeAjustarAoProjeto))]
    private void AjustarAoProjeto()
    {
        var dias = (_resultado!.DataTermino.Date - _cronograma!.DataInicio.Date).Days + 1
                   + CalculadoraLayoutGantt.DiasAntesDoInicio + FolgaAoAjustar;
        PixelsPorDia = Math.Clamp(Math.Floor(_larguraVisivel / dias * 10) / 10, EscalaMinima, EscalaMaxima);
        RolagemPedida?.Invoke(0);
    }

    private bool PodeAjustarAoProjeto() => _cronograma is { Tarefas.Count: > 0 } && _resultado is not null && _larguraVisivel > 0;

    /// <summary>Botão do zoom (Bloco 25, P8): rola o Gantt até o dia de hoje (com a linha ligada ou não).</summary>
    [RelayCommand(CanExecute = nameof(PodeIrParaHoje))]
    private void IrParaHoje() => RolagemPedida?.Invoke((Hoje!.Value.Date - Layout!.InicioEixo).Days * PixelsPorDia);

    private bool PodeIrParaHoje() => Hoje is { } hoje && Layout is { } layout && hoje.Date >= layout.InicioEixo && hoje.Date <= layout.FimEixo;

    partial void OnLayoutChanged(LayoutGantt? value)
    {
        AjustarAoProjetoCommand.NotifyCanExecuteChanged();
        IrParaHojeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Opção de exibição (só da sessão): mostra o ícone de câmera ao lado das barras das tarefas que têm
    /// fotos. Desligada, o ícone some e não dá para abrir a galeria pelo Gantt.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TarefasComIconeDeFotos))]
    public partial bool MostrarFotos { get; set; } = true;

    /// <summary>As tarefas visíveis que exibem o ícone de fotos agora: as que têm fotos, se a opção estiver ligada.</summary>
    public IReadOnlyList<Guid> TarefasComIconeDeFotos =>
        Layout?.Barras.Where(MostraIconeDeFotos).Select(b => b.TarefaId).ToList() ?? [];

    public bool MostraIconeDeFotos(BarraGantt barra) => MostrarFotos && barra.QuantidadeDeFotos > 0;

    /// <summary>O usuário clicou no ícone de fotos de uma tarefa; a janela principal abre a galeria.</summary>
    public event Action<Guid>? FotosPedidas;

    public void AbrirFotos(Guid tarefaId) => FotosPedidas?.Invoke(tarefaId);

    /// <summary>Quem cria os vínculos arrastados de uma barra para outra; sem ele, o arraste não cria nada.</summary>
    public ILigacoesNoGantt? Ligacoes { get; set; }

    /// <summary>O que desenhar; nulo quando não há cronograma calculado.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TarefasComIconeDeFotos))]
    public partial LayoutGantt? Layout { get; private set; }

    /// <summary>Se a barra vai na cor de crítica: crítica no cálculo e com o destaque ligado.</summary>
    public bool PintarComoCritica(BarraGantt barra) => MostrarCaminhoCritico && barra.EhCritica;

    /// <summary>Se a seta vai na cor de crítica: parte de um caminho crítico e com o destaque ligado.</summary>
    public bool PintarComoCritica(SetaGantt seta) => MostrarCaminhoCritico && seta.EhCritica;

    public bool EstaDestacada(BarraGantt barra) => TarefaDestacada == barra.TarefaId;

    public void ClicarNaBarra(Guid tarefaId) => BarraClicada?.Invoke(tarefaId);

    /// <summary>
    /// A prévia do arraste de vínculo, enquanto o mouse passa sobre as barras: o que aconteceria ao
    /// soltar no destino, com o tipo inferido das bordas (ver <see cref="InferenciaDependencia"/>). Nulo
    /// fora de qualquer barra. Não muda nada.
    /// </summary>
    public PrevisaoDeLigacao? PreverLigacao(Guid origemId, BordaDaBarra bordaDeOrigem, Guid? destinoId,
        BordaDaBarra bordaDeDestino)
    {
        if (destinoId is not { } destino)
            return null;
        if (destino == origemId)
            return new PrevisaoDeLigacao(EstadoDaLigacao.Neutra, "Solte sobre outra tarefa");

        var tipo = InferenciaDependencia.Tipo(bordaDeOrigem, bordaDeDestino);
        return Ligacoes?.MotivoParaNaoLigar(origemId, destino, tipo) is { } motivo
            ? new PrevisaoDeLigacao(EstadoDaLigacao.Invalida, motivo)
            : new PrevisaoDeLigacao(EstadoDaLigacao.Valida,
                $"{NomeDaBorda(bordaDeOrigem)} de \"{NomeDa(origemId)}\" → {NomeDaBorda(bordaDeDestino)} de " +
                $"\"{NomeDa(destino)}\" ({NotacaoPredecessoras.Sigla(tipo)})");
    }

    /// <summary>
    /// Solta o arraste: cria o vínculo da origem para o destino, com o tipo inferido das bordas. Soltar
    /// fora de qualquer barra ou na própria tarefa de origem cancela, sem mensagem.
    /// </summary>
    /// <returns>Se criou o vínculo.</returns>
    public bool SoltarLigacao(Guid origemId, BordaDaBarra bordaDeOrigem, Guid? destinoId, BordaDaBarra bordaDeDestino) =>
        destinoId is { } destino && destino != origemId && Ligacoes is not null
        && Ligacoes.Ligar(origemId, destino, InferenciaDependencia.Tipo(bordaDeOrigem, bordaDeDestino));

    private static string NomeDaBorda(BordaDaBarra borda) => borda == BordaDaBarra.Inicio ? "Início" : "Término";

    private string NomeDa(Guid tarefaId) => _cronograma?.Tarefas.FirstOrDefault(t => t.Id == tarefaId)?.Nome ?? "";

    /// <summary>
    /// Passa a mostrar um cronograma recém-calculado, sem as tarefas <paramref name="ocultas"/> (as de
    /// resumos recolhidos na grid; nulo mantém as de antes).
    /// </summary>
    public void Atualizar(Cronograma cronograma, ResultadoCronograma resultado, IReadOnlySet<Guid>? ocultas = null)
    {
        _cronograma = cronograma;
        _resultado = resultado;
        if (ocultas is not null)
            _ocultas = ocultas;
        RefazerLayout();
    }

    /// <summary>
    /// As tarefas que a grid escondeu (dentro de resumos recolhidos): somem daqui também, para que as
    /// linhas continuem lado a lado. Não recalcula nada.
    /// </summary>
    public void DefinirTarefasOcultas(IReadOnlySet<Guid> ocultas)
    {
        if (_ocultas.SetEquals(ocultas))
            return;
        _ocultas = ocultas;
        RefazerLayout();
    }

    /// <summary>
    /// As tarefas mudaram de ordem sem mudar de valores (arraste na grid, Bloco 18): refaz o desenho com o
    /// mesmo resultado do cálculo, só com as linhas na nova ordem.
    /// </summary>
    public void RedesenharNaNovaOrdem() => RefazerLayout();

    /// <summary>O cálculo falhou: não há o que desenhar.</summary>
    public void Limpar()
    {
        _cronograma = null;
        _resultado = null;
        Layout = null;
    }

    /// <summary>
    /// A área onde o Gantt aparece mudou de tamanho (janela redimensionada, divisor arrastado): o eixo de
    /// dias e a grade passam a preenchê-la.
    /// </summary>
    public void AtualizarAreaVisivel(double largura, double altura)
    {
        largura = double.IsFinite(largura) ? Math.Max(0, largura) : 0;
        altura = double.IsFinite(altura) ? Math.Max(0, altura) : 0;
        if (largura == _larguraVisivel && altura == _alturaVisivel)
            return;
        _larguraVisivel = largura;
        _alturaVisivel = altura;
        RefazerLayout();
    }

    partial void OnPixelsPorDiaChanged(double value)
    {
        var limitado = double.IsNaN(value) ? EscalaInicial : Math.Clamp(value, EscalaMinima, EscalaMaxima);
        if (limitado != value)
        {
            PixelsPorDia = limitado;
            return;
        }
        RefazerLayout();
    }

    private void RefazerLayout() =>
        Layout = _cronograma is null || _resultado is null
            ? null
            : CalculadoraLayoutGantt.Calcular(_cronograma, _resultado, new OpcoesGantt(PixelsPorDia, AlturaLinha)
            {
                LarguraVisivel = _larguraVisivel,
                AlturaVisivel = _alturaVisivel,
                TarefasOcultas = _ocultas,
                Hoje = MostrarHoje ? Hoje : null,
            });
}
