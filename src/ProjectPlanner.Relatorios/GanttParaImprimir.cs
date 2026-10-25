using System.Globalization;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Gantt;
using SkiaSharp;

namespace ProjectPlanner.Relatorios;

/// <summary>Bloco 57: o tamanho da folha do Gantt impresso.</summary>
public enum TamanhoDaFolha
{
    A4,
    A3,
    A2,
    A1,
    A0,
    Carta,
    Oficio,
}

/// <summary>
/// Bloco 57: a escala do tempo no Gantt impresso. <see cref="AjustarALargura"/> põe a obra inteira na largura da folha; as
/// outras têm uma largura fixa por dia e, se a obra não cabe, dividem o tempo em várias folhas (para emendar lado a lado).
/// </summary>
public enum EscalaDaImpressao
{
    AjustarALargura,
    Dias,
    Semanas,
    Meses,
}

/// <summary>Bloco 57: as colunas da tabela à esquerda do Gantt impresso.</summary>
public enum ColunaDoGanttImpresso
{
    Edt,
    Nome,
    Duracao,
    Inicio,
    Termino,
    Concluido,
    Predecessoras,
}

/// <summary>Bloco 57: o que entra no Gantt impresso e como.</summary>
public sealed record OpcoesDaImpressaoDoGantt
{
    public TamanhoDaFolha Folha { get; init; } = TamanhoDaFolha.A3;

    public bool Paisagem { get; init; } = true;

    public EscalaDaImpressao Escala { get; init; } = EscalaDaImpressao.AjustarALargura;

    public IReadOnlySet<ColunaDoGanttImpresso> Colunas { get; init; } = new HashSet<ColunaDoGanttImpresso>
    {
        ColunaDoGanttImpresso.Edt, ColunaDoGanttImpresso.Nome, ColunaDoGanttImpresso.Duracao, ColunaDoGanttImpresso.Inicio,
        ColunaDoGanttImpresso.Termino,
    };

    public bool CaminhoCritico { get; init; } = true;

    public bool LinhaDeBase { get; init; } = true;

    public bool Vinculos { get; init; } = true;

    public bool Hoje { get; init; } = true;

    public bool NomesNasBarras { get; init; } = true;

    public bool FinsDeSemana { get; init; } = true;

    public bool Legenda { get; init; } = true;

    /// <summary>Até que nível da EDT entra (1 = só as etapas do primeiro nível); nulo põe todas.</summary>
    public int? NivelMaximo { get; init; }

    /// <summary>Tarefas que ficam de fora (as dos resumos recolhidos na tela, quando a pessoa escolhe "como na tela").</summary>
    public IReadOnlySet<Guid> TarefasOcultas { get; init; } = new HashSet<Guid>();

    /// <summary>O texto das predecessoras de cada tarefa ("2;3II+2"), na notação da grid.</summary>
    public IReadOnlyDictionary<Guid, string> Predecessoras { get; init; } = new Dictionary<Guid, string>();

    /// <summary>O nome da obra.</summary>
    public string Titulo { get; init; } = "";

    /// <summary>O que vai depois das datas no cabeçalho ("gerado em 25/10/2026 15:00").</summary>
    public string Subtitulo { get; init; } = "";
}

/// <summary>
/// Bloco 57: o Gantt pronto para imprimir: as páginas (a tabela à esquerda, na primeira folha de cada fileira, e as barras à direita, com o
/// cabeçalho do tempo), a legenda, o cabeçalho com a obra e as datas e o rodapé com o número da página. As posições vêm do
/// mesmo <see cref="CalculadoraLayoutGantt"/> da tela, em pontos (1/72 de polegada). Gera o PDF e a prévia de cada página.
/// </summary>
public sealed class GanttParaImprimir
{
    /// <summary>Pontos por dia nas escalas fixas (o cabeçalho do tempo segue as escalas da tela).</summary>
    public const float PontosPorDiaNosDias = 18f;
    public const float PontosPorDiaNasSemanas = 6f;
    public const float PontosPorDiaNosMeses = 2f;

    private const float Margem = 28f;
    private const float AlturaDaLinha = 16f;
    private const float AlturaDoTempo = 26f;
    private const float AlturaDoCabecalho = 52f;
    private const float AlturaDaLegenda = 22f;
    private const float LarguraMinimaDasBarras = 120f;

    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly SKColor Texto = new(0x21, 0x21, 0x21);
    private static readonly SKColor Cinza = new(0x6B, 0x6B, 0x6B);
    private static readonly SKColor Grade = new(0xCF, 0xD8, 0xDC);
    private static readonly SKColor Faixa = new(0xF4, 0xF6, 0xF8);
    private static readonly SKColor CorDoFimDeSemana = new(0xEE, 0xF1, 0xF4);
    private static readonly SKColor CorDaTarefa = new(0x64, 0xB5, 0xF6);
    private static readonly SKColor CorDaTarefaConcluida = new(0x15, 0x65, 0xC0);
    private static readonly SKColor CorDaCritica = new(0xEF, 0x9A, 0x9A);
    private static readonly SKColor CorDaCriticaConcluida = new(0xC6, 0x28, 0x28);
    private static readonly SKColor CorDoResumo = new(0x37, 0x47, 0x4F);
    private static readonly SKColor CorDoMarco = new(0x26, 0x32, 0x38);
    private static readonly SKColor CorDaLinhaDeBase = new(0xB0, 0xBE, 0xC5);
    private static readonly SKColor CorDeHoje = new(0xE6, 0x51, 0x00);
    private static readonly SKColor CorDoVinculo = new(0x78, 0x90, 0x9C);
    private static readonly SKColor CorDoPrazo = new(0x2E, 0x7D, 0x32);

    private readonly Cronograma _cronograma;
    private readonly OpcoesDaImpressaoDoGantt _opcoes;
    private readonly LayoutGantt _layout;
    private readonly IReadOnlyList<Tarefa> _tarefas;
    private readonly IReadOnlyDictionary<Guid, string> _edt;
    private readonly IReadOnlyList<(ColunaDoGanttImpresso Coluna, float Largura)> _colunas;
    private readonly IReadOnlyList<(float X, float Largura)> _faixas;
    private readonly float _pontosPorDia;
    private readonly int _linhasPorPagina;
    /// <summary>"Início 28/09/2026 · término 30/10/2026 · 13 tarefas · linha de base LB1": a linha do cabeçalho.</summary>
    public string Datas { get; }

    private GanttParaImprimir(Cronograma cronograma, ResultadoCronograma resultado, OpcoesDaImpressaoDoGantt opcoes, DateTime hoje)
    {
        (_cronograma, _opcoes) = (cronograma, opcoes);
        var (largura, altura) = Tamanho(opcoes.Folha);
        (LarguraDaFolha, AlturaDaFolha) = opcoes.Paisagem ? (Math.Max(largura, altura), Math.Min(largura, altura)) : (Math.Min(largura, altura), Math.Max(largura, altura));

        _colunas = Colunas(opcoes.Colunas, LarguraDaFolha - 2 * Margem);
        var areaDasBarras = LarguraDaFolha - 2 * Margem - _colunas.Sum(c => c.Largura);

        var ocultas = new HashSet<Guid>(opcoes.TarefasOcultas);
        if (opcoes.NivelMaximo is { } nivel)
            foreach (var t in cronograma.Tarefas.Where(t => cronograma.NivelDe(t.Id) >= nivel))
                ocultas.Add(t.Id);
        _tarefas = cronograma.Tarefas.Where(t => !ocultas.Contains(t.Id)).ToList();
        _edt = cronograma.NumeracaoEdt();

        OpcoesGantt Opcoes(double pontosPorDia, double larguraVisivel) => new(pontosPorDia, AlturaDaLinha)
        {
            TarefasOcultas = ocultas,
            Cultura = Brasil,
            Hoje = opcoes.Hoje ? hoje.Date : null,
            LarguraVisivel = larguraVisivel,
        };
        if (opcoes.Escala == EscalaDaImpressao.AjustarALargura)
        {
            var eixo = CalculadoraLayoutGantt.Calcular(cronograma, resultado, Opcoes(1, 0));
            var dias = (eixo.FimEixo - eixo.InicioEixo).Days + 1;
            _pontosPorDia = areaDasBarras / dias;
            _layout = CalculadoraLayoutGantt.Calcular(cronograma, resultado, Opcoes(_pontosPorDia, areaDasBarras));
            _faixas = [(0, areaDasBarras)];
        }
        else
        {
            _pontosPorDia = opcoes.Escala switch
            {
                EscalaDaImpressao.Dias => PontosPorDiaNosDias,
                EscalaDaImpressao.Semanas => PontosPorDiaNasSemanas,
                _ => PontosPorDiaNosMeses,
            };
            _layout = CalculadoraLayoutGantt.Calcular(cronograma, resultado, Opcoes(_pontosPorDia, 0));
            // A tabela só vai na primeira folha de cada fileira; as seguintes usam a largura toda para as barras. Cada faixa
            // termina na divisa de um dia, para a emenda não cortar uma coluna ao meio.
            float Dias(float largura) => Math.Max(1, (int)(largura / _pontosPorDia)) * _pontosPorDia;
            var faixas = new List<(float, float)> { (0, Dias(areaDasBarras)) };
            for (var x = faixas[0].Item2; x < _layout.Largura - 0.01; x += faixas[^1].Item2)
                faixas.Add((x, Dias(LarguraDaFolha - 2 * Margem)));
            _faixas = faixas;
        }
        FaixasDeTempo = _faixas.Count;

        _linhasPorPagina = Math.Max(1, (int)((FundoDasLinhas - TopoDasLinhas) / AlturaDaLinha));
        BlocosDeLinhas = Math.Max(1, (int)Math.Ceiling(_tarefas.Count / (double)_linhasPorPagina));
        var linhaDeBase = opcoes.LinhaDeBase && cronograma.LinhaDeBaseEmUso is { } lb ? $" · linha de base LB{lb.Numero}" : "";
        Datas = $"Início {cronograma.DataInicio:dd/MM/yyyy} · término {resultado.DataTermino:dd/MM/yyyy} · {Plural(_tarefas.Count, "tarefa", "tarefas")}{linhaDeBase}";
    }

    /// <summary>Monta as páginas (não desenha nada ainda).</summary>
    /// <exception cref="InvalidOperationException">O cronograma não foi calculado.</exception>
    public static GanttParaImprimir Montar(Cronograma cronograma, ResultadoCronograma resultado, OpcoesDaImpressaoDoGantt opcoes, DateTime hoje)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(resultado);
        ArgumentNullException.ThrowIfNull(opcoes);
        return new GanttParaImprimir(cronograma, resultado, opcoes, hoje);
    }

    /// <summary>A largura e a altura da folha em retrato, em pontos.</summary>
    public static (float Largura, float Altura) Tamanho(TamanhoDaFolha folha) => folha switch
    {
        TamanhoDaFolha.A4 => (595.28f, 841.89f),
        TamanhoDaFolha.A3 => (841.89f, 1190.55f),
        TamanhoDaFolha.A2 => (1190.55f, 1683.78f),
        TamanhoDaFolha.A1 => (1683.78f, 2383.94f),
        TamanhoDaFolha.A0 => (2383.94f, 3370.39f),
        TamanhoDaFolha.Carta => (612f, 792f),
        TamanhoDaFolha.Oficio => (612.28f, 935.43f), // 216 × 330 mm
        _ => throw new ArgumentOutOfRangeException(nameof(folha), folha, null),
    };

    /// <summary>"A3 (297 × 420 mm)".</summary>
    public static string NomeDa(TamanhoDaFolha folha) => folha switch
    {
        TamanhoDaFolha.A4 => "A4 (210 × 297 mm)",
        TamanhoDaFolha.A3 => "A3 (297 × 420 mm)",
        TamanhoDaFolha.A2 => "A2 (420 × 594 mm)",
        TamanhoDaFolha.A1 => "A1 (594 × 841 mm)",
        TamanhoDaFolha.A0 => "A0 (841 × 1189 mm)",
        TamanhoDaFolha.Carta => "Carta (216 × 279 mm)",
        TamanhoDaFolha.Oficio => "Ofício (216 × 330 mm)",
        _ => folha.ToString(),
    };

    public float LarguraDaFolha { get; }

    public float AlturaDaFolha { get; }

    /// <summary>Quantas folhas o tempo ocupa, lado a lado (1 ao ajustar à largura).</summary>
    public int FaixasDeTempo { get; }

    /// <summary>Quantas folhas as linhas ocupam, uma abaixo da outra.</summary>
    public int BlocosDeLinhas { get; }

    public int Paginas => FaixasDeTempo * BlocosDeLinhas;

    public int LinhasPorPagina => _linhasPorPagina;

    /// <summary>A largura de um dia no papel, em pontos.</summary>
    public float PontosPorDia => _pontosPorDia;

    /// <summary>"3 páginas (2 × 1: o tempo em 2 folhas lado a lado) · A3 paisagem".</summary>
    public string Resumo
    {
        get
        {
            var folha = $"{NomeDa(_opcoes.Folha).Split(' ')[0]} {(_opcoes.Paisagem ? "paisagem" : "retrato")}";
            var divisao = FaixasDeTempo > 1
                ? $" (o tempo em {FaixasDeTempo} folhas lado a lado{(BlocosDeLinhas > 1 ? $", as tarefas em {BlocosDeLinhas}" : "")})"
                : BlocosDeLinhas > 1 ? $" ({LinhasPorPagina} tarefas por folha)" : "";
            return $"{Plural(Paginas, "página", "páginas")}{divisao} · {folha}";
        }
    }

    public void GerarPdf(Stream destino)
    {
        ArgumentNullException.ThrowIfNull(destino);
        using var fontes = new Fontes();
        using var documento = SKDocument.CreatePdf(destino, new SKDocumentPdfMetadata
        {
            Title = $"Gráfico de Gantt · {_opcoes.Titulo}",
            Creator = "ProjectPlanner",
            Producer = "ProjectPlanner",
        });
        for (var p = 0; p < Paginas; p++)
        {
            var c = documento.BeginPage(LarguraDaFolha, AlturaDaFolha);
            Desenhar(c, p, fontes);
            documento.EndPage();
        }
        documento.Close();
    }

    /// <summary>A página (0 = a primeira) em PNG, com a largura dada em pixels.</summary>
    public byte[] Previa(int pagina, int larguraEmPixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pagina);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pagina, Paginas);
        ArgumentOutOfRangeException.ThrowIfLessThan(larguraEmPixels, 50);
        var escala = larguraEmPixels / LarguraDaFolha;
        var altura = Math.Max(1, (int)Math.Round(AlturaDaFolha * escala));
        using var superficie = SKSurface.Create(new SKImageInfo(larguraEmPixels, altura, SKColorType.Rgba8888, SKAlphaType.Premul));
        var c = superficie.Canvas;
        c.Clear(SKColors.White);
        c.Scale(escala);
        using (var fontes = new Fontes())
            Desenhar(c, pagina, fontes);
        using var imagem = superficie.Snapshot();
        using var png = imagem.Encode(SKEncodedImageFormat.Png, 90);
        return png.ToArray();
    }

    // ---- Geometria da página ----

    private float TopoDoTempo => Margem + AlturaDoCabecalho;

    private float TopoDasLinhas => TopoDoTempo + AlturaDoTempo;

    private float FundoDasLinhas => AlturaDaFolha - Margem - (_opcoes.Legenda ? AlturaDaLegenda : 4) - 8;

    private float LarguraDaTabela => _colunas.Sum(c => c.Largura);

    private static IReadOnlyList<(ColunaDoGanttImpresso, float)> Colunas(IReadOnlySet<ColunaDoGanttImpresso> escolhidas, float larguraUtil)
    {
        float Fixa(ColunaDoGanttImpresso c) => c switch
        {
            ColunaDoGanttImpresso.Edt => 34,
            ColunaDoGanttImpresso.Duracao => 40,
            ColunaDoGanttImpresso.Inicio or ColunaDoGanttImpresso.Termino => 48,
            ColunaDoGanttImpresso.Concluido => 30,
            ColunaDoGanttImpresso.Predecessoras => 58,
            _ => 0,
        };
        var colunas = Enum.GetValues<ColunaDoGanttImpresso>().Where(escolhidas.Contains).ToList();
        var fixas = colunas.Sum(Fixa);
        // O nome fica com o que sobra, entre 80 e 190 pontos, deixando ao menos a largura mínima às barras.
        var nome = Math.Clamp(larguraUtil - fixas - Math.Max(LarguraMinimaDasBarras, larguraUtil * 0.45f), 80, 190);
        return colunas.Select(c => (c, c == ColunaDoGanttImpresso.Nome ? nome : Fixa(c))).ToList();
    }

    private static string Titulo(ColunaDoGanttImpresso c) => c switch
    {
        ColunaDoGanttImpresso.Edt => "EDT",
        ColunaDoGanttImpresso.Nome => "Tarefa",
        ColunaDoGanttImpresso.Duracao => "Duração",
        ColunaDoGanttImpresso.Inicio => "Início",
        ColunaDoGanttImpresso.Termino => "Término",
        ColunaDoGanttImpresso.Concluido => "%",
        _ => "Predec.",
    };

    // ---- Desenho ----

    private void Desenhar(SKCanvas c, int pagina, Fontes f)
    {
        var bloco = pagina / FaixasDeTempo;
        var faixa = pagina % FaixasDeTempo;
        var primeira = bloco * _linhasPorPagina;
        var linhas = Math.Min(_linhasPorPagina, _tarefas.Count - primeira);
        var esquerda = faixa == 0 ? Margem + LarguraDaTabela : Margem;

        Cabecalho(c, f);
        if (faixa == 0)
            Tabela(c, primeira, Math.Max(0, linhas), f);
        Barras(c, primeira, Math.Max(0, linhas), esquerda, _faixas[faixa].X, f);
        if (_opcoes.Legenda)
            Legenda(c, f);
        Rodape(c, pagina, primeira, linhas, faixa, f);
    }

    private void Cabecalho(SKCanvas c, Fontes f)
    {
        Escrever(c, "GRÁFICO DE GANTT", Margem, Margem + 4, f.Negrito(9), Cinza);
        Escrever(c, Cortar(_opcoes.Titulo, f.Negrito(15), LarguraDaFolha - 2 * Margem), Margem, Margem + 24, f.Negrito(15), Texto);
        var subtitulo = string.IsNullOrWhiteSpace(_opcoes.Subtitulo) ? Datas : $"{Datas} · {_opcoes.Subtitulo}";
        Escrever(c, Cortar(subtitulo, f.Normal(8.5f), LarguraDaFolha - 2 * Margem), Margem, Margem + 40, f.Normal(8.5f), Cinza);
    }

    private void Tabela(SKCanvas c, int primeira, int linhas, Fontes f)
    {
        using var traco = new SKPaint { Color = Grade, StrokeWidth = 0.6f };
        using var fundo = new SKPaint { Color = Faixa };
        var direita = Margem + LarguraDaTabela;
        c.DrawRect(new SKRect(Margem, TopoDoTempo, direita, TopoDasLinhas), fundo);
        var x = Margem;
        foreach (var (coluna, largura) in _colunas)
        {
            Escrever(c, Titulo(coluna), x + 3, TopoDasLinhas - 8, f.Negrito(7.5f), Texto);
            x += largura;
        }
        var fundoDaTabela = TopoDasLinhas + linhas * AlturaDaLinha;
        for (var i = 0; i < linhas; i++)
        {
            var tarefa = _tarefas[primeira + i];
            var y = TopoDasLinhas + i * AlturaDaLinha;
            var baseDoTexto = y + AlturaDaLinha / 2 + 2.6f;
            x = Margem;
            foreach (var (coluna, largura) in _colunas)
            {
                var fonte = tarefa.EhResumo ? f.Negrito(7.5f) : f.Normal(7.5f);
                var recuo = coluna == ColunaDoGanttImpresso.Nome ? 3 + 7 * _cronograma.NivelDe(tarefa.Id) : 3;
                var texto = Cortar(Valor(tarefa, coluna), fonte, largura - recuo - 3);
                Escrever(c, texto, x + recuo, baseDoTexto, fonte, Texto);
                x += largura;
            }
            c.DrawLine(Margem, y + AlturaDaLinha, direita, y + AlturaDaLinha, traco);
        }
        x = Margem;
        foreach (var (_, largura) in _colunas)
        {
            c.DrawLine(x, TopoDoTempo, x, fundoDaTabela, traco);
            x += largura;
        }
        c.DrawLine(direita, TopoDoTempo, direita, Math.Max(fundoDaTabela, FundoDasLinhas), traco);
        c.DrawLine(Margem, TopoDoTempo, direita, TopoDoTempo, traco);
        c.DrawLine(Margem, TopoDasLinhas, direita, TopoDasLinhas, traco);
    }

    private string Valor(Tarefa t, ColunaDoGanttImpresso coluna) => coluna switch
    {
        ColunaDoGanttImpresso.Edt => _edt.TryGetValue(t.Id, out var edt) ? edt : "",
        ColunaDoGanttImpresso.Nome => t.Nome,
        ColunaDoGanttImpresso.Duracao => t.EhMarco ? "marco" : $"{t.Duracao} d",
        ColunaDoGanttImpresso.Inicio => (t.InicioMaisCedo ?? t.DataInicio).ToString("dd/MM/yy", Brasil),
        ColunaDoGanttImpresso.Termino => (t.TerminoMaisCedo ?? t.DataTermino).ToString("dd/MM/yy", Brasil),
        ColunaDoGanttImpresso.Concluido => $"{t.PercentualConcluido}%",
        _ => _opcoes.Predecessoras.TryGetValue(t.Id, out var p) ? p : "",
    };

    private void Barras(SKCanvas c, int primeira, int linhas, float esquerda, float xDaFaixa, Fontes f)
    {
        var area = new SKRect(esquerda, TopoDoTempo, LarguraDaFolha - Margem, FundoDasLinhas);
        var areaDasLinhas = new SKRect(area.Left, TopoDasLinhas, area.Right, area.Bottom);
        var deslocamentoY = primeira * AlturaDaLinha;
        float X(double x) => (float)(area.Left + x - xDaFaixa);
        float Y(double y) => (float)(TopoDasLinhas + y - deslocamentoY);

        c.Save();
        c.ClipRect(area);

        // Fins de semana, ao fundo.
        if (_opcoes.FinsDeSemana && _pontosPorDia >= 1.5f)
        {
            using var fimDeSemana = new SKPaint { Color = CorDoFimDeSemana };
            foreach (var dia in _layout.FinsDeSemana)
                c.DrawRect(new SKRect(X(dia.X), areaDasLinhas.Top, X(dia.X + dia.Largura), areaDasLinhas.Bottom), fimDeSemana);
        }

        // O cabeçalho do tempo, em duas faixas.
        using var traco = new SKPaint { Color = Grade, StrokeWidth = 0.6f };
        using var fundo = new SKPaint { Color = Faixa };
        c.DrawRect(new SKRect(area.Left, TopoDoTempo, area.Right, TopoDasLinhas), fundo);
        var meio = TopoDoTempo + AlturaDoTempo / 2;
        void FaixaDoTempo(IReadOnlyList<MarcaEscala> marcas, float topo, float fundoDaFaixa, SKFont fonte, bool linhaInteira)
        {
            foreach (var marca in marcas)
            {
                var x = X(marca.X);
                c.DrawLine(x, topo, x, linhaInteira ? areaDasLinhas.Bottom : fundoDaFaixa, traco);
                var visivelDe = Math.Max(x, area.Left);
                var visivelAte = Math.Min(x + (float)marca.Largura, area.Right);
                if (fonte.MeasureText(marca.Rotulo) + 4 <= visivelAte - visivelDe)
                    Escrever(c, marca.Rotulo, visivelDe + 2, fundoDaFaixa - 4, fonte, Texto);
            }
        }
        FaixaDoTempo(_layout.Escala.Superior, TopoDoTempo, meio, f.Negrito(7.5f), linhaInteira: true);
        FaixaDoTempo(_layout.Escala.Inferior, meio, TopoDasLinhas, f.Normal(7f), linhaInteira: false);
        c.DrawLine(area.Left, meio, area.Right, meio, traco);
        c.DrawLine(area.Left, TopoDoTempo, area.Right, TopoDoTempo, traco);
        c.DrawLine(area.Left, TopoDasLinhas, area.Right, TopoDasLinhas, traco);
        c.DrawLine(area.Right, TopoDoTempo, area.Right, area.Bottom, traco);

        // As divisórias das linhas.
        for (var i = 1; i <= linhas; i++)
            c.DrawLine(area.Left, TopoDasLinhas + i * AlturaDaLinha, area.Right, TopoDasLinhas + i * AlturaDaLinha, traco);

        c.Save();
        c.ClipRect(areaDasLinhas);
        var barras = _layout.Barras.Skip(primeira).Take(linhas).ToList();

        // Linha de base, por baixo das barras.
        if (_opcoes.LinhaDeBase)
        {
            using var linhaDeBase = new SKPaint { Color = CorDaLinhaDeBase };
            foreach (var b in barras.Where(b => b.XLinhaDeBase is not null))
            {
                var topo = Y(b.Y + b.Altura) + 1;
                c.DrawRect(new SKRect(X(b.XLinhaDeBase!.Value), topo, X(b.XLinhaDeBase.Value + Math.Max(1, b.LarguraLinhaDeBase ?? 0)), topo + 3), linhaDeBase);
            }
        }

        // Vínculos.
        if (_opcoes.Vinculos)
        {
            var visiveis = barras.Select(b => b.TarefaId).ToHashSet();
            foreach (var seta in _layout.Setas.Where(s => visiveis.Contains(s.OrigemId) || visiveis.Contains(s.DestinoId)))
            {
                var cor = _opcoes.CaminhoCritico && seta.EhCritica ? CorDaCriticaConcluida : CorDoVinculo;
                using var linha = new SKPaint { Color = cor, StrokeWidth = 0.7f, Style = SKPaintStyle.Stroke, IsAntialias = true };
                using var ponta = new SKPaint { Color = cor, IsAntialias = true };
                using var caminho = new SKPath();
                caminho.MoveTo(X(seta.Pontos[0].X), Y(seta.Pontos[0].Y));
                foreach (var p in seta.Pontos.Skip(1))
                    caminho.LineTo(X(p.X), Y(p.Y));
                c.DrawPath(caminho, linha);
                using var triangulo = new SKPath();
                triangulo.MoveTo(X(seta.Ponta[0].X), Y(seta.Ponta[0].Y));
                foreach (var p in seta.Ponta.Skip(1))
                    triangulo.LineTo(X(p.X), Y(p.Y));
                triangulo.Close();
                c.DrawPath(triangulo, ponta);
            }
        }

        // Barras, marcos e resumos.
        foreach (var b in barras)
        {
            var (inicio, direita) = (X(b.X), X(b.Direita));
            var (topo, fundoDaBarra) = (Y(b.Y), Y(b.Y + b.Altura));
            var critica = _opcoes.CaminhoCritico && b.EhCritica;
            switch (b.Tipo)
            {
                case TipoTarefa.Marco:
                    using (var marco = new SKPaint { Color = critica ? CorDaCriticaConcluida : CorDoMarco, IsAntialias = true })
                        c.DrawPath(Losango((inicio + direita) / 2, (topo + fundoDaBarra) / 2, (fundoDaBarra - topo) / 2 + 1), marco);
                    break;
                case TipoTarefa.Resumo:
                    using (var resumo = new SKPaint { Color = CorDoResumo, IsAntialias = true })
                    {
                        var meioDaBarra = topo + (fundoDaBarra - topo) * 0.45f;
                        c.DrawRect(new SKRect(inicio, topo, Math.Max(direita, inicio + 1), meioDaBarra), resumo);
                        c.DrawPath(Ponta(inicio, meioDaBarra, fundoDaBarra, 1), resumo);
                        c.DrawPath(Ponta(direita, meioDaBarra, fundoDaBarra, -1), resumo);
                    }
                    break;
                default:
                    using (var cheia = new SKPaint { Color = critica ? CorDaCritica : CorDaTarefa, IsAntialias = true })
                    using (var feita = new SKPaint { Color = critica ? CorDaCriticaConcluida : CorDaTarefaConcluida, IsAntialias = true })
                    using (var borda = new SKPaint { Color = critica ? CorDaCriticaConcluida : CorDaTarefaConcluida, StrokeWidth = 0.5f, Style = SKPaintStyle.Stroke, IsAntialias = true })
                    {
                        var retangulo = new SKRect(inicio, topo, Math.Max(direita, inicio + 1), fundoDaBarra);
                        c.DrawRect(retangulo, cheia);
                        if (b.LarguraConcluida > 0)
                            c.DrawRect(new SKRect(inicio, topo + 2, X(b.X + b.LarguraConcluida), fundoDaBarra - 2), feita);
                        c.DrawRect(retangulo, borda);
                    }
                    break;
            }
            if (b.XDoPrazo is { } prazo)
                using (var marcaDoPrazo = new SKPaint { Color = CorDoPrazo, IsAntialias = true })
                    c.DrawPath(PontaParaBaixo(X(prazo), topo - 1), marcaDoPrazo);
            if (_opcoes.NomesNasBarras)
            {
                var meia = b.Tipo == TipoTarefa.Marco ? (fundoDaBarra - topo) / 2 + 1 : 0;
                var nome = b.Tipo == TipoTarefa.Marco ? $"{b.Nome} ({_tarefas[b.Linha].TerminoMaisCedo:dd/MM})" : b.Nome;
                var largura = f.Normal(6.5f).MeasureText(nome);
                // Perto da borda direita da folha, o nome vai para a esquerda da barra.
                var xDoNome = direita + meia + 3 + largura > area.Right && inicio - meia - 3 - largura >= area.Left
                    ? inicio - meia - 3 - largura
                    : direita + meia + 3;
                Escrever(c, nome, xDoNome, fundoDaBarra - 0.5f, f.Normal(6.5f), Cinza);
            }
        }
        c.Restore();

        // Hoje, por cima de tudo.
        if (_layout.XDeHoje is { } hoje)
        {
            using var linhaDeHoje = new SKPaint
            {
                Color = CorDeHoje, StrokeWidth = 1f, Style = SKPaintStyle.Stroke, PathEffect = SKPathEffect.CreateDash([3, 2], 0),
            };
            c.DrawLine(X(hoje), TopoDasLinhas, X(hoje), area.Bottom, linhaDeHoje);
        }
        c.Restore();
    }

    private void Legenda(SKCanvas c, Fontes f)
    {
        var y = AlturaDaFolha - Margem - 6;
        var x = Margem;
        var fonte = f.Normal(7.5f);
        void Item(string texto, Action<float, float> marca)
        {
            marca(x, y - 3);
            Escrever(c, texto, x + 22, y, fonte, Texto);
            x += 30 + fonte.MeasureText(texto);
        }
        void Barra(float x0, float meio, SKColor fundo, SKColor? feito)
        {
            using var cheia = new SKPaint { Color = fundo };
            c.DrawRect(new SKRect(x0, meio - 4, x0 + 18, meio + 4), cheia);
            if (feito is { } cor)
                using (var parte = new SKPaint { Color = cor })
                    c.DrawRect(new SKRect(x0, meio - 2, x0 + 9, meio + 2), parte);
        }
        Escrever(c, "Legenda:", x, y, f.Negrito(7.5f), Texto);
        x += 6 + f.Negrito(7.5f).MeasureText("Legenda:");
        Item("Tarefa (o escuro é o concluído)", (x0, m) => Barra(x0, m, CorDaTarefa, CorDaTarefaConcluida));
        if (_opcoes.CaminhoCritico)
            Item("Caminho crítico", (x0, m) => Barra(x0, m, CorDaCritica, CorDaCriticaConcluida));
        Item("Resumo", (x0, m) =>
        {
            using var resumo = new SKPaint { Color = CorDoResumo, IsAntialias = true };
            c.DrawRect(new SKRect(x0, m - 4, x0 + 18, m - 0.5f), resumo);
            c.DrawPath(Ponta(x0, m - 0.5f, m + 4, 1), resumo);
            c.DrawPath(Ponta(x0 + 18, m - 0.5f, m + 4, -1), resumo);
        });
        Item("Marco", (x0, m) =>
        {
            using var marco = new SKPaint { Color = CorDoMarco, IsAntialias = true };
            c.DrawPath(Losango(x0 + 9, m, 4.5f), marco);
        });
        if (_opcoes.LinhaDeBase && _cronograma.LinhaDeBaseEmUso is not null)
            Item("Linha de base", (x0, m) =>
            {
                using var linha = new SKPaint { Color = CorDaLinhaDeBase };
                c.DrawRect(new SKRect(x0, m - 1.5f, x0 + 18, m + 1.5f), linha);
            });
        if (_opcoes.Vinculos)
            Item("Vínculo", (x0, m) =>
            {
                using var linha = new SKPaint { Color = CorDoVinculo, StrokeWidth = 0.8f, IsAntialias = true };
                c.DrawLine(x0, m, x0 + 15, m, linha);
                using var ponta = new SKPaint { Color = CorDoVinculo, IsAntialias = true };
                using var triangulo = new SKPath();
                triangulo.MoveTo(x0 + 18, m);
                triangulo.LineTo(x0 + 14, m - 2.5f);
                triangulo.LineTo(x0 + 14, m + 2.5f);
                triangulo.Close();
                c.DrawPath(triangulo, ponta);
            });
        if (_layout.Barras.Any(b => b.XDoPrazo is not null))
            Item("Prazo", (x0, m) =>
            {
                using var prazo = new SKPaint { Color = CorDoPrazo, IsAntialias = true };
                c.DrawPath(PontaParaBaixo(x0 + 9, m + 3), prazo);
            });
        if (_opcoes.Hoje && _layout.XDeHoje is not null)
            Item("Hoje", (x0, m) =>
            {
                using var hoje = new SKPaint { Color = CorDeHoje, StrokeWidth = 1f, PathEffect = SKPathEffect.CreateDash([3, 2], 0) };
                c.DrawLine(x0 + 9, m - 5, x0 + 9, m + 5, hoje);
            });
        if (_opcoes.FinsDeSemana && _pontosPorDia >= 1.5f)
            Item("Sábado e domingo", (x0, m) =>
            {
                using var fim = new SKPaint { Color = CorDoFimDeSemana };
                using var borda = new SKPaint { Color = Grade, Style = SKPaintStyle.Stroke, StrokeWidth = 0.5f };
                c.DrawRect(new SKRect(x0 + 4, m - 5, x0 + 14, m + 5), fim);
                c.DrawRect(new SKRect(x0 + 4, m - 5, x0 + 14, m + 5), borda);
            });
    }

    private void Rodape(SKCanvas c, int pagina, int primeira, int linhas, int faixa, Fontes f)
    {
        var y = AlturaDaFolha - Margem + 12;
        var texto = $"Página {pagina + 1} de {Paginas}";
        Escrever(c, texto, LarguraDaFolha - Margem - f.Normal(7.5f).MeasureText(texto), y, f.Normal(7.5f), Cinza);
        if (Paginas == 1)
            return;
        var partes = new List<string>();
        if (BlocosDeLinhas > 1 && linhas > 0)
            partes.Add($"tarefas {primeira + 1} a {primeira + linhas}");
        if (FaixasDeTempo > 1)
        {
            var (x, largura) = _faixas[faixa];
            var de = _layout.InicioEixo.AddDays(Math.Round(x / _pontosPorDia));
            var ate = de.AddDays(Math.Round(largura / _pontosPorDia) - 1);
            if (ate > _layout.FimEixo)
                ate = _layout.FimEixo;
            partes.Add($"de {de:dd/MM/yyyy} a {ate:dd/MM/yyyy} (folha {faixa + 1} de {FaixasDeTempo} na horizontal)");
        }
        Escrever(c, string.Join(" · ", partes), Margem, y, f.Normal(7.5f), Cinza);
    }

    // ---- Peças ----

    private static SKPath Losango(float x, float y, float raio)
    {
        var p = new SKPath();
        p.MoveTo(x, y - raio);
        p.LineTo(x + raio, y);
        p.LineTo(x, y + raio);
        p.LineTo(x - raio, y);
        p.Close();
        return p;
    }

    /// <summary>A ponta do resumo, que desce da barra para dentro (sentido 1 na ponta esquerda, −1 na direita).</summary>
    private static SKPath Ponta(float x, float topo, float fundo, int sentido)
    {
        var p = new SKPath();
        p.MoveTo(x, topo);
        p.LineTo(x + sentido * 4, topo);
        p.LineTo(x, fundo);
        p.Close();
        return p;
    }

    private static SKPath PontaParaBaixo(float x, float fundo)
    {
        var p = new SKPath();
        p.MoveTo(x - 3, fundo - 4);
        p.LineTo(x + 3, fundo - 4);
        p.LineTo(x, fundo);
        p.Close();
        return p;
    }

    private static string Cortar(string texto, SKFont fonte, float largura)
    {
        if (largura <= 0)
            return "";
        if (fonte.MeasureText(texto) <= largura)
            return texto;
        while (texto.Length > 1 && fonte.MeasureText(texto + "…") > largura)
            texto = texto[..^1];
        return texto.Length > 1 ? texto.TrimEnd() + "…" : "";
    }

    private static string Plural(int n, string um, string varios) => n == 1 ? $"1 {um}" : $"{n} {varios}";

    private static void Escrever(SKCanvas c, string texto, float x, float y, SKFont fonte, SKColor cor)
    {
        if (texto.Length == 0)
            return;
        using var tinta = new SKPaint { Color = cor, IsAntialias = true };
        c.DrawText(texto, x, y, SKTextAlign.Left, fonte, tinta);
    }
}
