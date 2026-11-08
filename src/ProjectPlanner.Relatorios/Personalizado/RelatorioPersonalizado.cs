using System.Globalization;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Indicadores;
using ProjectPlanner.Core.OrdensDeServico;
using ProjectPlanner.Core.Planejamento;
using ProjectPlanner.Core.Relatorios;
using SkiaSharp;

namespace ProjectPlanner.Relatorios.Personalizado;

/// <summary>
/// Bloco 88: o relatório personalizado montado: as páginas (gravadas uma vez), para o PDF e para a prévia da tela, e os avisos do
/// que ficou de fora (uma seção sem dados no período).
/// </summary>
public sealed class RelatorioPersonalizado : IDisposable
{
    private readonly PaginasDoRelatorio _paginas;
    private readonly Fontes _fontes;
    private readonly SKImage? _logotipo;
    private readonly ModeloDeRelatorio _modelo;
    private readonly CabecalhoDoRelatorio _cabecalho;
    private readonly PeriodoDoRelatorio _periodo;
    private readonly DateTime _geradoEm;
    private readonly Lock _trava = new();
    private int _emUso;
    private bool _descartar;
    private bool _liberado;

    internal RelatorioPersonalizado(PaginasDoRelatorio paginas, Fontes fontes, SKImage? logotipo, ModeloDeRelatorio modelo, CabecalhoDoRelatorio cabecalho,
        PeriodoDoRelatorio periodo, DateTime geradoEm, IReadOnlyList<string> avisos)
    {
        (_paginas, _fontes, _logotipo, _modelo, _cabecalho, _periodo, _geradoEm) = (paginas, fontes, logotipo, modelo, cabecalho, periodo, geradoEm);
        Avisos = avisos;
    }

    public int Paginas => _paginas.Prontas.Count;
    public float Largura => _paginas.Largura;
    public float Altura => _paginas.Altura;

    /// <summary>O que ficou de fora ("Fotos do período: nenhuma foto de 01/09 a 30/09").</summary>
    public IReadOnlyList<string> Avisos { get; }

    /// <summary>"Relatório mensal de obra - Casa da Ana - 2026-09-30" (sem a extensão).</summary>
    public string NomeSugerido => Limpar($"{_modelo.Titulo} - {_cabecalho.Obra} - {_periodo.Ate:yyyy-MM-dd}");

    /// <exception cref="ObjectDisposedException">O relatório já foi descartado (a tela montou outro).</exception>
    public void GerarPdf(Stream destino)
    {
        ArgumentNullException.ThrowIfNull(destino);
        Usar(() => EscreverPdf(destino));
    }

    private bool EscreverPdf(Stream destino)
    {
        var metadados = new SKDocumentPdfMetadata
        {
            Title = _modelo.Titulo,
            Author = _cabecalho.Responsavel ?? "",
            Subject = _cabecalho.Obra,
            Creator = "ProjectPlanner",
            Producer = "ProjectPlanner",
            EncodingQuality = 85,
            Creation = _geradoEm,
            Modified = _geradoEm,
        };
        using var documento = SKDocument.CreatePdf(destino, metadados);
        for (var i = 0; i < Paginas; i++)
        {
            var c = documento.BeginPage(Largura, Altura);
            Desenhar(c, i);
            documento.EndPage();
        }
        documento.Close();
        return true;
    }

    /// <summary>A página (a partir de 0) como PNG, com esta largura em pixels.</summary>
    /// <exception cref="ObjectDisposedException">O relatório já foi descartado (a tela montou outro).</exception>
    public byte[] Previa(int pagina, int larguraEmPixels) => Usar(() =>
    {
        if (pagina < 0 || pagina >= Paginas)
            throw new ArgumentOutOfRangeException(nameof(pagina));
        return DesenharPrevia(pagina, larguraEmPixels);
    });

    private byte[] DesenharPrevia(int pagina, int larguraEmPixels)
    {
        var escala = larguraEmPixels / Largura;
        var info = new SKImageInfo(larguraEmPixels, (int)Math.Ceiling(Altura * escala));
        using var superficie = SKSurface.Create(info);
        var c = superficie.Canvas;
        c.Clear(SKColors.White);
        c.Scale(escala);
        Desenhar(c, pagina);
        using var imagem = superficie.Snapshot();
        using var dados = imagem.Encode(SKEncodedImageFormat.Png, 90);
        return dados.ToArray();
    }

    // O conteúdo gravado e, fora da capa, o cabeçalho (o logotipo, a obra, o título e o período) e o rodapé (o texto do tema e a página).
    private void Desenhar(SKCanvas c, int indice)
    {
        var (conteudo, capa) = _paginas.Prontas[indice];
        c.DrawPicture(conteudo);
        if (capa)
            return;
        var (m, l, a) = (PaginasDoRelatorio.Margem, Largura, Altura);
        using var cinza = new SKPaint { Color = PaginasDoRelatorio.Cinza, IsAntialias = true };
        using var fio = new SKPaint { Color = _paginas.Cor, StrokeWidth = 1.2f };
        var x = m;
        if (_modelo.Tema.ComLogotipo && _logotipo is { } logo)
        {
            var caixa = GeradorPdf.Encaixar(logo.Width, logo.Height, new SKRect(m, m - 4, m + 70, m + 16), centralizar: false);
            c.DrawImage(logo, caixa);
            x = caixa.Right + 8;
        }
        var titulo = PaginasDoRelatorio.Encurtar($"{_cabecalho.Obra} · {_modelo.Titulo}", _fontes.Normal(8), l - m - x - 150);
        c.DrawText(titulo, x, m + 9, SKTextAlign.Left, _fontes.Normal(8), cinza);
        c.DrawText(_periodo.Texto, l - m, m + 9, SKTextAlign.Right, _fontes.Normal(8), cinza);
        c.DrawLine(m, m + 18, l - m, m + 18, fio);
        using var fioDoRodape = new SKPaint { Color = PaginasDoRelatorio.Linha, StrokeWidth = 0.75f };
        c.DrawLine(m, a - m + 2, l - m, a - m + 2, fioDoRodape);
        var rodape = string.IsNullOrWhiteSpace(_modelo.Tema.Rodape) ? $"Gerado pelo ProjectPlanner em {_geradoEm:dd/MM/yyyy}" : _modelo.Tema.Rodape.Trim();
        c.DrawText(PaginasDoRelatorio.Encurtar(rodape, _fontes.Normal(7.5f), l - 2 * m - 90), m, a - m + 13, SKTextAlign.Left, _fontes.Normal(7.5f), cinza);
        c.DrawText($"Página {indice + 1} de {Paginas}", l - m, a - m + 13, SKTextAlign.Right, _fontes.Normal(7.5f), cinza);
    }

    // A prévia de uma página ou o PDF podem estar sendo feitos fora da tela quando ela troca este relatório por um novo e o descarta:
    // o descarte espera o uso terminar, para as páginas gravadas não sumirem no meio do desenho.
    private T Usar<T>(Func<T> uso)
    {
        lock (_trava)
        {
            ObjectDisposedException.ThrowIf(_descartar, this);
            _emUso++;
        }
        try
        {
            return uso();
        }
        finally
        {
            bool liberar;
            lock (_trava)
            {
                _emUso--;
                liberar = _descartar && _emUso == 0 && !_liberado;
                _liberado |= liberar;
            }
            if (liberar)
                Liberar();
        }
    }

    /// <summary>Descarta as páginas gravadas; com uma prévia ou um PDF em curso, depois que ele terminar.</summary>
    public void Dispose()
    {
        bool liberar;
        lock (_trava)
        {
            _descartar = true;
            liberar = _emUso == 0 && !_liberado;
            _liberado |= liberar;
        }
        if (liberar)
            Liberar();
    }

    private void Liberar()
    {
        _paginas.Dispose();
        _logotipo?.Dispose();
        _fontes.Dispose();
    }

    private static string Limpar(string nome) =>
        string.Concat(nome.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch is '/' or '\\' or ':' ? '-' : ch)).Trim();
}

/// <summary>
/// Bloco 88: monta o relatório personalizado: as seções do modelo, na ordem, com os dados da obra no período. A data de referência
/// (o "hoje" do relatório) é o fim do período: o previsto, as atrasadas e o medido são os daquele dia.
/// </summary>
public static class GeradorRelatorioPersonalizado
{
    /// <param name="imagem">Os bytes (JPEG) de uma foto pelo id dela.</param>
    /// <exception cref="DomainValidationException">O modelo não vale (ver <see cref="ModeloDeRelatorio.Validar"/>).</exception>
    public static RelatorioPersonalizado Montar(ModeloDeRelatorio modelo, Cronograma cronograma, CabecalhoDoRelatorio cabecalho, PeriodoDoRelatorio periodo,
        Func<Guid, byte[]?> imagem, DateTime agora)
    {
        ArgumentNullException.ThrowIfNull(modelo);
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(cabecalho);
        ArgumentNullException.ThrowIfNull(periodo);
        ArgumentNullException.ThrowIfNull(imagem);
        modelo.Validar();
        var (largura, altura) = modelo.Tema.Papel == PapelDoRelatorio.Carta ? (612f, 792f) : (595.28f, 841.89f);
        if (modelo.Tema.Paisagem)
            (largura, altura) = (altura, largura);
        var fontes = new Fontes();
        var paginas = new PaginasDoRelatorio(largura, altura, SKColor.Parse(modelo.Tema.Cor), fontes);
        var logotipo = modelo.Tema.ComLogotipo && cabecalho.Logotipo is { Length: > 0 } bytes ? SKImage.FromEncodedData(bytes) : null;
        var contexto = new ContextoDoRelatorio(cronograma, modelo, periodo, cabecalho, imagem, agora, paginas, logotipo);
        foreach (var secao in modelo.Ativas)
        {
            if (secao.Tipo == TipoDeSecao.Capa)
            {
                SecoesDoRelatorio.Capa(contexto, secao);
                continue;
            }
            if (secao.PaginaNova && !paginas.Vazia)
                paginas.Nova();
            SecoesDoRelatorio.Desenhar(contexto, secao);
            paginas.Y += 8;
        }
        paginas.Fechar();
        if (paginas.Prontas.Count == 0) // só seções sem nada: uma página com o aviso
        {
            paginas.Nova();
            paginas.Aviso("Nada para mostrar com as seções escolhidas.");
            paginas.Fechar();
        }
        return new RelatorioPersonalizado(paginas, fontes, logotipo, modelo, cabecalho, periodo, agora, contexto.Avisos);
    }
}

/// <summary>Bloco 88: o que as seções usam: a obra, o período, os cálculos do dia (feitos uma vez) e as páginas.</summary>
internal sealed class ContextoDoRelatorio(Cronograma cronograma, ModeloDeRelatorio modelo, PeriodoDoRelatorio periodo, CabecalhoDoRelatorio cabecalho,
    Func<Guid, byte[]?> imagem, DateTime agora, PaginasDoRelatorio paginas, SKImage? logotipo)
{
    public Cronograma C { get; } = cronograma;
    public ModeloDeRelatorio Modelo { get; } = modelo;
    public PeriodoDoRelatorio Periodo { get; } = periodo;
    public CabecalhoDoRelatorio Cabecalho { get; } = cabecalho;
    public Func<Guid, byte[]?> Imagem { get; } = imagem;
    public DateTime Agora { get; } = agora;
    public PaginasDoRelatorio P { get; } = paginas;
    public SKImage? Logotipo { get; } = logotipo;
    public List<string> Avisos { get; } = [];

    private IReadOnlyDictionary<Guid, string>? _edt;
    private Dictionary<Guid, int>? _niveis;
    private IReadOnlyDictionary<Guid, AndamentoDaTarefa>? _andamento;
    private PainelDeIndicadores? _painel;
    private bool _painelTentado;

    public IReadOnlyDictionary<Guid, string> Edt => _edt ??= C.NumeracaoEdt();

    /// <summary>O nível de cada tarefa na EDT (0 no primeiro).</summary>
    public IReadOnlyDictionary<Guid, int> Niveis
    {
        get
        {
            if (_niveis is not null)
                return _niveis;
            _niveis = [];
            foreach (var t in C.Tarefas) // em pré-ordem: o pai vem antes
                _niveis[t.Id] = t.TarefaPaiId is { } pai && _niveis.TryGetValue(pai, out var n) ? n + 1 : 0;
            return _niveis;
        }
    }

    /// <summary>A situação de cada tarefa no fim do período (as mesmas regras da grade e dos indicadores).</summary>
    public IReadOnlyDictionary<Guid, AndamentoDaTarefa> Andamento =>
        _andamento ??= AcompanhamentoDoCronograma.Calcular(C, Periodo.Ate.ToDateTime(TimeOnly.MinValue));

    /// <summary>O painel dos indicadores no fim do período; nulo se o cronograma ainda não foi calculado.</summary>
    public PainelDeIndicadores? Painel
    {
        get
        {
            if (_painelTentado)
                return _painel;
            _painelTentado = true;
            try
            {
                _painel = PainelDeIndicadores.Montar(C, Periodo.Ate, Periodicidade.Mensal);
            }
            catch (DomainValidationException)
            {
                _painel = null;
            }
            return _painel;
        }
    }

    public IEnumerable<Tarefa> Folhas => C.Tarefas.Where(t => !t.EhResumo);

    public static DateTime Inicio(Tarefa t) => t.InicioMaisCedo ?? t.DataInicio;

    public static DateTime Termino(Tarefa t) => t.TerminoMaisCedo ?? t.DataTermino;

    public string Nome(Tarefa t) => $"{Edt.GetValueOrDefault(t.Id)} {t.Nome}".Trim();

    /// <summary>As fotos do período que vão para relatórios, com a tarefa.</summary>
    public List<(Tarefa Tarefa, FotoTarefa Foto)> FotosDoPeriodo() =>
        [.. C.Tarefas.SelectMany(t => t.Fotos.Select(f => (Tarefa: t, Foto: f))).Where(x => x.Foto.NoRelatorio && Periodo.Contem(x.Foto.DataFoto))];
}

/// <summary>Bloco 88: o desenho de cada seção do relatório personalizado.</summary>
internal static class SecoesDoRelatorio
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly string[] Meses = ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];

    public static void Desenhar(ContextoDoRelatorio x, SecaoDoRelatorio s)
    {
        switch (s.Tipo)
        {
            case TipoDeSecao.Resumo: Resumo(x, s); break;
            case TipoDeSecao.Indicadores: Indicadores(x, s); break;
            case TipoDeSecao.Etapas: Etapas(x, s); break;
            case TipoDeSecao.Gantt: Gantt(x, s); break;
            case TipoDeSecao.Tarefas: Tarefas(x, s); break;
            case TipoDeSecao.Proximas: Proximas(x, s); break;
            case TipoDeSecao.Fotos: Fotos(x, s); break;
            case TipoDeSecao.Diario: Diario(x, s); break;
            case TipoDeSecao.Medicoes: Medicoes(x, s); break;
            case TipoDeSecao.Custos: Custos(x, s); break;
            case TipoDeSecao.OrdensDeServico: Ordens(x, s); break;
            case TipoDeSecao.Texto: Texto(x, s); break;
            case TipoDeSecao.Assinaturas: Assinaturas(x, s); break;
        }
    }

    // ---- Capa ----

    public static void Capa(ContextoDoRelatorio x, SecaoDoRelatorio s)
    {
        var p = x.P;
        p.Nova(capa: true);
        var faixa = p.Altura * (p.Largura > p.Altura ? 0.30f : 0.25f);
        using (var cor = new SKPaint { Color = p.Cor })
            p.C.DrawRect(new SKRect(0, 0, p.Largura, faixa), cor);
        if (x.Modelo.Tema.ComLogotipo && x.Logotipo is { } logo)
        {
            var moldura = new SKRect(p.Esquerda, 30, p.Esquerda + 130, 90);
            using (var branco = new SKPaint { Color = SKColors.White, IsAntialias = true })
                p.C.DrawRoundRect(moldura, 8, 8, branco);
            var caixa = GeradorPdf.Encaixar(logo.Width, logo.Height, SKRect.Inflate(moldura, -8, -6), centralizar: true);
            p.C.DrawImage(logo, caixa);
        }
        var titulo = GeradorPdf.Quebrar(x.Modelo.Titulo, p.F.Negrito(26), p.Util).Take(2).ToList();
        var subtitulo = string.IsNullOrWhiteSpace(s.Texto) ? null : PaginasDoRelatorio.Encurtar(s.Texto.Trim(), p.F.Normal(13), p.Util);
        var y = faixa - 26 - (subtitulo is null ? 0 : 20) - (titulo.Count - 1) * 30;
        foreach (var linha in titulo)
        {
            p.Escrever(linha, p.Esquerda, y, p.F.Negrito(26), SKColors.White);
            y += 30;
        }
        if (subtitulo is not null)
            p.Escrever(subtitulo, p.Esquerda, y - 6, p.F.Normal(13), new SKColor(0xFF, 0xFF, 0xFF, 0xE0));

        y = faixa + 40;
        foreach (var linha in GeradorPdf.Quebrar(x.Cabecalho.Obra, p.F.Negrito(22), p.Util).Take(2))
        {
            p.Escrever(linha, p.Esquerda, y, p.F.Negrito(22), PaginasDoRelatorio.Texto);
            y += 27;
        }
        p.Escrever($"Período: {x.Periodo.Texto}  ·  Emitido em {x.Agora:dd/MM/yyyy}", p.Esquerda, y + 2, p.F.Normal(11), PaginasDoRelatorio.Cinza);
        y += 22;

        var dados = new List<(string, string)>();
        if (x.Cabecalho.DadosDaObra.Contratante is { } contratante)
            dados.Add(("Contratante", contratante));
        if (x.Cabecalho.DadosDaObra.Contrato is { } contrato)
            dados.Add(("Contrato", contrato));
        if (x.Cabecalho.DadosDaObra.Endereco is { } endereco)
            dados.Add(("Endereço", endereco));
        if (x.Cabecalho.Responsavel is { } responsavel)
            dados.Add(("Responsável técnico", x.Cabecalho.Registro is { } registro ? $"{responsavel} · {registro}" : responsavel));
        var alturaDosDados = dados.Count * 17 + (dados.Count > 0 ? 16 : 0);

        if (s.ComFoto && FotoDaCapa(x) is { } capa && x.Imagem(capa.Foto.Id) is { Length: > 0 } bytes && SKImage.FromEncodedData(bytes) is { } foto)
            using (foto)
            {
                var area = new SKRect(p.Esquerda, y + 6, p.Direita, p.Altura - PaginasDoRelatorio.Margem - alturaDosDados - 24);
                if (area.Height > 80)
                {
                    var desenho = GeradorPdf.Encaixar(foto.Width, foto.Height, area, centralizar: true);
                    p.C.DrawImage(foto, desenho);
                    p.Centralizado(PaginasDoRelatorio.Encurtar($"{x.Nome(capa.Tarefa)} · {capa.Foto.DataFoto:dd/MM/yyyy}", p.F.Normal(8.5f), p.Util),
                        desenho.MidX, desenho.Bottom + 12, p.F.Normal(8.5f), PaginasDoRelatorio.Cinza);
                }
            }
        if (dados.Count > 0)
        {
            var yd = p.Altura - PaginasDoRelatorio.Margem - alturaDosDados + 16;
            using (var fio = new SKPaint { Color = p.Cor, StrokeWidth = 1.2f })
                p.C.DrawLine(p.Esquerda, yd - 14, p.Direita, yd - 14, fio);
            foreach (var (rotulo, valor) in dados)
            {
                p.Escrever(rotulo, p.Esquerda, yd, p.F.Negrito(9.5f), PaginasDoRelatorio.Cinza);
                p.Escrever(PaginasDoRelatorio.Encurtar(valor, p.F.Normal(10), p.Util - 130), p.Esquerda + 130, yd, p.F.Normal(10), PaginasDoRelatorio.Texto);
                yd += 17;
            }
        }
        p.Fechar();
    }

    // A foto da capa: a destacada mais recente do período; senão, a mais recente do período; senão, a mais recente até o fim dele.
    private static (Tarefa Tarefa, FotoTarefa Foto)? FotoDaCapa(ContextoDoRelatorio x)
    {
        var doPeriodo = x.FotosDoPeriodo();
        if (doPeriodo.Count > 0)
            return doPeriodo.OrderByDescending(f => f.Foto.Destaque).ThenByDescending(f => f.Foto.DataFoto).First();
        var antes = x.C.Tarefas.SelectMany(t => t.Fotos.Select(f => (Tarefa: t, Foto: f)))
            .Where(f => f.Foto.NoRelatorio && DateOnly.FromDateTime(f.Foto.DataFoto) <= x.Periodo.Ate).OrderByDescending(f => f.Foto.DataFoto).ToList();
        return antes.Count > 0 ? antes[0] : null;
    }

    // ---- A obra em números ----

    private static void Resumo(ContextoDoRelatorio x, SecaoDoRelatorio s)
    {
        var p = x.P;
        p.Secao(s.TituloOuPadrao, 160);
        var (realizado, previsto) = Avanco(x);
        var folhas = x.Folhas.ToList();
        var atrasadas = folhas.Count(t => x.Andamento.GetValueOrDefault(t.Id)?.Situacao == SituacaoDaTarefa.Atrasada);
        var painel = x.Painel;
        var termino = painel?.ValorAgregado.TerminoAtual ?? (folhas.Count == 0 ? (DateOnly?)null : DateOnly.FromDateTime(folhas.Max(ContextoDoRelatorio.Termino)));
        var detalheDoTermino = painel?.ValorAgregado.TerminoPrevisto is { } lb ? $"linha de base {lb:dd/MM/yyyy}"
            : painel?.ValorAgregado.TerminoPelaTendencia is { } tendencia ? $"pela tendência {tendencia:dd/MM/yyyy}" : null;
        var fotos = x.FotosDoPeriodo();
        var custoNoPeriodo = x.C.Custos.Lancamentos.Where(l => x.Periodo.Contem(l.Data)).Sum(l => l.Valor);
        p.Cartoes(
        [
            ($"{realizado:0}%", "Avanço físico", $"previsto {previsto:0}% em {x.Periodo.Ate:dd/MM}"),
            (termino is { } t ? t.ToString("dd/MM/yyyy", Brasil) : "—", "Término", detalheDoTermino),
            (atrasadas.ToString(Brasil), "Tarefas atrasadas", $"de {folhas.Count} tarefas"),
            (painel is { ValorContratado: > 0 } pm ? Moeda.ComSimbolo(pm.Medido) : "—", "Medido (aprovado)",
                painel is { ValorContratado: > 0 } pf ? $"{pf.FinanceiroMedido:0}% do contrato" : "sem valores no contrato"),
            (Moeda.ComSimbolo(painel?.ValorAgregado.CustoReal ?? x.C.Custos.Lancamentos.Where(l => l.Data <= x.Periodo.Ate).Sum(l => l.Valor)), "Custo realizado",
                custoNoPeriodo > 0 ? $"{Moeda.ComSimbolo(custoNoPeriodo)} no período" : "nada no período"),
            (fotos.Count.ToString(Brasil), "Fotos no período", fotos.Count == 0 ? null : $"de {fotos.Select(f => f.Tarefa.Id).Distinct().Count()} tarefa(s)"),
        ], porLinha: p.Largura > p.Altura ? 6 : 3);
        p.Barra("Avanço físico", realizado, previsto);
    }

    // O avanço físico e o previsto no fim do período: os dos indicadores, com valores; sem valores, pela duração das tarefas.
    private static (double Realizado, double Previsto) Avanco(ContextoDoRelatorio x)
    {
        if (x.Painel is { TemValores: true } painel)
            return (painel.ValorAgregado.FisicoRealizado, painel.ValorAgregado.FisicoPrevisto);
        var folhas = x.Folhas.Where(t => t.Duracao > 0).ToList();
        var pesos = folhas.Sum(t => (double)t.Duracao);
        if (pesos == 0)
            return (0, 0);
        return (folhas.Sum(t => t.Duracao * (double)t.PercentualConcluido) / pesos,
            folhas.Sum(t => t.Duracao * (double)(x.Andamento.GetValueOrDefault(t.Id)?.PercentualPrevisto ?? 0)) / pesos);
    }

    // ---- Indicadores ----

    private static void Indicadores(ContextoDoRelatorio x, SecaoDoRelatorio s)
    {
        var p = x.P;
        if (x.Painel is not { } painel)
        {
            p.Secao(s.TituloOuPadrao);
            p.Aviso("Os indicadores precisam do cronograma calculado.");
            x.Avisos.Add($"{s.TituloOuPadrao}: o cronograma ainda não foi calculado.");
            return;
        }
        if (!p.Vazia)
            p.Nova();
        p.Caber(p.Base - p.Topo); // a página inteira (ela tem o título dela: "Indicadores em 30/09/2026")
        PaginaDeIndicadoresPdf.Desenhar(p.C, painel, new SKRect(p.Esquerda, p.Y, p.Direita, p.Base), p.F);
        p.Y = p.Base;
    }

    // ---- Etapas ----

    private static void Etapas(ContextoDoRelatorio x, SecaoDoRelatorio s)
    {
        var p = x.P;
        var etapas = x.C.Tarefas.Where(t => t.TarefaPaiId is null).ToList();
        p.Secao(s.TituloOuPadrao);
        if (etapas.Count == 0)
        {
            p.Aviso("O cronograma ainda não tem tarefas.");
            return;
        }
        p.Tabela(
        [
            new("EDT", 0.6f), new("Etapa", 2.8f), new("Início", 1.25f), new("Término", 1.25f), new("Previsto", 0.9f, Direita: true),
            new("Realizado", 1.8f), new("Situação", 1.2f),
        ], [.. etapas.Select(t => new Celula[]
        {
            x.Edt.GetValueOrDefault(t.Id) ?? "", new(t.Nome, Negrito: true), Dia(ContextoDoRelatorio.Inicio(t)), Dia(ContextoDoRelatorio.Termino(t)),
            $"{x.Andamento.GetValueOrDefault(t.Id)?.PercentualPrevisto ?? 0}%", new("", t.PercentualConcluido), Situacao(x, t),
        })]);
    }

    // ---- Gantt ----

    private static void Gantt(ContextoDoRelatorio x, SecaoDoRelatorio s)
    {
        var p = x.P;
        var linhas = x.C.Tarefas.Where(t => x.Niveis.GetValueOrDefault(t.Id) < s.Nivel).ToList();
        p.Secao(s.TituloOuPadrao, 50);
        if (linhas.Count == 0)
        {
            p.Aviso("O cronograma ainda não tem tarefas.");
            return;
        }
        var inicio = linhas.Min(ContextoDoRelatorio.Inicio).Date;
        var fim = linhas.Max(ContextoDoRelatorio.Termino).Date.AddDays(1);
        if (fim <= inicio)
            fim = inicio.AddDays(1);
        var colunaDosNomes = p.Util * 0.32f;
        var (x0, x1) = (p.Esquerda + colunaDosNomes + 8, p.Direita);
        float X(DateTime d) => x0 + (x1 - x0) * (float)Math.Clamp((d - inicio).TotalDays / (fim - inicio).TotalDays, 0, 1);
        var hoje = x.Periodo.Ate.ToDateTime(TimeOnly.MinValue).AddDays(1);
        using var fio = new SKPaint { Color = PaginasDoRelatorio.Linha, StrokeWidth = 0.5f };
        using var planejado = new SKPaint { Color = PaginasDoRelatorio.Misturar(p.Cor, SKColors.White, 0.6f), IsAntialias = true };
        using var feito = new SKPaint { Color = p.Cor, IsAntialias = true };
        using var resumo = new SKPaint { Color = PaginasDoRelatorio.Misturar(p.Cor, SKColors.Black, 0.25f), IsAntialias = true };
        using var linhaDeHoje = new SKPaint { Color = PaginasDoRelatorio.Vermelho, StrokeWidth = 1, PathEffect = SKPathEffect.CreateDash([3, 2], 0) };

        void Escala()
        {
            p.Caber(30);
            var mes = new DateTime(inicio.Year, inicio.Month, 1);
            var passo = Math.Max(1, (int)Math.Ceiling(30 / Math.Max(1f, (x1 - x0) / (float)Math.Max(1, (fim - inicio).TotalDays / 30.4))));
            for (var i = 0; mes < fim; mes = mes.AddMonths(1), i++)
            {
                var px = X(mes < inicio ? inicio : mes);
                p.C.DrawLine(px, p.Y, px, p.Y + 16, fio);
                if (i % passo == 0)
                    p.Escrever($"{Meses[mes.Month - 1]}/{mes:yy}", px + 2, p.Y + 10, p.F.Normal(7.5f), PaginasDoRelatorio.Cinza);
            }
            p.Y += 18;
        }
        Escala();
        foreach (var t in linhas)
        {
            const float altura = 15;
            if (p.Caber(altura))
                Escala();
            var nivel = x.Niveis.GetValueOrDefault(t.Id);
            var fonte = t.EhResumo ? p.F.Negrito(8) : p.F.Normal(8);
            p.Escrever(PaginasDoRelatorio.Encurtar(x.Nome(t), fonte, colunaDosNomes - nivel * 8), p.Esquerda + nivel * 8, p.Y + 10.5f, fonte, PaginasDoRelatorio.Texto);
            var (a, b) = (X(ContextoDoRelatorio.Inicio(t).Date), X(ContextoDoRelatorio.Termino(t).Date.AddDays(1)));
            if (t.EhMarco)
            {
                var meio = (a + b) / 2;
                using var losango = new SKPath();
                losango.MoveTo(meio, p.Y + 2);
                losango.LineTo(meio + 5, p.Y + 7.5f);
                losango.LineTo(meio, p.Y + 13);
                losango.LineTo(meio - 5, p.Y + 7.5f);
                losango.Close();
                p.C.DrawPath(losango, t.PercentualConcluido == 100 ? feito : resumo);
            }
            else if (t.EhResumo)
            {
                var barra = new SKRect(a, p.Y + 4.5f, Math.Max(a + 2, b), p.Y + 10.5f);
                p.C.DrawRect(barra, planejado);
                if (t.PercentualConcluido > 0)
                    p.C.DrawRect(new SKRect(barra.Left, barra.Top, barra.Left + barra.Width * t.PercentualConcluido / 100f, barra.Bottom), resumo);
            }
            else
            {
                var barra = new SKRect(a, p.Y + 3.5f, Math.Max(a + 2, b), p.Y + 11.5f);
                p.C.DrawRoundRect(barra, 2, 2, planejado);
                if (t.PercentualConcluido > 0)
                    p.C.DrawRoundRect(new SKRect(barra.Left, barra.Top, barra.Left + barra.Width * t.PercentualConcluido / 100f, barra.Bottom), 2, 2, feito);
            }
            if (hoje >= inicio && hoje <= fim)
                p.C.DrawLine(X(hoje), p.Y, X(hoje), p.Y + altura, linhaDeHoje);
            p.Y += altura;
        }
        p.Y += 4;
        p.Escrever("Barra clara: o planejado · escura: o feito · tracejado vermelho: o fim do período", p.Esquerda, p.Y + 8, p.F.Normal(7.5f), PaginasDoRelatorio.Cinza);
        p.Y += 16;
    }

    // ---- Tarefas e próximas ----

    private static void Tarefas(ContextoDoRelatorio x, SecaoDoRelatorio s)
    {
        var p = x.P;
        var folhas = x.Folhas.ToList();
        var escolhidas = s.Filtro switch
        {
            FiltroDeTarefas.EmAndamento => folhas.Where(t => t.PercentualConcluido < 100 && (t.PercentualConcluido > 0
                || (DateOnly.FromDateTime(ContextoDoRelatorio.Inicio(t)) <= x.Periodo.Ate && DateOnly.FromDateTime(ContextoDoRelatorio.Termino(t)) >= x.Periodo.De)))
                .ToList(),
            FiltroDeTarefas.Atrasadas => folhas.Where(t => x.Andamento.GetValueOrDefault(t.Id)?.Situacao == SituacaoDaTarefa.Atrasada)
                .OrderByDescending(t => (x.Andamento[t.Id].PercentualPrevisto - t.PercentualConcluido)).ToList(),
            FiltroDeTarefas.Concluidas => folhas.Where(t => t.PercentualConcluido == 100).ToList(),
            _ => folhas,
        };
        p.Secao(s.TituloOuPadrao);
        if (escolhidas.Count == 0)
        {
            p.Aviso(s.Filtro switch
            {
                FiltroDeTarefas.EmAndamento => "Nenhuma tarefa em andamento no período.",
                FiltroDeTarefas.Atrasadas => $"Nenhuma tarefa atrasada em {x.Periodo.Ate:dd/MM/yyyy}.",
                FiltroDeTarefas.Concluidas => "Nenhuma tarefa concluída ainda.",
                _ => "O cronograma ainda não tem tarefas.",
            });
            return;
        }
        p.Tabela(
        [
            new("EDT", 0.6f), new("Tarefa", 3f), new("Início", 1.25f), new("Término", 1.25f), new("Previsto", 0.9f, Direita: true),
            new("Realizado", 1.7f), new("Situação", 1.2f),
        ], [.. escolhidas.Select(t => new Celula[]
        {
            x.Edt.GetValueOrDefault(t.Id) ?? "", t.Nome, Dia(ContextoDoRelatorio.Inicio(t)), Dia(ContextoDoRelatorio.Termino(t)),
            $"{x.Andamento.GetValueOrDefault(t.Id)?.PercentualPrevisto ?? 0}%", new("", t.PercentualConcluido), Situacao(x, t),
        })]);
    }

    private static void Proximas(ContextoDoRelatorio x, SecaoDoRelatorio s)
    {
        var p = x.P;
        var limite = x.Periodo.Ate.AddDays(s.Dias);
        var proximas = x.Folhas.Where(t => t.PercentualConcluido == 0)
            .Where(t => DateOnly.FromDateTime(ContextoDoRelatorio.Inicio(t)) is var d && d > x.Periodo.Ate && d <= limite)
            .OrderBy(ContextoDoRelatorio.Inicio).ToList();
        p.Secao($"{s.TituloOuPadrao} ({s.Dias} dias)");
        if (proximas.Count == 0)
        {
            p.Aviso($"Nenhuma tarefa começa de {x.Periodo.Ate.AddDays(1):dd/MM} a {limite:dd/MM/yyyy}.");
            return;
        }
        p.Tabela(
        [
            new("Início", 1.25f), new("EDT", 0.6f), new("Tarefa", 3f), new("Término", 1.25f), new("Duração", 0.9f, Direita: true), new("Responsáveis", 2.2f),
        ], [.. proximas.Select(t => new Celula[]
        {
            Dia(ContextoDoRelatorio.Inicio(t)), x.Edt.GetValueOrDefault(t.Id) ?? "", t.Nome, Dia(ContextoDoRelatorio.Termino(t)),
            t.EhMarco ? "marco" : t.Duracao == 1 ? "1 dia" : $"{t.Duracao} dias", Responsaveis(x, t),
        })]);
    }

    private static string Responsaveis(ContextoDoRelatorio x, Tarefa t) =>
        string.Join(", ", t.Atribuicoes.Select(a => x.C.Recursos.Obter(a.RecursoId)).Where(r => r?.Tipo == Core.Recursos.TipoDeRecurso.Profissional)
            .Select(r => r!.Nome));

    // ---- Fotos ----

    private static void Fotos(ContextoDoRelatorio x, SecaoDoRelatorio s)
    {
        var p = x.P;
        var candidatas = x.FotosDoPeriodo();
        p.Secao(s.TituloOuPadrao, 200);
        if (candidatas.Count == 0)
        {
            p.Aviso($"Nenhuma foto de {x.Periodo.Texto}.");
            x.Avisos.Add($"{s.TituloOuPadrao}: nenhuma foto de {x.Periodo.Texto}.");
            return;
        }
        // As destacadas primeiro (e as mais recentes) quando passa do máximo; no relatório, em ordem de data.
        var fotos = candidatas.OrderByDescending(f => f.Foto.Destaque).ThenByDescending(f => f.Foto.DataFoto).Take(s.MaximoDeFotos)
            .OrderBy(f => f.Foto.DataFoto).ToList();
        if (candidatas.Count > fotos.Count)
            p.Aviso($"{fotos.Count} de {candidatas.Count} fotos do período (as destacadas e as mais recentes).");
        var paisagem = p.Largura > p.Altura;
        var (colunas, linhas) = (s.FotosPorPagina, paisagem) switch
        {
            (2, false) => (1, 2),
            (2, true) => (2, 1),
            (6, false) => (2, 3),
            (6, true) => (3, 2),
            _ => (2, 2),
        };
        const float espaco = 14, legenda = 44;
        var indice = 0;
        while (indice < fotos.Count)
        {
            if (p.Base - p.Y < 260)
                p.Nova();
            var area = new SKRect(p.Esquerda, p.Y, p.Direita, p.Base);
            var largura = (area.Width - espaco * (colunas - 1)) / colunas;
            // Cada foto numa moldura 4:3 (a de paisagem a ocupa toda), sem passar da altura de uma linha da grade.
            var altura = Math.Min((area.Height - espaco * (linhas - 1)) / linhas, largura * 0.75f + legenda);
            for (var l = 0; l < linhas && indice < fotos.Count; l++)
                for (var c = 0; c < colunas && indice < fotos.Count; c++, indice++)
                {
                    var caixa = SKRect.Create(area.Left + c * (largura + espaco), area.Top + l * (altura + espaco), largura, altura);
                    Foto(x, fotos[indice], caixa);
                }
            p.Y = p.Base;
            if (indice < fotos.Count)
                p.Nova();
        }
    }

    private static void Foto(ContextoDoRelatorio x, (Tarefa Tarefa, FotoTarefa Foto) item, SKRect caixa)
    {
        var p = x.P;
        var (tarefa, foto) = item;
        var legenda = foto.Titulo ?? foto.Descricao;
        var linhas = new List<(string Texto, SKFont Fonte, SKColor Cor)>
        {
            (PaginasDoRelatorio.Encurtar(x.Nome(tarefa), p.F.Negrito(8.5f), caixa.Width), p.F.Negrito(8.5f), PaginasDoRelatorio.Texto),
            (PaginasDoRelatorio.Encurtar($"{foto.DataFoto:dd/MM/yyyy}" + (foto.Local is { Length: > 0 } local ? $" · {local}" : ""), p.F.Normal(8), caixa.Width),
                p.F.Normal(8), PaginasDoRelatorio.Cinza),
        };
        if (!string.IsNullOrWhiteSpace(legenda))
            linhas.AddRange(GeradorPdf.Quebrar(legenda.Trim(), p.F.Normal(8), caixa.Width).Take(2).Select(l => (l, p.F.Normal(8), PaginasDoRelatorio.Texto)));
        var areaDaFoto = new SKRect(caixa.Left, caixa.Top, caixa.Right, Math.Max(caixa.Top + 30, caixa.Bottom - 44));
        using var moldura = new SKPaint { Color = PaginasDoRelatorio.Linha, Style = SKPaintStyle.Stroke, StrokeWidth = 0.75f };
        if (x.Imagem(foto.Id) is { Length: > 0 } bytes && SKImage.FromEncodedData(bytes) is { } imagem)
            using (imagem)
            {
                var desenho = GeradorPdf.Encaixar(imagem.Width, imagem.Height, areaDaFoto, centralizar: true);
                desenho.Offset(0, areaDaFoto.Top - desenho.Top); // no alto da moldura; as legendas da linha começam juntas, embaixo dela
                p.C.DrawImage(imagem, desenho);
                p.C.DrawRect(desenho, moldura);
            }
        else
        {
            using var vazio = new SKPaint { Color = PaginasDoRelatorio.Faixa };
            p.C.DrawRect(areaDaFoto, vazio);
            p.Centralizado("Imagem indisponível", areaDaFoto.MidX, areaDaFoto.MidY, p.F.Normal(9), PaginasDoRelatorio.Cinza);
        }
        var y = areaDaFoto.Bottom + 12;
        foreach (var (texto, fonte, cor) in linhas)
        {
            p.Escrever(texto, caixa.Left, y, fonte, cor);
            y += 11;
        }
    }

    // ---- Diário ----

    private static void Diario(ContextoDoRelatorio x, SecaoDoRelatorio s)
    {
        var p = x.P;
        var dias = x.C.Diario.Dias.Where(d => x.Periodo.Contem(d.Data)).OrderBy(d => d.Data).ToList();
        p.Secao(s.TituloOuPadrao);
        if (dias.Count == 0)
        {
            p.Aviso($"Nenhum dia do diário de obra de {x.Periodo.Texto}.");
            x.Avisos.Add($"{s.TituloOuPadrao}: nenhum dia do diário no período.");
            return;
        }
        var impraticaveis = dias.Count(d => !d.Manha.Praticavel || !d.Tarde.Praticavel);
        var media = dias.Average(d => d.TotalDoEfetivo);
        p.Paragrafo($"{(dias.Count == 1 ? "1 dia registrado" : $"{dias.Count} dias registrados")} · {impraticaveis} com período impraticável · " +
                    $"efetivo médio de {media.ToString("0.#", Brasil)} pessoa(s) por dia · {dias.Count(d => d.Fechado)} fechado(s).", 9.5f, PaginasDoRelatorio.Cinza);
        p.Tabela(
        [
            new("Dia", 1f), new("Clima", 1.7f), new("Efetivo", 0.75f, Direita: true), new("Serviços", 3f), new("Ocorrências", 2.3f),
        ], [.. dias.Select(d => new Celula[]
        {
            $"{d.Data:dd/MM} {Brasil.DateTimeFormat.GetAbbreviatedDayName(d.Data.DayOfWeek).TrimEnd('.')}",
            new(Clima(d), Cor: !d.Manha.Praticavel || !d.Tarde.Praticavel ? PaginasDoRelatorio.Vermelho : null),
            d.TotalDoEfetivo.ToString(Brasil),
            string.Join("; ", d.Atividades.Select(a => a.Descricao + (a.Quantidade is { } q ? $" {q.ToString("#,##0.##", Brasil)} {a.Unidade}".TrimEnd() : "")
                                                        + (a.Percentual is { } pct ? $" ({pct}%)" : ""))),
            d.Ocorrencias?.Replace('\n', ' ') ?? "",
        })]);
    }

    // "Bom", "Bom / Chuva · tarde sem trabalho".
    private static string Clima(Core.Diario.DiaDaObra d)
    {
        var (manha, tarde) = (Core.Diario.ClimaDoPeriodo.NomeDoTempo(d.Manha.Tempo), Core.Diario.ClimaDoPeriodo.NomeDoTempo(d.Tarde.Tempo));
        var tempo = manha == tarde ? manha : $"{manha} / {tarde}";
        return tempo + (d.Manha.Praticavel, d.Tarde.Praticavel) switch
        {
            (false, false) => " · sem trabalho",
            (false, true) => " · manhã sem trabalho",
            (true, false) => " · tarde sem trabalho",
            _ => "",
        };
    }

    // ---- Medições ----

    private static void Medicoes(ContextoDoRelatorio x, SecaoDoRelatorio s)
    {
        var p = x.P;
        var medicoes = x.C.Contrato.Medicoes.Where(m => m.De <= x.Periodo.Ate).ToList();
        p.Secao(s.TituloOuPadrao);
        if (medicoes.Count == 0)
        {
            p.Aviso(x.C.Contrato.ValorContratado > 0 ? "Nenhuma medição até o fim do período." : "Sem medições: as tarefas não têm valor no contrato.");
            return;
        }
        var boletins = medicoes.Select(m => x.C.Contrato.Boletim(m.Numero)).ToList();
        var ultimo = boletins[^1].Resumo;
        p.Cartoes(
        [
            (Moeda.ComSimbolo(ultimo.ValorContratado), "Valor do contrato", ultimo.QuantidadeDeAditivos > 0 ? $"com {ultimo.QuantidadeDeAditivos} aditivo(s)" : null),
            (Moeda.ComSimbolo(ultimo.MedidoAcumulado), "Medido acumulado", $"{ultimo.PercentualMedido:0.#}% do contrato"),
            (Moeda.ComSimbolo(ultimo.Saldo), "Saldo a medir", null),
        ], porLinha: 3);
        p.Tabela(
        [
            new("Nº", 0.5f), new("Período", 2.2f), new("Situação", 1.1f), new("No período", 1.4f, Direita: true), new("Acumulado", 1.4f, Direita: true),
            new("Líquido", 1.4f, Direita: true),
        ], [.. boletins.Select(b => new Celula[]
        {
            $"{b.Medicao.Numero:00}", $"{b.Medicao.De:dd/MM/yyyy} a {b.Medicao.Ate:dd/MM/yyyy}",
            b.Medicao.Situacao switch { Core.Medicoes.SituacaoDaMedicao.Rascunho => "Em rascunho", Core.Medicoes.SituacaoDaMedicao.Aprovada => "Aprovada", _ => "Paga" },
            Moeda.ComSimbolo(b.Resumo.MedidoNoPeriodo), Moeda.ComSimbolo(b.Resumo.MedidoAcumulado), Moeda.ComSimbolo(b.Resumo.Liquido),
        })]);
    }

    // ---- Custos ----

    private static void Custos(ContextoDoRelatorio x, SecaoDoRelatorio s)
    {
        var p = x.P;
        var lancamentos = x.C.Custos.Lancamentos.Where(l => x.Periodo.Contem(l.Data)).ToList();
        p.Secao(s.TituloOuPadrao);
        if (lancamentos.Count == 0)
        {
            p.Aviso($"Nenhum custo lançado de {x.Periodo.Texto}.");
            return;
        }
        var total = lancamentos.Sum(l => l.Valor);
        p.Paragrafo($"Total no período: {Moeda.ComSimbolo(total)} em {lancamentos.Count} lançamento(s).", 10, PaginasDoRelatorio.Texto);
        p.Tabela([new("Categoria", 3f), new("Valor", 1.4f, Direita: true), new("Do total", 2f)],
            [.. lancamentos.GroupBy(l => l.Categoria).OrderByDescending(g => g.Sum(l => l.Valor)).Select(g => new Celula[]
            {
                Categorias.Texto(g.Key), Moeda.ComSimbolo(g.Sum(l => l.Valor)),
                new("", total == 0 ? 0 : (int)Math.Round(g.Sum(l => l.Valor) / total * 100, MidpointRounding.AwayFromZero)),
            })]);
        var maiores = lancamentos.OrderByDescending(l => l.Valor).Take(10).ToList();
        p.Paragrafo(maiores.Count == lancamentos.Count ? "Os lançamentos:" : $"Os {maiores.Count} maiores lançamentos:", 9.5f, PaginasDoRelatorio.Cinza);
        p.Tabela([new("Data", 1.25f), new("Descrição", 3f), new("Categoria", 1.4f), new("Tarefa", 2f), new("Valor", 1.3f, Direita: true)],
            [.. maiores.Select(l => new Celula[]
            {
                l.Data.ToString("dd/MM/yyyy", Brasil), l.Descricao, Categorias.Texto(l.Categoria),
                l.TarefaId is { } id && x.C.Tarefas.FirstOrDefault(t => t.Id == id) is { } t ? x.Nome(t) : "custo geral", Moeda.ComSimbolo(l.Valor),
            })]);
    }

    // ---- Ordens de serviço ----

    private static void Ordens(ContextoDoRelatorio x, SecaoDoRelatorio s)
    {
        var p = x.P;
        var ordens = x.C.OrdensDeServico.Todas.Where(o => o.Situacao != SituacaoDaOrdem.Cancelada).ToList();
        p.Secao(s.TituloOuPadrao);
        if (ordens.Count == 0)
        {
            p.Aviso("Nenhuma ordem de serviço.");
            return;
        }
        p.Tabela(
        [
            new("OS", 1f), new("Serviço", 2.5f), new("Executante", 1.8f), new("Situação", 1.3f), new("Prazo", 1.25f), new("Valor", 1.35f, Direita: true),
            new("Medido", 1.35f, Direita: true),
        ], [.. ordens.Select(o => new Celula[]
        {
            o.Codigo, o.Dados.Titulo, o.Dados.Executante ?? "", TextosDaOrdem.Texto(o.Situacao),
            new(o.Dados.Prazo is { } prazo ? prazo.ToString("dd/MM/yyyy", Brasil) : "—", Cor: o.Vencida(x.Periodo.Ate) ? PaginasDoRelatorio.Vermelho : null),
            Moeda.ComSimbolo(o.Total), Moeda.ComSimbolo(o.TotalMedido),
        })]);
    }

    // ---- Texto e assinaturas ----

    private static void Texto(ContextoDoRelatorio x, SecaoDoRelatorio s)
    {
        var p = x.P;
        p.Secao(s.TituloOuPadrao);
        if (string.IsNullOrWhiteSpace(s.Texto))
        {
            p.Aviso("(Escreva o texto desta seção na tela do relatório.)");
            return;
        }
        foreach (var paragrafo in s.Texto.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            p.Paragrafo(paragrafo, 10.5f);
    }

    private static void Assinaturas(ContextoDoRelatorio x, SecaoDoRelatorio s)
    {
        var p = x.P;
        p.Caber(110);
        p.Y += 50;
        var nomes = new (string? Nome, string Cargo)[]
        {
            (x.Cabecalho.Responsavel, x.Cabecalho.Registro is { } registro ? $"Responsável técnico · {registro}" : "Responsável técnico"),
            (x.Cabecalho.DadosDaObra.Contratante, "Contratante"),
            (null, "Fiscalização"),
        };
        const float espaco = 24;
        var largura = (p.Util - espaco * (nomes.Length - 1)) / nomes.Length;
        using var traco = new SKPaint { Color = PaginasDoRelatorio.Texto, StrokeWidth = 0.75f };
        for (var i = 0; i < nomes.Length; i++)
        {
            var x0 = p.Esquerda + i * (largura + espaco);
            p.C.DrawLine(x0, p.Y, x0 + largura, p.Y, traco);
            p.Centralizado(PaginasDoRelatorio.Encurtar(nomes[i].Nome ?? "", p.F.Normal(9), largura), x0 + largura / 2, p.Y + 14, p.F.Normal(9), PaginasDoRelatorio.Texto);
            p.Centralizado(PaginasDoRelatorio.Encurtar(nomes[i].Cargo, p.F.Normal(8), largura), x0 + largura / 2, p.Y + 27, p.F.Normal(8), PaginasDoRelatorio.Cinza);
        }
        p.Y += 40;
    }

    // ---- Apoio ----

    private static Celula Situacao(ContextoDoRelatorio x, Tarefa t) => x.Andamento.GetValueOrDefault(t.Id)?.Situacao switch
    {
        SituacaoDaTarefa.Concluida => new Celula("Concluída", Cor: PaginasDoRelatorio.Verde),
        SituacaoDaTarefa.Atrasada => new Celula("Atrasada", Cor: PaginasDoRelatorio.Vermelho, Negrito: true),
        SituacaoDaTarefa.EmDia => new Celula("Em dia"),
        _ => new Celula("Não iniciada", Cor: PaginasDoRelatorio.Cinza),
    };

    private static string Dia(DateTime d) => d.ToString("dd/MM/yyyy", Brasil);
}
