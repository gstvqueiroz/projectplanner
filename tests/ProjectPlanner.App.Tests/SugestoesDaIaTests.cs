using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.Fotos;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Tests;

// Bloco 29: Fotos do projeto > Sugestões da IA e Antes e depois, e o texto do relatório sugerido pela IA. "Hoje" é seg
// 28/09/2026; o projeto começa na seg 14/09: Fundação (14–16/09), Alvenaria (17–21/09), Cobertura (22–28/09).
public class SugestoesDaIaTests : TesteDeFotos
{
    private readonly TarefaViewModel _fundacao, _alvenaria, _cobertura;

    public SugestoesDaIaTests()
    {
        Vm.InicioProjeto = new DateTime(2026, 9, 14);
        _fundacao = Ambiente.AdicionarTarefa(Vm, "Fundação", 3);
        _alvenaria = Ambiente.AdicionarTarefa(Vm, "Alvenaria", 3, "1");
        _cobertura = Ambiente.AdicionarTarefa(Vm, "Cobertura", 5, "2");
        Ambiente.Dialogos.ArquivoParaSalvar = "/obra.pplan";
    }

    private void Datas(params (string Arquivo, string Data)[] datas)
    {
        foreach (var (arquivo, data) in datas)
            Ambiente.Processador.Datas[arquivo] = DateTime.Parse(data, System.Globalization.CultureInfo.InvariantCulture);
    }

    private FotosDoProjetoViewModel AbrirFotos()
    {
        Vm.FotosDoProjetoCommand.Execute(null);
        return Ambiente.Dialogos.FotosDoProjeto.Last();
    }

    private async Task TresFotos()
    {
        Datas(("f1.jpg", "2026-09-15 08:00"), ("a1.jpg", "2026-09-18 09:00"), ("c1.jpg", "2026-09-24 10:00"));
        await ComFotos(_fundacao, "f1.jpg");
        await ComFotos(_alvenaria, "a1.jpg");
        await ComFotos(_cobertura, "c1.jpg");
    }

    // Põe o local em todas as fotos da tarefa, pela galeria (cada aplicação refaz a galeria: o item é buscado de novo).
    private void Local(TarefaViewModel linha, string local)
    {
        var galeria = AbrirGaleria(linha);
        foreach (var arquivo in galeria.Grupos.SelectMany(g => g.Fotos).Select(f => f.Foto.Imagem.NomeArquivoOriginal).ToList())
        {
            galeria.Selecionar(Item(galeria, arquivo), adicionar: false);
            galeria.EdicaoLocal = local;
            galeria.AplicarEdicaoCommand.Execute(null);
        }
    }

    // ---- Legendas ----

    [Fact]
    public async Task Legendas_SugeridasRevisadasEAplicadas_UmPassoDoDesfazer()
    {
        await TresFotos();
        var fotos = AbrirFotos();
        fotos.TextoDeSugerirLegendas.Should().Be("Sugerir legendas (3 fotos sem legenda)");
        var desfazerAntes = Vm.TextoDesfazer;

        await fotos.SugerirLegendasCommand.ExecuteAsync(null);

        var pedido = Ambiente.Analista.PedidosDeLegendas.Single();
        pedido.Obra.Should().Be("Projeto sem título");
        pedido.Fotos.Select(f => (f.Tarefa, f.Data)).Should().Equal(
            ("3 Cobertura", new DateOnly(2026, 9, 24)), ("2 Alvenaria", new DateOnly(2026, 9, 18)), ("1 Fundação", new DateOnly(2026, 9, 15)));
        ProcessadorFalso.Descrever(pedido.Fotos[0].Jpeg).Should().Be("c1.jpg|1600|1200", "vai a imagem guardada");
        fotos.LegendasSugeridas.Select(l => (l.Descricao, l.Legenda, l.Aplicar)).Should().Equal(
            ("3 Cobertura · 24/09/2026", "Legenda da IA para 3 Cobertura", true),
            ("2 Alvenaria · 18/09/2026", "Legenda da IA para 2 Alvenaria", true),
            ("1 Fundação · 15/09/2026", "Legenda da IA para 1 Fundação", true));
        fotos.AvisoDaIa.Should().Be("3 legendas sugeridas. Revise, ajuste e aplique as marcadas.");
        Vm.TextoDesfazer.Should().Be(desfazerAntes, "sugerir não muda nada");

        fotos.LegendasSugeridas[0].Legenda = "Telhado com as tesouras montadas.";
        fotos.LegendasSugeridas[2].Aplicar = false;
        fotos.AplicarLegendasCommand.Execute(null);

        _cobertura.Tarefa.Fotos.Single().Descricao.Should().Be("Telhado com as tesouras montadas.");
        _alvenaria.Tarefa.Fotos.Single().Descricao.Should().Be("Legenda da IA para 2 Alvenaria");
        _fundacao.Tarefa.Fotos.Single().Descricao.Should().BeNull("estava desmarcada");
        (fotos.AvisoDaIa, fotos.LegendasSugeridas.Count, fotos.TextoDeSugerirLegendas).Should().Be(
            ("2 legendas aplicadas.", 1, "Sugerir legenda (1 foto sem legenda)"));
        Vm.TextoDesfazer.Should().Be("Desfazer Legendas sugeridas pela IA");

        Vm.DesfazerAlteracaoCommand.Execute(null);
        Vm.Tarefas.Select(l => l.Tarefa.Fotos.Single().Descricao).Should().OnlyContain(d => d == null);
    }

    [Fact]
    public async Task Legendas_SoAsSemLegenda_DaLinhaDoTempoComFiltros()
    {
        await TresFotos();
        var galeria = AbrirGaleria(_fundacao);
        galeria.Selecionar(Item(galeria, "f1.jpg"), adicionar: false);
        galeria.EdicaoDescricao = "Já tem";
        galeria.AplicarEdicaoCommand.Execute(null);
        var fotos = AbrirFotos();
        fotos.TarefaFiltro = fotos.TarefasComFotos.Single(t => t.Nome == "2 Alvenaria");

        fotos.FotosParaLegendar.Select(f => f.Imagem.NomeArquivoOriginal).Should().Equal("a1.jpg");
        fotos.TarefaFiltro = fotos.TarefasComFotos.Single(t => t.Nome == "1 Fundação");
        (fotos.FotosParaLegendar.Count, fotos.SugerirLegendasCommand.CanExecute(null)).Should().Be((0, false));
        fotos.TextoDeSugerirLegendas.Should().Be("Sugerir legendas (nenhuma foto sem legenda)");
    }

    [Fact]
    public async Task Legendas_IdsDesconhecidos_ENenhumaSugerida()
    {
        await TresFotos();
        Ambiente.Analista.Legendas = _ => [new LegendaSugerida("outra", "x")];
        var fotos = AbrirFotos();

        await fotos.SugerirLegendasCommand.ExecuteAsync(null);

        (fotos.LegendasSugeridas.Count, fotos.AvisoDaIa, fotos.AplicarLegendasCommand.CanExecute(null))
            .Should().Be((0, "A IA não sugeriu nenhuma legenda. Tente de novo.", false));
    }

    [Fact]
    public async Task IaNaoConfigurada_Avisa_ENaoChama()
    {
        await TresFotos();
        Ambiente.Analista.EstaConfigurado = false;
        var fotos = AbrirFotos();

        await fotos.SugerirLegendasCommand.ExecuteAsync(null);

        (fotos.IaConfigurada, fotos.AvisoDaIa).Should().Be((false, AnalistaIndisponivel.Mensagem));
        Ambiente.Analista.PedidosDeLegendas.Should().BeEmpty();
    }

    [Fact]
    public async Task ErroDaIa_EAvisado_ENadaMuda()
    {
        await TresFotos();
        Ambiente.Analista.Erro = new ErroDoAssistenteException("O limite de uso do Gemini foi atingido.");
        var fotos = AbrirFotos();

        await fotos.ConferirAvancoCommand.ExecuteAsync(null);

        (fotos.AvisoDaIa, fotos.IaOcupada, fotos.AvancosSugeridos.Count, Vm.TextoDesfazer)
            .Should().Be(("O limite de uso do Gemini foi atingido.", false, 0, "Desfazer Fotos de \"Cobertura\""));
    }

    [Fact]
    public async Task Cancelar_DuranteOPedido()
    {
        await TresFotos();
        Ambiente.Analista.Segurar = new TaskCompletionSource();
        var fotos = AbrirFotos();

        var pedido = fotos.SugerirLegendasCommand.ExecuteAsync(null);
        (fotos.IaOcupada, fotos.CancelarIaCommand.CanExecute(null), fotos.SugerirLegendasCommand.CanExecute(null),
            fotos.AvisoDaIa).Should().Be((true, true, false, "Pedindo legendas para 3 fotos à IA…"));
        fotos.CancelarIaCommand.Execute(null);
        await pedido;

        (fotos.IaOcupada, fotos.AvisoDaIa, fotos.LegendasSugeridas.Count).Should().Be((false, "Pedido cancelado.", 0));
    }

    // ---- % pelas fotos ----

    [Fact]
    public async Task Avanco_TarefasEmAndamentoComFotosRecentes_RevisadasEAplicadas()
    {
        await TresFotos();
        _fundacao.PercentualConcluido = 100; // concluída: não entra
        _alvenaria.PercentualConcluido = 30;
        var fotos = AbrirFotos();
        fotos.TarefasParaConferir.Select(t => t.Nome).Should().Equal(["Cobertura", "Alvenaria"], "a mais recentemente fotografada primeiro");
        fotos.TextoDeConferirAvanco.Should().Be("Conferir o % pelas fotos (2 tarefas)");
        Ambiente.Analista.Avanco = p =>
        [
            new AvancoSugerido(p.Tarefas[0].Id, 0, "Ainda sem telhas.", false),
            new AvancoSugerido(p.Tarefas[1].Id, 80, "Alvenaria quase toda levantada.", true),
        ];

        await fotos.ConferirAvancoCommand.ExecuteAsync(null);

        var pedido = Ambiente.Analista.PedidosDeAvanco.Single();
        (pedido.Hoje, pedido.Tarefas[1].Edt, pedido.Tarefas[1].Percentual, pedido.Tarefas[1].PercentualPrevisto, pedido.Tarefas[1].Fotos.Count)
            .Should().Be((new DateOnly(2026, 9, 28), "2", 30, 100, 1));
        fotos.AvancosSugeridos.Select(a => (a.Nome, a.Atual, a.Sugerido, a.Diverge, a.Aplicar)).Should().Equal(
            ("3 Cobertura", 0, 0m, false, false), ("2 Alvenaria", 30, 80m, true, true));
        fotos.AvisoDaIa.Should().Be("1 tarefa com o % diferente do que as fotos mostram (vêm marcadas). Revise e aplique.");

        fotos.AvancosSugeridos[1].Sugerido = 75;
        fotos.AplicarAvancoCommand.Execute(null);

        (_alvenaria.PercentualConcluido, _cobertura.PercentualConcluido, fotos.AvisoDaIa).Should().Be((75, 0, "1 tarefa atualizada."));
        Vm.TextoDesfazer.Should().Be("Desfazer % sugerido pela IA");
        Vm.DesfazerAlteracaoCommand.Execute(null);
        Vm.Tarefas.Single(l => l.Nome == "Alvenaria").PercentualConcluido.Should().Be(30);
    }

    [Fact]
    public async Task Avanco_FotosAntigasOuDoFuturo_NaoContam()
    {
        Datas(("velha.jpg", "2026-08-01 08:00"), ("futuro.jpg", "2026-10-30 08:00"));
        await ComFotos(_alvenaria, "velha.jpg", "futuro.jpg");
        var fotos = AbrirFotos();

        (fotos.TarefasParaConferir.Count, fotos.ConferirAvancoCommand.CanExecute(null)).Should().Be((0, false));
        fotos.TextoDeConferirAvanco.Should().Be("Conferir o % pelas fotos (nenhuma tarefa em andamento com fotos dos últimos 30 dias)");
    }

    // ---- Antes e depois ----

    [Fact]
    public async Task AntesEDepois_PorLocalEPorTarefa_EscolherAsFotos()
    {
        Datas(("a.jpg", "2026-09-15 08:00"), ("b.jpg", "2026-09-16 08:00"), ("c.jpg", "2026-09-18 08:00"));
        await ComFotos(_fundacao, "a.jpg", "b.jpg");
        await ComFotos(_alvenaria, "c.jpg");
        var fotos = AbrirFotos();
        fotos.SemComparacao.Should().BeFalse();
        fotos.AssuntosDaComparacao.Select(a => a.Nome).Should().Equal("1 Fundação");
        (fotos.FotoAntes!.Texto, fotos.FotoDepois!.Texto, fotos.Evolucao).Should().Be(
            ("15/09/2026 08:00 · 1 Fundação", "16/09/2026 08:00 · 1 Fundação", "De 0% para 0% concluído em 1 dia"));
        ProcessadorFalso.Descrever(fotos.ImagemAntes).Should().Be("a.jpg|1600|1200");
        fotos.Serie.Select(s => s.Data).Should().Equal("15/09", "16/09");

        // Um local nas três fotos: vira um assunto.
        Local(_fundacao, "Bloco A");
        Local(_alvenaria, "Bloco A");
        fotos.Atualizar();
        fotos.AssuntosDaComparacao.Select(a => a.Nome).Should().Equal("Bloco A", "1 Fundação");
        fotos.AssuntoEscolhido!.Nome.Should().Be("1 Fundação", "a escolha continua");
        fotos.AssuntoEscolhido = fotos.AssuntosDaComparacao[0];
        fotos.FotosDoAssunto.Should().HaveCount(3);
        fotos.Evolucao.Should().Be("3 dias entre as fotos");
        fotos.FotoAntes = fotos.FotosDoAssunto[1];
        (fotos.Evolucao, fotos.Serie.Count).Should().Be(("2 dias entre as fotos", 2));
    }

    [Fact]
    public async Task AntesEDepois_SemFotosEmDoisDias_Mensagem()
    {
        Datas(("a.jpg", "2026-09-15 08:00"), ("b.jpg", "2026-09-15 10:00"));
        await ComFotos(_fundacao, "a.jpg", "b.jpg");

        var fotos = AbrirFotos();

        (fotos.SemComparacao, fotos.AssuntoEscolhido, fotos.ImagemAntes, fotos.Evolucao).Should().Be((true, null, null, ""));
    }

    // ---- Texto do relatório ----

    private RelatorioFotograficoViewModel AbrirRelatorio()
    {
        Vm.RelatorioFotograficoCommand.Execute(null);
        return Ambiente.Dialogos.Relatorios.Last();
    }

    [Fact]
    public async Task Relatorio_TextoSugeridoPelaIa_EntraNoRelatorio()
    {
        await TresFotos();
        var relatorio = AbrirRelatorio();
        var paginas = relatorio.Previa;

        await relatorio.SugerirTextoCommand.ExecuteAsync(null);

        var pedido = Ambiente.Analista.PedidosDeTexto.Single();
        (pedido.De, pedido.Ate, pedido.Fotos.Count).Should().Be((new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 28), 3));
        pedido.Etapas.Select(e => (e.Edt, e.Nome, e.Situacao)).Should().Equal(
            ("1", "Fundação", "Atrasada"), ("2", "Alvenaria", "Atrasada"), ("3", "Cobertura", "Atrasada"));
        pedido.Resumo[0].Should().StartWith("Andamento em 28/09/2026: 0% concluído");
        relatorio.TextoDoAndamento.Should().Be($"No período de 14/09 a 28/09, a obra avançou.{Environment.NewLine}{Environment.NewLine}Segundo parágrafo.");
        (relatorio.TextoDosPontos, relatorio.AvisoDaIa).Should().Be(("Atenção à laje.", "Texto sugerido pela IA. Revise e ajuste antes de gerar o relatório."));
        relatorio.Previa.Should().NotBe(paginas, "o texto é uma página a mais");
        Ambiente.Dialogos.Confirmacoes.Should().BeEmpty("não havia texto para trocar");
    }

    [Fact]
    public async Task Relatorio_TrocarTextoEscrito_Pergunta()
    {
        await TresFotos();
        var relatorio = AbrirRelatorio();
        relatorio.TextoDoAndamento = "Meu texto.";
        Ambiente.Dialogos.Confirmar = false;

        await relatorio.SugerirTextoCommand.ExecuteAsync(null);

        (relatorio.TextoDoAndamento, Ambiente.Analista.PedidosDeTexto.Count).Should().Be(("Meu texto.", 0));
        Ambiente.Dialogos.Confirmacoes.Last().Should().Be("Trocar o texto que já está escrito pelo texto sugerido pela IA?");
    }

    [Fact]
    public async Task Relatorio_IaNaoConfiguradaOuComErro_Avisa()
    {
        await TresFotos();
        var relatorio = AbrirRelatorio();
        Ambiente.Analista.EstaConfigurado = false;
        await relatorio.SugerirTextoCommand.ExecuteAsync(null);
        relatorio.AvisoDaIa.Should().Be(AnalistaIndisponivel.Mensagem);

        Ambiente.Analista.EstaConfigurado = true;
        Ambiente.Analista.Erro = new ErroDoAssistenteException("Sem conexão com o Gemini.");
        await relatorio.SugerirTextoCommand.ExecuteAsync(null);
        (relatorio.AvisoDaIa, relatorio.TextoDoAndamento, relatorio.IaOcupada).Should().Be(("Sem conexão com o Gemini.", "", false));
    }

    [Fact]
    public async Task Relatorio_AntesEDepoisETextoDigitado_GeramAsPaginas()
    {
        Datas(("a.jpg", "2026-09-15 08:00"), ("b.jpg", "2026-09-16 08:00"));
        await ComFotos(_fundacao, "a.jpg", "b.jpg");
        Local(_fundacao, "Bloco A");
        var relatorio = AbrirRelatorio();
        var antes = relatorio.Previa;

        relatorio.ComAntesEDepois = true;
        relatorio.TextoDoAndamento = "Sapatas prontas.";
        relatorio.TextoDosPontos = "- Chuva\n- Laje";

        relatorio.Previa.Should().Be(antes.Replace("3 páginas", "5 páginas"));
        var caminho = Path.Combine(Path.GetTempPath(), $"pp-relatorio-{Guid.NewGuid():N}.pdf");
        Ambiente.Dialogos.ArquivoDoRelatorio = caminho;
        try
        {
            await relatorio.GerarPdfCommand.ExecuteAsync(null);
            File.Exists(caminho).Should().BeTrue();
        }
        finally
        {
            File.Delete(caminho);
        }
    }
}
