using System.Collections.Specialized;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;

namespace ProjectPlanner.App.Tests;

// O exemplo de referência do Bloco 11, montado pelos comandos da tela ("hoje" é seg 28/09/2026):
//   1  1      Obra
//   2  1.1      Fase 1
//   3  1.1.1      A (3 dias, 100%)
//   4  1.1.2      B (2 dias, pred 3)
//   5  1.1.3      Fase 1 concluída (marco, pred 4)
//   6  1.2      Fase 2
//   7  1.2.1      C (4 dias, pred 5)
public class HierarquiaNaGridTests
{
    private readonly Ambiente _ambiente = new();

    private sealed record Referencia(
        MainWindowViewModel Vm,
        TarefaViewModel Obra,
        TarefaViewModel Fase1,
        TarefaViewModel A,
        TarefaViewModel B,
        TarefaViewModel Marco,
        TarefaViewModel Fase2,
        TarefaViewModel C);

    private static TarefaViewModel Subtarefa(MainWindowViewModel vm, TarefaViewModel pai, string nome,
        int? duracao = null, string predecessoras = "")
    {
        vm.TarefaSelecionada = pai;
        vm.AdicionarSubtarefaCommand.Execute(null);
        var linha = vm.TarefaSelecionada!;
        linha.Nome = nome;
        if (duracao is { } dias)
            linha.Duracao = dias;
        linha.Predecessoras = predecessoras;
        return linha;
    }

    private Referencia MontarReferencia()
    {
        var vm = _ambiente.CriarViewModel();
        var obra = Ambiente.AdicionarTarefa(vm, "Obra", 1);
        var fase1 = Subtarefa(vm, obra, "Fase 1");
        var a = Subtarefa(vm, fase1, "A", 3);
        var b = Subtarefa(vm, fase1, "B", 2, "3");
        var marco = Subtarefa(vm, fase1, "Fase 1 concluída", 0, "4");
        var fase2 = Subtarefa(vm, obra, "Fase 2");
        var c = Subtarefa(vm, fase2, "C", 4, "5");
        a.PercentualConcluido = 100;
        return new Referencia(vm, obra, fase1, a, b, marco, fase2, c);
    }

    private static List<string> Nomes(IEnumerable<TarefaViewModel> linhas) => linhas.Select(l => l.Nome).ToList();

    [Fact]
    public void Estrutura_NumeracaoNivelTipoEValoresDosResumosNaGrid()
    {
        var r = MontarReferencia();

        r.Vm.Tarefas.Select(l => (l.Numero, l.Edt, l.Nivel, l.EhResumo, l.EhMarco, l.Nome)).Should().Equal(
            (1, "1", 0, true, false, "Obra"),
            (2, "1.1", 1, true, false, "Fase 1"),
            (3, "1.1.1", 2, false, false, "A"),
            (4, "1.1.2", 2, false, false, "B"),
            (5, "1.1.3", 2, false, true, "Fase 1 concluída"),
            (6, "1.2", 1, true, false, "Fase 2"),
            (7, "1.2.1", 2, false, false, "C"));
        var (sexta2, quinta8) = (new DateTime(2026, 10, 2), new DateTime(2026, 10, 8));
        (r.Fase1.Duracao, r.Fase1.DataInicio, r.Fase1.DataTermino, r.Fase1.PercentualConcluido)
            .Should().Be((5, Ambiente.Segunda, sexta2, 60));
        (r.Obra.Duracao, r.Obra.DataInicio, r.Obra.DataTermino, r.Obra.PercentualConcluido)
            .Should().Be((9, Ambiente.Segunda, quinta8, 33));
        (r.Marco.InicioMaisCedo, r.Marco.TerminoMaisCedo).Should().Be((sexta2, sexta2));
        r.C.InicioMaisCedo.Should().Be(new DateTime(2026, 10, 5));
        r.Vm.LinhasVisiveis.Should().Equal(r.Vm.Tarefas);
        r.Vm.TerminoProjeto.Should().Be(quinta8);
    }

    // --- Recolher e expandir ---

    [Fact]
    public void RecolherUmResumo_EscondeAsSubtarefasEmTodosOsNiveis()
    {
        var r = MontarReferencia();

        r.Obra.AlternarExpansaoCommand.Execute(null);

        r.Obra.Expandido.Should().BeFalse();
        r.Obra.SimboloExpansao.Should().Be("▸");
        Nomes(r.Vm.LinhasVisiveis).Should().Equal("Obra");
        r.Vm.Tarefas.Should().HaveCount(7);

        r.Obra.AlternarExpansaoCommand.Execute(null);

        r.Vm.LinhasVisiveis.Should().Equal(r.Vm.Tarefas);
    }

    [Fact]
    public void RecolherUmResumoInterno_EscondeSoAsDele_ENumeroEEdtNaoMudam()
    {
        var r = MontarReferencia();

        r.Fase1.Expandido = false;

        Nomes(r.Vm.LinhasVisiveis).Should().Equal("Obra", "Fase 1", "Fase 2", "C");
        (r.C.Numero, r.C.Edt).Should().Be((7, "1.2.1"));
    }

    [Fact]
    public void RecolherDentroDeUmRecolhido_ContinuaEscondidoAoExpandirODeFora()
    {
        var r = MontarReferencia();
        r.Fase1.Expandido = false;
        r.Obra.Expandido = false;

        r.Obra.Expandido = true;

        Nomes(r.Vm.LinhasVisiveis).Should().Equal("Obra", "Fase 1", "Fase 2", "C");
    }

    [Fact]
    public void Recolher_NaoMarcaAlteracaoNemRecalcula()
    {
        var r = MontarReferencia();
        _ambiente.Dialogos.ArquivoParaSalvar = "/obra.pplan";
        r.Vm.SalvarProjetoCommand.Execute(null);
        var chamadas = _ambiente.Calculadora.Chamadas;

        r.Fase1.Expandido = false;
        r.Fase1.Expandido = true;

        r.Vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();
        _ambiente.Calculadora.Chamadas.Should().Be(chamadas);
    }

    [Fact]
    public void Recolher_ComASelecionadaDentro_ASelecaoPassaParaOResumo()
    {
        var r = MontarReferencia();
        r.Vm.TarefaSelecionada = r.B;

        r.Fase1.Expandido = false;

        r.Vm.TarefaSelecionada.Should().BeSameAs(r.Fase1);
        r.Vm.Gantt.TarefaDestacada.Should().Be(r.Fase1.Tarefa.Id);
    }

    [Fact]
    public void Recolher_OGanttEscondeAsMesmasLinhasEOResumoContinuaInteiro()
    {
        var r = MontarReferencia();
        var antes = r.Vm.Gantt.Layout!.Barras.Single(b => b.Nome == "Fase 1");

        r.Fase1.Expandido = false;

        var layout = r.Vm.Gantt.Layout!;
        layout.Barras.Select(b => (b.Nome, b.Linha, b.Tipo)).Should().Equal(
            ("Obra", 0, TipoTarefa.Resumo), ("Fase 1", 1, TipoTarefa.Resumo),
            ("Fase 2", 2, TipoTarefa.Resumo), ("C", 3, TipoTarefa.Normal));
        var depois = layout.Barras.Single(b => b.Nome == "Fase 1");
        (depois.X, depois.Largura).Should().Be((antes.X, antes.Largura));
        layout.Setas.Should().BeEmpty(); // o vínculo marco → C tem uma ponta escondida
    }

    // --- Rebaixar, promover e subtarefa ---

    [Fact]
    public void Rebaixar_ViraSubtarefaDaDeCimaERecalculaOResumo()
    {
        var vm = _ambiente.CriarViewModel();
        var x = Ambiente.AdicionarTarefa(vm, "X", 2);
        var y = Ambiente.AdicionarTarefa(vm, "Y", 3);
        vm.TarefaSelecionada = y;
        var chamadas = _ambiente.Calculadora.Chamadas;

        vm.RebaixarCommand.Execute(null);

        (y.Nivel, y.Edt, x.EhResumo, x.Duracao).Should().Be((1, "1.1", true, 3));
        vm.TarefaSelecionada.Should().BeSameAs(y);
        vm.PossuiAlteracoesNaoSalvas.Should().BeTrue();
        _ambiente.Calculadora.Chamadas.Should().BeGreaterThan(chamadas);
        vm.Gantt.Layout!.Barras[0].Tipo.Should().Be(TipoTarefa.Resumo);
        vm.PromoverCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Rebaixar_SemTarefaAcimaNoMesmoNivel_MostraOMotivoENaoMudaNada()
    {
        var vm = _ambiente.CriarViewModel();
        var x = Ambiente.AdicionarTarefa(vm, "X", 2);
        vm.TarefaSelecionada = x;
        var chamadas = _ambiente.Calculadora.Chamadas;

        vm.RebaixarCommand.Execute(null);

        vm.Mensagem.Should().Be("Não há tarefa acima de \"X\" no mesmo nível para ser o resumo dela.");
        x.Nivel.Should().Be(0);
        _ambiente.Calculadora.Chamadas.Should().Be(chamadas);
    }

    [Fact]
    public void Rebaixar_AbaixoDeTarefaComVinculos_Recusado()
    {
        var vm = _ambiente.CriarViewModel();
        Ambiente.AdicionarTarefa(vm, "X", 1);
        Ambiente.AdicionarTarefa(vm, "Y", 1, "1");
        var z = Ambiente.AdicionarTarefa(vm, "Z", 1);
        vm.TarefaSelecionada = z;

        vm.RebaixarCommand.Execute(null);

        vm.Mensagem.Should().StartWith("\"Y\" tem vínculos");
        z.Nivel.Should().Be(0);
    }

    [Fact]
    public void Promover_TiraDoResumoEPoeLogoDepoisDele_RenumerandoAsPredecessoras()
    {
        var r = MontarReferencia();
        r.Vm.TarefaSelecionada = r.B;

        r.Vm.PromoverCommand.Execute(null);

        Nomes(r.Vm.Tarefas).Should().Equal("Obra", "Fase 1", "A", "Fase 1 concluída", "B", "Fase 2", "C");
        (r.B.Numero, r.B.Edt, r.B.Nivel).Should().Be((5, "1.2", 1));
        r.Fase2.Edt.Should().Be("1.3");
        // Os vínculos não mudam; a notação acompanha os números novos.
        (r.B.Predecessoras, r.Marco.Predecessoras, r.C.Predecessoras).Should().Be(("3", "5", "4"));
        r.Vm.TarefaSelecionada.Should().BeSameAs(r.B);
        r.Vm.LinhasVisiveis.Should().Equal(r.Vm.Tarefas);
    }

    [Fact]
    public void MudarAEstrutura_AGridRecebeRemocaoEInsercao_NuncaMove()
    {
        // O DataGrid do Avalonia não reposiciona uma linha num Move: ela continuaria desenhada no lugar antigo.
        var r = MontarReferencia();
        var acoes = new List<NotifyCollectionChangedAction>();
        r.Vm.LinhasVisiveis.CollectionChanged += (_, e) => acoes.Add(e.Action);
        r.Vm.TarefaSelecionada = r.B;

        r.Vm.PromoverCommand.Execute(null);

        acoes.Should().NotBeEmpty().And.NotContain(NotifyCollectionChangedAction.Move);
        r.Vm.LinhasVisiveis.Should().Equal(r.Vm.Tarefas);
        r.Vm.TarefaSelecionada.Should().BeSameAs(r.B);
    }

    [Fact]
    public void Promover_NoPrimeiroNivel_FicaDesabilitado()
    {
        var r = MontarReferencia();

        r.Vm.TarefaSelecionada = r.Obra;

        r.Vm.PromoverCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Subtarefa_NumResumoRecolhido_ExpandeEleESelecionaANova()
    {
        var r = MontarReferencia();
        r.Fase1.Expandido = false;

        var nova = Subtarefa(r.Vm, r.Fase1, "D", 1);

        r.Fase1.Expandido.Should().BeTrue();
        r.Vm.LinhasVisiveis.Should().Contain(nova);
        (nova.Numero, nova.Edt).Should().Be((6, "1.1.4"));
        r.Vm.Gantt.Layout!.Barras.Should().Contain(b => b.Nome == "D");
    }

    [Fact]
    public void Subtarefa_NumMarco_RecusadaComMensagem()
    {
        var r = MontarReferencia();
        r.Vm.TarefaSelecionada = r.Marco;

        r.Vm.AdicionarSubtarefaCommand.Execute(null);

        r.Vm.Mensagem.Should().Be("\"Fase 1 concluída\" é um marco, e marcos não têm subtarefas.");
        r.Vm.Tarefas.Should().HaveCount(7);
    }

    // --- Remover ---

    [Fact]
    public void RemoverResumo_PedeConfirmacao_ECancelarNaoRemove()
    {
        var r = MontarReferencia();
        r.Vm.TarefaSelecionada = r.Fase1;
        _ambiente.Dialogos.Confirmar = false;

        r.Vm.RemoverTarefaCommand.Execute(null);

        _ambiente.Dialogos.Confirmacoes.Should().Equal("Remover o resumo \"Fase 1\" e as 3 subtarefas dele?");
        r.Vm.Tarefas.Should().HaveCount(7);
    }

    [Fact]
    public void RemoverResumo_Confirmado_RemoveComAsSubtarefas()
    {
        var r = MontarReferencia();
        r.Vm.TarefaSelecionada = r.Fase1;

        r.Vm.RemoverTarefaCommand.Execute(null);

        Nomes(r.Vm.Tarefas).Should().Equal("Obra", "Fase 2", "C");
        r.C.Predecessoras.Should().BeEmpty();
        r.Vm.TarefaSelecionada.Should().BeSameAs(r.Fase2);
    }

    [Fact]
    public void RemoverTarefaComum_NaoPedeConfirmacao()
    {
        var r = MontarReferencia();
        r.Vm.TarefaSelecionada = r.A;

        r.Vm.RemoverTarefaCommand.Execute(null);

        _ambiente.Dialogos.Confirmacoes.Should().BeEmpty();
        r.Vm.Tarefas.Should().HaveCount(6);
    }

    // --- Edição na grid ---

    [Fact]
    public void DuracaoZeroNaGrid_ViraMarco_EDuracaoMaiorVoltaASerTarefa()
    {
        var vm = _ambiente.CriarViewModel();
        var x = Ambiente.AdicionarTarefa(vm, "Entrega", 3);

        x.Duracao = 0;

        (x.EhMarco, x.DataInicio == x.DataTermino).Should().Be((true, true));
        vm.Gantt.Layout!.Barras[0].Tipo.Should().Be(TipoTarefa.Marco);

        x.Duracao = 2;

        x.EhMarco.Should().BeFalse();
    }

    [Fact]
    public void EditarValoresDeUmResumo_RecusadoComMensagem()
    {
        var r = MontarReferencia();

        r.Fase1.Duracao = 10;

        r.Fase1.HasErrors.Should().BeTrue();
        r.Vm.Mensagem.Should().Contain("A duração de um resumo é calculada a partir das subtarefas.");
        r.Fase1.Tarefa.Duracao.Should().Be(5);
    }

    [Fact]
    public void AvisoDeValorCalculado_QuandoAGridBloqueiaAEdicaoDeUmResumo()
    {
        var r = MontarReferencia();

        r.Vm.AvisarValorCalculadoNoResumo();

        r.Vm.Mensagem.Should().Be("Num resumo, esse valor é calculado a partir das subtarefas.");
    }

    [Fact]
    public void PredecessoraApontandoParaUmResumo_Recusada()
    {
        var r = MontarReferencia();

        r.C.Predecessoras = "2";

        r.Vm.Mensagem.Should().Contain("\"Fase 1\" é um resumo, e resumos não têm predecessoras nem sucessoras.");
        r.C.Tarefa.Id.Should().NotBeEmpty();
        r.Vm.Cronograma.PredecessorasDe(r.C.Tarefa.Id).Should().ContainSingle()
            .Which.TarefaOrigemId.Should().Be(r.Marco.Tarefa.Id);
    }

    [Fact]
    public void EditarUmaSubtarefa_AtualizaOsResumosNaGrid()
    {
        var r = MontarReferencia();
        var avisos = new List<string?>();
        r.Obra.PropertyChanged += (_, e) => avisos.Add(e.PropertyName);

        r.A.Duracao = 5;

        (r.Fase1.Duracao, r.Obra.Duracao, r.Obra.PercentualConcluido).Should().Be((7, 11, 45));
        avisos.Should().Contain(nameof(TarefaViewModel.Duracao));
    }

    [Fact]
    public void AbrirProjetoComEstrutura_ComecaTudoExpandidoComANumeracaoCerta()
    {
        var repositorio = new RepositorioEmMemoria();
        MainWindowViewModel Janela() =>
            new(repositorio, _ambiente.Calculadora, _ambiente.Dialogos, _ambiente.Relogio, _ambiente.Preferencias);
        var r = MontarReferencia();
        _ambiente.Dialogos.ArquivoParaSalvar = "/obra.pplan";
        repositorio.Salvar(r.Vm.Cronograma, "/obra.pplan");
        var aberta = Janela();

        _ambiente.Dialogos.ArquivoParaAbrir = "/obra.pplan";
        aberta.AbrirProjetoCommand.Execute(null);

        aberta.Tarefas.Select(l => (l.Edt, l.Nome, l.Expandido)).Should().Equal(r.Vm.Tarefas.Select(l => (l.Edt, l.Nome, true)));
        aberta.LinhasVisiveis.Should().HaveCount(7);
        aberta.Tarefas.Single(l => l.Nome == "Obra").PercentualConcluido.Should().Be(33);
    }
}
