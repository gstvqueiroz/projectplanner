using System.Text;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.OrdensDeServico;

namespace ProjectPlanner.App.Tests;

// Bloco 58, na tela: Projeto > Ordens de serviço…. "Hoje" é seg 28/09/2026: Alvenaria (5 d, R$ 3.000) → Reboco (4 d).
public sealed class OrdensDeServicoTelaTests : IDisposable
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly TarefaViewModel _alvenaria, _reboco;
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-os-" + Guid.NewGuid().ToString("N"));

    public OrdensDeServicoTelaTests()
    {
        Directory.CreateDirectory(_pasta);
        _vm = _ambiente.CriarViewModel();
        _alvenaria = Ambiente.AdicionarTarefa(_vm, "Alvenaria", 5);
        _alvenaria.Valor = "3.000";
        _reboco = Ambiente.AdicionarTarefa(_vm, "Reboco", 4, "1");
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private OrdensDeServicoViewModel Abrir(TarefaViewModel? selecionada = null)
    {
        _vm.TarefaSelecionada = selecionada;
        _vm.OrdensDeServicoCommand.Execute(null);
        return _ambiente.Dialogos.OrdensDeServico.Last();
    }

    private Core.Cronograma Cronograma => _vm.Cronograma;

    [Fact]
    public void SemOrdens_MostraComoComecar()
    {
        var tela = Abrir();

        (tela.SemOrdens, tela.TemSelecionada, tela.Resumo, tela.Lista.Count).Should().Be((true, false, "", 0));
    }

    [Fact]
    public void NovaOS_ComATarefaSelecionada_TrazItemEDatas_EEmitirPedeOExecutante()
    {
        var tela = Abrir(_alvenaria);

        tela.NovaOrdemCommand.Execute(null);

        (tela.Cabecalho, tela.TituloDaOrdem, tela.PodeEditar).Should().Be(("OS-001 · Rascunho", "Alvenaria", true));
        tela.Itens.Select(i => (i.Tarefa, i.Descricao, i.Unidade, i.Quantidade, i.Preco, i.Total)).Should().Equal(("1", "Alvenaria", "vb", "1,00", "3.000,00", "3.000,00"));
        (tela.InicioPrevisto, tela.Prazo).Should().Be((new DateTime(2026, 9, 28), (DateTime?)new DateTime(2026, 10, 2)));
        tela.Tarefas.Single(t => t.Marcada).Id.Should().Be(_alvenaria.Tarefa.Id);
        tela.Mensagem.Should().StartWith("OS-001 criada com 1 item da tarefa selecionada");
        _vm.TextoDesfazer.Should().Be("Desfazer Nova OS-001");

        tela.EmitirCommand.Execute(null);
        tela.Mensagem.Should().Be("Para emitir a OS-001, informe o executante (a equipe ou o empreiteiro).");

        tela.Executante = "Construtora Silva";
        tela.Itens[0].Preco = "2.500";
        tela.EmitirCommand.Execute(null);

        var os = Cronograma.OrdensDeServico.Todas.Single();
        (os.Situacao, os.Dados.Executante, os.Total).Should().Be((SituacaoDaOrdem.Emitida, "Construtora Silva", 2500m));
        (tela.Cabecalho, tela.PodeEditar, tela.PodeAceitar, tela.PodeEmitir).Should().Be(("OS-001 · Emitida", false, true, false));
        tela.Lista.Single().Detalhe.Should().Be("Construtora Silva · prazo 02/10 · R$ 2.500,00");
        tela.Resumo.Should().Be("1 em andamento · R$ 2.500,00 a pagar");
    }

    [Fact]
    public void Fluxo_PelosBotoes_ComMotivos_HistoricoEDesfazer()
    {
        var tela = Abrir(_alvenaria);
        tela.NovaOrdemCommand.Execute(null);
        tela.Executante = "Equipe do João";
        tela.EmitirCommand.Execute(null);

        _ambiente.Dialogos.TextoDigitado = "Preço baixo";
        tela.RecusarCommand.Execute(null);
        tela.Ordem!.Situacao.Should().Be(SituacaoDaOrdem.Recusada);
        tela.VoltarARascunhoCommand.Execute(null);
        tela.Itens[0].Preco = "3.200";
        tela.EmitirCommand.Execute(null);
        tela.AceitarCommand.Execute(null);
        tela.IniciarCommand.Execute(null);
        tela.ConcluirCommand.Execute(null);
        _ambiente.Dialogos.TextoDigitado = null; // cancelou a pergunta: nada muda
        tela.ReprovarCommand.Execute(null);
        tela.Ordem!.Situacao.Should().Be(SituacaoDaOrdem.Concluida);
        tela.ConferirCommand.Execute(null);

        (tela.Ordem!.Situacao, tela.Ordem.Total, tela.PodeCancelar).Should().Be((SituacaoDaOrdem.Conferida, 3200m, false));
        tela.Historico[0].Should().Be("28/09/2026 12:00 · Conferida e aprovada");
        tela.Historico.Should().Contain("28/09/2026 12:00 · Recusada: Preço baixo");

        _vm.DesfazerAlteracaoCommand.Execute(null);
        Cronograma.OrdensDeServico.Todas.Single().Situacao.Should().Be(SituacaoDaOrdem.Concluida, "a conferência foi desfeita");
    }

    [Fact]
    public void TrazerItensDasTarefasMarcadas_TrocaOsDasTarefas_EMantemOsAvulsos()
    {
        var tela = Abrir();
        tela.NovaOrdemCommand.Execute(null);
        tela.Itens.Should().BeEmpty();
        tela.AdicionarItemCommand.Execute(null);
        (tela.Itens[0].Descricao, tela.Itens[0].Quantidade, tela.Itens[0].Preco) = ("Andaime", "2", "150");

        foreach (var t in tela.Tarefas)
            t.Marcada = true;
        tela.TrazerItensDasTarefasCommand.Execute(null);

        tela.Itens.Select(i => i.Descricao).Should().Equal("Alvenaria", "Reboco", "Andaime");
        tela.TotalDaFicha.Should().Be("R$ 3.300,00");
        (tela.InicioPrevisto, tela.Prazo).Should().Be((new DateTime(2026, 9, 28), (DateTime?)new DateTime(2026, 10, 8)));
        tela.SalvarCommand.Execute(null);
        Cronograma.OrdensDeServico.Todas.Single().Dados.Tarefas.Should().Equal(_alvenaria.Tarefa.Id, _reboco.Tarefa.Id);
    }

    [Fact]
    public void FichaComErro_NaoSalva_EAvisa_ETrocarDeOSGravaOQueMudou()
    {
        var tela = Abrir(_alvenaria);
        tela.NovaOrdemCommand.Execute(null);
        tela.NovaOrdemCommand.Execute(null);
        var primeira = tela.Lista[0];

        tela.Itens[0].Quantidade = "muito";
        tela.SalvarCommand.Execute(null);
        tela.Mensagem.Should().Be("\"muito\" não é um número (use, por exemplo, 12,5).");

        tela.Itens[0].Quantidade = "3";
        tela.Contato = "11 98888-7777";
        tela.Selecionada = primeira;

        Cronograma.OrdensDeServico.Todas[1].Dados.Contato.Should().Be("11 98888-7777", "trocar de OS grava o rascunho");
        Cronograma.OrdensDeServico.Todas[1].Dados.Itens[0].Quantidade.Should().Be(3);
        tela.Cabecalho.Should().Be("OS-001 · Rascunho");
    }

    [Fact]
    public void Verificacao_NoRascunhoEditaALista_DepoisDeAceitaMarcarMudaAOS()
    {
        var tela = Abrir(_alvenaria);
        tela.NovaOrdemCommand.Execute(null);
        tela.Executante = "Equipe";
        tela.NovaVerificacao = "Limpeza feita";
        tela.AdicionarVerificacaoCommand.Execute(null);
        tela.EmitirCommand.Execute(null);
        tela.PodeMarcarVerificacao.Should().BeFalse("emitida: o executante ainda não aceitou");
        tela.AceitarCommand.Execute(null);

        tela.Verificacao.Single().Feito = true;

        Cronograma.OrdensDeServico.Todas.Single().Dados.Verificacao.Single().Feito.Should().BeTrue();
        tela.Mensagem.Should().Be("Marcado: Limpeza feita.");
    }

    [Fact]
    public void Filtros_EVencidas()
    {
        var tela = Abrir(_alvenaria);
        tela.NovaOrdemCommand.Execute(null);
        tela.Executante = "Equipe";
        tela.Prazo = new DateTime(2026, 9, 25);
        tela.InicioPrevisto = new DateTime(2026, 9, 21);
        tela.EmitirCommand.Execute(null);
        tela.NovaOrdemCommand.Execute(null);

        tela.FiltroEscolhido = tela.Filtros.Single(f => f.Valor == FiltroDasOrdens.Vencidas);

        tela.Lista.Select(l => (l.Codigo, l.Situacao)).Should().Equal(("OS-001", "Emitida · vencida"));
        tela.Resumo.Should().Be("1 em andamento · 1 vencida · R$ 3.000,00 a pagar · 1 rascunho");
        tela.FiltroEscolhido = tela.Filtros.Single(f => f.Valor == FiltroDasOrdens.Encerradas);
        tela.Lista.Should().BeEmpty();
    }

    [Fact]
    public async Task SalvarPdf_EExcluirRascunho()
    {
        var tela = Abrir(_alvenaria);
        tela.NovaOrdemCommand.Execute(null);
        _ambiente.Dialogos.ArquivoExportado = Path.Combine(_pasta, "os");

        await tela.SalvarPdfCommand.ExecuteAsync(null);

        _ambiente.Dialogos.Exportacoes.Single().Should().Be(("Salvar a OS-001 em PDF", "OS-001 - Alvenaria", "pdf"));
        Encoding.ASCII.GetString(await File.ReadAllBytesAsync(Path.Combine(_pasta, "os.pdf")), 0, 5).Should().Be("%PDF-");

        await tela.ExcluirRascunhoCommand.ExecuteAsync(null);
        (Cronograma.OrdensDeServico.Vazio, tela.Mensagem).Should().Be((true, "OS-001 excluída."));
    }
}
