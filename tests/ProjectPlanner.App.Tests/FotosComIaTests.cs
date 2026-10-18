using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Fotos;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Tests;

// Bloco 42: a IA classificando as fotos do lote (tarefa, legenda, local, %), a pasta da obra e as fotos pelo chat. Projeto na
// seg 28/09/2026: 1 Fundação (3 d: 28/09–30/09) → 2 Alvenaria (3 d: 01/10–05/10) → 3 Cobertura (5 d: 06/10–12/10).
public sealed class FotosComIaTests : TesteDeFotos, IDisposable
{
    private readonly TarefaViewModel _fundacao, _alvenaria, _cobertura;
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-pasta-obra-" + Guid.NewGuid().ToString("N"));

    public FotosComIaTests()
    {
        _fundacao = Ambiente.AdicionarTarefa(Vm, "Fundação", 3);
        _alvenaria = Ambiente.AdicionarTarefa(Vm, "Alvenaria", 3, "1");
        _cobertura = Ambiente.AdicionarTarefa(Vm, "Cobertura", 5, "2");
        Vm.Cronograma.DefinirConfiguracaoDasFotos(new ConfiguracaoDasFotos(["Bloco A", "Bloco B"], 7));
        Ambiente.Processador.Datas["f1.jpg"] = new DateTime(2026, 9, 29, 9, 0, 0);
        Ambiente.Processador.Datas["f2.jpg"] = new DateTime(2026, 10, 2, 9, 0, 0);
        Ambiente.Processador.Datas["antiga.jpg"] = new DateTime(2026, 9, 1, 9, 0, 0);
    }

    public void Dispose()
    {
        if (Directory.Exists(_pasta))
            Directory.Delete(_pasta, recursive: true);
    }

    private FotosDoProjetoViewModel AbrirFotos()
    {
        Vm.FotosDoProjetoCommand.Execute(null);
        return Ambiente.Dialogos.FotosDoProjeto.Last();
    }

    private static LinhaDaImportacao Linha(FotosDoProjetoViewModel fotos, string arquivo) => fotos.Lote.First(l => l.Arquivo == arquivo);

    [Fact]
    public async Task Classificar_ATarefaALegendaOLocalEOPercentual_ComADuvidaMarcada()
    {
        Ambiente.Analista.Classificar = p =>
        [
            new ClassificacaoDaFoto(p.Fotos[0].Id, "T2", ConfiancaDaIA.Alta, "Blocos cerâmicos assentados até meia altura.", "Bloco A", 60, "Paredes de bloco."),
            new ClassificacaoDaFoto(p.Fotos[1].Id, null, ConfiancaDaIA.Alta, "Céu e árvores.", null, null, "Não mostra serviço."),
        ];
        var fotos = AbrirFotos();
        await fotos.LerLoteAsync(["/f/f1.jpg", "/f/f2.jpg"]);
        Linha(fotos, "f1.jpg").TarefaEscolhida.Nome.Should().Be("1 Fundação", "pela data, antes da IA");

        await fotos.ClassificarComIaCommand.ExecuteAsync(null);

        var pedido = Ambiente.Analista.PedidosDeClassificacao.Single();
        pedido.Tarefas.Select(t => (t.Id, t.Nome, t.EmExecucao)).Should().Equal(("T1", "Fundação", true), ("T2", "Alvenaria", true), ("T3", "Cobertura", true));
        pedido.Locais.Should().Equal("Bloco A", "Bloco B");
        pedido.Fotos.Select(f => f.Tarefa).Should().Equal("sugerida pela data: 1 Fundação", "sugerida pela data: 2 Alvenaria");
        var f1 = Linha(fotos, "f1.jpg");
        (f1.TarefaEscolhida.Nome, f1.Legenda, f1.Local, f1.PercentualSugerido, f1.Confianca, f1.Duvidosa)
            .Should().Be(("2 Alvenaria", "Blocos cerâmicos assentados até meia altura.", "Bloco A", 60, "IA: certeza alta", false));
        f1.AvisoDeData.Should().BeNull("29/09 está a menos de 7 dias do início da alvenaria (01/10)");
        (f1.Situacao, f1.TextoDoPercentual).Should().Be(("Escolhida pela IA pelo que aparece na foto.", "60%"));
        var f2 = Linha(fotos, "f2.jpg");
        (f2.TarefaEscolhida.Nome, f2.Confianca, f2.Duvidosa).Should().Be(("2 Alvenaria", "IA: não soube a tarefa", true), "fica a sugerida pela data");
        (f2.Situacao, f2.TextoDoPercentual).Should().Be(("Sugerida pela data da foto.", ""));
        fotos.AvisoDoLote.Should().Be("2 fotos classificadas pela IA; 1 com dúvida (marcadas). Confira as tarefas e as legendas e clique em Importar.");
    }

    [Fact]
    public async Task Importar_ComALegendaOLocalDaFoto_EOPercentualSoComAOpcao_UmPassoDoDesfazer()
    {
        Ambiente.Analista.Classificar = p => [.. p.Fotos.Select(f => new ClassificacaoDaFoto(f.Id, "T2", ConfiancaDaIA.Alta, $"Alvenaria {f.Id}", "Bloco B", 70, "x"))];
        var fotos = AbrirFotos();
        await fotos.LerLoteAsync(["/f/f1.jpg", "/f/f2.jpg"]);
        await fotos.ClassificarComIaCommand.ExecuteAsync(null);
        Linha(fotos, "f2.jpg").Legenda = "  Legenda escrita à mão ";
        fotos.LocalDoLote = "Bloco A";

        fotos.ImportarLoteCommand.Execute(null);

        var importadas = _alvenaria.Tarefa.Fotos;
        importadas.Select(f => (f.Imagem.NomeArquivoOriginal, f.Descricao, f.Local)).Should().BeEquivalentTo(
            [("f1.jpg", "Alvenaria F1", "Bloco B"), ("f2.jpg", "Legenda escrita à mão", "Bloco B")], "o local da foto vale mais que o do lote");
        _alvenaria.Tarefa.PercentualConcluido.Should().Be(0, "sem a opção, o % não muda");

        Vm.DesfazerAlteracaoCommand.Execute(null);
        Vm.Cronograma.Tarefas[1].Fotos.Should().BeEmpty();

        fotos = AbrirFotos(); // a janela é modal: o Desfazer acontece com ela fechada
        await fotos.LerLoteAsync(["/f/f1.jpg"]);
        await fotos.ClassificarComIaCommand.ExecuteAsync(null);
        fotos.AtualizarPercentualPelasFotos = true;
        fotos.ImportarLoteCommand.Execute(null);
        Vm.Cronograma.Tarefas[1].PercentualConcluido.Should().Be(70);
        Vm.Cronograma.Tarefas[1].Fotos.Single().PercentualConcluidoNaData.Should().Be(70);
        fotos.AvisoDoLote.Should().EndWith("O % de 1 tarefa subiu pelas fotos.");
    }

    [Fact]
    public async Task DataIncoerente_AvisoAoEscolherATarefa_SemIA_EErro()
    {
        var fotos = AbrirFotos();
        await fotos.LerLoteAsync(["/f/antiga.jpg"]);
        var linha = Linha(fotos, "antiga.jpg");
        linha.TarefaEscolhida = linha.Tarefas.First(o => o.Nome == "3 Cobertura");
        (linha.TemAvisoDeData, linha.AvisoDeData).Should().Be((true, "A foto é de 01/09/2026, antes do início previsto da tarefa (06/10/2026)."));

        Ambiente.Analista.EstaConfigurado = false;
        await fotos.ClassificarComIaCommand.ExecuteAsync(null);
        fotos.AvisoDoLote.Should().Be(AnalistaIndisponivel.Mensagem);

        Ambiente.Analista.EstaConfigurado = true;
        Ambiente.Analista.Erro = new ErroDoAssistenteException("O limite de uso do Gemini foi atingido.");
        await fotos.ClassificarComIaCommand.ExecuteAsync(null);
        (fotos.AvisoDoLote, fotos.Classificando).Should().Be(("O limite de uso do Gemini foi atingido.", false));
    }

    [Fact]
    public async Task PastaDaObra_EscolheLeSoAsNovas_EGuardaNoProjeto()
    {
        Directory.CreateDirectory(Path.Combine(_pasta, "sub"));
        File.WriteAllText(Path.Combine(_pasta, "f1.jpg"), "x");
        File.WriteAllText(Path.Combine(_pasta, "sub", "f2.JPG"), "x");
        File.WriteAllText(Path.Combine(_pasta, "notas.txt"), "x");
        Ambiente.Dialogos.PastaEscolhida = _pasta;
        var fotos = AbrirFotos();

        await fotos.BuscarNaPastaDaObraCommand.ExecuteAsync(null);

        fotos.Lote.Select(l => l.Arquivo).Should().BeEquivalentTo(["f1.jpg", "f2.JPG"]);
        Ambiente.Analista.PedidosDeClassificacao.Should().ContainSingle("já classifica com a IA");
        var config = Vm.Cronograma.ConfiguracaoDasFotos;
        (config.PastaDaObra, config.UltimaLeituraDaPasta).Should().Be((_pasta, new DateTime(2026, 9, 28, 12, 0, 0)));
        fotos.TextoDaPasta.Should().Be($"Pasta da obra: {_pasta} · lida em 28/09/2026 12:00");
        fotos.ImportarLoteCommand.Execute(null);

        // De novo: os arquivos são mais velhos que a última leitura? Não (o relógio do teste é fixo e eles são de agora):
        // as já importadas ficam de fora pelo conteúdo.
        Vm.Cronograma.DefinirConfiguracaoDasFotos(config with { UltimaLeituraDaPasta = null });
        await fotos.BuscarNaPastaDaObraCommand.ExecuteAsync(null);
        (fotos.Lote.Count, fotos.AvisoDoLote).Should().Be((0, "As fotos da pasta já estão no projeto."));
        Ambiente.Dialogos.PedidosDePasta.Should().ContainSingle("a pasta escolhida ficou guardada");

        Vm.Cronograma.DefinirConfiguracaoDasFotos(Vm.Cronograma.ConfiguracaoDasFotos with { UltimaLeituraDaPasta = DateTime.Now.AddHours(1) });
        await fotos.BuscarNaPastaDaObraCommand.ExecuteAsync(null);
        fotos.AvisoDoLote.Should().StartWith("Nenhuma foto nova na pasta desde ");
    }

    [Fact]
    public async Task Chat_FotosAnexadas_AbremAImportacaoJaClassificada()
    {
        var anexo = Path.Combine(_pasta, "f1.jpg");
        Directory.CreateDirectory(_pasta);
        File.WriteAllBytes(anexo, [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3]);
        Ambiente.Assistente.ResponderComAlteracoes = _ => new RespostaIA("Vejo alvenaria em execução. Confira a tabela.") { ImportarFotos = "anexos" };
        Vm.Assistente.AnexosPendentes.Add(AnexoIA.DoArquivo(anexo));
        Vm.Assistente.TextoDigitado = "coloca essa foto na tarefa certa";

        await Vm.Assistente.EnviarCommand.ExecuteAsync(null);

        var fotos = Ambiente.Dialogos.FotosDoProjeto.Last();
        (fotos.AbaEscolhida, fotos.Lote.Single().Arquivo).Should().Be((FotosDoProjetoViewModel.AbaImportacao, "f1.jpg"));
        Ambiente.Analista.PedidosDeClassificacao.Should().ContainSingle();
        Linha(fotos, "f1.jpg").Legenda.Should().StartWith("Legenda da IA");
    }

    [Fact]
    public async Task Chat_RelatorioComTexto_JaPedeOTextoDaIA()
    {
        var fotos = AbrirFotos();
        await fotos.LerLoteAsync(["/f/f1.jpg"]);
        fotos.ImportarLoteCommand.Execute(null);

        await Vm.AbrirTelaAsync(new TelaParaAbrir("relatorioFotografico", De: "2026-09-28", Ate: "2026-10-02", Texto: true));

        var relatorio = Ambiente.Dialogos.Relatorios.Last();
        (relatorio.De, relatorio.Ate).Should().Be((new DateTime(2026, 9, 28), new DateTime(2026, 10, 2)));
        Ambiente.Analista.PedidosDeTexto.Should().ContainSingle();
        relatorio.TextoDoAndamento.Should().StartWith("No período de 28/09 a 02/10");

        await Vm.AbrirTelaAsync(new TelaParaAbrir("relatorioFotografico", De: "2026-09-28"));
        Ambiente.Analista.PedidosDeTexto.Should().ContainSingle("sem texto: true, não pede");
    }
}
