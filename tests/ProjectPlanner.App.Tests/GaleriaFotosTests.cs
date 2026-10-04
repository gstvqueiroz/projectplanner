using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Fotos;

namespace ProjectPlanner.App.Tests;

// Bloco 22: a galeria de fotos de uma tarefa ("hoje" é seg 28/09/2026 12:00; as fotos do processador falso são de
// ter 29/09/2026 10:00, salvo indicação).
public class GaleriaFotosTests : TesteDeFotos
{
    private readonly TarefaViewModel _linha;

    public GaleriaFotosTests()
    {
        _linha = Ambiente.AdicionarTarefa(Vm, "Fundação", 3);
        Ambiente.Dialogos.ArquivoParaSalvar = "/obra.pplan";
        Vm.SalvarProjetoCommand.Execute(null);
    }

    private Tarefa Tarefa => _linha.Tarefa;

    private static IEnumerable<string> Nomes(GaleriaFotosViewModel galeria) =>
        galeria.Grupos.SelectMany(g => g.Fotos).Select(f => f.Foto.Imagem.NomeArquivoOriginal);

    private void Datas(params (string Arquivo, DateTime Data)[] datas)
    {
        foreach (var (arquivo, data) in datas)
            Ambiente.Processador.Datas[arquivo] = data;
    }

    // ---- Cabeçalho ----

    [Fact]
    public void Cabecalho_MostraEdtNomeDatasDuracaoEPercentual()
    {
        _linha.PercentualConcluido = 40;

        var galeria = AbrirGaleria(_linha);

        galeria.Titulo.Should().Be("Fotos — 1 Fundação");
        galeria.InfoDaTarefa.Should().Be("EDT 1 · Fundação · Início 28/09/2026 · Término 30/09/2026 · 3 dias · 40% concluído");
    }

    [Fact]
    public void Construtor_ArgumentosNulos_Recusados()
    {
        var fotos = Vm.Fotos;
        var acoes = new Func<GaleriaFotosViewModel>[]
        {
            () => new GaleriaFotosViewModel(null!, fotos, Ambiente.Dialogos, Ambiente.Relogio, () => { }),
            () => new GaleriaFotosViewModel(_linha, null!, Ambiente.Dialogos, Ambiente.Relogio, () => { }),
            () => new GaleriaFotosViewModel(_linha, fotos, null!, Ambiente.Relogio, () => { }),
            () => new GaleriaFotosViewModel(_linha, fotos, Ambiente.Dialogos, null!, () => { }),
            () => new GaleriaFotosViewModel(_linha, fotos, Ambiente.Dialogos, Ambiente.Relogio, null!),
        };

        foreach (var acao in acoes)
            acao.Should().Throw<ArgumentNullException>();
    }

    // ---- Adicionar ----

    [Fact]
    public async Task AdicionarFotos_VariasPeloDialogo_EntramNaTarefaComDataPercentualECadastro()
    {
        _linha.PercentualConcluido = 40;
        Vm.SalvarProjetoCommand.Execute(null);
        var galeria = AbrirGaleria(_linha);
        Ambiente.Dialogos.FotosParaAdicionar.AddRange(["/fotos/a.jpg", "/fotos/b.png", "/fotos/c.webp"]);

        await galeria.AdicionarFotosCommand.ExecuteAsync(null);

        Tarefa.Fotos.Should().HaveCount(3);
        var foto = Tarefa.Fotos.Single(f => f.Imagem.NomeArquivoOriginal == "a.jpg");
        (foto.TarefaId, foto.DataFoto, foto.DataCadastro, foto.PercentualConcluidoNaData)
            .Should().Be((Tarefa.Id, new DateTime(2026, 9, 29, 10, 0, 0), new DateTime(2026, 9, 28, 12, 0, 0), 40));
        (foto.Imagem.Largura, foto.Imagem.Altura, foto.Imagem.HashOriginal.Length).Should().Be((1600, 1200, 64));
        (galeria.Aviso, galeria.Contador, galeria.Vazia, galeria.Ocupado, galeria.Progresso)
            .Should().Be(("3 fotos adicionadas.", "3 fotos", false, false, ""));
        galeria.FotoAtual!.Foto.Imagem.NomeArquivoOriginal.Should().Be("c.webp", "a última adicionada fica escolhida");
        (Vm.PossuiAlteracoesNaoSalvas, Vm.Titulo.StartsWith('*')).Should().Be((true, true));
        (_linha.QuantidadeDeFotos, Vm.Fotos.Pendentes).Should().Be((3, 3));
    }

    [Fact]
    public async Task AdicionarFotos_DialogoCancelado_NadaMuda()
    {
        var galeria = AbrirGaleria(_linha);

        await galeria.AdicionarFotosCommand.ExecuteAsync(null);

        (Ambiente.Processador.Importacoes, galeria.Aviso, galeria.Vazia).Should().Be((0, "", true));
        Vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();
    }

    [Fact]
    public async Task AdicionarFotos_RepetidasERecusadas_SaoPuladasComOMotivo()
    {
        var galeria = await ComFotos(_linha, "a.jpg");

        await galeria.AdicionarArquivosAsync(
            ["/fotos/a.jpg", "/fotos/d.jpg", "/outra/pasta/d.jpg", "/fotos/x.heic", "/fotos/y-corrompida.jpg", "/fotos/doc.pdf"]);

        Nomes(galeria).Should().BeEquivalentTo(["a.jpg", "d.jpg"]);
        galeria.Aviso.Should().Be(
            "1 foto adicionada. Já anexadas: a.jpg, d.jpg. " +
            "Recusadas: x.heic: fotos HEIC ainda não são aceitas. No iPhone, use Ajustes > Câmera > Formatos > Mais compatível, ou exporte a foto como JPEG. " +
            "y-corrompida.jpg: o arquivo não é uma imagem válida (está corrompido ou não é uma foto). " +
            "doc.pdf: formato não aceito. Use fotos JPG, PNG ou WebP.");
    }

    [Fact]
    public async Task AdicionarFotos_SoRecusadas_NaoMarcaOProjeto()
    {
        var galeria = AbrirGaleria(_linha);

        await galeria.AdicionarArquivosAsync(["/fotos/x.heic"]);

        (galeria.Vazia, galeria.Aviso.StartsWith("Nenhuma foto adicionada. Recusada: x.heic")).Should().Be((true, true));
        Vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();
        _linha.TemFotos.Should().BeFalse();
    }

    [Fact]
    public async Task AdicionarArquivos_ListaVazia_NaoFazNada()
    {
        var galeria = AbrirGaleria(_linha);

        await galeria.AdicionarArquivosAsync([]);

        (Ambiente.Processador.Importacoes, galeria.Aviso).Should().Be((0, ""));
    }

    // ---- Grupos por dia ----

    [Fact]
    public async Task Grupos_PorDia_ComCabecalho_EOrdemAlternavel()
    {
        Datas(("a.jpg", new DateTime(2026, 9, 29, 10, 0, 0)), ("b.jpg", new DateTime(2026, 9, 29, 8, 30, 0)),
            ("c.jpg", new DateTime(2026, 10, 1, 14, 0, 0)));
        var galeria = await ComFotos(_linha, "a.jpg", "b.jpg", "c.jpg");

        galeria.Grupos.Select(g => g.Cabecalho).Should().Equal("Terça-feira, 29/09/2026 · 2 fotos", "Quinta-feira, 01/10/2026 · 1 foto");
        galeria.Grupos[0].Fotos.Select(f => (f.Rotulo, f.Hora)).Should().Equal(("b.jpg", "08:30"), ("a.jpg", "10:00"));
        galeria.Grupos[0].Fotos.Should().OnlyContain(f => f.TemMiniatura);

        galeria.MaisRecentesPrimeiro = true;

        Nomes(galeria).Should().Equal("c.jpg", "a.jpg", "b.jpg");
        galeria.FotosFiltradas.Select(f => f.Imagem.NomeArquivoOriginal).Should().Equal("c.jpg", "a.jpg", "b.jpg");
    }

    // ---- Filtros ----

    [Fact]
    public async Task Filtro_Atalhos_HojeEUltimos7Dias()
    {
        Datas(("hoje.jpg", new DateTime(2026, 9, 28, 9, 0, 0)), ("semana.jpg", new DateTime(2026, 9, 23, 9, 0, 0)),
            ("antiga.jpg", new DateTime(2026, 8, 1, 9, 0, 0)));
        var galeria = await ComFotos(_linha, "hoje.jpg", "semana.jpg", "antiga.jpg");

        galeria.AtalhoEscolhido = galeria.Atalhos.Single(a => a.Valor == AtalhoDePeriodo.Hoje);
        (string.Join(",", Nomes(galeria)), galeria.Contador).Should().Be(("hoje.jpg", "1 de 3 fotos"));

        galeria.AtalhoEscolhido = galeria.Atalhos.Single(a => a.Valor == AtalhoDePeriodo.Ultimos7Dias);
        Nomes(galeria).Should().Equal("semana.jpg", "hoje.jpg");

        galeria.AtalhoEscolhido = galeria.Atalhos.Single(a => a.Valor == AtalhoDePeriodo.EsteMes);
        Nomes(galeria).Should().Equal("semana.jpg", "hoje.jpg");
        galeria.Atalhos.Select(a => a.ToString()).Should().Equal("Todas as datas", "Hoje", "Últimos 7 dias", "Este mês");
    }

    [Fact]
    public async Task Filtro_DiaExato_EIntervalo()
    {
        Datas(("a.jpg", new DateTime(2026, 9, 29, 10, 0, 0)), ("b.jpg", new DateTime(2026, 10, 1, 10, 0, 0)),
            ("c.jpg", new DateTime(2026, 10, 5, 10, 0, 0)));
        var galeria = await ComFotos(_linha, "a.jpg", "b.jpg", "c.jpg");

        galeria.DiaFiltro = new DateTime(2026, 10, 1);
        Nomes(galeria).Should().Equal("b.jpg");

        galeria.DiaFiltro = null;
        (galeria.DeFiltro, galeria.AteFiltro) = (new DateTime(2026, 9, 30), new DateTime(2026, 10, 5));
        Nomes(galeria).Should().Equal("b.jpg", "c.jpg");
    }

    [Fact]
    public async Task Filtro_IntervaloInvertido_MostraOErroEmVezDasFotos()
    {
        var galeria = await ComFotos(_linha, "a.jpg");

        (galeria.DeFiltro, galeria.AteFiltro) = (new DateTime(2026, 10, 10), new DateTime(2026, 10, 1));

        galeria.ErroDeFiltro.Should().Be("O início do período (10/10/2026) é depois do fim (01/10/2026).");
        (galeria.Grupos.Count, galeria.NadaComOsFiltros, galeria.Contador).Should().Be((0, false, "0 de 1 fotos"));

        galeria.AteFiltro = null;
        (galeria.ErroDeFiltro, galeria.Grupos.Count).Should().Be(("", 0), "a foto (29/09) é antes do início");
    }

    [Fact]
    public async Task Filtro_Texto_SemAcentoNemMaiusculas_EmTituloLegendaLocalAutor()
    {
        var galeria = await ComFotos(_linha, "a.jpg", "b.jpg", "c.jpg", "d.jpg");
        var fotos = Tarefa.FotosNaOrdemDeCadastro;
        Vm.Fotos.Editar(Tarefa, fotos[0].Id, new MetadadosFoto(fotos[0].DataFoto, titulo: "Concretagem da laje"));
        Vm.Fotos.Editar(Tarefa, fotos[1].Id, new MetadadosFoto(fotos[1].DataFoto, descricao: "Fundação pronta"));
        Vm.Fotos.Editar(Tarefa, fotos[2].Id, new MetadadosFoto(fotos[2].DataFoto, local: "Bloco B"));
        Vm.Fotos.Editar(Tarefa, fotos[3].Id, new MetadadosFoto(fotos[3].DataFoto, autor: "João"));
        galeria.Atualizar();

        galeria.TextoFiltro = "LAJE";
        Nomes(galeria).Should().Equal("a.jpg");
        galeria.TextoFiltro = "fundacao";
        Nomes(galeria).Should().Equal("b.jpg");
        galeria.TextoFiltro = "bloco b";
        Nomes(galeria).Should().Equal("c.jpg");
        galeria.TextoFiltro = "joao";
        Nomes(galeria).Should().Equal("d.jpg");
    }

    [Fact]
    public async Task Filtro_Tag_ListaAsTagsDaTarefa()
    {
        var galeria = await ComFotos(_linha, "a.jpg", "b.jpg");
        var a = Tarefa.FotosNaOrdemDeCadastro[0];
        Vm.Fotos.Editar(Tarefa, a.Id, new MetadadosFoto(a.DataFoto, tags: ["problema", "Antes"]));
        galeria.Atualizar();

        galeria.TagsDisponiveis.Should().Equal(GaleriaFotosViewModel.TodasAsTags, "Antes", "problema");
        galeria.TagEscolhida = "problema";

        (string.Join(",", Nomes(galeria)), galeria.Contador).Should().Be(("a.jpg", "1 de 2 fotos"));
    }

    [Fact]
    public async Task Filtro_SemResultado_AvisaELimparVolta()
    {
        var galeria = await ComFotos(_linha, "a.jpg", "b.jpg");

        galeria.TextoFiltro = "nada disso";
        galeria.AtalhoEscolhido = galeria.Atalhos[1];
        galeria.TagEscolhida = "x";

        (galeria.NadaComOsFiltros, galeria.Vazia, galeria.Contador).Should().Be((true, false, "0 de 2 fotos"));

        galeria.LimparFiltrosCommand.Execute(null);

        (galeria.NadaComOsFiltros, galeria.Contador, galeria.TextoFiltro, galeria.TagEscolhida, galeria.AtalhoEscolhido.Valor)
            .Should().Be((false, "2 fotos", "", GaleriaFotosViewModel.TodasAsTags, AtalhoDePeriodo.Tudo));
        Vm.PossuiAlteracoesNaoSalvas.Should().BeTrue("por causa das fotos; os filtros não mudam o projeto");
    }

    [Fact]
    public async Task Filtros_NaoMarcamOProjetoComoAlterado()
    {
        await ComFotos(_linha, "a.jpg");
        Vm.SalvarProjetoCommand.Execute(null);
        var galeria = AbrirGaleria(_linha);

        galeria.TextoFiltro = "x";
        galeria.MaisRecentesPrimeiro = true;
        galeria.LimparFiltrosCommand.Execute(null);

        Vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();
    }

    // ---- Seleção ----

    [Fact]
    public async Task Selecao_CliqueSoUma_ComAdicionarAcumula_ESobreviveAoFiltro()
    {
        var galeria = await ComFotos(_linha, "a.jpg", "b.jpg", "c.jpg");
        galeria.ExcluirCommand.CanExecute(null).Should().BeTrue("a última adicionada fica selecionada");

        galeria.Selecionar(Item(galeria, "a.jpg"), adicionar: false);
        galeria.Selecionar(Item(galeria, "b.jpg"), adicionar: true);

        (galeria.QuantidadeSelecionada, galeria.FotoAtual!.Rotulo).Should().Be((2, "b.jpg"));

        galeria.Selecionar(Item(galeria, "b.jpg"), adicionar: true); // tira da seleção
        (galeria.QuantidadeSelecionada, galeria.FotoAtual!.Rotulo).Should().Be((1, "a.jpg"));

        galeria.MaisRecentesPrimeiro = true;
        (galeria.QuantidadeSelecionada, Item(galeria, "a.jpg").Selecionada).Should().Be((1, true));

        galeria.SelecionarTodasCommand.Execute(null);
        galeria.QuantidadeSelecionada.Should().Be(3);

        galeria.Selecionar(Item(galeria, "c.jpg"), adicionar: false);
        galeria.QuantidadeSelecionada.Should().Be(1);
    }

    [Fact]
    public async Task Selecao_Nenhuma_DesabilitaExcluirGirarEAmpliar()
    {
        var galeria = await ComFotos(_linha, "a.jpg");

        galeria.Selecionar(Item(galeria, "a.jpg"), adicionar: true);

        (galeria.QuantidadeSelecionada, galeria.FotoAtual).Should().Be((0, null));
        galeria.ExcluirCommand.CanExecute(null).Should().BeFalse();
        galeria.GirarDireitaCommand.CanExecute(null).Should().BeFalse();
        galeria.AmpliarCommand.CanExecute(null).Should().BeFalse();
        galeria.AmpliarCommand.CanExecute(Item(galeria, "a.jpg")).Should().BeTrue("duplo clique numa miniatura");
        var semItem = () => galeria.Selecionar(null!, adicionar: false);
        semItem.Should().Throw<ArgumentNullException>();
    }

    // ---- Detalhes e edição ----

    [Fact]
    public async Task Detalhes_MostramArquivoECadastro_EAImagem()
    {
        var galeria = await ComFotos(_linha, "a.jpg");

        (galeria.DetalheDoArquivo, galeria.DetalheDoCadastro).Should().Be(("a.jpg · 1600 × 1200 · 1 KB", "Cadastrada em 28/09/2026 12:00"));
        ProcessadorFalso.Descrever(galeria.ImagemAtual).Should().Be("a.jpg|1600|1200");
        (galeria.EdicaoData, galeria.EdicaoHora, galeria.EdicaoTitulo, galeria.EdicaoAlterada)
            .Should().Be((new DateTime(2026, 9, 29), new TimeSpan(10, 0, 0), null, false));
    }

    [Fact]
    public async Task Edicao_Aplicar_GravaTudoNaFoto_EMarcaOProjeto()
    {
        var galeria = await ComFotos(_linha, "a.jpg");
        Vm.SalvarProjetoCommand.Execute(null);

        galeria.EdicaoTitulo = "Concretagem";
        galeria.EdicaoDescricao = "Laje do 2º pavimento";
        galeria.EdicaoAutor = "Ana";
        galeria.EdicaoLocal = "Bloco B";
        galeria.EdicaoPercentual = 55.4m;
        galeria.EdicaoTags = "antes; laje, antes";
        galeria.EdicaoData = new DateTime(2026, 9, 30);
        galeria.EdicaoHora = new TimeSpan(15, 45, 0);
        galeria.EdicaoAlterada.Should().BeTrue();
        galeria.AplicarEdicaoCommand.CanExecute(null).Should().BeTrue();

        galeria.AplicarEdicaoCommand.Execute(null);

        var foto = Tarefa.Fotos.Single();
        (foto.Titulo, foto.Descricao, foto.Autor, foto.Local, foto.PercentualConcluidoNaData, foto.DataFoto)
            .Should().Be(("Concretagem", "Laje do 2º pavimento", "Ana", "Bloco B", 55, new DateTime(2026, 9, 30, 15, 45, 0)));
        foto.Tags.Should().Equal("antes", "laje");
        (galeria.Aviso, galeria.ErroDeEdicao, galeria.EdicaoAlterada).Should().Be(("Informações da foto atualizadas.", "", false));
        (galeria.Grupos.Single().Cabecalho, galeria.FotoAtual!.Rotulo).Should().Be(("Quarta-feira, 30/09/2026 · 1 foto", "Concretagem"));
        galeria.TagsDisponiveis.Should().Equal(GaleriaFotosViewModel.TodasAsTags, "antes", "laje");
        Vm.PossuiAlteracoesNaoSalvas.Should().BeTrue();
    }

    [Fact]
    public async Task Edicao_Invalida_MostraOErro_ENaoMudaAFoto()
    {
        var galeria = await ComFotos(_linha, "a.jpg");
        var antes = Tarefa.Fotos.Single();

        galeria.EdicaoTitulo = new string('x', 121);
        galeria.AplicarEdicaoCommand.Execute(null);
        galeria.ErroDeEdicao.Should().Be("O título tem 121 caracteres; o máximo é 120.");

        galeria.EdicaoTitulo = "ok";
        galeria.EdicaoPercentual = 101;
        galeria.AplicarEdicaoCommand.Execute(null);
        galeria.ErroDeEdicao.Should().Contain("100");

        galeria.EdicaoPercentual = null;
        galeria.EdicaoData = null;
        galeria.AplicarEdicaoCommand.Execute(null);
        galeria.ErroDeEdicao.Should().Be("A data da foto é obrigatória.");

        galeria.EdicaoData = new DateTime(1850, 1, 1);
        galeria.AplicarEdicaoCommand.Execute(null);
        galeria.ErroDeEdicao.Should().Be("A data da foto precisa estar entre 01/01/1900 e 31/12/2100.");

        galeria.EdicaoData = antes.DataFoto.Date;
        galeria.EdicaoTags = string.Join(",", Enumerable.Range(1, 21).Select(i => $"t{i}"));
        galeria.AplicarEdicaoCommand.Execute(null);
        galeria.ErroDeEdicao.Should().Be("São 21 tags; o máximo é 20.");

        Tarefa.Fotos.Single().Should().Be(antes);
    }

    [Fact]
    public async Task Edicao_Descartar_VoltaOsCampos()
    {
        var galeria = await ComFotos(_linha, "a.jpg");

        galeria.EdicaoTitulo = "Rascunho";
        galeria.EdicaoData = null;
        galeria.DescartarEdicaoCommand.Execute(null);

        galeria.EdicaoTitulo.Should().BeNull();
        (galeria.EdicaoData, galeria.EdicaoAlterada).Should().Be((new DateTime(2026, 9, 29), false));
        galeria.AplicarEdicaoCommand.CanExecute(null).Should().BeFalse();
        Tarefa.Fotos.Single().Titulo.Should().BeNull();
    }

    [Fact]
    public async Task Edicao_AplicarSemDiferenca_NaoMarcaOProjeto()
    {
        var galeria = await ComFotos(_linha, "a.jpg");
        Vm.SalvarProjetoCommand.Execute(null);

        galeria.EdicaoTitulo = "x";
        galeria.EdicaoTitulo = "  ";
        galeria.AplicarEdicaoCommand.Execute(null);

        Vm.PossuiAlteracoesNaoSalvas.Should().BeFalse("título em branco é o mesmo que sem título");
    }

    [Fact]
    public async Task Edicao_TrocarDeFoto_CarregaOsCamposDaOutra()
    {
        var galeria = await ComFotos(_linha, "a.jpg", "b.jpg");
        var b = Tarefa.FotosNaOrdemDeCadastro[1];
        Vm.Fotos.Editar(Tarefa, b.Id, new MetadadosFoto(b.DataFoto, titulo: "Formas", autor: "Ana"));
        galeria.Atualizar();

        galeria.Selecionar(Item(galeria, "a.jpg"), adicionar: false);
        galeria.EdicaoTitulo.Should().BeNull();
        galeria.Selecionar(Item(galeria, "b.jpg"), adicionar: false);

        (galeria.EdicaoTitulo, galeria.EdicaoAutor, galeria.EdicaoAlterada).Should().Be(("Formas", "Ana", false));
    }

    // ---- Excluir ----

    [Fact]
    public async Task Excluir_Uma_ComConfirmacao()
    {
        var galeria = await ComFotos(_linha, "a.jpg", "b.jpg");
        Vm.SalvarProjetoCommand.Execute(null);
        galeria.Selecionar(Item(galeria, "a.jpg"), adicionar: false);

        await galeria.ExcluirCommand.ExecuteAsync(null);

        Ambiente.Dialogos.Confirmacoes.Last().Should().Be("Excluir a foto \"a.jpg\"? Não dá para desfazer.");
        Nomes(galeria).Should().Equal("b.jpg");
        (galeria.Aviso, galeria.FotoAtual, galeria.Contador).Should().Be(("1 foto excluída.", null, "1 foto"));
        (_linha.QuantidadeDeFotos, Vm.PossuiAlteracoesNaoSalvas).Should().Be((1, true));
    }

    [Fact]
    public async Task Excluir_Varias_ComConfirmacao_ETiraAsImagensPendentes()
    {
        var galeria = await ComFotos(_linha, "a.jpg", "b.jpg", "c.jpg");
        galeria.Selecionar(Item(galeria, "a.jpg"), adicionar: false);
        galeria.Selecionar(Item(galeria, "c.jpg"), adicionar: true);

        await galeria.ExcluirCommand.ExecuteAsync(null);

        Ambiente.Dialogos.Confirmacoes.Last().Should().Be("Excluir as 2 fotos selecionadas? Não dá para desfazer.");
        (string.Join(",", Nomes(galeria)), galeria.Aviso, Vm.Fotos.Pendentes).Should().Be(("b.jpg", "2 fotos excluídas.", 1));
    }

    [Fact]
    public async Task Excluir_Cancelado_NadaMuda()
    {
        var galeria = await ComFotos(_linha, "a.jpg");
        Vm.SalvarProjetoCommand.Execute(null);
        Ambiente.Dialogos.Confirmar = false;

        await galeria.ExcluirCommand.ExecuteAsync(null);

        (Tarefa.Fotos.Count, galeria.QuantidadeSelecionada, Vm.PossuiAlteracoesNaoSalvas).Should().Be((1, 1, false));
    }

    // ---- Girar ----

    [Fact]
    public async Task Girar_DireitaEEsquerda_TrocaAImagemEAsDimensoes_EMantemASelecao()
    {
        var galeria = await ComFotos(_linha, "a.jpg");
        var hash = Tarefa.Fotos.Single().Imagem.HashOriginal;

        await galeria.GirarDireitaCommand.ExecuteAsync(null);

        var foto = Tarefa.Fotos.Single();
        (foto.Imagem.Largura, foto.Imagem.Altura, foto.Imagem.HashOriginal).Should().Be((1200, 1600, hash));
        ProcessadorFalso.Descrever(galeria.ImagemAtual).Should().Be("a.jpg↻|1200|1600");
        ProcessadorFalso.Descrever(Item(galeria, "a.jpg").Miniatura).Should().Be("mini:a.jpg↻");
        (galeria.FotoAtual!.Foto.Id, galeria.QuantidadeSelecionada, galeria.DetalheDoArquivo)
            .Should().Be((foto.Id, 1, "a.jpg · 1200 × 1600 · 1 KB"));
        Vm.Fotos.Pendentes.Should().Be(1);

        await galeria.GirarEsquerdaCommand.ExecuteAsync(null);

        ProcessadorFalso.Descrever(galeria.ImagemAtual).Should().Be("a.jpg↻↺|1600|1200");
    }

    [Fact]
    public async Task Girar_ImagemIndisponivel_AvisaENaoMuda()
    {
        var vm = AbrirProjetoComFotoSemImagemNoArquivo();
        vm.GerenciarFotosCommand.Execute(vm.Tarefas[0]);
        var galeria = Ambiente.Dialogos.Galerias.Last();
        galeria.Selecionar(Item(galeria, "a.jpg"), adicionar: false);
        galeria.ImagemAtual.Should().BeNull();

        await galeria.GirarDireitaCommand.ExecuteAsync(null);

        galeria.Aviso.Should().Be("a.jpg: a imagem desta foto não está disponível (o arquivo do projeto foi movido?).");
        (vm.Tarefas[0].Tarefa.Fotos.Single().Imagem.Largura, vm.PossuiAlteracoesNaoSalvas).Should().Be((1600, false));
    }

    // Um .pplan "aberto" pelo repositório falso, que não tem as imagens (como um arquivo movido depois de aberto).
    private MainWindowViewModel AbrirProjetoComFotoSemImagemNoArquivo()
    {
        Ambiente.Repositorio.AoAbrir = _ =>
        {
            var cronograma = new Cronograma(Guid.NewGuid(), "Obra", Ambiente.Segunda);
            var tarefa = new Tarefa(Guid.NewGuid(), "Fundação", Ambiente.Segunda, 3);
            tarefa.AdicionarFoto(new FotoTarefa(Guid.NewGuid(), tarefa.Id, new DadosDaImagem("a.jpg", 1600, 1200, 1000, new string('a', 64)),
                new MetadadosFoto(new DateTime(2026, 9, 29, 10, 0, 0)), new DateTime(2026, 9, 29, 11, 0, 0)));
            cronograma.AdicionarTarefa(tarefa);
            return cronograma;
        };
        var vm = Ambiente.CriarViewModel();
        Ambiente.Dialogos.ArquivoParaAbrir = "/movido.pplan";
        vm.AbrirProjetoCommand.Execute(null);
        return vm;
    }

    // ---- Visualizador ----

    [Fact]
    public async Task Ampliar_AbreOVisualizadorNaFotoCerta_ComAnteriorEProxima()
    {
        Datas(("a.jpg", new DateTime(2026, 9, 29, 8, 0, 0)), ("b.jpg", new DateTime(2026, 9, 29, 9, 0, 0)),
            ("c.jpg", new DateTime(2026, 9, 29, 10, 0, 0)));
        var galeria = await ComFotos(_linha, "a.jpg", "b.jpg", "c.jpg");
        var b = Tarefa.FotosNaOrdemDeCadastro[1];
        Vm.Fotos.Editar(Tarefa, b.Id, new MetadadosFoto(b.DataFoto, "Formas", "Antes da concretagem", "Ana", "Bloco B", 30, ["antes"]));
        galeria.Atualizar();

        await galeria.AmpliarCommand.ExecuteAsync(Item(galeria, "b.jpg"));

        var visualizador = Ambiente.Dialogos.Visualizadores.Single();
        (visualizador.Titulo, visualizador.Posicao, visualizador.Data).Should().Be(("Fotos — 1 Fundação", "2 de 3", "29/09/2026 09:00"));
        (visualizador.TituloDaFoto, visualizador.Legenda, visualizador.Autor, visualizador.Local, visualizador.Percentual, visualizador.Tags)
            .Should().Be(("Formas", "Antes da concretagem", "Ana", "Bloco B", "30% concluído na data", "antes"));
        (visualizador.Arquivo, ProcessadorFalso.Descrever(visualizador.Imagem), visualizador.ImagemIndisponivel)
            .Should().Be(("b.jpg · 1600 × 1200", "b.jpg|1600|1200", false));

        visualizador.ProximaCommand.Execute(null);
        (visualizador.Posicao, visualizador.Atual.Imagem.NomeArquivoOriginal, visualizador.ProximaCommand.CanExecute(null))
            .Should().Be(("3 de 3", "c.jpg", false));
        (visualizador.TituloDaFoto, visualizador.Percentual, visualizador.Tags).Should().Be((null, "0% concluído na data", null));

        visualizador.AnteriorCommand.Execute(null);
        visualizador.AnteriorCommand.Execute(null);
        (visualizador.Posicao, visualizador.AnteriorCommand.CanExecute(null)).Should().Be(("1 de 3", false));
    }

    [Fact]
    public async Task Ampliar_SemArgumento_UsaAAtual_ESoAsFiltradas()
    {
        var galeria = await ComFotos(_linha, "a.jpg", "b.jpg");
        var a = Tarefa.FotosNaOrdemDeCadastro[0];
        Vm.Fotos.Editar(Tarefa, a.Id, new MetadadosFoto(a.DataFoto, titulo: "Laje"));
        galeria.TextoFiltro = "laje";
        galeria.Selecionar(Item(galeria, "a.jpg"), adicionar: false);

        await galeria.AmpliarCommand.ExecuteAsync(null);

        var visualizador = Ambiente.Dialogos.Visualizadores.Single();
        (visualizador.Posicao, visualizador.TituloDaFoto).Should().Be(("1 de 1", "Laje"));
    }

    [Fact]
    public void Visualizador_SemFotos_Recusado_EIndiceForaDosLimitesVaiParaABorda()
    {
        var vazio = () => new VisualizadorDeFotosViewModel([], 0, Vm.Fotos, "x");
        vazio.Should().Throw<ArgumentException>().WithMessage("Não há fotos para mostrar.*");

        var vm = AbrirProjetoComFotoSemImagemNoArquivo();
        var fotos = vm.Tarefas[0].Tarefa.Fotos;
        var visualizador = new VisualizadorDeFotosViewModel(fotos, 5, vm.Fotos, "x");
        (visualizador.Posicao, visualizador.ImagemIndisponivel).Should().Be(("1 de 1", true));
    }

    // ---- Salvar e abrir ----

    [Fact]
    public async Task Salvar_LevaAsImagensNovas_EDepoisCopiaDaOrigem()
    {
        await ComFotos(_linha, "a.jpg", "b.jpg");

        await Vm.SalvarProjetoCommand.ExecuteAsync(null);

        var primeira = Ambiente.Repositorio.ImagensSalvas.Last();
        (primeira.ArquivoDeOrigem, primeira.Novas.Count).Should().Be(("/obra.pplan", 2));
        (Vm.Fotos.Pendentes, Vm.Fotos.ArquivoDeOrigem).Should().Be((0, "/obra.pplan"));

        Ambiente.Dialogos.ArquivoParaSalvar = "/copia.pplan";
        await Vm.SalvarComoProjetoCommand.ExecuteAsync(null);

        var copia = Ambiente.Repositorio.ImagensSalvas.Last();
        (copia.ArquivoDeOrigem, copia.Novas.Count, Vm.Fotos.ArquivoDeOrigem).Should().Be(("/obra.pplan", 0, "/copia.pplan"));
    }

    [Fact]
    public async Task Salvar_ComErro_MantemAsImagensNaMemoria()
    {
        await ComFotos(_linha, "a.jpg");
        Ambiente.Repositorio.ErroAoSalvar = new IOException("Disco cheio.");

        await Vm.SalvarProjetoCommand.ExecuteAsync(null);

        (Vm.Fotos.Pendentes, Vm.PossuiAlteracoesNaoSalvas).Should().Be((1, true));
        Vm.Mensagem.Should().Contain("Disco cheio.");
    }

    [Fact]
    public async Task SalvarComo_EReabrir_LevaAsImagens_EGirarDepoisFunciona()
    {
        var repositorio = new RepositorioEmMemoria();
        var vm = new MainWindowViewModel(repositorio, Ambiente.Calculadora, Ambiente.Dialogos, Ambiente.Relogio, Ambiente.Preferencias,
            Ambiente.Assistente, Ambiente.PreferenciasInterface, Ambiente.Processador, Ambiente.ConfiguracaoAssistente);
        var linha = Ambiente.AdicionarTarefa(vm, "Fundação", 3);
        vm.GerenciarFotosCommand.Execute(linha);
        await Ambiente.Dialogos.Galerias.Last().AdicionarArquivosAsync(["/fotos/a.jpg"]);
        Ambiente.Dialogos.ArquivoParaSalvar = "/obra.pplan";
        await vm.SalvarProjetoCommand.ExecuteAsync(null);
        Ambiente.Dialogos.ArquivoParaSalvar = "/copia.pplan";
        await vm.SalvarComoProjetoCommand.ExecuteAsync(null);
        repositorio.ImagensDe("/copia.pplan").Should().HaveCount(1);

        var reaberta = new MainWindowViewModel(repositorio, Ambiente.Calculadora, Ambiente.Dialogos, Ambiente.Relogio, Ambiente.Preferencias,
            Ambiente.Assistente, Ambiente.PreferenciasInterface, Ambiente.Processador, Ambiente.ConfiguracaoAssistente);
        Ambiente.Dialogos.ArquivoParaAbrir = "/copia.pplan";
        await reaberta.AbrirProjetoCommand.ExecuteAsync(null);
        reaberta.GerenciarFotosCommand.Execute(reaberta.Tarefas[0]);
        var galeria = Ambiente.Dialogos.Galerias.Last();
        galeria.Selecionar(Item(galeria, "a.jpg"), adicionar: false);
        ProcessadorFalso.Descrever(galeria.ImagemAtual).Should().Be("a.jpg|1600|1200", "lida do arquivo");

        await galeria.GirarDireitaCommand.ExecuteAsync(null);
        reaberta.PossuiAlteracoesNaoSalvas.Should().BeTrue("girar muda o projeto");
        await reaberta.SalvarProjetoCommand.ExecuteAsync(null);

        ProcessadorFalso.Descrever(repositorio.ImagensDe("/copia.pplan").Single().Value.Imagem).Should().Be("a.jpg↻|1200|1600");
        ProcessadorFalso.Descrever(repositorio.ImagensDe("/obra.pplan").Single().Value.Imagem).Should().Be("a.jpg|1600|1200");
    }

    [Fact]
    public void Abrir_ComRegistrosDeExemploDescartados_Avisa()
    {
        var descartadas = 3;
        Ambiente.Repositorio.AoAbrir = _ =>
        {
            var cronograma = new Cronograma(Guid.NewGuid(), "Obra", Ambiente.Segunda);
            cronograma.RegistrarFotosDescartadas(descartadas);
            return cronograma;
        };
        var vm = Ambiente.CriarViewModel();
        Ambiente.Dialogos.ArquivoParaAbrir = "/antigo.pplan";

        vm.AbrirProjetoCommand.Execute(null);
        vm.Mensagem.Should().Be("Projeto aberto: /antigo.pplan 3 registros de exemplo de fotos (sem imagem) foram descartados; eles somem do arquivo ao salvar.");

        descartadas = 1;
        var outra = Ambiente.CriarViewModel();
        outra.AbrirProjetoCommand.Execute(null);
        outra.Mensagem.Should().Be("Projeto aberto: /antigo.pplan 1 registro de exemplo de foto (sem imagem) foi descartado; ele some do arquivo ao salvar.");

        descartadas = 0;
        var limpa = Ambiente.CriarViewModel();
        limpa.AbrirProjetoCommand.Execute(null);
        limpa.Mensagem.Should().Be("Projeto aberto: /antigo.pplan");
    }

    [Fact]
    public async Task AbrirOutroProjeto_EsqueceAsImagensDoAnterior()
    {
        await ComFotos(_linha, "a.jpg");
        Vm.SalvarProjetoCommand.Execute(null);
        Ambiente.Repositorio.AoAbrir = _ => new Cronograma(Guid.NewGuid(), "Outra", Ambiente.Segunda);
        Ambiente.Dialogos.ArquivoParaAbrir = "/outra.pplan";

        await Vm.AbrirProjetoCommand.ExecuteAsync(null);

        (Vm.Fotos.Pendentes, Vm.Fotos.ArquivoDeOrigem).Should().Be((0, "/outra.pplan"));
    }

    // ---- Remover tarefa com fotos ----

    [Fact]
    public async Task RemoverTarefa_ComFotos_PedeConfirmacaoComAQuantidade()
    {
        await ComFotos(_linha, "a.jpg", "b.jpg");
        Vm.TarefaSelecionada = _linha;
        Ambiente.Dialogos.Confirmar = false;

        await Vm.RemoverTarefaCommand.ExecuteAsync(null);

        Ambiente.Dialogos.Confirmacoes.Last().Should().Be("Remover a tarefa \"Fundação\"? As 2 fotos dela também serão excluídas.");
        Vm.Tarefas.Should().ContainSingle("cancelou");

        Ambiente.Dialogos.Confirmar = true;
        await Vm.RemoverTarefaCommand.ExecuteAsync(null);
        Vm.Tarefas.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoverTarefa_UmaFoto_NoSingular_ESemFotos_NaoPergunta()
    {
        var outra = Ambiente.AdicionarTarefa(Vm, "Estrutura", 2);
        await ComFotos(_linha, "a.jpg");
        Vm.TarefaSelecionada = _linha;
        Ambiente.Dialogos.Confirmar = false;

        await Vm.RemoverTarefaCommand.ExecuteAsync(null);
        Ambiente.Dialogos.Confirmacoes.Last().Should().Be("Remover a tarefa \"Fundação\"? A foto dela também será excluída.");

        var perguntas = Ambiente.Dialogos.Confirmacoes.Count;
        Vm.TarefaSelecionada = outra;
        await Vm.RemoverTarefaCommand.ExecuteAsync(null);
        (Ambiente.Dialogos.Confirmacoes.Count, Vm.Tarefas.Count).Should().Be((perguntas, 1));
    }

    [Fact]
    public async Task RemoverResumo_ContaAsFotosDasSubtarefas()
    {
        Vm.TarefaSelecionada = _linha;
        Vm.AdicionarSubtarefaCommand.Execute(null);
        var a = Vm.TarefaSelecionada!;
        Vm.TarefaSelecionada = _linha;
        Vm.AdicionarSubtarefaCommand.Execute(null);
        var b = Vm.TarefaSelecionada!;
        await ComFotos(a, "a.jpg");
        await ComFotos(b, "b.jpg", "c.jpg");
        Vm.TarefaSelecionada = _linha;
        Ambiente.Dialogos.Confirmar = false;

        await Vm.RemoverTarefaCommand.ExecuteAsync(null);

        Ambiente.Dialogos.Confirmacoes.Last().Should().Be("Remover o resumo \"Fundação\" e as 2 subtarefas dele? As 3 fotos delas também serão excluídas.");
        Vm.Tarefas.Should().HaveCount(3);
    }
}
