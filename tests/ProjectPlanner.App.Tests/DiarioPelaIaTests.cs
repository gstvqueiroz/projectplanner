using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.Diario;
using ProjectPlanner.IA;
using ProjectPlanner.IA.Conta;

namespace ProjectPlanner.App.Tests;

// Bloco 82: o diário de obra (RDO) pela IA, no painel do assistente e pelo Telegram (que chegam ao mesmo cartão): o tipo
// "diario", com o clima de um período, o efetivo, os equipamentos, os serviços e as ocorrências; o dia que não existe é criado
// (sem as listas do cronograma); um dia fechado não muda; o diário vai no retrato; a IA abre o diário no dia. "Hoje" é segunda
// 28/09/2026. Tarefas: 1 Escavação (3 dias) e 2 Formas (2 dias, depois dela).
public class DiarioPelaIaTests
{
    private static readonly DateOnly D28 = new(2026, 9, 28), D29 = new(2026, 9, 29);

    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly TarefaViewModel _escavacao;

    public DiarioPelaIaTests()
    {
        _vm = _ambiente.CriarViewModel();
        _escavacao = Ambiente.AdicionarTarefa(_vm, "Escavação", 3);
        Ambiente.AdicionarTarefa(_vm, "Formas", 2, "1");
    }

    private static AtualizacaoDaObra R(string acao, string? data = null, string? periodo = null, string? nome = null, string? praticavel = null,
        decimal? quantidade = null, string? fornecedor = null, string? tarefa = null, string? descricao = null, decimal? percentual = null) =>
        new(TiposDeAtualizacao.Diario, tarefa, percentual, quantidade, Data: data, Descricao: descricao, Nome: nome, Acao: acao, Fornecedor: fornecedor,
            Periodo: periodo, Praticavel: praticavel);

    private IReadOnlyList<ItemDoCartao> Conferir(params AtualizacaoDaObra[] atualizacoes) => _vm.ConferirAtualizacoes(atualizacoes, _vm.RetratoParaOAssistente());

    private async Task Aplicar(params AtualizacaoDaObra[] atualizacoes)
    {
        var cartao = Conferir(atualizacoes);
        cartao.Should().OnlyContain(i => i.Valido);
        (await _vm.AplicarAtualizacoesAsync([.. cartao.Select(i => i.Operacao!)], "Diário")).Situacao.Should().Be(SituacaoDaAplicacao.Aplicada);
    }

    [Fact]
    public async Task TodasAsAcoes_NumDiaNovo_PeloCartao_NumPassoDoDesfazer()
    {
        var cartao = Conferir(
            R("clima", periodo: "tarde", nome: "chuva", praticavel: "nao"),
            R("efetivo", nome: "Pedreiro", quantidade: 4, fornecedor: "Construtora Alfa"),
            R("efetivo", nome: "Servente", quantidade: 3),
            R("equipamento", nome: "Betoneira", quantidade: 1),
            R("servico", tarefa: "1", percentual: 40),
            R("servico", descricao: "Limpeza do canteiro"),
            R("ocorrencia", descricao: "Visita da fiscalização às 10h."));

        cartao.Select(i => (i.Texto, i.Problema)).Should().Equal(
            ("RDO de 28/09/2026 (dia novo): tarde: chuva, impraticável", null),
            ("RDO de 28/09/2026 (dia novo): efetivo Pedreiro = 4 (Construtora Alfa)", null),
            ("RDO de 28/09/2026 (dia novo): efetivo Servente = 3", null),
            ("RDO de 28/09/2026 (dia novo): equipamento Betoneira = 1", null),
            ("RDO de 28/09/2026 (dia novo): serviço 1 Escavação (40% no fim do dia)", null),
            ("RDO de 28/09/2026 (dia novo): serviço Limpeza do canteiro", null),
            ("RDO de 28/09/2026 (dia novo): ocorrência \"Visita da fiscalização às 10h.\"", null));

        var resultado = await _vm.AplicarAtualizacoesAsync([.. cartao.Select(i => i.Operacao!)], "Diário");

        resultado.Situacao.Should().Be(SituacaoDaAplicacao.Aplicada);
        var dia = _vm.Cronograma.Diario.Dia(D28)!;
        (dia.Manha, dia.Tarde).Should().Be((ClimaDoPeriodo.Bom, new ClimaDoPeriodo(TempoNoPeriodo.Chuva, false)));
        dia.Efetivo.Should().Equal(new EfetivoNoDia("Pedreiro", 4, "Construtora Alfa"), new EfetivoNoDia("Servente", 3));
        dia.Equipamentos.Should().Equal(new EquipamentoNoDia("Betoneira", 1));
        dia.Atividades.Should().Equal(new AtividadeNoDia(_escavacao.Tarefa.Id, "1 Escavação", 40), new AtividadeNoDia(null, "Limpeza do canteiro"));
        (dia.Ocorrencias, dia.Fechado).Should().Be(("Visita da fiscalização às 10h.", false));
        _escavacao.PercentualConcluido.Should().Be(0, "o % do diário não muda o cronograma");

        _vm.TextoDesfazer.Should().Be("Desfazer Atualizações do assistente");
        _vm.DesfazerAlteracaoCommand.Execute(null);
        _vm.Cronograma.Diario.Vazio.Should().BeTrue();
    }

    [Fact]
    public async Task DiaQueJaExiste_MudaSoOQueVeio_PeloNomeSemPlural_EZeroTira()
    {
        _vm.Cronograma.Diario.Abrir(new DiaDaObra(D28)
        {
            Efetivo = [new EfetivoNoDia("Pedreiro", 2, "Construtora Alfa"), new EfetivoNoDia("Servente", 4)],
            Equipamentos = [new EquipamentoNoDia("Betoneira", 1)],
            Atividades = [new AtividadeNoDia(_escavacao.Tarefa.Id, "1 Escavação", 20)],
            Ocorrencias = "Entrega de cimento.",
        });

        AtualizacaoDaObra[] pedidas =
        [
            R("efetivo", nome: "pedreiros", quantidade: 5),
            R("efetivo", nome: "Serventes", quantidade: 0),
            R("equipamento", nome: "betoneira", quantidade: 0),
            R("servico", tarefa: "1", percentual: 50),
            R("ocorrencia", descricao: "entrega de cimento."),
            R("ocorrencia", descricao: "Visita do fiscal."),
            R("clima", periodo: "dia", nome: "nublado"),
        ];
        var cartao = Conferir(pedidas);

        cartao.Select(i => i.Texto).Should().Equal(
            "RDO de 28/09/2026: efetivo pedreiros = 5",
            "RDO de 28/09/2026: tirar Serventes do efetivo",
            "RDO de 28/09/2026: tirar betoneira dos equipamentos",
            "RDO de 28/09/2026: serviço 1 Escavação (50% no fim do dia)",
            "RDO de 28/09/2026: ocorrência \"entrega de cimento.\"",
            "RDO de 28/09/2026: ocorrência \"Visita do fiscal.\"",
            "RDO de 28/09/2026: manhã e tarde: nublado");
        await Aplicar(pedidas);

        var dia = _vm.Cronograma.Diario.Dia(D28)!;
        dia.Efetivo.Should().Equal([new EfetivoNoDia("Pedreiro", 5, "Construtora Alfa")], "a empresa e o nome da linha ficam");
        dia.Equipamentos.Should().BeEmpty();
        dia.Atividades.Should().Equal(new AtividadeNoDia(_escavacao.Tarefa.Id, "1 Escavação", 50));
        dia.Ocorrencias.Should().Be("Entrega de cimento.\nVisita do fiscal.", "a que já estava não se repete");
        (dia.Manha, dia.Tarde).Should().Be((new ClimaDoPeriodo(TempoNoPeriodo.Nublado), new ClimaDoPeriodo(TempoNoPeriodo.Nublado)));
    }

    [Fact]
    public void OsProblemas_SaoExplicados_EODiaFechadoNaoMuda()
    {
        _vm.Cronograma.Diario.Abrir(new DiaDaObra(D28));
        _vm.Cronograma.Diario.Fechar(D28, new DateTime(2026, 9, 28, 18, 0, 0), "Eng. Ana");

        var cartao = Conferir(
            R("clima", data: "2026-09-29"),
            R("clima", data: "2026-09-29", nome: "granizo"),
            R("clima", data: "2026-09-29", periodo: "noite", nome: "chuva"),
            R("efetivo", data: "2026-09-29", quantidade: 2),
            R("efetivo", data: "2026-09-29", nome: "Armador"),
            R("efetivo", data: "2026-09-29", nome: "Armador", quantidade: 2.5m),
            R("efetivo", data: "2026-09-29", nome: "Armador", quantidade: 0),
            R("servico", data: "2026-09-29", tarefa: "9"),
            R("servico", data: "2026-09-29"),
            R("servico", data: "2026-09-29", tarefa: "1", percentual: 120),
            R("ocorrencia", data: "2026-09-29"),
            R("fechar", data: "2026-09-29"),
            R("ocorrencia", data: "ontem", descricao: "Chuva."),
            R("ocorrencia", descricao: "Visita."),
            R("ocorrencia", data: "2026-09-25", descricao: "Visita."));

        cartao.Select(i => i.Problema).Should().Equal(
            "informe o tempo (bom, nublado ou chuva) ou se foi praticável",
            "o tempo é bom, nublado ou chuva",
            "o período é manha, tarde ou dia",
            "informe a função (nome)",
            "informe a quantidade",
            "a quantidade vai de 0 a 999 (0 tira)",
            "O efetivo de 29/09/2026 não tem \"Armador\".",
            "não há a tarefa nº 9 no cronograma",
            "informe a tarefa ou a descrição do serviço",
            "o % precisa ir de 0 a 100",
            "informe a ocorrência (descricao)",
            "ação desconhecida (clima, efetivo, equipamento, servico, ocorrencia)",
            "não entendi a data (AAAA-MM-DD)",
            "O dia 28/09/2026 está fechado (assinado): reabra-o para mudar.",
            "O dia 25/09/2026 ficaria antes do último dia fechado (RDO nº 1, 28/09/2026): os números dos dias fechados não podem mudar.");
        _vm.Cronograma.Diario.Dias.Should().ContainSingle("o cartão confere em cópias");
    }

    [Fact]
    public async Task Retrato_TemOResumoEOsDiasMaisRecentes_EAsInstrucoesExplicam()
    {
        await Aplicar(
            R("clima", periodo: "tarde", nome: "chuva", praticavel: "nao"),
            R("efetivo", nome: "Pedreiro", quantidade: 4, fornecedor: "Construtora Alfa"),
            R("servico", tarefa: "1", percentual: 40),
            R("ocorrencia", descricao: "Visita da fiscalização."),
            R("ocorrencia", data: "2026-09-29", descricao: "Entrega do aço."));
        _vm.Cronograma.Diario.Fechar(D28, new DateTime(2026, 9, 28, 18, 0, 0), null);

        var retrato = _vm.RetratoParaOAssistente();

        retrato.Diario.Should().Equal(
            "2 dias; o último fechado (assinado: não muda, e não há dia novo antes dele) é o RDO nº 001, de 28/09/2026.",
            "RDO nº 001 · 28/09/2026 (seg) · fechado · manhã: Bom, praticável; tarde: Chuva, impraticável · efetivo 4: Pedreiro 4 (Construtora Alfa) · " +
            "serviços: 1 Escavação 40% · ocorrências: Visita da fiscalização.",
            "RDO nº 002 · 29/09/2026 (ter) · aberto · manhã: Bom, praticável; tarde: Bom, praticável · efetivo: nenhum · ocorrências: Entrega do aço.");
        retrato.ComoTexto().Should().Contain("Diário de obra (RDO): 2 dias; o último fechado")
            .And.Contain("- RDO nº 002 · 29/09/2026 (ter) · aberto");

        for (var i = 2; i < 12; i++)
            _vm.Cronograma.Diario.Abrir(new DiaDaObra(D28.AddDays(i)));
        var longo = _vm.RetratoParaOAssistente().Diario!;
        (longo.Count, longo[0], longo[1]).Should().Be((11, "12 dias; o último fechado (assinado: não muda, e não há dia novo antes dele) é o RDO nº 001, " +
            "de 28/09/2026; abaixo, os 10 mais recentes.", "RDO nº 003 · 30/09/2026 (qua) · aberto · manhã: Bom, praticável; tarde: Bom, praticável · efetivo: nenhum"));

        InstrucoesDoAssistente.Controle.Should().Contain("registre com o tipo diario").And.Contain("fechar o dia é com o usuário no programa")
            .And.Contain("diario com de = o dia");
        InstrucoesDoAssistente.Obra.Should().Contain("ou o dia (de) do diario");
        InstrucoesDoAssistente.Telegram.Should().Contain("o quadro Kanban").And.Contain("o diário de obra (o RDO do dia")
            .And.Contain("Fechar (assinar) o RDO só no programa", "senão a IA do Telegram recusa o RDO e o Kanban");
        CatalogoDaIa.Referencia.Should().Contain("diario: acao, data, periodo, nome, praticavel, quantidade, fornecedor, tarefa, descricao, percentual")
            .And.Contain("periodo = manha, tarde, dia").And.Contain("praticavel = sim, nao");
    }

    [Fact]
    public async Task AIAAbreODiarioNoDia_EUmDiaQueNaoExisteFicaParaONovoDia()
    {
        await Aplicar(R("ocorrencia", descricao: "Visita."), R("ocorrencia", data: "2026-09-29", descricao: "Entrega."));

        await _vm.AbrirTelaAsync(new TelaParaAbrir(TelasDoPrograma.Diario, De: "2026-09-28"));
        await _vm.AbrirTelaAsync(new TelaParaAbrir(TelasDoPrograma.Diario, De: "2026-10-01"));

        var (noDia, semODia) = (_ambiente.Dialogos.Diarios[0], _ambiente.Dialogos.Diarios[1]);
        (noDia.Selecionado!.Data, noDia.Ocorrencias).Should().Be((D28, "Visita."));
        (semODia.Selecionado!.Data, semODia.DataDoNovo).Should().Be((D29, (DateTime?)new DateTime(2026, 10, 1)));
        semODia.Mensagem.Should().Be("O diário ainda não tem o dia 01/10/2026: use Novo dia para criá-lo, já preenchido pelo cronograma.");
        TelasDoPrograma.Nome(TelasDoPrograma.Diario).Should().Be("Diário de obra");
    }

    [Fact]
    public async Task PeloTelegram_ORdoSemDataEODoDiaEmQueAMensagemChegou()
    {
        _ambiente.Conta.Conectar(plano: "profissional");
        await _vm.UsarObraNoTelegramCommand.ExecuteAsync(null);
        // Chegou na sexta 25/09 (o programa só buscou na segunda 28/09): o clima vai para a sexta; a que tem data fica na dela.
        _ambiente.Telegram.Caixas[_vm.Cronograma.Id] =
        [
            new ItemDoTelegram(Guid.NewGuid(), ItemDoTelegram.DeAtualizacoes, new DateTime(2026, 9, 25, 21, 0, 0, DateTimeKind.Utc), "choveu a tarde toda", null,
                [R("clima", periodo: "tarde", nome: "chuva", praticavel: "nao"), R("ocorrencia", data: "2026-09-24", descricao: "Entrega do aço.")], []),
        ];
        await _vm.AtualizarTelegramAsync();

        await _vm.BuscarDoTelegramCommand.ExecuteAsync(null);

        var cartao = _vm.Assistente.Mensagens[^1].Cartao!;
        cartao.Itens.Select(i => (i.Texto, i.Problema)).Should().Equal(
            ("RDO de 25/09/2026 (dia novo): tarde: chuva, impraticável", null),
            ("RDO de 24/09/2026 (dia novo): ocorrência \"Entrega do aço.\"", null));
        await _vm.Assistente.AplicarCartaoCommand.ExecuteAsync(cartao);
        _vm.Cronograma.Diario.Dias.Select(d => (d.Data, d.Impraticavel)).Should().Equal((new DateOnly(2026, 9, 24), false), (new DateOnly(2026, 9, 25), false));
        _vm.Cronograma.Diario.Dia(new DateOnly(2026, 9, 25))!.Tarde.Should().Be(new ClimaDoPeriodo(TempoNoPeriodo.Chuva, false));
    }
}
