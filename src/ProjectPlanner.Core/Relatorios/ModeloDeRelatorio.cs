using System.Globalization;
using System.Text.RegularExpressions;

namespace ProjectPlanner.Core.Relatorios;

/// <summary>Bloco 88: os blocos com que se monta um relatório personalizado.</summary>
public enum TipoDeSecao
{
    /// <summary>A capa: a faixa na cor do tema com o título, a obra, o período, os dados da obra e a foto mais recente.</summary>
    Capa,

    /// <summary>"A obra em números": os cartões (avanço, término, atrasadas, medido, custo, fotos) e a barra do avanço.</summary>
    Resumo,

    /// <summary>A página de indicadores (a Curva S, as etapas, as atrasadas), a mesma da tela Indicadores.</summary>
    Indicadores,

    /// <summary>As etapas (as tarefas do primeiro nível): as datas, o previsto, o realizado e a situação.</summary>
    Etapas,

    /// <summary>O cronograma em barras (até o nível escolhido), com a linha de hoje.</summary>
    Gantt,

    /// <summary>Uma tabela de tarefas (todas, em andamento, atrasadas ou concluídas).</summary>
    Tarefas,

    /// <summary>As tarefas que começam nos próximos dias (depois do período).</summary>
    Proximas,

    /// <summary>As fotos do período, numa grade (2, 4 ou 6 por página), com a tarefa, a data e a legenda.</summary>
    Fotos,

    /// <summary>O diário de obra (RDO) do período: um dia por linha e o resumo.</summary>
    Diario,

    /// <summary>As medições do contrato e o resumo dele.</summary>
    Medicoes,

    /// <summary>Os custos realizados no período, por categoria, e os maiores lançamentos.</summary>
    Custos,

    /// <summary>As ordens de serviço.</summary>
    OrdensDeServico,

    /// <summary>Um texto livre (observações, recomendações, o que mais precisar).</summary>
    Texto,

    /// <summary>As linhas de assinatura (o responsável técnico, o contratante e a fiscalização).</summary>
    Assinaturas,
}

/// <summary>Bloco 88: quais tarefas a seção <see cref="TipoDeSecao.Tarefas"/> mostra.</summary>
public enum FiltroDeTarefas
{
    Todas,
    EmAndamento,
    Atrasadas,
    Concluidas,
}

/// <summary>Bloco 88: o período com que o modelo abre (a tela ainda muda o período antes de gerar).</summary>
public enum PeriodoPadrao
{
    EsteMes,
    EstaSemana,
    MesPassado,
    DesdeOInicio,
}

/// <summary>Bloco 88: o tamanho da folha do relatório personalizado.</summary>
public enum PapelDoRelatorio
{
    A4,
    Carta,
}

/// <summary>
/// Bloco 88: uma seção do relatório personalizado e as opções dela (cada tipo usa as suas; as outras ficam como vieram).
/// </summary>
/// <param name="Tipo">O bloco.</param>
public sealed record SecaoDoRelatorio(TipoDeSecao Tipo)
{
    public const int TamanhoMaximoDoTitulo = 120;
    public const int TamanhoMaximoDoTexto = 8000;
    public static IReadOnlyList<int> FotosPorPaginaAceitas { get; } = [2, 4, 6];

    /// <summary>Desligada, a seção fica no modelo mas não sai no relatório.</summary>
    public bool Ativa { get; init; } = true;

    /// <summary>O título da seção; vazio, o do tipo (<see cref="TituloPadrao"/>).</summary>
    public string? Titulo { get; init; }

    /// <summary>Texto: o conteúdo (parágrafos separados por uma linha em branco); Capa: o subtítulo.</summary>
    public string? Texto { get; init; }

    /// <summary>Tarefas: quais.</summary>
    public FiltroDeTarefas Filtro { get; init; }

    /// <summary>Próximas atividades: quantos dias depois do período.</summary>
    public int Dias { get; init; } = 15;

    /// <summary>Fotos: 2, 4 ou 6 por página.</summary>
    public int FotosPorPagina { get; init; } = 4;

    /// <summary>Fotos: no máximo (as destacadas primeiro, depois as mais recentes).</summary>
    public int MaximoDeFotos { get; init; } = 24;

    /// <summary>Gantt: até que nível da EDT (1 = só as etapas).</summary>
    public int Nivel { get; init; } = 2;

    /// <summary>Capa: com a foto mais recente do período (a destacada, se houver).</summary>
    public bool ComFoto { get; init; } = true;

    /// <summary>A seção começa numa página nova.</summary>
    public bool PaginaNova { get; init; }

    public string TituloOuPadrao => string.IsNullOrWhiteSpace(Titulo) ? TituloPadrao(Tipo) : Titulo.Trim();

    /// <summary>"A obra em números", "Fotos do período"…</summary>
    public static string TituloPadrao(TipoDeSecao tipo) => tipo switch
    {
        TipoDeSecao.Capa => "Capa",
        TipoDeSecao.Resumo => "A obra em números",
        TipoDeSecao.Indicadores => "Indicadores e Curva S",
        TipoDeSecao.Etapas => "Andamento das etapas",
        TipoDeSecao.Gantt => "Cronograma",
        TipoDeSecao.Tarefas => "Tarefas",
        TipoDeSecao.Proximas => "Próximas atividades",
        TipoDeSecao.Fotos => "Fotos do período",
        TipoDeSecao.Diario => "Diário de obra",
        TipoDeSecao.Medicoes => "Medições do contrato",
        TipoDeSecao.Custos => "Custos realizados",
        TipoDeSecao.OrdensDeServico => "Ordens de serviço",
        TipoDeSecao.Texto => "Observações",
        _ => "Assinaturas",
    };

    /// <summary>O que a seção mostra, para a lista da tela.</summary>
    public static string Descricao(TipoDeSecao tipo) => tipo switch
    {
        TipoDeSecao.Capa => "Título, obra, período, dados da obra, logotipo e a foto mais recente",
        TipoDeSecao.Resumo => "Avanço previsto e realizado, término, atrasadas, medido, custo e fotos",
        TipoDeSecao.Indicadores => "A página da tela Indicadores, com a Curva S",
        TipoDeSecao.Etapas => "Cada etapa com as datas, o previsto, o realizado e a situação",
        TipoDeSecao.Gantt => "As barras do cronograma até o nível escolhido, com a linha de hoje",
        TipoDeSecao.Tarefas => "Uma tabela de tarefas: todas, em andamento, atrasadas ou concluídas",
        TipoDeSecao.Proximas => "As tarefas que começam logo depois do período",
        TipoDeSecao.Fotos => "As fotos do período numa grade, com a tarefa, a data e a legenda",
        TipoDeSecao.Diario => "O RDO de cada dia do período: clima, efetivo, serviços e ocorrências",
        TipoDeSecao.Medicoes => "As medições do contrato: o medido, o acumulado e o saldo",
        TipoDeSecao.Custos => "Os custos do período por categoria e os maiores lançamentos",
        TipoDeSecao.OrdensDeServico => "As OS: o executante, a situação, o prazo, o valor e o medido",
        TipoDeSecao.Texto => "Um texto seu (observações, recomendações, pendências)",
        _ => "As linhas para assinar: responsável técnico, contratante e fiscalização",
    };

    /// <exception cref="DomainValidationException">Uma opção fora do aceito.</exception>
    public void Validar()
    {
        if (Titulo?.Trim().Length > TamanhoMaximoDoTitulo)
            throw new DomainValidationException(nameof(Titulo), $"O título de \"{TituloPadrao(Tipo)}\" passa de {TamanhoMaximoDoTitulo} caracteres.");
        if (Texto?.Length > TamanhoMaximoDoTexto)
            throw new DomainValidationException(nameof(Texto), $"O texto de \"{TituloOuPadrao}\" passa de {TamanhoMaximoDoTexto} caracteres.");
        if (!FotosPorPaginaAceitas.Contains(FotosPorPagina))
            throw new DomainValidationException(nameof(FotosPorPagina), "As fotos vão 2, 4 ou 6 por página.");
        if (MaximoDeFotos is < 1 or > 200)
            throw new DomainValidationException(nameof(MaximoDeFotos), "O máximo de fotos vai de 1 a 200.");
        if (Dias is < 1 or > 90)
            throw new DomainValidationException(nameof(Dias), "As próximas atividades vão de 1 a 90 dias.");
        if (Nivel is < 1 or > 6)
            throw new DomainValidationException(nameof(Nivel), "O nível do cronograma vai de 1 a 6.");
    }
}

/// <summary>Bloco 88: a aparência do relatório personalizado.</summary>
public sealed partial record TemaDoRelatorio
{
    /// <summary>As cores da paleta da tela (azul, verde-água, verde, laranja, vermelho, roxo, grafite e vinho).</summary>
    public static IReadOnlyList<string> Cores { get; } = ["#1565C0", "#00897B", "#2E7D32", "#EF6C00", "#C62828", "#6A1B9A", "#37474F", "#AD1457"];

    public const int TamanhoMaximoDoRodape = 160;

    /// <summary>A cor principal (#RRGGBB): a faixa da capa, os títulos, as barras e as tabelas.</summary>
    public string Cor { get; init; } = Cores[0];

    public PapelDoRelatorio Papel { get; init; } = PapelDoRelatorio.A4;

    public bool Paisagem { get; init; }

    /// <summary>O logotipo das preferências dos relatórios na capa e no alto das páginas.</summary>
    public bool ComLogotipo { get; init; } = true;

    /// <summary>Um texto no rodapé de todas as páginas (a empresa, o CNPJ, o telefone); vazio, só a obra e o número da página.</summary>
    public string? Rodape { get; init; }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex CorValida();

    /// <exception cref="DomainValidationException">A cor não é #RRGGBB ou o rodapé é comprido demais.</exception>
    public void Validar()
    {
        if (!CorValida().IsMatch(Cor ?? ""))
            throw new DomainValidationException(nameof(Cor), "A cor do relatório precisa ser como #1565C0.");
        if (Rodape?.Trim().Length > TamanhoMaximoDoRodape)
            throw new DomainValidationException(nameof(Rodape), $"O rodapé passa de {TamanhoMaximoDoRodape} caracteres.");
    }
}

/// <summary>
/// Bloco 88: um modelo de relatório personalizado: o nome (como ele aparece na lista), o título que sai na capa e no alto das
/// páginas, a aparência e as seções, na ordem. Os modelos do usuário ficam nas preferências dele (servem para qualquer obra); os
/// prontos (<see cref="ModelosProntos"/>) são o ponto de partida.
/// </summary>
public sealed record ModeloDeRelatorio
{
    public const int TamanhoMaximoDoNome = 80;
    public const int TamanhoMaximoDoTitulo = 120;

    public string Nome { get; init; } = "Meu relatório";

    public string Titulo { get; init; } = "Relatório da obra";

    public TemaDoRelatorio Tema { get; init; } = new();

    /// <summary>O período com que o modelo abre na tela (o semanal, na semana; o mensal, no mês passado).</summary>
    public PeriodoPadrao Periodo { get; init; } = PeriodoPadrao.EsteMes;

    public IReadOnlyList<SecaoDoRelatorio> Secoes { get; init; } = [];

    /// <summary>As seções que saem no relatório, na ordem.</summary>
    public IReadOnlyList<SecaoDoRelatorio> Ativas => [.. Secoes.Where(s => s.Ativa)];

    /// <exception cref="DomainValidationException">Sem nome, sem título, sem nenhuma seção ligada, ou uma opção fora do aceito.</exception>
    public void Validar()
    {
        if (string.IsNullOrWhiteSpace(Nome))
            throw new DomainValidationException(nameof(Nome), "Dê um nome ao modelo.");
        if (Nome.Trim().Length > TamanhoMaximoDoNome)
            throw new DomainValidationException(nameof(Nome), $"O nome do modelo passa de {TamanhoMaximoDoNome} caracteres.");
        if (string.IsNullOrWhiteSpace(Titulo))
            throw new DomainValidationException(nameof(Titulo), "Dê um título ao relatório.");
        if (Titulo.Trim().Length > TamanhoMaximoDoTitulo)
            throw new DomainValidationException(nameof(Titulo), $"O título passa de {TamanhoMaximoDoTitulo} caracteres.");
        if (Ativas.Count == 0)
            throw new DomainValidationException(nameof(Secoes), "Ligue pelo menos uma seção.");
        Tema.Validar();
        foreach (var secao in Secoes)
            secao.Validar();
    }
}

/// <summary>Bloco 88: os modelos que vêm com o programa (o usuário parte de um e salva o seu com outro nome).</summary>
public static class ModelosProntos
{
    public static ModeloDeRelatorio Mensal { get; } = new()
    {
        Nome = "Relatório mensal ao cliente",
        Titulo = "Relatório mensal de obra",
        Periodo = PeriodoPadrao.MesPassado,
        Secoes =
        [
            new(TipoDeSecao.Capa), new(TipoDeSecao.Resumo), new(TipoDeSecao.Etapas), new(TipoDeSecao.Gantt) { Nivel = 1 },
            new(TipoDeSecao.Fotos) { FotosPorPagina = 4, MaximoDeFotos = 24, PaginaNova = true }, new(TipoDeSecao.Medicoes),
            new(TipoDeSecao.Texto) { Ativa = false }, new(TipoDeSecao.Assinaturas),
        ],
    };

    public static ModeloDeRelatorio Semanal { get; } = new()
    {
        Nome = "Relatório semanal da obra",
        Titulo = "Relatório semanal de obra",
        Periodo = PeriodoPadrao.EstaSemana,
        Tema = new() { Cor = TemaDoRelatorio.Cores[1] },
        Secoes =
        [
            new(TipoDeSecao.Capa) { ComFoto = false }, new(TipoDeSecao.Resumo), new(TipoDeSecao.Tarefas) { Filtro = FiltroDeTarefas.EmAndamento, Titulo = "Em andamento" },
            new(TipoDeSecao.Tarefas) { Filtro = FiltroDeTarefas.Atrasadas, Titulo = "Atrasadas" }, new(TipoDeSecao.Proximas) { Dias = 7 },
            new(TipoDeSecao.Diario), new(TipoDeSecao.Fotos) { FotosPorPagina = 6, MaximoDeFotos = 18, PaginaNova = true },
        ],
    };

    public static ModeloDeRelatorio Executivo { get; } = new()
    {
        Nome = "Resumo executivo",
        Titulo = "Resumo executivo da obra",
        Tema = new() { Cor = TemaDoRelatorio.Cores[6] },
        Secoes =
        [
            new(TipoDeSecao.Resumo), new(TipoDeSecao.Etapas), new(TipoDeSecao.Tarefas) { Filtro = FiltroDeTarefas.Atrasadas, Titulo = "Pontos de atenção" },
            new(TipoDeSecao.Proximas) { Dias = 15 },
        ],
    };

    public static ModeloDeRelatorio Financeiro { get; } = new()
    {
        Nome = "Relatório financeiro",
        Titulo = "Relatório financeiro da obra",
        Periodo = PeriodoPadrao.MesPassado,
        Tema = new() { Cor = TemaDoRelatorio.Cores[2] },
        Secoes =
        [
            new(TipoDeSecao.Capa) { ComFoto = false }, new(TipoDeSecao.Resumo), new(TipoDeSecao.Indicadores), new(TipoDeSecao.Medicoes),
            new(TipoDeSecao.Custos), new(TipoDeSecao.OrdensDeServico), new(TipoDeSecao.Assinaturas),
        ],
    };

    public static IReadOnlyList<ModeloDeRelatorio> Todos { get; } = [Mensal, Semanal, Executivo, Financeiro];

    /// <summary>Um modelo pronto com este nome (sem diferença de maiúsculas); nulo se não é um deles.</summary>
    public static ModeloDeRelatorio? PeloNome(string? nome) =>
        Todos.FirstOrDefault(m => string.Equals(m.Nome, nome?.Trim(), StringComparison.CurrentCultureIgnoreCase));
}

/// <summary>Bloco 88: o período do relatório (os dois dias inclusive) e os atalhos da tela.</summary>
public sealed record PeriodoDoRelatorio
{
    /// <exception cref="DomainValidationException">O período termina antes de começar.</exception>
    public PeriodoDoRelatorio(DateOnly de, DateOnly ate)
    {
        if (ate < de)
            throw new DomainValidationException(nameof(Ate), "O período termina antes de começar.");
        (De, Ate) = (de, ate);
    }

    public DateOnly De { get; }
    public DateOnly Ate { get; }

    public bool Contem(DateOnly dia) => dia >= De && dia <= Ate;

    public bool Contem(DateTime instante) => Contem(DateOnly.FromDateTime(instante));

    /// <summary>"01/09/2026 a 30/09/2026" (um dia só: "28/09/2026").</summary>
    public string Texto => De == Ate ? Data(De) : $"{Data(De)} a {Data(Ate)}";

    /// <summary>De segunda até hoje.</summary>
    public static PeriodoDoRelatorio EstaSemana(DateOnly hoje) => new(hoje.AddDays(-(((int)hoje.DayOfWeek + 6) % 7)), hoje);

    /// <summary>Do dia 1º até hoje.</summary>
    public static PeriodoDoRelatorio EsteMes(DateOnly hoje) => new(new DateOnly(hoje.Year, hoje.Month, 1), hoje);

    /// <summary>O mês anterior inteiro.</summary>
    public static PeriodoDoRelatorio MesPassado(DateOnly hoje)
    {
        var primeiro = new DateOnly(hoje.Year, hoje.Month, 1).AddMonths(-1);
        return new(primeiro, primeiro.AddMonths(1).AddDays(-1));
    }

    /// <summary>Do início da obra até hoje (ou até o início, se ela ainda não começou).</summary>
    public static PeriodoDoRelatorio DesdeOInicio(DateOnly inicio, DateOnly hoje) => new(inicio, hoje < inicio ? inicio : hoje);

    /// <summary>O período de um atalho.</summary>
    public static PeriodoDoRelatorio Do(PeriodoPadrao padrao, DateOnly inicioDaObra, DateOnly hoje) => padrao switch
    {
        PeriodoPadrao.EstaSemana => EstaSemana(hoje),
        PeriodoPadrao.MesPassado => MesPassado(hoje),
        PeriodoPadrao.DesdeOInicio => DesdeOInicio(inicioDaObra, hoje),
        _ => EsteMes(hoje),
    };

    private static string Data(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
