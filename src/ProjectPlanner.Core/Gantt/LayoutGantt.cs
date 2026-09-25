using System.Globalization;

namespace ProjectPlanner.Core.Gantt;

/// <summary>Parâmetros de desenho do Gantt, em pixels (ou na unidade que a tela usar).</summary>
public sealed record OpcoesGantt
{
    /// <summary>Escala usada quando nenhuma é informada: 30 px por dia.</summary>
    public const double EscalaPadrao = 30;

    public OpcoesGantt(double pixelsPorDia = EscalaPadrao, double alturaLinha = 30)
    {
        if (!(pixelsPorDia > 0))
            throw new ArgumentOutOfRangeException(nameof(pixelsPorDia), pixelsPorDia, "A escala precisa ser maior que zero.");
        if (!(alturaLinha > 0))
            throw new ArgumentOutOfRangeException(nameof(alturaLinha), alturaLinha, "A altura da linha precisa ser maior que zero.");

        PixelsPorDia = pixelsPorDia;
        AlturaLinha = alturaLinha;
    }

    /// <summary>Largura de um dia corrido no eixo de tempo.</summary>
    public double PixelsPorDia { get; }

    /// <summary>Altura de cada linha; deve ser igual à altura das linhas da grid, para que fiquem alinhadas.</summary>
    public double AlturaLinha { get; }

    /// <summary>Altura das barras, centralizadas na linha.</summary>
    public double AlturaBarra => AlturaLinha / 2;

    /// <summary>Quanto a seta anda na horizontal ao sair de uma barra e antes de chegar na outra.</summary>
    public double AfastamentoSeta { get; init; } = 8;

    /// <summary>Comprimento da ponta da seta.</summary>
    public double TamanhoPontaSeta { get; init; } = 6;

    /// <summary>
    /// Largura da área visível do Gantt. O eixo se estende até preenchê-la, mesmo sem tarefas para
    /// tanto; 0 (o padrão) não estende nada.
    /// </summary>
    public double LarguraVisivel
    {
        get;
        init => field = value >= 0 ? value
            : throw new ArgumentOutOfRangeException(nameof(LarguraVisivel), value, "A largura visível não pode ser negativa.");
    }

    /// <summary>
    /// Altura da área visível do Gantt. A <see cref="LayoutGantt.Altura"/> é pelo menos ela, para a
    /// grade de dias ir até embaixo mesmo com poucas linhas; 0 (o padrão) não estende nada.
    /// </summary>
    public double AlturaVisivel
    {
        get;
        init => field = value >= 0 ? value
            : throw new ArgumentOutOfRangeException(nameof(AlturaVisivel), value, "A altura visível não pode ser negativa.");
    }

    /// <summary>Cultura dos rótulos do cabeçalho (nomes de meses, formato de datas).</summary>
    public CultureInfo Cultura { get; init; } = CultureInfo.CurrentCulture;
}

/// <summary>
/// Tudo o que a tela precisa para desenhar o Gantt, já posicionado. X cresce para a direita a partir de
/// <see cref="InicioEixo"/>; Y cresce para baixo a partir do topo da primeira linha.
/// </summary>
/// <param name="InicioEixo">Dia desenhado em X = 0.</param>
/// <param name="FimEixo">Último dia desenhado (inclusivo).</param>
/// <param name="Largura">Largura total do eixo, de <see cref="InicioEixo"/> ao fim de <see cref="FimEixo"/>.</param>
/// <param name="Altura">Altura total das linhas, ou a altura visível, se ela for maior.</param>
/// <param name="Barras">Uma por tarefa, na ordem do cronograma (a mesma da grid).</param>
/// <param name="Setas">Uma por vínculo, na ordem do cronograma.</param>
/// <param name="FinsDeSemana">Uma coluna por sábado e domingo do eixo, em ordem, para o fundo diferenciado.</param>
public sealed record LayoutGantt(
    DateTime InicioEixo,
    DateTime FimEixo,
    double Largura,
    double Altura,
    IReadOnlyList<BarraGantt> Barras,
    IReadOnlyList<SetaGantt> Setas,
    EscalaGantt Escala,
    IReadOnlyList<ColunaDia> FinsDeSemana);

/// <summary>A faixa vertical de um dia no eixo.</summary>
public sealed record ColunaDia(DateTime Dia, double X, double Largura);

/// <summary>A barra de uma tarefa, do começo do Início cedo ao fim do Término cedo.</summary>
/// <param name="Linha">Linha da tarefa (0 = primeira), a mesma da grid.</param>
/// <param name="LarguraConcluida">Parte da barra que corresponde ao percentual concluído, a partir de <see cref="X"/>.</param>
/// <param name="FimDaFolga">X do fim do Término tarde, quando a tarefa tem folga; a linha de folga vai de <see cref="Direita"/> até aqui.</param>
public sealed record BarraGantt(
    Guid TarefaId,
    string Nome,
    int Linha,
    double X,
    double Y,
    double Largura,
    double Altura,
    double LarguraConcluida,
    double? FimDaFolga,
    bool EhCritica)
{
    public double Direita => X + Largura;

    public double CentroY => Y + Altura / 2;
}

public readonly record struct PontoGantt(double X, double Y);

/// <summary>
/// A linha de um vínculo, em segmentos só horizontais e verticais (como no MS Project), e a ponta
/// triangular no ponto de chegada.
/// </summary>
/// <param name="Pontos">Da saída, na barra predecessora, até a chegada, na sucessora.</param>
/// <param name="Ponta">Os três vértices da ponta; o primeiro é a própria chegada.</param>
/// <param name="EhCritica">Se o vínculo faz parte de um caminho crítico (liga duas críticas e determina a sucessora).</param>
public sealed record SetaGantt(
    Guid OrigemId,
    Guid DestinoId,
    TipoDependencia Tipo,
    IReadOnlyList<PontoGantt> Pontos,
    IReadOnlyList<PontoGantt> Ponta,
    bool EhCritica)
{
    public PontoGantt Saida => Pontos[0];

    public PontoGantt Chegada => Pontos[^1];
}

public enum UnidadeEscala
{
    Dia,
    Semana,
    Mes,
    Ano,
}

/// <summary>Um período do cabeçalho de tempo (um dia, uma semana, um mês ou um ano), cortado nos limites do eixo.</summary>
/// <param name="Inicio">Primeiro dia do período (mesmo que fique antes do início do eixo).</param>
public sealed record MarcaEscala(DateTime Inicio, double X, double Largura, string Rotulo);

/// <summary>O cabeçalho de tempo em duas faixas: a superior agrupa a inferior.</summary>
public sealed record EscalaGantt(
    UnidadeEscala UnidadeSuperior,
    IReadOnlyList<MarcaEscala> Superior,
    UnidadeEscala UnidadeInferior,
    IReadOnlyList<MarcaEscala> Inferior);
