using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Tests;

// Bloco 71: Projeto > Calendário de trabalho…: a tela (dias, feriados, exceções, a prévia e o término), a troca no projeto
// (um passo do Desfazer, com o recálculo e o Gantt), o calendário dos projetos novos e os pedidos de calendário da IA.
// "Hoje" é segunda 28/09/2026; as tarefas começam no início do projeto.
public sealed class CalendarioTelaTests
{
    private static DateTime D(string data) => DateTime.Parse(data);

    private static DateOnly O(string data) => DateOnly.Parse(data);

    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;

    public CalendarioTelaTests()
    {
        _vm = _ambiente.CriarViewModel();
        _vm.InicioProjeto = D("2026-10-05");
        Ambiente.AdicionarTarefa(_vm, "Alvenaria", 5);
        Ambiente.AdicionarTarefa(_vm, "Reboco", 5, "1");
    }

    private CalendarioViewModel Tela() =>
        new(_vm.Cronograma.Calendario, _vm.Cronograma.DataInicio, _vm.TerminoProjeto, c =>
        {
            var copia = _vm.Cronograma.Copiar();
            copia.DefinirCalendario(c);
            return CalculadoraCronograma.Calcular(copia).DataTermino;
        });

    [Fact]
    public void Tela_MostraOCalendario_ODiaForaDaRegra_EOTermino()
    {
        var tela = Tela();
        tela.Descricao.Should().Be("Segunda a sexta · sem feriados nacionais");
        tela.Mudou.Should().BeFalse();

        tela.FeriadosNacionais = true;

        tela.Descricao.Should().Be("Segunda a sexta · feriados nacionais");
        tela.Impacto.Should().Be("O término do projeto passa de 16/10/2026 para 19/10/2026.", "o 12/10 (Aparecida) fica de fora");
        tela.DiasForaDaRegra.Should().Contain("12/10/2026 (segunda) — Nossa Senhora Aparecida");
        tela.Mudou.Should().BeTrue();

        tela.Dias.Single(d => d.Dia == DayOfWeek.Saturday).Marcado = true;
        tela.Descricao.Should().StartWith("Segunda a sábado");
        tela.Impacto.Should().Be("O término do projeto continua em 16/10/2026.", "o sábado 10 entra, e o feriado 12 sai");
    }

    [Fact]
    public void Tela_FeriadosEExcecoes_ORecesso_EOQueNaoVale()
    {
        var tela = Tela();
        (tela.DataDoFeriado, tela.NomeDoFeriado, tela.FeriadoTodoAno) = (D("2026-10-07"), "Padroeira", true);
        tela.AcrescentarFeriadoCommand.Execute(null);
        (tela.DeDaExcecao, tela.AteDaExcecao, tela.MotivoDaExcecao) = (D("2026-10-08"), D("2026-10-09"), "Chuva");
        tela.AcrescentarExcecaoCommand.Execute(null);
        (tela.DeDaExcecao, tela.MotivoDaExcecao, tela.ExcecaoComTrabalho) = (D("2026-10-10"), "Mutirão", true);
        tela.AcrescentarExcecaoCommand.Execute(null);
        tela.AcrescentarRecessoCommand.Execute(null);

        tela.Feriados.Select(f => f.Texto).Should().Equal("07/10 (todo ano) — Padroeira");
        tela.Excecoes.Select(e => e.Texto).Should().Equal("08/10/2026 a 09/10/2026 — Chuva (sem trabalho)", "10/10/2026 — Mutirão (com trabalho)",
            "24/12/2026 a 01/01/2027 — Recesso de fim de ano (sem trabalho)");
        (tela.DataDoFeriado, tela.DeDaExcecao, tela.MotivoDaExcecao).Should().Be(((DateTime?)null, (DateTime?)null, ""), "os campos ficam prontos para o próximo");
        tela.DiasForaDaRegra.Should().Contain("10/10/2026 (sábado) — Mutirão (com trabalho)");

        tela.RemoverExcecaoCommand.Execute(tela.Excecoes[^1]);
        tela.Excecoes.Should().HaveCount(2);
        tela.AcrescentarFeriadoCommand.Execute(null);
        tela.Mensagem.Should().Be("Escolha a data do feriado.");
        foreach (var dia in tela.Dias)
            dia.Marcado = false;
        (tela.Calendario, tela.Mensagem, tela.AplicarCommand.CanExecute(null)).Should().Be(((CalendarioDoProjeto?)null, "Marque pelo menos um dia de trabalho na semana.", false));
        tela.UsarOCalendarioDoBrasilCommand.Execute(null);
        tela.Calendario.Should().Be(CalendarioDoProjeto.Brasil);
    }

    [Fact]
    public async Task Aplicar_Recalcula_EntraNoDesfazer_EOGanttSombreiaOFeriado()
    {
        _ambiente.Dialogos.AoMostrarCalendario = tela =>
        {
            tela.FeriadosNacionais = true;
            tela.AplicarCommand.Execute(null);
        };

        await _vm.CalendarioDeTrabalhoCommand.ExecuteAsync(null);

        _vm.Cronograma.Calendario.FeriadosNacionais.Should().BeTrue();
        _vm.Mensagem.Should().Be("Calendário de trabalho: Segunda a sexta · feriados nacionais. O término passou de 16/10/2026 para 19/10/2026.");
        (_vm.Tarefas[1].Tarefa.InicioMaisCedo, _vm.TerminoProjeto).Should().Be((D("2026-10-13"), (DateTime?)D("2026-10-19")));
        _vm.Gantt.Layout!.FinsDeSemana.Select(f => f.Dia).Should().Contain(D("2026-10-12"));
        _vm.TextoDesfazer.Should().Contain("Calendário de trabalho");
        _vm.PossuiAlteracoesNaoSalvas.Should().BeTrue();

        _vm.DesfazerAlteracaoCommand.Execute(null);

        _vm.Cronograma.Calendario.Should().Be(CalendarioDoProjeto.Padrao);
        _vm.TerminoProjeto.Should().Be(D("2026-10-16"));
    }

    [Fact]
    public async Task FecharSemAplicar_NaoMuda_EORetratoDaIATemOCalendario()
    {
        await _vm.CalendarioDeTrabalhoCommand.ExecuteAsync(null);
        _vm.Cronograma.Calendario.Should().Be(CalendarioDoProjeto.Padrao);
        _ambiente.Dialogos.Calendarios.Single().PodeUsarNosNovos.Should().BeFalse("sem as preferências (nos testes), a opção some");

        _vm.AplicarCalendario(CalendarioDoProjeto.Brasil).Should().BeTrue();
        _vm.AplicarCalendario(CalendarioDoProjeto.Brasil).Should().BeFalse();
        _vm.Mensagem.Should().Be("O calendário de trabalho não mudou.");

        var retrato = _vm.RetratoParaOAssistente();
        retrato.Calendario![0].Should().Be("Segunda a sexta · feriados nacionais, Carnaval e Corpus Christi");
        retrato.Calendario[1].Should().StartWith("Dias sem trabalho no período (além das folgas da semana): 12/10/2026 (seg) Nossa Senhora Aparecida; 02/11/2026 (seg) Finados");
        retrato.ComoTexto().Should().Contain("Calendário de trabalho: Segunda a sexta · feriados nacionais, Carnaval e Corpus Christi; durações");

        // Bloco 83: o calendário também em dados, para o servidor contar os dias úteis como o programa (/atrasadas no Telegram).
        var dias = retrato.DiasUteis!;
        dias.DiasDaSemana.Should().Equal(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday);
        dias.SemTrabalho.Should().Contain([O("2026-10-12"), O("2026-11-02"), O("2026-12-25"), O("2027-01-01")]);
        dias.Extras.Should().BeEmpty();
        (dias.EhDiaUtil(O("2026-10-12")), dias.EhDiaUtil(O("2026-10-13")), dias.EhDiaUtil(O("2026-10-17"))).Should().Be((false, true, false));
        dias.ContarDiasUteis(O("2026-10-05"), O("2026-10-16")).Should().Be(9, "duas semanas menos o feriado de 12/10");
    }

    [Fact]
    public async Task ProjetosNovos_UsamOCalendarioDoBrasil_OuOEscolhido()
    {
        var preferencias = new PreferenciasDoCalendarioEmMemoria();
        var ambiente = new Ambiente { PreferenciasDoCalendario = preferencias };
        var vm = ambiente.CriarViewModel();
        vm.Cronograma.Calendario.Should().Be(CalendarioDoProjeto.Brasil, "sem escolha salva, o do Brasil");

        ambiente.Dialogos.AoMostrarCalendario = tela =>
        {
            tela.Dias.Single(d => d.Dia == DayOfWeek.Saturday).Marcado = true;
            tela.UsarNosProjetosNovos = true;
            tela.AplicarCommand.Execute(null);
        };
        await vm.CalendarioDeTrabalhoCommand.ExecuteAsync(null);

        preferencias.Salvo!.TextoDosDias.Should().Be("segunda a sábado");
        vm.Mensagem.Should().Be("Calendário de trabalho: Segunda a sábado · feriados nacionais, Carnaval e Corpus Christi. Vale também nos projetos novos.");
        ambiente.CriarViewModel().Cronograma.Calendario.Should().Be(preferencias.Salvo);
    }

    [Fact]
    public async Task IA_ChuvaFeriadoESabados_PeloCartao()
    {
        var cartao = _vm.ConferirAtualizacoes([
            new AtualizacaoDaObra("calendario", De: "2026-10-06", Descricao: "Chuva", Acao: "parar"),
            new AtualizacaoDaObra("calendario", Data: "2026-10-15", Nome: "Aniversário da cidade", Acao: "feriadoAnual"),
            new AtualizacaoDaObra("calendario", Acao: "dias", Dias: "segunda a sábado"),
            new AtualizacaoDaObra("calendario", Acao: "trabalhar", De: "2026-10-11"),
            new AtualizacaoDaObra("calendario", Acao: "dias", Dias: "quando der"),
            new AtualizacaoDaObra("calendario", Acao: "parar"),
        ], _vm.RetratoParaOAssistente());

        cartao.Select(i => (i.Texto, i.Problema)).Should().Equal(
            ("Calendário: sem trabalho em 06/10/2026 (terça) (Chuva)", null),
            ("Calendário: feriado todo ano em 15/10 (Aniversário da cidade)", null),
            ("Calendário: trabalhar segunda a sábado", null),
            ("Calendário: com trabalho em 11/10/2026 (domingo)", null),
            ("Calendário: trabalhar quando der", "não entendi os dias da semana (por exemplo, \"segunda a sábado\")"),
            ("Calendário: sem trabalho ?", "informe a data"));

        var resultado = await _vm.AplicarAtualizacoesAsync([.. cartao.Take(4).Select(i => i.Operacao!)], "Calendário");

        resultado.Situacao.Should().Be(SituacaoDaAplicacao.Aplicada);
        var calendario = _vm.Cronograma.Calendario;
        (calendario.TextoDosDias, calendario.Feriados.Single().TodoAno, calendario.Excecoes.Count).Should().Be(("segunda a sábado", true, 2));
        calendario.EhDiaUtil(O("2026-10-06")).Should().BeFalse();
        calendario.EhDiaUtil(O("2026-10-11")).Should().BeTrue();
    }
}
