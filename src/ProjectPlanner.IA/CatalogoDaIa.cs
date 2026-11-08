namespace ProjectPlanner.IA;

/// <summary>Bloco 68: como o valor de um campo das atualizações chega no JSON.</summary>
public enum FormatoDoCampo
{
    Texto,
    Inteiro,
    Numero,
}

/// <summary>Bloco 68: um campo das atualizações da obra: o nome no JSON, o formato e, quando são fixos, os valores aceitos.</summary>
public sealed record CampoDaAtualizacao(string Nome, FormatoDoCampo Formato, IReadOnlyList<string>? Valores = null);

/// <summary>Bloco 68: um tipo de atualização da obra e os campos que ele usa (e as ações, nos tipos que têm ação).</summary>
public sealed record TipoNoCatalogo(string Nome, IReadOnlyList<string> Campos, IReadOnlyList<string>? Acoes = null);

/// <summary>
/// Bloco 68: o vocabulário das atualizações da obra num lugar só. Daqui saem o esquema que o Gemini segue, a leitura da
/// resposta (<see cref="FormatoDaResposta"/>), a lista dos campos de cada tipo nas instruções e os testes que conferem que
/// todo tipo tem como ser aplicado no programa. Um tipo ou campo novo entra aqui e no <see cref="AtualizacaoDaObra"/> (um teste
/// confere que os dois batem).
/// </summary>
public static class CatalogoDaIa
{
    public static IReadOnlyList<CampoDaAtualizacao> Campos { get; } =
    [
        new("tarefa", FormatoDoCampo.Texto),
        new("percentual", FormatoDoCampo.Numero),
        new("quantidade", FormatoDoCampo.Numero),
        new("medicao", FormatoDoCampo.Inteiro),
        new("de", FormatoDoCampo.Texto),
        new("ate", FormatoDoCampo.Texto),
        new("data", FormatoDoCampo.Texto),
        new("categoria", FormatoDoCampo.Texto),
        new("descricao", FormatoDoCampo.Texto),
        new("valor", FormatoDoCampo.Numero),
        new("documento", FormatoDoCampo.Texto),
        new("nome", FormatoDoCampo.Texto),
        new("os", FormatoDoCampo.Inteiro),
        new("titulo", FormatoDoCampo.Texto),
        new("executante", FormatoDoCampo.Texto),
        new("tipoDeExecutante", FormatoDoCampo.Texto, ["equipe", "terceiro"]),
        new("tarefas", FormatoDoCampo.Texto),
        new("novaTarefa", FormatoDoCampo.Texto),
        new("duracao", FormatoDoCampo.Inteiro),
        new("apos", FormatoDoCampo.Texto),
        new("prazo", FormatoDoCampo.Texto),
        new("pagamento", FormatoDoCampo.Texto, ["empreitada", "medicao", "diaria"]),
        new("retencao", FormatoDoCampo.Numero),
        new("acao", FormatoDoCampo.Texto, [.. TiposDeAtualizacao.AcoesDaOs.Concat(TiposDeAtualizacao.AcoesDaMedicao).Concat(TiposDeAtualizacao.AcoesDoCalendario)
            .Concat(TiposDeAtualizacao.AcoesDoQuadro).Distinct()]),
        new("unidade", FormatoDoCampo.Texto),
        new("contratante", FormatoDoCampo.Texto),
        new("contrato", FormatoDoCampo.Texto),
        new("endereco", FormatoDoCampo.Texto),
        new("tipoDoRecurso", FormatoDoCampo.Texto, ["profissional", "material", "equipamento"]),
        new("fornecedor", FormatoDoCampo.Texto),
        new("recurso", FormatoDoCampo.Texto),
        new("dias", FormatoDoCampo.Texto), // Bloco 71
        new("coluna", FormatoDoCampo.Texto), // Bloco 80
    ];

    public static IReadOnlyList<TipoNoCatalogo> Tipos { get; } =
    [
        new(TiposDeAtualizacao.Percentual, ["tarefa", "percentual"]),
        new(TiposDeAtualizacao.Medicao, ["tarefa", "quantidade", "percentual", "medicao", "de", "ate"]),
        new(TiposDeAtualizacao.Custo, ["data", "valor", "descricao", "categoria", "tarefa", "documento"]),
        new(TiposDeAtualizacao.Inicio, ["tarefa", "data"]),
        new(TiposDeAtualizacao.LinhaDeBase, ["nome"]),
        new(TiposDeAtualizacao.Os, ["os", "titulo", "executante", "tipoDeExecutante", "tarefas", "novaTarefa", "duracao", "apos", "de", "prazo",
            "pagamento", "retencao", "valor", "descricao"]),
        new(TiposDeAtualizacao.SituacaoOs, ["os", "acao", "percentual", "descricao", "documento"], TiposDeAtualizacao.AcoesDaOs),
        new(TiposDeAtualizacao.Tarefa, ["tarefa", "valor", "unidade", "quantidade", "prazo"]),
        new(TiposDeAtualizacao.Obra, ["contratante", "contrato", "endereco"]),
        new(TiposDeAtualizacao.Recurso, ["nome", "tipoDoRecurso", "unidade", "valor", "quantidade", "fornecedor"]),
        new(TiposDeAtualizacao.Atribuir, ["tarefa", "recurso", "quantidade"]),
        new(TiposDeAtualizacao.MedicaoDoContrato, ["medicao", "acao", "data"], TiposDeAtualizacao.AcoesDaMedicao),
        new(TiposDeAtualizacao.Calendario, ["acao", "data", "de", "ate", "nome", "descricao", "dias"], TiposDeAtualizacao.AcoesDoCalendario), // Bloco 71
        new(TiposDeAtualizacao.Quadro, ["acao", "tarefa", "coluna", "descricao", "nome"], TiposDeAtualizacao.AcoesDoQuadro), // Bloco 80
    ];

    public static CampoDaAtualizacao Campo(string nome) => Campos.First(c => c.Nome == nome);

    /// <summary>
    /// A atualização com os valores lidos de cada campo (pelo nome do catálogo). É o único lugar que liga os campos do JSON
    /// aos do <see cref="AtualizacaoDaObra"/>; um teste confere que cada campo vai para a propriedade de mesmo nome.
    /// </summary>
    public static AtualizacaoDaObra Montar(string tipo, IReadOnlyDictionary<string, object?> valores)
    {
        ArgumentNullException.ThrowIfNull(valores);
        string? T(string nome) => valores.GetValueOrDefault(nome) as string;
        int? I(string nome) => valores.GetValueOrDefault(nome) as int?;
        decimal? N(string nome) => valores.GetValueOrDefault(nome) as decimal?;
        return new AtualizacaoDaObra(tipo, T("tarefa"), N("percentual"), N("quantidade"), I("medicao"), T("de"), T("ate"), T("data"),
            T("categoria"), T("descricao"), N("valor"), T("documento"), T("nome"), I("os"), T("titulo"), T("executante"), T("tipoDeExecutante"),
            T("tarefas"), T("novaTarefa"), I("duracao"), T("apos"), T("prazo"), T("pagamento"), N("retencao"), T("acao"), T("unidade"),
            T("contratante"), T("contrato"), T("endereco"), T("tipoDoRecurso"), T("fornecedor"), T("recurso"), T("dias"), T("coluna"));
    }

    /// <summary>
    /// Para as instruções: os campos de cada tipo e os valores fixos ("percentual: tarefa, percentual. …"), para o assistente
    /// (qualquer provedor) mandar os nomes certos.
    /// </summary>
    public static string Referencia
    {
        get
        {
            var tipos = string.Join("; ", Tipos.Select(t => $"{t.Nome}: {string.Join(", ", t.Campos)}"));
            var fixos = string.Join("; ", Campos.Where(c => c.Valores is not null && c.Nome != "acao")
                .Select(c => $"{c.Nome} = {string.Join(", ", c.Valores!)}")
                .Concat(Tipos.Where(t => t.Acoes is not null).Select(t => $"acao de {t.Nome} = {string.Join(", ", t.Acoes!)}")));
            return "CAMPOS DE CADA ATUALIZAÇÃO (além de \"tipo\"; mande só os que usa): " + tipos + ".\n" +
                   "VALORES FIXOS: " + fixos + ".";
        }
    }
}
