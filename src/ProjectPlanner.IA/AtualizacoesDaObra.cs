namespace ProjectPlanner.IA;

/// <summary>Bloco 41: os tipos de atualização da obra que o assistente pode propor (o usuário confirma num cartão).</summary>
public static class TiposDeAtualizacao
{
    public const string Percentual = "percentual";
    public const string Medicao = "medicao";
    public const string Custo = "custo";
    public const string Inicio = "inicio";
    public const string LinhaDeBase = "linhaDeBase";

    /// <summary>Bloco 63: criar uma ordem de serviço (sem "os") ou alterar uma em rascunho (com "os").</summary>
    public const string Os = "os";

    /// <summary>Bloco 63: levar uma OS a outra situação (emitir, aceitar, iniciar, concluir, conferir, cancelar…).</summary>
    public const string SituacaoOs = "situacaoOs";

    /// <summary>Bloco 67: o valor, a unidade, a quantidade e o prazo de uma tarefa.</summary>
    public const string Tarefa = "tarefa";

    /// <summary>Bloco 67: os dados da obra (contratante, contrato, endereço).</summary>
    public const string Obra = "obra";

    /// <summary>Bloco 67: criar um recurso do projeto (ou mudar o de mesmo nome).</summary>
    public const string Recurso = "recurso";

    /// <summary>Bloco 67: pôr um recurso numa tarefa (quantidade zero tira).</summary>
    public const string Atribuir = "atribuir";

    /// <summary>Bloco 67: aprovar ou marcar como paga uma medição do contrato.</summary>
    public const string MedicaoDoContrato = "medicaoContrato";

    public static IReadOnlyList<string> Todos { get; } =
        [Percentual, Medicao, Custo, Inicio, LinhaDeBase, Os, SituacaoOs, Tarefa, Obra, Recurso, Atribuir, MedicaoDoContrato];

    /// <summary>Bloco 63: as ações de "situacaoOs" (Bloco 67: pagar, liberar a retenção e duplicar).</summary>
    public static IReadOnlyList<string> AcoesDaOs { get; } =
        ["emitir", "aceitar", "recusar", "iniciar", "concluir", "conferir", "reprovar", "cancelar", "rascunho", "medir", "pagar", "liberar", "duplicar"];

    /// <summary>Bloco 67: as ações de "medicaoContrato".</summary>
    public static IReadOnlyList<string> AcoesDaMedicao { get; } = ["aprovar", "pagar"];
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
/// <param name="Os">Bloco 63: o número da OS (os: para alterar; situacaoOs: qual).</param>
/// <param name="Titulo">os: o serviço em poucas palavras.</param>
/// <param name="Executante">os: a equipe ou o empreiteiro.</param>
/// <param name="TipoDeExecutante">os: "equipe" (própria) ou "terceiro".</param>
/// <param name="Tarefas">os: os Nº das tarefas da OS ("3, 4"); uma etapa traz as de dentro.</param>
/// <param name="NovaTarefa">os: o nome de uma tarefa que ainda não existe no cronograma (criada junto, para a OS).</param>
/// <param name="Duracao">os, com nova tarefa: a duração em dias úteis.</param>
/// <param name="Apos">os, com nova tarefa: o Nº da predecessora (a nova entra logo depois dela, no mesmo nível).</param>
/// <param name="Prazo">os: o prazo (AAAA-MM-DD); o início vai em "de".</param>
/// <param name="Pagamento">os: "empreitada", "medicao" ou "diaria".</param>
/// <param name="Retencao">os: a retenção de garantia em %.</param>
/// <param name="Acao">situacaoOs: emitir, aceitar, recusar, iniciar, concluir, conferir, reprovar, cancelar ou rascunho.</param>
/// <param name="Unidade">Bloco 67: tarefa e recurso: a unidade (m², m³, un, dia…).</param>
/// <param name="Contratante">Bloco 67: obra.</param>
/// <param name="Contrato">Bloco 67: obra: o número ou o nome do contrato.</param>
/// <param name="Endereco">Bloco 67: obra.</param>
/// <param name="TipoDoRecurso">Bloco 67: recurso: profissional, material ou equipamento.</param>
/// <param name="Fornecedor">Bloco 67: recurso.</param>
/// <param name="Recurso">Bloco 67: atribuir: o nome do recurso.</param>
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
    string? Nome = null,
    int? Os = null,
    string? Titulo = null,
    string? Executante = null,
    string? TipoDeExecutante = null,
    string? Tarefas = null,
    string? NovaTarefa = null,
    int? Duracao = null,
    string? Apos = null,
    string? Prazo = null,
    string? Pagamento = null,
    decimal? Retencao = null,
    string? Acao = null,
    string? Unidade = null,
    string? Contratante = null,
    string? Contrato = null,
    string? Endereco = null,
    string? TipoDoRecurso = null,
    string? Fornecedor = null,
    string? Recurso = null);

/// <summary>Bloco 42: de onde vêm as fotos que o assistente pede para importar.</summary>
public static class OrigensDasFotos
{
    public const string Anexos = "anexos";
    public const string Pasta = "pasta";

    public static IReadOnlyList<string> Todas { get; } = [Anexos, Pasta];
}

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
    public const string OrdensDeServico = "ordensDeServico"; // Bloco 64
    public const string RelatorioDasOrdens = "relatorioDasOrdens";
    public const string ImprimirGantt = "imprimirGantt"; // Bloco 67

    public static IReadOnlyList<string> Todas { get; } =
        [Indicadores, Orcamento, Medicoes, Custos, Recursos, UsoDeRecursos, FisicoFinanceiro, RelatorioFotografico, FotosDoProjeto, Resumo, LinhasDeBase,
         OrdensDeServico, RelatorioDasOrdens, ImprimirGantt];

    /// <summary>As abas de cada tela que tem abas.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Abas { get; } = new Dictionary<string, IReadOnlyList<string>>
    {
        [Indicadores] = ["visaoGeral", "custos", "medicoes", "alertas"],
        [Orcamento] = ["planilha", "composicoes", "bases", "sugestoes"],
        [Medicoes] = ["medicoes", "aditivos", "contrato", "plano"],
        [RelatorioDasOrdens] = ["todas", "abertas", "encerradas", "vencidas"], // Bloco 67: as situações
        [ImprimirGantt] = ["A4", "A3", "A2", "A1", "A0", "Carta", "Oficio"], // Bloco 67: a folha
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
        OrdensDeServico => "Ordens de serviço",
        RelatorioDasOrdens => "Relatório das ordens de serviço",
        ImprimirGantt => "Imprimir o Gantt",
        _ => tela,
    };
}

/// <summary>Bloco 41: a tela que o assistente pede para abrir, com a aba e o que mostrar nela.</summary>
/// <param name="Medicao">medicoes: o nº da medição para selecionar.</param>
/// <param name="De">relatorioFotografico: o início do período (AAAA-MM-DD).</param>
/// <param name="Texto">Bloco 42, relatorioFotografico: já pedir à IA o texto do andamento.</param>
/// <param name="Os">Bloco 66: a OS (relatorioFotografico de uma OS; ordensDeServico já nela).</param>
public sealed record TelaParaAbrir(string Tela, string? Aba = null, int? Medicao = null, string? De = null, string? Ate = null, bool Texto = false, int? Os = null)
{
    /// <summary>"Abrir Indicadores › custos".</summary>
    public string Rotulo => $"Abrir {TelasDoPrograma.Nome(Tela)}";
}
