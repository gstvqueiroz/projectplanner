using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Fotos;

namespace ProjectPlanner.App.Tests;

// Bloco 28: Exibir > Fotos do projeto… (linha do tempo, importação em lote, locais e alerta) e o alerta na grid.
// "Hoje" é seg 28/09/2026 12:00; o projeto começa na seg 14/09: Fundação (3 d: 14–16/09), Alvenaria (3 d: 17–21/09),
// Cobertura (5 d: 22–28/09).
public class FotosDoProjetoTests : TesteDeFotos
{
    private readonly TarefaViewModel _fundacao, _alvenaria, _cobertura;

    public FotosDoProjetoTests()
    {
        Vm.InicioProjeto = new DateTime(2026, 9, 14);
        _fundacao = Ambiente.AdicionarTarefa(Vm, "Fundação", 3);
        _alvenaria = Ambiente.AdicionarTarefa(Vm, "Alvenaria", 3, "1");
        _cobertura = Ambiente.AdicionarTarefa(Vm, "Cobertura", 5, "2");
        Ambiente.Dialogos.ArquivoParaSalvar = "/obra.pplan";
        Vm.SalvarProjetoCommand.Execute(null);
    }

    private FotosDoProjetoViewModel AbrirFotos()
    {
        Vm.FotosDoProjetoCommand.Execute(null);
        return Ambiente.Dialogos.FotosDoProjeto.Last();
    }

    private void Datas(params (string Arquivo, string Data)[] datas)
    {
        foreach (var (arquivo, data) in datas)
            Ambiente.Processador.Datas[arquivo] = DateTime.Parse(data, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static IEnumerable<string> Nomes(FotosDoProjetoViewModel fotos) =>
        fotos.Grupos.SelectMany(g => g.Fotos).Select(f => f.Foto.Imagem.NomeArquivoOriginal);

    private static LinhaDaImportacao Linha(FotosDoProjetoViewModel fotos, string arquivo) => fotos.Lote.First(l => l.Arquivo == arquivo);

    [Fact]
    public void Datas_DoCenario()
    {
        (_fundacao.InicioMaisCedo, _alvenaria.InicioMaisCedo, _cobertura.TerminoMaisCedo)
            .Should().Be(((DateTime?)new DateTime(2026, 9, 14), (DateTime?)new DateTime(2026, 9, 17), (DateTime?)new DateTime(2026, 9, 28)));
    }

    // ---- Abrir ----

    [Fact]
    public void ProjetoSemFotos_TelaVazia()
    {
        var fotos = AbrirFotos();

        (fotos.Titulo, fotos.Vazia, fotos.Contador, fotos.AbaEscolhida).Should().Be(("Fotos do projeto — Projeto sem título", true, "0 fotos em 0 tarefas", 0));
        fotos.TarefasComFotos.Select(t => t.Nome).Should().Equal(FotosDoProjetoViewModel.TodasAsTarefas);
        fotos.LocaisDoFiltro.Should().Equal(FotosDoProjetoViewModel.TodosOsLocais);
        (fotos.TemLote, fotos.ResumoDoLote, fotos.TextoDosLocais, fotos.DiasSemFoto).Should().Be((false, "", "", 7m));
        Vm.PossuiAlteracoesNaoSalvas.Should().BeFalse("abrir não altera o projeto");
    }

    [Fact]
    public void Construtor_ArgumentosNulos_Recusados()
    {
        var c = new Cronograma("x", Ambiente.Segunda);
        var (d, r) = (Ambiente.Dialogos, Ambiente.Relogio);
        Action<IReadOnlyCollection<Guid>> mudaram = _ => { };
        Func<Guid, Task> galeria = _ => Task.CompletedTask;
        var acoes = new Func<FotosDoProjetoViewModel>[]
        {
            () => new(null!, Vm.Fotos, d, r, mudaram, () => { }, galeria),
            () => new(c, null!, d, r, mudaram, () => { }, galeria),
            () => new(c, Vm.Fotos, null!, r, mudaram, () => { }, galeria),
            () => new(c, Vm.Fotos, d, null!, mudaram, () => { }, galeria),
            () => new(c, Vm.Fotos, d, r, null!, () => { }, galeria),
            () => new(c, Vm.Fotos, d, r, mudaram, null!, galeria),
            () => new(c, Vm.Fotos, d, r, mudaram, () => { }, null!),
        };

        foreach (var acao in acoes)
            acao.Should().Throw<ArgumentNullException>();
    }

    // ---- FT1: linha do tempo ----

    private async Task<FotosDoProjetoViewModel> TresTarefasComFotos()
    {
        Datas(("f1.jpg", "2026-09-15 08:00"), ("f2.jpg", "2026-09-16 09:00"), ("a1.jpg", "2026-09-16 15:00"), ("c1.jpg", "2026-09-24 10:00"));
        Ambiente.Processador.Gps["c1.jpg"] = new Coordenadas(-22.9, -47.06);
        await ComFotos(_fundacao, "f1.jpg", "f2.jpg");
        await ComFotos(_alvenaria, "a1.jpg");
        await ComFotos(_cobertura, "c1.jpg");
        return AbrirFotos();
    }

    [Fact]
    public async Task LinhaDoTempo_TodasAsFotos_PorDia_MaisRecentesPrimeiro_ComATarefa()
    {
        var fotos = await TresTarefasComFotos();

        fotos.Contador.Should().Be("4 fotos em 3 tarefas");
        fotos.Grupos.Select(g => g.Cabecalho).Should().Equal(
            "Quinta-feira, 24/09/2026 · 1 foto", "Quarta-feira, 16/09/2026 · 2 fotos em 2 tarefas", "Terça-feira, 15/09/2026 · 1 foto");
        Nomes(fotos).Should().Equal("c1.jpg", "a1.jpg", "f2.jpg", "f1.jpg");
        fotos.Grupos.SelectMany(g => g.Fotos).Select(f => (f.Tarefa, f.TemGps))
            .Should().Equal(("3 Cobertura", true), ("2 Alvenaria", false), ("1 Fundação", false), ("1 Fundação", false));
        fotos.TarefasComFotos.Select(t => t.Nome).Should().Equal(FotosDoProjetoViewModel.TodasAsTarefas, "1 Fundação", "2 Alvenaria", "3 Cobertura");

        fotos.MaisRecentesPrimeiro = false;
        Nomes(fotos).Should().Equal("f1.jpg", "f2.jpg", "a1.jpg", "c1.jpg");
    }

    [Fact]
    public async Task Filtros_PorTarefaPeriodoTexto_ELimpar()
    {
        var fotos = await TresTarefasComFotos();

        fotos.TarefaFiltro = fotos.TarefasComFotos[1];
        Nomes(fotos).Should().Equal("f2.jpg", "f1.jpg");
        fotos.Contador.Should().Be("2 de 4 fotos");

        fotos.TarefaFiltro = fotos.TarefasComFotos[0];
        fotos.DeFiltro = new DateTime(2026, 9, 16);
        fotos.AteFiltro = new DateTime(2026, 9, 20);
        Nomes(fotos).Should().Equal("a1.jpg", "f2.jpg");

        fotos.TextoFiltro = "nada disso";
        (fotos.NadaComOsFiltros, fotos.Vazia).Should().Be((true, false));

        fotos.LimparFiltrosCommand.Execute(null);
        Nomes(fotos).Should().HaveCount(4);
        (fotos.DeFiltro, fotos.AteFiltro, fotos.TextoFiltro, fotos.TarefaFiltro!.Nome).Should().Be((null, null, "", FotosDoProjetoViewModel.TodasAsTarefas));
    }

    [Fact]
    public async Task Filtro_IntervaloInvertido_MostraOErro()
    {
        var fotos = await TresTarefasComFotos();

        fotos.DeFiltro = new DateTime(2026, 9, 20);
        fotos.AteFiltro = new DateTime(2026, 9, 15);

        fotos.ErroDeFiltro.Should().Be("O início do período (20/09/2026) é depois do fim (15/09/2026).");
        (fotos.Grupos.Count, fotos.NadaComOsFiltros).Should().Be((0, false));
    }

    [Fact]
    public async Task Selecionar_DetalheAmpliarMapaEGaleria()
    {
        var fotos = await TresTarefasComFotos();
        fotos.AbrirGaleriaDaFotoCommand.CanExecute(null).Should().BeFalse();
        var c1 = fotos.Grupos[0].Fotos[0];
        var avisos = 0;
        fotos.AmpliarCommand.CanExecuteChanged += (_, _) => avisos++;

        fotos.Selecionar(c1);

        (fotos.AmpliarCommand.CanExecute(null), avisos > 0).Should().Be((true, true), "o botão Ampliar se habilita ao selecionar");
        c1.Selecionada.Should().BeTrue();
        fotos.DetalheDaAtual.Should().Be("3 Cobertura · 24/09/2026 10:00 · GPS -22.900000, -47.060000");
        await fotos.VerNoMapaCommand.ExecuteAsync(null);
        Ambiente.Dialogos.EnderecosAbertos.Should().Equal(new Coordenadas(-22.9, -47.06).Mapa());

        await fotos.AmpliarCommand.ExecuteAsync(null);
        var visualizador = Ambiente.Dialogos.Visualizadores.Last();
        (visualizador.Posicao, visualizador.Titulo).Should().Be(("1 de 4", fotos.Titulo));

        await fotos.AbrirGaleriaDaFotoCommand.ExecuteAsync(null);
        Ambiente.Dialogos.Galerias.Last().Titulo.Should().Be("Fotos — 3 Cobertura");

        fotos.Selecionar(fotos.Grupos[1].Fotos[0]);
        fotos.VerNoMapaCommand.CanExecute(null).Should().BeFalse("a1 não tem GPS");
        fotos.Grupos.SelectMany(g => g.Fotos).Where(f => f.Selecionada).Select(f => f.Foto.Imagem.NomeArquivoOriginal).Should().Equal("a1.jpg");
    }

    [Fact]
    public async Task FotoExcluidaNaGaleria_ALinhaDoTempoSeRefazAoVoltar()
    {
        var fotos = await TresTarefasComFotos();
        fotos.TarefaFiltro = fotos.TarefasComFotos[3];
        fotos.Selecionar(fotos.Grupos[0].Fotos[0]);
        Ambiente.Dialogos.AoMostrarGaleria = async galeria =>
        {
            galeria.SelecionarTodasCommand.Execute(null);
            await galeria.ExcluirCommand.ExecuteAsync(null);
        };

        await fotos.AbrirGaleriaDaFotoCommand.ExecuteAsync(null);

        Nomes(fotos).Should().HaveCount(3);
        fotos.TarefaFiltro!.Nome.Should().Be(FotosDoProjetoViewModel.TodasAsTarefas, "a tarefa escolhida ficou sem fotos");
        (fotos.FotoAtual, fotos.Contador).Should().Be((null, "3 fotos em 2 tarefas"));
    }

    // ---- FT2: importação em lote ----

    // Uma foto de fim de semana dentro do período de uma tarefa (Alvenaria vai de qui 17 a seg 21/09) vai para ela.
    [Fact]
    public async Task Lote_FimDeSemanaDentroDoPeriodo_VaiParaATarefa()
    {
        Datas(("sab.jpg", "2026-09-19 08:00"));
        var fotos = AbrirFotos();

        await fotos.LerLoteAsync(["/f/sab.jpg"]);

        Linha(fotos, "sab.jpg").TarefaEscolhida.Nome.Should().Be("2 Alvenaria");
    }

    [Fact]
    public async Task Lote_SugereATarefaPelaData_EMostraOsProblemas()
    {
        await ComFotos(_fundacao, "ja.jpg");
        Vm.SalvarProjetoCommand.Execute(null);
        Datas(("l1.jpg", "2026-09-15 08:00"), ("l2.jpg", "2026-09-18 08:00"), ("l3.jpg", "2026-09-30 08:00"),
            ("semexif4.jpg", "2026-09-28 18:00"));
        var fotos = AbrirFotos();
        fotos.AbaEscolhida.Should().Be(FotosDoProjetoViewModel.AbaLinhaDoTempo);
        Ambiente.Dialogos.FotosParaAdicionar.AddRange(
            ["/f/l1.jpg", "/f/l2.jpg", "/f/l3.jpg", "/f/semexif4.jpg", "/f/corrompida.jpg", "/f/l1.jpg", "/f/ja.jpg", "/f/x.heic"]);

        await fotos.EscolherFotosCommand.ExecuteAsync(null);

        fotos.AbaEscolhida.Should().Be(FotosDoProjetoViewModel.AbaImportacao);
        fotos.Lote.Select(l => (l.Arquivo, l.TarefaEscolhida.Nome, l.Situacao)).Should().Equal(
            ("l1.jpg", "1 Fundação", "Sugerida pela data da foto."),
            ("l2.jpg", "2 Alvenaria", "Sugerida pela data da foto."),
            ("l3.jpg", FotosDoProjetoViewModel.NaoImportar, "Nenhuma tarefa prevista nesse dia: escolha uma."),
            ("semexif4.jpg", "3 Cobertura", "Sugerida pela data da foto."),
            ("corrompida.jpg", FotosDoProjetoViewModel.NaoImportar, "Recusada: corrompida.jpg: o arquivo não é uma imagem válida (está corrompido ou não é uma foto)."),
            ("l1.jpg", FotosDoProjetoViewModel.NaoImportar, "Repetida: a mesma foto já está nesta lista."),
            ("ja.jpg", FotosDoProjetoViewModel.NaoImportar, "Já está no projeto, em \"1 Fundação\"."),
            ("x.heic", FotosDoProjetoViewModel.NaoImportar, "Recusada: x.heic: fotos HEIC ainda não são aceitas. No iPhone, use Ajustes > Câmera > " +
                                                            "Formatos > Mais compatível, ou exporte a foto como JPEG."));
        Linha(fotos, "l2.jpg").Data.Should().Be("18/09/2026 08:00 (EXIF)");
        Linha(fotos, "semexif4.jpg").Data.Should().Be("28/09/2026 18:00 (data do arquivo)");
        (Linha(fotos, "corrompida.jpg").Recusada, Linha(fotos, "corrompida.jpg").PodeEscolher, Linha(fotos, "corrompida.jpg").Miniatura)
            .Should().Be((true, false, null));
        Linha(fotos, "l1.jpg").Tarefas.Select(t => t.Nome).Should().Equal(FotosDoProjetoViewModel.NaoImportar, "1 Fundação", "2 Alvenaria", "3 Cobertura");
        (fotos.ResumoDoLote, fotos.TemLote).Should().Be(("3 de 8 fotos vão ser importadas.", true));
        Vm.PossuiAlteracoesNaoSalvas.Should().BeFalse("ler não importa nada");

        Linha(fotos, "l3.jpg").TarefaEscolhida = Linha(fotos, "l3.jpg").Tarefas[2];
        fotos.ResumoDoLote.Should().Be("4 de 8 fotos vão ser importadas.");
    }

    [Fact]
    public async Task Lote_Importar_PoeNasTarefasComLocalEGps_EAtualizaATela()
    {
        Datas(("l1.jpg", "2026-09-15 08:00"), ("l2.jpg", "2026-09-18 08:00"), ("l3.jpg", "2026-09-18 09:00"));
        Ambiente.Processador.Gps["l2.jpg"] = new Coordenadas(-22.9, -47.06);
        var fotos = AbrirFotos();
        await fotos.LerLoteAsync(["/f/l1.jpg", "/f/l2.jpg", "/f/l3.jpg", "/f/corrompida.jpg"]);
        Linha(fotos, "l3.jpg").TarefaEscolhida = Linha(fotos, "l3.jpg").Tarefas[0];
        fotos.LocalDoLote = "  Bloco A ";

        fotos.ImportarLoteCommand.Execute(null);

        _fundacao.Tarefa.Fotos.Select(f => (f.Imagem.NomeArquivoOriginal, f.Local)).Should().Equal(("l1.jpg", "Bloco A"));
        var l2 = _alvenaria.Tarefa.Fotos.Should().ContainSingle().Subject;
        (l2.Gps, l2.DataFoto, l2.PercentualConcluidoNaData).Should().Be((new Coordenadas(-22.9, -47.06), new DateTime(2026, 9, 18, 8, 0, 0), 0));
        (_fundacao.QuantidadeDeFotos, _alvenaria.QuantidadeDeFotos, _cobertura.QuantidadeDeFotos).Should().Be((1, 1, 0));
        Vm.PossuiAlteracoesNaoSalvas.Should().BeTrue();
        (fotos.AvisoDoLote, fotos.TemLote, fotos.Contador).Should().Be(("2 fotos importadas em 2 tarefas.", false, "2 fotos em 2 tarefas"));
        fotos.LocaisDoFiltro.Should().Equal(FotosDoProjetoViewModel.TodosOsLocais, "Bloco A");
        fotos.ImportarLoteCommand.CanExecute(null).Should().BeFalse();

        fotos.LocalFiltro = "bloco a";
        Nomes(fotos).Should().Equal("l2.jpg", "l1.jpg");
        l2.Local.Should().Be("Bloco A");
    }

    [Fact]
    public async Task Lote_NadaParaImportar_BotaoDesabilitado_EDescartar()
    {
        Datas(("l3.jpg", "2026-09-30 08:00"));
        var fotos = AbrirFotos();
        fotos.ImportarLoteCommand.CanExecute(null).Should().BeFalse();
        fotos.DescartarLoteCommand.CanExecute(null).Should().BeFalse();

        await fotos.LerLoteAsync(["/f/l3.jpg"]);
        fotos.ImportarLoteCommand.CanExecute(null).Should().BeFalse("nenhuma tarefa no dia");
        fotos.ResumoDoLote.Should().Be("0 de 1 foto vai ser importada.");
        fotos.DescartarLoteCommand.Execute(null);

        (fotos.TemLote, fotos.ResumoDoLote, Vm.PossuiAlteracoesNaoSalvas).Should().Be((false, "", false));
        await fotos.LerLoteAsync([]);
        fotos.TemLote.Should().BeFalse();
    }

    [Fact]
    public async Task Lote_LocalLongoDemais_NaoImportaNada()
    {
        Datas(("l1.jpg", "2026-09-15 08:00"));
        var fotos = AbrirFotos();
        await fotos.LerLoteAsync(["/f/l1.jpg"]);
        fotos.LocalDoLote = new string('x', 201);

        fotos.ImportarLoteCommand.Execute(null);

        fotos.AvisoDoLote.Should().Be("O local tem 201 caracteres; o máximo é 200.");
        (_fundacao.QuantidadeDeFotos, fotos.TemLote, Vm.PossuiAlteracoesNaoSalvas).Should().Be((0, true, false));
    }

    [Fact]
    public async Task Lote_FotoQueEntrouNaTarefaDepoisDeLer_ContaComoJaEstava()
    {
        Datas(("l1.jpg", "2026-09-15 08:00"), ("l2.jpg", "2026-09-15 09:00"));
        var fotos = AbrirFotos();
        await fotos.LerLoteAsync(["/f/l1.jpg", "/f/l2.jpg"]);
        await ComFotos(_fundacao, "l1.jpg");

        fotos.ImportarLoteCommand.Execute(null);

        fotos.AvisoDoLote.Should().Be("1 foto importada em 1 tarefa. 1 já estava na tarefa escolhida.");
        _fundacao.QuantidadeDeFotos.Should().Be(2);
    }

    // ---- FT4: locais padronizados ----

    [Fact]
    public void Locais_Aplicar_NormalizaESalvaNoProjeto()
    {
        var fotos = AbrirFotos();
        fotos.AbaEscolhida = FotosDoProjetoViewModel.AbaLocaisEAlerta;
        fotos.TextoDosLocais = "Bloco A\r\n\r\n  bloco a\nFachada  \n";
        fotos.DiasSemFoto = 3;

        fotos.AplicarConfiguracaoCommand.Execute(null);

        Vm.ConfiguracaoDasFotos.Should().Be(new ConfiguracaoDasFotos(["Bloco A", "Fachada"], 3));
        fotos.TextoDosLocais.Should().Be("Bloco A" + Environment.NewLine + "Fachada");
        (fotos.ErroDaConfiguracao, fotos.AvisoDaConfiguracao).Should().Be(("",
            "Salvo no projeto. Alerta para tarefas em andamento sem foto há 3 dias ou mais."));
        Vm.PossuiAlteracoesNaoSalvas.Should().BeTrue();
        fotos.SugestoesDeLocal.Should().Equal("Bloco A", "Fachada");
    }

    [Fact]
    public void Locais_AplicarSemMudar_NaoAlteraOProjeto_EZeroDesligaOAlerta()
    {
        var fotos = AbrirFotos();

        fotos.AplicarConfiguracaoCommand.Execute(null);
        Vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();

        fotos.DiasSemFoto = 0;
        fotos.AplicarConfiguracaoCommand.Execute(null);
        fotos.AvisoDaConfiguracao.Should().Be("Salvo no projeto. Alerta de tarefa sem foto desligado.");
        (fotos.TituloDoAlerta, fotos.SemFotoRecente.Count).Should().Be(("O alerta de tarefa sem foto está desligado.", 0));
        Vm.Tarefas.Should().OnlyContain(l => !l.TemAlertaDeFoto);
    }

    [Theory]
    [InlineData(366, "Os dias sem foto para o alerta precisam estar entre 0 (sem alerta) e 365.")]
    [InlineData(-1, "Os dias sem foto para o alerta precisam estar entre 0 (sem alerta) e 365.")]
    public void Locais_DiasForaDoIntervalo_Erro_ENadaMuda(int dias, string erro)
    {
        var fotos = AbrirFotos();
        fotos.TextoDosLocais = "Bloco A";
        fotos.DiasSemFoto = dias;

        fotos.AplicarConfiguracaoCommand.Execute(null);

        (fotos.ErroDaConfiguracao, fotos.AvisoDaConfiguracao).Should().Be((erro, ""));
        Vm.ConfiguracaoDasFotos.Should().Be(ConfiguracaoDasFotos.Padrao);
        Vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();
    }

    [Fact]
    public void Locais_LocalLongoDemais_Erro()
    {
        var fotos = AbrirFotos();
        fotos.TextoDosLocais = new string('x', 201);

        fotos.AplicarConfiguracaoCommand.Execute(null);

        fotos.ErroDaConfiguracao.Should().StartWith("O local \"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxx…\" tem 201 caracteres");
    }

    [Fact]
    public async Task Locais_TrazerOsUsadosNasFotos()
    {
        var galeria = await ComFotos(_fundacao, "a.jpg", "b.jpg");
        foreach (var (arquivo, local) in new[] { ("a.jpg", "Térreo"), ("b.jpg", "bloco a") })
        {
            galeria.Selecionar(Item(galeria, arquivo), adicionar: false);
            galeria.EdicaoLocal = local;
            galeria.AplicarEdicaoCommand.Execute(null);
        }
        var fotos = AbrirFotos();
        fotos.TextoDosLocais = "Bloco A";

        fotos.TrazerLocaisUsadosCommand.Execute(null);

        fotos.TextoDosLocais.Should().Be("Bloco A" + Environment.NewLine + "Térreo");
        fotos.AvisoDaConfiguracao.Should().Be("1 local trazido das fotos. Clique em Aplicar para salvar.");
        fotos.TrazerLocaisUsadosCommand.Execute(null);
        fotos.AvisoDaConfiguracao.Should().Be("Todos os locais usados nas fotos já estão na lista.");
        Vm.ConfiguracaoDasFotos.Locais.Should().BeEmpty("só vale depois de Aplicar");
    }

    [Fact]
    public async Task Galeria_SugereOsLocaisDoProjeto_EMostraOGps()
    {
        Ambiente.Processador.Gps["a.jpg"] = new Coordenadas(-22.9, -47.06);
        var fotos = AbrirFotos();
        fotos.TextoDosLocais = "Bloco B\nBloco A";
        fotos.AplicarConfiguracaoCommand.Execute(null);
        var galeria = await ComFotos(_fundacao, "a.jpg", "b.jpg");

        galeria.Selecionar(Item(galeria, "a.jpg"), adicionar: false);

        galeria.LocaisSugeridos.Should().Equal("Bloco B", "Bloco A");
        galeria.DetalheDoGps.Should().Be("GPS -22.900000, -47.060000");
        await galeria.VerNoMapaCommand.ExecuteAsync(null);
        Ambiente.Dialogos.EnderecosAbertos.Should().ContainSingle();

        galeria.Selecionar(Item(galeria, "b.jpg"), adicionar: false);
        (galeria.DetalheDoGps, galeria.VerNoMapaCommand.CanExecute(null)).Should().Be(("", false));
    }

    // ---- FT5: alerta de tarefa sem foto ----

    [Fact]
    public void Alerta_NaGrid_NaTelaENoResumo()
    {
        _fundacao.Tarefa.FotosNaOrdemDeCadastro.Should().BeEmpty();

        (_fundacao.AlertaDeFoto, _alvenaria.AlertaDeFoto, _cobertura.AlertaDeFoto).Should().Be((
            "Sem nenhuma foto desde o início, há 14 dias", "Sem nenhuma foto desde o início, há 11 dias", null));
        _fundacao.DicaDoAlertaDeFoto.Should().Be("Sem nenhuma foto desde o início, há 14 dias — abrir a galeria");

        var fotos = AbrirFotos();
        fotos.TituloDoAlerta.Should().Be("Tarefas em andamento sem foto há 7 dias ou mais:");
        fotos.SemFotoRecente.Select(a => a.Tarefa.Nome).Should().Equal("Fundação", "Alvenaria");

        Vm.ResumoDoProjetoCommand.Execute(null);
        var resumo = Ambiente.Dialogos.Resumos.Last();
        resumo.TemSemFotoRecente.Should().BeTrue();
        resumo.SemFotoRecente.Should().Equal("1 Fundação — Sem nenhuma foto desde o início, há 14 dias",
            "2 Alvenaria — Sem nenhuma foto desde o início, há 11 dias");
    }

    [Fact]
    public async Task Alerta_SomeComFotoRecente_ConclusaoOuMaisDias()
    {
        Datas(("f.jpg", "2026-09-25 10:00"));
        await ComFotos(_fundacao, "f.jpg");
        _fundacao.TemAlertaDeFoto.Should().BeFalse("a foto é de 3 dias atrás");

        _alvenaria.PercentualConcluido = 100;
        _alvenaria.TemAlertaDeFoto.Should().BeFalse();

        var fotos = AbrirFotos();
        fotos.DiasSemFoto = 2;
        fotos.AplicarConfiguracaoCommand.Execute(null);
        (_fundacao.AlertaDeFoto, _cobertura.AlertaDeFoto).Should().Be(("Sem foto há 3 dias (a última é de 25/09/2026)",
            "Sem nenhuma foto desde o início, há 6 dias"));
        fotos.TituloDoAlerta.Should().Be("Tarefas em andamento sem foto há 2 dias ou mais:");
    }

    [Fact]
    public async Task Alerta_AbrirAGaleriaDaTarefaDaLista()
    {
        var fotos = AbrirFotos();

        await fotos.AbrirGaleriaDaTarefaCommand.ExecuteAsync(fotos.SemFotoRecente[1]);
        await fotos.AbrirGaleriaDaTarefaCommand.ExecuteAsync(null);

        Ambiente.Dialogos.Galerias.Select(g => g.Titulo).Should().Equal("Fotos — 2 Alvenaria");
    }

    [Fact]
    public async Task Alerta_ConfiguracaoVaiParaOArquivo()
    {
        var fotos = AbrirFotos();
        fotos.TextoDosLocais = "Bloco A";
        fotos.DiasSemFoto = 20;
        fotos.AplicarConfiguracaoCommand.Execute(null);
        Vm.SalvarProjetoCommand.Execute(null);

        Ambiente.Dialogos.ArquivoParaAbrir = "/obra.pplan";
        await Vm.AbrirProjetoCommand.ExecuteAsync(null);

        Vm.ConfiguracaoDasFotos.Should().Be(new ConfiguracaoDasFotos(["Bloco A"], 20));
        Vm.Tarefas.Should().OnlyContain(l => !l.TemAlertaDeFoto);
    }
}
