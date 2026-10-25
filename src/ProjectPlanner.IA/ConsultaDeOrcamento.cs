namespace ProjectPlanner.IA;

/// <summary>Uma composição da base que serve para a tarefa (achada pelo App nas palavras do nome).</summary>
public sealed record CandidataParaIA(string Codigo, string Descricao, string Unidade, decimal Custo);

/// <summary>As horas de um profissional ou equipamento que a composição pede na tarefa, e quantos por dia isso dá.</summary>
public sealed record HorasNaTarefa(string Insumo, decimal Horas, decimal PorDia);

/// <summary>Uma tarefa (sem subtarefas) mandada ao consultor de orçamento.</summary>
/// <param name="Id">"T3": é como o consultor se refere à tarefa.</param>
/// <param name="Caminho">Os resumos acima dela ("Estrutura > Térreo").</param>
/// <param name="Composicao">A composição atual ("SINAPI 87495 ALVENARIA… (M2) · custo 52,53"), nula sem.</param>
/// <param name="Candidatas">As composições da base para escolher (vazia sem base ou sem nenhuma parecida).</param>
public sealed record TarefaParaOrcamento(
    string Id,
    string Edt,
    string Nome,
    string Caminho,
    int Dias,
    string? Unidade,
    decimal? Quantidade,
    bool QuantidadeEstimada,
    decimal? Valor,
    string? Composicao,
    string? Notas,
    IReadOnlyList<CandidataParaIA> Candidatas,
    IReadOnlyList<HorasNaTarefa> Equipe);

/// <summary>Um recurso do projeto: "R2", o nome, o tipo e quantos há por dia (nulo: sem limite informado).</summary>
public sealed record RecursoParaOrcamento(string Id, string Nome, string Tipo, decimal? Disponivel, decimal Pico);

/// <summary>Os grupos de sugestões que o usuário pediu.</summary>
[Flags]
public enum GruposDeSugestao
{
    Nenhum = 0,

    /// <summary>Composições da base e, sem uma adequada, preços estimados.</summary>
    Composicoes = 1,

    /// <summary>Quantidades das tarefas sem quantidade.</summary>
    Quantidades = 2,

    /// <summary>Revisão: o que falta, unidades, quantidades fora do padrão, BDI.</summary>
    Revisao = 4,

    /// <summary>Equipes por dia e durações coerentes com as horas das composições.</summary>
    Equipes = 8,

    Todos = Composicoes | Quantidades | Revisao | Equipes,
}

/// <summary>Bloco 38: o pedido de sugestões para o orçamento: a obra, a base, o BDI, as tarefas e os recursos.</summary>
/// <param name="Foco">O que o usuário pediu ("só as vedações"); nulo para o orçamento todo.</param>
/// <param name="Ficha">O que se sabe da obra (a ficha do assistente: área, pavimentos, padrão…), "Campo: valor".</param>
/// <param name="Base">"SINAPI SP 09/2026 desonerado"; nula sem base importada.</param>
public sealed record PedidoDeOrcamento(
    string Obra,
    string? Foco,
    GruposDeSugestao Grupos,
    IReadOnlyList<string> Ficha,
    string? Base,
    decimal Bdi,
    decimal? BdiDeMateriais,
    IReadOnlyList<TarefaParaOrcamento> Tarefas,
    IReadOnlyList<RecursoParaOrcamento> Recursos);

/// <summary>Os tipos de sugestão, como vêm no JSON.</summary>
public static class TiposDeSugestaoIA
{
    public const string Composicao = "composicao";
    public const string Estimativa = "estimativa";
    public const string Quantidade = "quantidade";
    public const string Duracao = "duracao";
    public const string Disponivel = "disponivel";
    public const string Bdi = "bdi";
    public const string Alerta = "alerta";

    public static IReadOnlyList<string> Todos { get; } = [Composicao, Estimativa, Quantidade, Duracao, Disponivel, Bdi, Alerta];
}

/// <summary>Uma sugestão como a IA mandou, já conferida (tipo conhecido, tarefa ou recurso do pedido, números válidos).</summary>
/// <param name="Tarefa">"T3" (composição, estimativa, quantidade, duração; opcional no alerta).</param>
/// <param name="Recurso">"R2" (disponível).</param>
/// <param name="Valor">Quantidade, dias, quantos por dia ou o BDI em %.</param>
public sealed record SugestaoDaIA(
    string Tipo,
    string Justificativa,
    string? Tarefa = null,
    string? Recurso = null,
    string? Codigo = null,
    string? Descricao = null,
    string? Unidade = null,
    decimal? Valor = null,
    decimal? CustoDeMateriais = null,
    decimal? CustoDeMaoDeObra = null,
    IReadOnlyList<PessoaDaEquipe>? Equipe = null);

/// <summary>
/// Bloco 54: uma função na equipe de uma tarefa, como a IA estimou: "Pedreiro", quantas pessoas por dia na duração da tarefa e
/// o custo de um dia de cada uma (com encargos), se ela informou.
/// </summary>
public sealed record PessoaDaEquipe(string Funcao, decimal Quantidade, decimal? CustoPorDia = null);

/// <summary>A resposta: um resumo curto e as sugestões.</summary>
public sealed record RespostaDoConsultor(string Resumo, IReadOnlyList<SugestaoDaIA> Sugestoes);

/// <summary>
/// Bloco 38: a IA sugerindo o orçamento (composições, preços estimados, quantidades, revisão e equipes). Tudo é sugestão: o
/// App mostra a lista para o usuário aceitar, e os preços das composições vêm sempre da base.
/// </summary>
public interface IConsultorDeOrcamento
{
    bool EstaConfigurado { get; }

    /// <exception cref="ErroDoAssistenteException">Falha da IA, já com a mensagem para o usuário.</exception>
    Task<RespostaDoConsultor> SugerirAsync(PedidoDeOrcamento pedido, CancellationToken cancelamento);
}

/// <summary>Os limites do pedido e da resposta.</summary>
public static class LimitesDoConsultor
{
    /// <summary>Tarefas por pedido (as que mais precisam vão primeiro).</summary>
    public const int Tarefas = 150;

    /// <summary>Candidatas de cada tarefa.</summary>
    public const int CandidatasPorTarefa = 10;

    /// <summary>Sugestões aceitas de uma resposta.</summary>
    public const int Sugestoes = 300;

    public const int TamanhoDaJustificativa = 400;

    /// <summary>Bloco 54: as tarefas de cada consulta quando o orçamento é da obra inteira (em lotes, para a resposta não sair cortada).</summary>
    public const int TarefasPorLote = 30;

    /// <summary>Bloco 54: funções na equipe de uma estimativa, e pessoas de cada uma por dia.</summary>
    public const int FuncoesNaEquipe = 6;
    public const int PessoasPorFuncao = 50;
}

/// <summary>Sem chave de API: avisa que é preciso configurar o assistente.</summary>
public sealed class ConsultorIndisponivel : IConsultorDeOrcamento
{
    public bool EstaConfigurado => false;

    public Task<RespostaDoConsultor> SugerirAsync(PedidoDeOrcamento pedido, CancellationToken cancelamento) =>
        Task.FromException<RespostaDoConsultor>(new ErroDoAssistenteException(AnalistaIndisponivel.Mensagem));
}

/// <summary>Usa a configuração do momento, como o assistente do painel.</summary>
public sealed class ConsultorConformeConfiguracao(IConfiguracaoIA configuracao, IProvedorIA provedor) : IConsultorDeOrcamento
{
    private readonly IConsultorDeOrcamento _semChave = new ConsultorIndisponivel();

    public bool EstaConfigurado => !string.IsNullOrWhiteSpace(configuracao.ChaveApi);

    public Task<RespostaDoConsultor> SugerirAsync(PedidoDeOrcamento pedido, CancellationToken cancelamento) =>
        (configuracao.ChaveApi is { Length: > 0 } chave && !string.IsNullOrWhiteSpace(chave)
            ? provedor.CriarConsultor(chave, configuracao.Modelo)
            : _semChave).SugerirAsync(pedido, cancelamento);
}
