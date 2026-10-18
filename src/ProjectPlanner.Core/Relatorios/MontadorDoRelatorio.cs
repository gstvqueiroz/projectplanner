using System.Globalization;

namespace ProjectPlanner.Core.Relatorios;

/// <summary>Quais tarefas entram no relatório.</summary>
public enum TarefasDoRelatorio
{
    Todas,
    SoCriticas,
    SoAtrasadas,

    /// <summary>As que estão (ou estavam previstas) em execução em algum dia do período.</summary>
    EmExecucaoNoPeriodo,

    /// <summary>Um resumo e tudo o que está dentro dele.</summary>
    DeUmRamo,
}

public enum AgrupamentoDoRelatorio
{
    PorTarefa,
    PorData,
    PorLocal,
}

/// <summary>O que entra e como organizar o relatório fotográfico (Bloco 27).</summary>
/// <param name="De">Primeiro dia do período (pela data da foto).</param>
/// <param name="Ate">Último dia do período; também é a data de status do resumo do andamento.</param>
public sealed record OpcoesDoRelatorio(DateOnly De, DateOnly Ate)
{
    public static IReadOnlyList<int> FotosPorPaginaAceitas { get; } = [1, 2, 4, 6];

    public TarefasDoRelatorio Tarefas { get; init; } = TarefasDoRelatorio.Todas;

    /// <summary>O resumo do ramo, com <see cref="TarefasDoRelatorio.DeUmRamo"/>.</summary>
    public Guid? Ramo { get; init; }

    /// <summary>Só fotos com alguma destas tags (vazia: todas).</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    public AgrupamentoDoRelatorio Agrupamento { get; init; } = AgrupamentoDoRelatorio.PorTarefa;

    public int FotosPorPagina { get; init; } = 2;

    public bool ComResumo { get; init; } = true;

    public bool ComIndice { get; init; } = true;

    public bool ComAssinaturas { get; init; } = true;

    /// <summary>Bloco 29: páginas de antes e depois por local (a primeira foto do local até o fim do período e a última do período).</summary>
    public bool ComAntesEDepois { get; init; }

    /// <summary>Bloco 37: uma página de indicadores (valor agregado, Curva S, etapas e atrasos) na data do fim do período.</summary>
    public bool ComIndicadores { get; init; }

    /// <summary>Bloco 29: o texto do andamento do período e os pontos de atenção (escritos ou sugeridos pela IA); nulo sem texto.</summary>
    public TextoDoRelatorio? Texto { get; init; }

    /// <exception cref="DomainValidationException">Período invertido, fotos por página fora de 1/2/4/6 ou ramo faltando.</exception>
    public void Validar()
    {
        if (Ate < De)
            throw new DomainValidationException(nameof(Ate), "O período termina antes de começar.");
        if (!FotosPorPaginaAceitas.Contains(FotosPorPagina))
            throw new DomainValidationException(nameof(FotosPorPagina), "As fotos por página precisam ser 1, 2, 4 ou 6.");
        if (Tarefas == TarefasDoRelatorio.DeUmRamo && Ramo is null)
            throw new DomainValidationException(nameof(Ramo), "Escolha o resumo do ramo.");
    }

    /// <summary>Colunas e linhas de fotos por página: 1 = 1×1, 2 = 1×2, 4 = 2×2, 6 = 2×3.</summary>
    public (int Colunas, int Linhas) Grade => FotosPorPagina switch
    {
        1 => (1, 1),
        2 => (1, 2),
        4 => (2, 2),
        _ => (2, 3),
    };
}

/// <summary>Bloco 29: o texto do relatório: um ou mais parágrafos do andamento e os pontos de atenção.</summary>
public sealed record TextoDoRelatorio
{
    public const int TamanhoMaximo = 8000;

    /// <exception cref="DomainValidationException">Texto longo demais.</exception>
    public TextoDoRelatorio(string? andamento, IEnumerable<string>? pontosDeAtencao = null)
    {
        Andamento = string.IsNullOrWhiteSpace(andamento) ? null : andamento.Trim().ReplaceLineEndings("\n");
        PontosDeAtencao = (pontosDeAtencao ?? []).Select(p => p?.Trim() ?? "").Where(p => p.Length > 0).ToList();
        if ((Andamento?.Length ?? 0) + PontosDeAtencao.Sum(p => p.Length) > TamanhoMaximo)
            throw new DomainValidationException(nameof(Andamento), $"O texto do relatório passa de {TamanhoMaximo} caracteres.");
    }

    public string? Andamento { get; }

    public IReadOnlyList<string> PontosDeAtencao { get; }

    public bool Vazio => Andamento is null && PontosDeAtencao.Count == 0;

    /// <summary>Os parágrafos do andamento (separados por linha em branco ou quebra de linha).</summary>
    public IReadOnlyList<string> Paragrafos =>
        Andamento?.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];

    /// <summary>Os pontos de atenção de um texto com um por linha (marcadores "-", "•" e "*" no começo são tirados).</summary>
    public static IReadOnlyList<string> LerPontos(string? texto) =>
        (texto ?? "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimStart('-', '•', '*', ' ').Trim()).Where(l => l.Length > 0).ToList();
}

/// <summary>Bloco 29: uma página de antes e depois no relatório.</summary>
/// <param name="Local">O local comparado.</param>
public sealed record AntesEDepoisNoRelatorio(string Local, FotoTarefa Antes, string LegendaAntes, FotoTarefa Depois, string LegendaDepois, string Evolucao);

/// <summary>Os dados da capa: a obra (do projeto) e a empresa (das preferências).</summary>
public sealed record CabecalhoDoRelatorio(
    string Obra,
    DadosDaObra DadosDaObra,
    string? Responsavel,
    string? Registro,
    byte[]? Logotipo);

/// <summary>Uma foto no relatório: o número, a legenda automática e o que o usuário escreveu.</summary>
/// <param name="Legenda">"Foto 12 · 29/09/2026 08:15 · 2.1 Escavação · 60% concluído · Bloco A · Gustavo".</param>
public sealed record FotoNoRelatorio(int Numero, FotoTarefa Foto, string Tarefa, string Legenda)
{
    public string? Titulo => Foto.Titulo;
    public string? Texto => Foto.Descricao;
    public bool Destaque => Foto.Destaque;
}

/// <summary>Um grupo de fotos (uma tarefa, um dia ou um local), com o título e uma linha de detalhe.</summary>
public sealed record GrupoDoRelatorio(string Titulo, string? Detalhe, IReadOnlyList<FotoNoRelatorio> Fotos);

/// <summary>Uma página de fotos: o grupo, se é continuação dele, e as fotos com quantas colunas cada uma ocupa.</summary>
public sealed record PaginaDeFotos(GrupoDoRelatorio Grupo, bool Continuacao, IReadOnlyList<(FotoNoRelatorio Foto, int Colunas)> Fotos);

/// <summary>Duas fotos da mesma tarefa tiradas com poucos minutos de diferença: talvez baste uma.</summary>
public sealed record FotosParecidas(FotoNoRelatorio Primeira, FotoNoRelatorio Segunda);

/// <summary>O relatório pronto para ser desenhado em PDF ou Word: tudo calculado, nada a decidir no desenho.</summary>
public sealed record RelatorioFotografico(
    int Numero,
    OpcoesDoRelatorio Opcoes,
    DateTime GeradoEm,
    CabecalhoDoRelatorio Cabecalho,
    ResumoDoProjeto? Resumo,
    IReadOnlyList<GrupoDoRelatorio> Grupos,
    IReadOnlyList<PaginaDeFotos> Paginas,
    IReadOnlyList<FotosParecidas> Parecidas)
{
    public IEnumerable<FotoNoRelatorio> Fotos => Grupos.SelectMany(g => g.Fotos);

    /// <summary>Bloco 29: as páginas de antes e depois (com <see cref="OpcoesDoRelatorio.ComAntesEDepois"/>).</summary>
    public IReadOnlyList<AntesEDepoisNoRelatorio> AntesEDepois { get; init; } = [];

    /// <summary>Bloco 29: o texto do andamento e os pontos de atenção (nulo sem texto).</summary>
    public TextoDoRelatorio? Texto => Opcoes.Texto is { Vazio: false } texto ? texto : null;

    /// <summary>Bloco 37: o painel de indicadores no fim do período (com <see cref="OpcoesDoRelatorio.ComIndicadores"/>); nulo sem ele.</summary>
    public Indicadores.PainelDeIndicadores? Indicadores { get; init; }

    public int TotalDeFotos => Grupos.Sum(g => g.Fotos.Count);

    /// <summary>
    /// Capa (com o resumo), o texto do andamento, os indicadores, as páginas de fotos, as de antes e depois e o índice com as
    /// assinaturas. Um texto ou um índice muito longo pode ocupar mais de uma página: o desenho conta as páginas de verdade.
    /// </summary>
    public int TotalDePaginas => 1 + (Texto is null ? 0 : 1) + (Indicadores is null ? 0 : 1) + Paginas.Count + AntesEDepois.Count +
                                 (Opcoes.ComIndice || Opcoes.ComAssinaturas ? 1 : 0);

    /// <summary>"Relatório fotográfico nº 3 · 01/10/2026 a 31/10/2026".</summary>
    public string Titulo => $"Relatório fotográfico nº {Numero} · {Opcoes.De:dd/MM/yyyy} a {Opcoes.Ate:dd/MM/yyyy}";

    /// <summary>O nome sugerido para o arquivo: "Relatorio-fotografico-03-Residencia Silva".</summary>
    public string NomeSugerido => $"Relatório fotográfico {Numero:00} - {Cabecalho.Obra}";
}

/// <summary>
/// Monta o relatório fotográfico (Bloco 27): escolhe as fotos (período, tarefas, tags; nunca as tiradas do relatório),
/// agrupa (por tarefa na ordem da EDT, por dia ou por local), põe os destaques primeiro, numera, escreve as legendas,
/// pagina e aponta fotos parecidas. Puro: quem desenha o PDF e o Word só segue o resultado.
/// </summary>
public static class MontadorDoRelatorio
{
    /// <summary>Até quantos minutos de diferença duas fotos da mesma tarefa contam como parecidas.</summary>
    public const int MinutosDeFotosParecidas = 2;

    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    /// <param name="cronograma">O cronograma já calculado (as datas e a situação das tarefas vêm do cálculo).</param>
    /// <exception cref="DomainValidationException">Opções inválidas.</exception>
    public static RelatorioFotografico Montar(Cronograma cronograma, OpcoesDoRelatorio opcoes, CabecalhoDoRelatorio cabecalho,
        DateTime geradoEm, int? numero = null)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(opcoes);
        ArgumentNullException.ThrowIfNull(cabecalho);
        opcoes.Validar();

        var dataDeStatus = opcoes.Ate.ToDateTime(TimeOnly.MinValue);
        var andamento = AcompanhamentoDoCronograma.Calcular(cronograma, dataDeStatus);
        var edt = cronograma.NumeracaoEdt();
        var tarefas = Tarefas(cronograma, opcoes, andamento).ToList();
        var nomeDaTarefa = tarefas.ToDictionary(t => t.Id, t => $"{edt[t.Id]} {t.Nome}");

        var escolhidas = tarefas
            .SelectMany(t => t.Fotos.Select(f => (Tarefa: t, Foto: f)))
            .Where(x => x.Foto.NoRelatorio)
            .Where(x => DateOnly.FromDateTime(x.Foto.DataFoto) is var dia && dia >= opcoes.De && dia <= opcoes.Ate)
            .Where(x => opcoes.Tags.Count == 0 || x.Foto.Tags.Any(t => opcoes.Tags.Contains(t, StringComparer.CurrentCultureIgnoreCase)))
            .ToList();

        var grupos = opcoes.Agrupamento switch
        {
            AgrupamentoDoRelatorio.PorData => escolhidas
                .GroupBy(x => x.Foto.DataFoto.Date)
                .OrderBy(g => g.Key)
                .Select(g => (Titulo: Maiuscula(g.Key.ToString("dddd, dd/MM/yyyy", Brasil)), Detalhe: (string?)null,
                    Fotos: g.Select(x => x).ToList())),
            AgrupamentoDoRelatorio.PorLocal => escolhidas
                .GroupBy(x => x.Foto.Local ?? "")
                .OrderBy(g => g.Key.Length == 0 ? 1 : 0).ThenBy(g => g.Key, StringComparer.Create(Brasil, true))
                .Select(g => (Titulo: g.Key.Length == 0 ? "Sem local informado" : g.Key, Detalhe: (string?)null, Fotos: g.ToList())),
            _ => escolhidas
                .GroupBy(x => x.Tarefa)
                .OrderBy(g => tarefas.IndexOf(g.Key))
                .Select(g => (Titulo: nomeDaTarefa[g.Key.Id], Detalhe: (string?)DetalheDaTarefa(g.Key, andamento), Fotos: g.ToList())),
        };

        var proximo = 1;
        var montados = new List<GrupoDoRelatorio>();
        foreach (var (titulo, detalhe, fotos) in grupos)
            montados.Add(new GrupoDoRelatorio(titulo, detalhe,
            [
                .. fotos.OrderByDescending(x => x.Foto.Destaque).ThenBy(x => x.Foto.DataFoto).ThenBy(x => x.Foto.DataCadastro)
                    .Select(x => new FotoNoRelatorio(proximo, x.Foto, nomeDaTarefa[x.Tarefa.Id], Legenda(proximo++, x.Foto, nomeDaTarefa[x.Tarefa.Id]))),
            ]));

        var resumo = opcoes.ComResumo ? ResumoDoProjeto.Calcular(cronograma, andamento, dataDeStatus) : null;
        return new RelatorioFotografico(numero ?? cronograma.ProximoNumeroDeRelatorio, opcoes, geradoEm, cabecalho, resumo, montados,
            Paginar(montados, opcoes), Parecidas(montados))
        {
            AntesEDepois = opcoes.ComAntesEDepois ? MontarAntesEDepois(tarefas, opcoes, nomeDaTarefa) : [],
            Indicadores = opcoes.ComIndicadores
                ? Indicadores.PainelDeIndicadores.Montar(cronograma, opcoes.Ate, Planejamento.Periodicidade.Mensal)
                : null,
        };
    }

    // Por local (só fotos que podem ir ao relatório, das tarefas escolhidas): o antes é a primeira foto do local até o fim do
    // período; o depois, a última do período. Locais sem foto no período, ou com tudo num dia só, ficam de fora.
    private static List<AntesEDepoisNoRelatorio> MontarAntesEDepois(IReadOnlyList<Tarefa> tarefas, OpcoesDoRelatorio opcoes,
        IReadOnlyDictionary<Guid, string> nomeDaTarefa)
    {
        var lista = new List<AntesEDepoisNoRelatorio>();
        var fotos = tarefas.SelectMany(t => t.Fotos)
            .Where(f => f.NoRelatorio && f.Local is not null && DateOnly.FromDateTime(f.DataFoto) <= opcoes.Ate)
            .GroupBy(f => Fotos.FiltroDeFotos.Normalizar(f.Local));
        foreach (var grupo in fotos)
        {
            var doLocal = grupo.OrderBy(f => f.DataFoto).ThenBy(f => f.DataCadastro).ToList();
            var noPeriodo = doLocal.Where(f => DateOnly.FromDateTime(f.DataFoto) >= opcoes.De).ToList();
            if (noPeriodo.Count == 0 || doLocal[0].DataFoto.Date == noPeriodo[^1].DataFoto.Date)
                continue;
            var assunto = Fotos.AssuntoDaComparacao.DoLocal(doLocal[0].Local!);
            var comparacao = Fotos.AntesEDepois.Comparar(assunto, doLocal, doLocal[0], noPeriodo[^1])!;
            string Legenda(string qual, FotoTarefa foto) =>
                string.Join(" · ", new[]
                {
                    Fotos.ComparacaoAntesEDepois.Rotulo(qual, foto), nomeDaTarefa[foto.TarefaId],
                    foto.PercentualConcluidoNaData is { } p ? $"{p}% concluído" : null,
                }.OfType<string>());
            lista.Add(new AntesEDepoisNoRelatorio(assunto.Nome, comparacao.Antes, Legenda("Antes", comparacao.Antes),
                comparacao.Depois, Legenda("Depois", comparacao.Depois), comparacao.Evolucao));
        }
        return [.. lista.OrderBy(a => a.Local, StringComparer.Create(Brasil, ignoreCase: true))];
    }

    /// <summary>"Foto 12 · 29/09/2026 08:15 · 2.1 Escavação · 60% concluído · Bloco A · Gustavo" (só as partes que há).</summary>
    public static string Legenda(int numero, FotoTarefa foto, string tarefa)
    {
        var partes = new List<string> { $"Foto {numero}", foto.DataFoto.ToString("dd/MM/yyyy HH:mm", Brasil), tarefa };
        if (foto.PercentualConcluidoNaData is { } percentual)
            partes.Add($"{percentual}% concluído");
        if (foto.Local is { } local)
            partes.Add(local);
        if (foto.Autor is { } autor)
            partes.Add(autor);
        return string.Join(" · ", partes);
    }

    /// <summary>
    /// O período que começa no dia seguinte ao fim do último relatório (no primeiro, no início do projeto ou, se houver
    /// foto tirada antes dele, no dia da foto mais antiga) e vai até hoje; se o último já chega até hoje, só hoje.
    /// </summary>
    public static (DateOnly De, DateOnly Ate) DesdeOUltimo(Cronograma cronograma, DateOnly hoje)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        var primeiraFoto = cronograma.Tarefas.SelectMany(t => t.Fotos).Where(f => f.NoRelatorio)
            .Select(f => (DateOnly?)DateOnly.FromDateTime(f.DataFoto)).Min();
        var inicio = DateOnly.FromDateTime(cronograma.DataInicio);
        var de = cronograma.Relatorios.Count == 0
            ? primeiraFoto is { } foto && foto < inicio ? foto : inicio // Bloco 42: as fotos de antes do início entram no primeiro
            : cronograma.Relatorios.Max(r => r.Ate).AddDays(1);
        return de > hoje ? (hoje, hoje) : (de, hoje);
    }

    private static IEnumerable<Tarefa> Tarefas(Cronograma cronograma, OpcoesDoRelatorio opcoes,
        IReadOnlyDictionary<Guid, AndamentoDaTarefa> andamento)
    {
        var ramo = opcoes.Tarefas == TarefasDoRelatorio.DeUmRamo
            ? cronograma.DescendentesDe(opcoes.Ramo!.Value).Select(t => t.Id).Append(opcoes.Ramo.Value).ToHashSet()
            : null;
        var (de, ate) = (opcoes.De.ToDateTime(TimeOnly.MinValue), opcoes.Ate.ToDateTime(TimeOnly.MinValue));
        return cronograma.Tarefas.Where(t => opcoes.Tarefas switch
        {
            TarefasDoRelatorio.SoCriticas => t.EhCritica,
            TarefasDoRelatorio.SoAtrasadas => andamento.TryGetValue(t.Id, out var a) && a.Situacao == SituacaoDaTarefa.Atrasada,
            TarefasDoRelatorio.EmExecucaoNoPeriodo => (t.InicioMaisCedo ?? t.DataInicio).Date <= ate && (t.TerminoMaisCedo ?? t.DataTermino).Date >= de,
            TarefasDoRelatorio.DeUmRamo => ramo!.Contains(t.Id),
            _ => true,
        });
    }

    private static string Maiuscula(string texto) => texto.Length == 0 ? texto : char.ToUpper(texto[0], Brasil) + texto[1..];

    private static string DetalheDaTarefa(Tarefa tarefa, IReadOnlyDictionary<Guid, AndamentoDaTarefa> andamento)
    {
        var (inicio, termino) = (tarefa.InicioMaisCedo ?? tarefa.DataInicio, tarefa.TerminoMaisCedo ?? tarefa.DataTermino);
        var detalhe = $"Previsto {inicio:dd/MM/yyyy} a {termino:dd/MM/yyyy} · {tarefa.PercentualConcluido}% concluído";
        return andamento.TryGetValue(tarefa.Id, out var a) ? $"{detalhe} · {a.Texto}" : detalhe;
    }

    // Cada grupo começa numa página; uma foto de destaque ocupa a linha inteira quando há duas colunas.
    private static List<PaginaDeFotos> Paginar(IReadOnlyList<GrupoDoRelatorio> grupos, OpcoesDoRelatorio opcoes)
    {
        var (colunas, linhas) = opcoes.Grade;
        var paginas = new List<PaginaDeFotos>();
        foreach (var grupo in grupos)
        {
            var atual = new List<(FotoNoRelatorio, int)>();
            var ocupadas = 0;
            var continuacao = false;
            foreach (var foto in grupo.Fotos)
            {
                var largura = foto.Destaque ? colunas : 1;
                // Uma foto inteira por linha: se a linha atual já tem foto e esta é de destaque, ela começa outra linha.
                var inicioDaLinha = ocupadas % colunas == 0 ? ocupadas : ocupadas + (colunas - ocupadas % colunas);
                var posicao = largura == colunas ? inicioDaLinha : ocupadas;
                if (posicao + largura > colunas * linhas)
                {
                    paginas.Add(new PaginaDeFotos(grupo, continuacao, atual));
                    (atual, ocupadas, continuacao, posicao) = ([], 0, true, 0);
                }
                atual.Add((foto, largura));
                ocupadas = posicao + largura;
            }
            if (atual.Count > 0)
                paginas.Add(new PaginaDeFotos(grupo, continuacao, atual));
        }
        return paginas;
    }

    private static List<FotosParecidas> Parecidas(IReadOnlyList<GrupoDoRelatorio> grupos)
    {
        var porTarefa = grupos.SelectMany(g => g.Fotos).GroupBy(f => f.Foto.TarefaId);
        var parecidas = new List<FotosParecidas>();
        foreach (var fotos in porTarefa)
        {
            var ordenadas = fotos.OrderBy(f => f.Foto.DataFoto).ToList();
            for (var i = 1; i < ordenadas.Count; i++)
                if (ordenadas[i].Foto.DataFoto - ordenadas[i - 1].Foto.DataFoto <= TimeSpan.FromMinutes(MinutosDeFotosParecidas))
                    parecidas.Add(new FotosParecidas(ordenadas[i - 1], ordenadas[i]));
        }
        return [.. parecidas.OrderBy(p => p.Primeira.Numero)];
    }
}
