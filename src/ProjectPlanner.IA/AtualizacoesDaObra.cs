namespace ProjectPlanner.IA;

/// <summary>Bloco 41: os tipos de atualização da obra que o assistente pode propor (o usuário confirma num cartão).</summary>
public static class TiposDeAtualizacao
{
    public const string Percentual = "percentual";
    public const string Medicao = "medicao";
    public const string Custo = "custo";
    public const string Inicio = "inicio";
    public const string LinhaDeBase = "linhaDeBase";

    public static IReadOnlyList<string> Todos { get; } = [Percentual, Medicao, Custo, Inicio, LinhaDeBase];
}

/// <summary>
/// Bloco 41: uma atualização da obra proposta pelo assistente, como ele a mandou. Diferente das alterações do cronograma
/// (Bloco 24), que entram direto, estas aparecem num cartão para o usuário aplicar ou descartar.
/// </summary>
/// <param name="Tarefa">O Nº da linha no retrato ("3"): percentual, medicao, inicio e, opcional, custo.</param>
/// <param name="Percentual">percentual: o % concluído (0 a 100); medicao: o % medido no período, num item sem quantidade.</param>
/// <param name="Quantidade">medicao: a quantidade medida no período, na unidade da tarefa.</param>
/// <param name="Medicao">medicao: o nº da medição em rascunho; vazio = a última em rascunho, ou uma nova.</param>
/// <param name="De">medicao nova: o início do período (AAAA-MM-DD).</param>
/// <param name="Ate">medicao nova: o fim do período (AAAA-MM-DD).</param>
/// <param name="Data">custo: a data (AAAA-MM-DD); inicio: a data mínima de início ("não iniciar antes de").</param>
/// <param name="Categoria">custo: material, mão de obra, equipamento, serviço, administração ou outros.</param>
/// <param name="Nome">linhaDeBase: o nome (opcional).</param>
public sealed record AtualizacaoDaObra(
    string Tipo,
    string? Tarefa = null,
    decimal? Percentual = null,
    decimal? Quantidade = null,
    int? Medicao = null,
    string? De = null,
    string? Ate = null,
    string? Data = null,
    string? Categoria = null,
    string? Descricao = null,
    decimal? Valor = null,
    string? Documento = null,
    string? Nome = null);

/// <summary>Bloco 41: as telas que o assistente pode abrir.</summary>
public static class TelasDoPrograma
{
    public const string Indicadores = "indicadores";
    public const string Orcamento = "orcamento";
    public const string Medicoes = "medicoes";
    public const string Custos = "custos";
    public const string Recursos = "recursos";
    public const string UsoDeRecursos = "usoDeRecursos";
    public const string FisicoFinanceiro = "fisicoFinanceiro";
    public const string RelatorioFotografico = "relatorioFotografico";
    public const string FotosDoProjeto = "fotosDoProjeto";
    public const string Resumo = "resumo";
    public const string LinhasDeBase = "linhasDeBase";

    public static IReadOnlyList<string> Todas { get; } =
        [Indicadores, Orcamento, Medicoes, Custos, Recursos, UsoDeRecursos, FisicoFinanceiro, RelatorioFotografico, FotosDoProjeto, Resumo, LinhasDeBase];

    /// <summary>As abas de cada tela que tem abas.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Abas { get; } = new Dictionary<string, IReadOnlyList<string>>
    {
        [Indicadores] = ["visaoGeral", "custos", "medicoes", "alertas"],
        [Orcamento] = ["planilha", "composicoes", "bases", "sugestoes"],
        [Medicoes] = ["medicoes", "aditivos", "contrato", "plano"],
    };

    /// <summary>"Indicadores", "Relatório fotográfico"…, para o botão no chat.</summary>
    public static string Nome(string tela) => tela switch
    {
        Indicadores => "Indicadores",
        Orcamento => "Orçamento",
        Medicoes => "Medições",
        Custos => "Custos realizados",
        Recursos => "Recursos",
        UsoDeRecursos => "Uso de recursos",
        FisicoFinanceiro => "Cronograma físico-financeiro",
        RelatorioFotografico => "Relatório fotográfico",
        FotosDoProjeto => "Fotos do projeto",
        Resumo => "Resumo do projeto",
        LinhasDeBase => "Linhas de base",
        _ => tela,
    };
}

/// <summary>Bloco 41: a tela que o assistente pede para abrir, com a aba e o que mostrar nela.</summary>
/// <param name="Medicao">medicoes: o nº da medição para selecionar.</param>
/// <param name="De">relatorioFotografico: o início do período (AAAA-MM-DD).</param>
public sealed record TelaParaAbrir(string Tela, string? Aba = null, int? Medicao = null, string? De = null, string? Ate = null)
{
    /// <summary>"Abrir Indicadores › custos".</summary>
    public string Rotulo => $"Abrir {TelasDoPrograma.Nome(Tela)}";
}
