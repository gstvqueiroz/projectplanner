using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;

namespace ProjectPlanner.App.Tests;

// Bloco 31, na tela: Início e Término com as datas calculadas, a restrição e o prazo, o valor, e as linhas de base.
// "Hoje" é seg 28/09/2026, o início do projeto: A (3 d: 28–30/09) → B (2 d).
public class LinhaDeBaseNaTelaTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly TarefaViewModel _a, _b;

    public LinhaDeBaseNaTelaTests()
    {
        _vm = _ambiente.CriarViewModel();
        _a = Ambiente.AdicionarTarefa(_vm, "A", 3);
        _b = Ambiente.AdicionarTarefa(_vm, "B", 2, "1");
    }

    private static DateTime D(int dia, int mes = 10) => new(2026, mes, dia);

    // ---- Início e Término calculados (o B1 da auditoria) ----

    [Fact]
    public void InicioETermino_MostramAsDatasCalculadas()
    {
        (_b.DataInicio, _b.DataTermino).Should().Be((D(1), D(2)), "B começa depois de A, como no Gantt");
        _b.Tarefa.DataInicio.Should().Be(Ambiente.Segunda, "a data informada continua a de antes");
    }

    [Fact]
    public void DigitarOInicio_CriaARestricao_EOCronogramaAnda()
    {
        _a.DataInicio = D(5);

        (_a.DataInicio, _a.NaoIniciarAntesDe, _a.TemRestricao, _a.DicaDoInicio).Should().Be((D(5), D(5), true, "Não iniciar antes de 05/10/2026"));
        (_b.DataInicio, _vm.TerminoProjeto, _vm.TextoDesfazer).Should().Be((D(8), D(9), "Desfazer Início de \"A\""));

        _vm.DesfazerAlteracaoCommand.Execute(null);
        (_vm.Tarefas[0].NaoIniciarAntesDe, _vm.Tarefas[0].DataInicio).Should().Be((null, Ambiente.Segunda));
    }

    [Fact]
    public void DigitarOInicio_AntesDoQueAsPredecessorasPermitem_AvisaEGuarda()
    {
        _b.DataInicio = new DateTime(2026, 9, 29);

        (_b.DataInicio, _b.NaoIniciarAntesDe).Should().Be((D(1), new DateTime(2026, 9, 29)));
        _vm.Mensagem.Should().Be("\"B\" não pode começar em 29/09/2026: as predecessoras a levam para 01/10/2026. " +
                                 "A restrição \"não iniciar antes de\" ficou guardada.");
    }

    [Fact]
    public void DigitarOMesmoInicio_NaoCriaRestricao()
    {
        _b.DataInicio = D(1);

        (_b.NaoIniciarAntesDe, _vm.TextoDesfazer).Should().Be((null, "Desfazer Predecessoras de \"B\""));
    }

    [Fact]
    public void ColunaDaRestricao_ApagarTira()
    {
        _a.NaoIniciarAntesDe = D(12);
        _a.DataInicio.Should().Be(D(12));

        _a.NaoIniciarAntesDe = null;

        (_a.DataInicio, _a.TemRestricao).Should().Be((Ambiente.Segunda, false));
    }

    [Fact]
    public void DigitarOTermino_MudaADuracao()
    {
        _b.DataTermino = D(7);

        (_b.Duracao, _b.DataInicio, _b.DataTermino).Should().Be((5, D(1), D(7)));
        _b.DataTermino = new DateTime(2026, 9, 29);
        _vm.Mensagem.Should().StartWith("Linha 2, Término: A data de término (29/09/2026) não pode ser anterior à data de início (01/10/2026).");
    }

    [Fact]
    public void NumResumo_InicioRecusado_PrazoAceito()
    {
        _vm.AdicionarResumoCommand.Execute(null);
        var resumo = _vm.Tarefas.Single(t => t.EhResumo);

        resumo.DataInicio = D(20);
        _vm.Mensagem.Should().Contain("O início de um resumo é calculado a partir das subtarefas.");
        resumo.Prazo = D(1);
        resumo.Prazo.Should().Be(D(1));
    }

    // ---- Prazo ----

    [Fact]
    public void Prazo_Vencido_AvisaEAFolgaFicaNegativa()
    {
        _b.Prazo = D(1);

        (_b.ForaDoPrazo, _b.Folga, _a.Folga, _b.DicaDoPrazo).Should().Be((true, -1, -1, "Fora do prazo: termina em 02/10/2026, e o prazo é 01/10/2026"));
        _vm.Mensagem.Should().Be("\"B\" termina em 02/10/2026, depois do prazo (01/10/2026): a folga ficou negativa.");

        _b.Prazo = D(9);
        (_b.ForaDoPrazo, _b.Folga, _b.DicaDoPrazo).Should().Be((false, 0, null)); // o prazo limita; não estende o projeto
    }

    [Fact]
    public void Resumo_DoProjeto_MostraForaDoPrazoEALinhaDeBase()
    {
        _ambiente.Dialogos.TextoDigitado = "";
        _vm.SalvarLinhaDeBaseCommand.Execute(null);
        _a.Duracao = 5;
        _b.Prazo = D(2);

        _vm.ResumoDoProjetoCommand.Execute(null);
        var resumo = _ambiente.Dialogos.Resumos.Last();

        (resumo.TemForaDoPrazo, resumo.ForaDoPrazo.Single()).Should().Be((true, "2 B — prazo 02/10/2026"));
        (resumo.LinhaDeBase, resumo.AtrasouNaLinhaDeBase).Should().Be(("Linha de base: término 02/10/2026 · 2 dias úteis de atraso", true));
    }

    // ---- Valor ----

    [Fact]
    public void Valor_DigitadoEmReais_EResumoSoma()
    {
        _vm.TarefaSelecionada = _a;
        _vm.AdicionarResumoCommand.Execute(null);
        var resumo = _vm.Tarefas.Single(t => t.EhResumo);
        var sub = _vm.Tarefas.Single(t => t.Tarefa.TarefaPaiId == resumo.Tarefa.Id);

        sub.Valor = "R$ 1.500,50";
        _a.Valor = "1234.5";

        (sub.Valor, _a.Valor, resumo.Valor).Should().Be(("1.500,50", "1.234,50", "1.500,50"));
        _a.Valor = "abc";
        _vm.Mensagem.Should().StartWith("Linha 1, Valor: \"abc\" não é um valor em reais");
        _a.Valor = "";
        _vm.Tarefas[0].Tarefa.Valor.Should().BeNull();
        resumo.Valor = "10";
        _vm.Mensagem.Should().Contain("O valor de um resumo é a soma");
    }

    // ---- Linhas de base ----

    [Fact]
    public void SalvarLinhaDeBase_ComNome_ColunasEDesvios_EDesfazer()
    {
        _ambiente.Dialogos.TextoDigitado = "Contrato assinado";

        _vm.SalvarLinhaDeBaseCommand.Execute(null);

        _ambiente.Dialogos.TextosPedidos.Last().Pergunta.Should().StartWith("Guardar o cronograma de agora como a linha de base LB0");
        _vm.LinhaDeBaseEmUso!.Descricao.Should().StartWith("LB0 · Contrato assinado · salva em 28/09/2026");
        _vm.Mensagem.Should().StartWith("Linha de base salva: LB0 · Contrato assinado");
        (_b.InicioLinhaDeBase, _b.TerminoLinhaDeBase, _b.DesvioDoInicio, _b.DesvioDoTermino).Should().Be((D(1), D(2), 0, 0));

        _a.Duracao = 5;
        (_b.DesvioDoInicio, _b.DesvioDoTermino).Should().Be((2, 2));

        _vm.DesfazerAlteracaoCommand.Execute(null); // a duração
        _vm.TextoDesfazer.Should().Be("Desfazer Salvar a linha de base LB0");
        _vm.DesfazerAlteracaoCommand.Execute(null);
        (_vm.LinhaDeBaseEmUso, _vm.Tarefas[1].InicioLinhaDeBase, _vm.Tarefas[1].DesvioDoInicio).Should().Be((null, null, null));
    }

    [Fact]
    public void SalvarLinhaDeBase_Cancelado_NadaMuda()
    {
        _ambiente.Dialogos.TextoDigitado = null;

        _vm.SalvarLinhaDeBaseCommand.Execute(null);

        _vm.LinhasDeBase.Should().BeEmpty();
    }

    [Fact]
    public void SalvarLinhaDeBase_ProjetoVazio_Avisa()
    {
        var vm = _ambiente.CriarViewModel();
        _ambiente.Dialogos.TextoDigitado = "";

        vm.SalvarLinhaDeBaseCommand.Execute(null);

        vm.Mensagem.Should().Be("O projeto ainda não tem tarefas para guardar na linha de base.");
    }

    [Fact]
    public async Task GerenciarLinhasDeBase_UsarNenhumaRemover()
    {
        _ambiente.Dialogos.TextoDigitado = "";
        _vm.SalvarLinhaDeBaseCommand.Execute(null);
        _a.Duracao = 4;
        _vm.SalvarLinhaDeBaseCommand.Execute(null);
        _vm.GerenciarLinhasDeBaseCommand.Execute(null);
        var tela = _ambiente.Dialogos.LinhasDeBase.Single();

        tela.Linhas.Select(l => (l.Linha.Numero, l.EmUso, l.Detalhe)).Should().Equal(
            (0, false, "Término previsto: 02/10/2026 · 2 tarefas"), (1, true, "Término previsto: 05/10/2026 · 2 tarefas"));

        tela.UsarCommand.Execute(tela.Linhas[0]);
        (_vm.LinhaDeBaseEmUso!.Numero, _b.DesvioDoTermino, tela.Mensagem, _vm.TextoDesfazer).Should().Be(
            (0, 1, "Comparando com a LB0.", "Desfazer Usar a linha de base LB0"));

        tela.UsarNenhumaCommand.Execute(null);
        (_vm.LinhaDeBaseEmUso, _b.DesvioDoTermino, tela.UsarNenhumaCommand.CanExecute(null)).Should().Be((null, null, false));

        _ambiente.Dialogos.Confirmar = false;
        await tela.RemoverCommand.ExecuteAsync(tela.Linhas[1]);
        _vm.LinhasDeBase.Should().HaveCount(2, "cancelou");
        _ambiente.Dialogos.Confirmar = true;
        await tela.RemoverCommand.ExecuteAsync(tela.Linhas[1]);
        (_vm.LinhasDeBase.Single().Numero, tela.Linhas.Count, tela.Mensagem).Should().Be((0, 1, "LB1 removida."));
        _ambiente.Dialogos.Confirmacoes.Last().Should().StartWith("Remover a LB1 · salva em");
    }

    [Fact]
    public void LinhasDeBase_VaoParaOArquivo()
    {
        var repositorio = new RepositorioEmMemoria();
        var vm = new MainWindowViewModel(repositorio, _ambiente.Calculadora, _ambiente.Dialogos, _ambiente.Relogio, _ambiente.Preferencias,
            _ambiente.Assistente, _ambiente.PreferenciasInterface, _ambiente.Processador, _ambiente.ConfiguracaoAssistente);
        var a = Ambiente.AdicionarTarefa(vm, "A", 3);
        a.DataInicio = D(5);
        a.Prazo = D(9);
        a.Valor = "100";
        _ambiente.Dialogos.TextoDigitado = "Contrato";
        vm.SalvarLinhaDeBaseCommand.Execute(null);
        _ambiente.Dialogos.ArquivoParaSalvar = "/obra.pplan";
        vm.SalvarProjetoCommand.Execute(null);

        _ambiente.Dialogos.ArquivoParaAbrir = "/obra.pplan";
        vm.NovoProjetoCommand.Execute(null);
        vm.AbrirProjetoCommand.Execute(null);

        var aberta = vm.Tarefas.Single();
        (aberta.DataInicio, aberta.Prazo, aberta.Valor, aberta.TemRestricao, aberta.InicioLinhaDeBase).Should().Be((D(5), D(9), "100,00", true, D(5)));
        vm.LinhaDeBaseEmUso!.Nome.Should().Be("Contrato");
    }

    [Fact]
    public void Colunas_NovasOcultasPorPadrao()
    {
        foreach (var id in new[] { "NaoIniciarAntesDe", "Prazo", "InicioLinhaDeBase", "TerminoLinhaDeBase", "DesvioDoInicio", "DesvioDoTermino", "Valor" })
            _vm.Colunas[id].Visivel.Should().BeFalse(id);
        (_vm.Colunas["Inicio"].Visivel, _vm.Gantt.MostrarLinhaDeBase).Should().Be((true, true));
    }
}
