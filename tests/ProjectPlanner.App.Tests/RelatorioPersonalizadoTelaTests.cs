using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.Relatorios;
using SkiaSharp;

namespace ProjectPlanner.App.Tests;

// Bloco 88: Arquivo > Relatório personalizado…: os modelos (os prontos e os do usuário), o período com os atalhos, a aparência, as
// seções (ligar, ordenar, as opções, acrescentar), a prévia que acompanha cada mudança, salvar e excluir modelos e o PDF. "Hoje" é
// segunda 28/09/2026.
public sealed class RelatorioPersonalizadoTelaTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-relatorio-" + Guid.NewGuid().ToString("N"));
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;

    public RelatorioPersonalizadoTelaTests()
    {
        Directory.CreateDirectory(_pasta);
        _vm = _ambiente.CriarViewModel();
        _vm.InicioProjeto = new DateTime(2026, 8, 3);
        Ambiente.AdicionarTarefa(_vm, "Fundação", 10);
        Ambiente.AdicionarTarefa(_vm, "Alvenaria", 15, "1").PercentualConcluido = 40;
        Ambiente.AdicionarTarefa(_vm, "Reboco", 10, "2");
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private async Task<RelatorioPersonalizadoViewModel> Abrir()
    {
        await _vm.RelatorioPersonalizadoCommand.ExecuteAsync(null);
        var tela = _ambiente.Dialogos.RelatoriosPersonalizados[^1];
        tela.Espera = TimeSpan.Zero;
        tela.Mudou();
        await tela.Atualizacao;
        return tela;
    }

    private static async Task Esperar(RelatorioPersonalizadoViewModel tela) => await tela.Atualizacao;

    [Fact]
    public async Task AbreNoMensal_ComOMesPassado_EAPreviaDaPrimeiraPagina()
    {
        var tela = await Abrir();

        tela.Modelos.Select(m => m.Texto).Should().Equal("Relatório mensal ao cliente (pronto)", "Relatório semanal da obra (pronto)",
            "Resumo executivo (pronto)", "Relatório financeiro (pronto)");
        (tela.ModeloEscolhido!.Nome, tela.Titulo, tela.De, tela.Ate).Should().Be(("Relatório mensal ao cliente", "Relatório mensal de obra",
            (DateTime?)new DateTime(2026, 8, 1), (DateTime?)new DateTime(2026, 8, 31)));
        tela.Secoes.Select(s => (s.Tipo, s.Ativa)).Should().Equal(ModelosProntos.Mensal.Secoes.Select(s => (s.Tipo, s.Ativa)));
        (tela.Erro, tela.TotalDePaginas > 1, tela.TextoDaPagina).Should().Be(("", true, $"Página 1 de {tela.TotalDePaginas}"));
        using var previa = SKBitmap.Decode(tela.Previa);
        previa.Width.Should().Be(RelatorioPersonalizadoViewModel.LarguraDaPrevia);
        tela.TituloDaJanela.Should().StartWith("Relatório personalizado — ");
    }

    [Fact]
    public async Task ASecoes_LigarOrdenarAcrescentar_EAPreviaAcompanha()
    {
        var tela = await Abrir();
        var antes = tela.TotalDePaginas;

        foreach (var secao in tela.Secoes.Where(s => s.Tipo != TipoDeSecao.Resumo))
            secao.Ativa = false;
        await Esperar(tela);
        tela.TotalDePaginas.Should().Be(1, $"só a obra em números (antes, {antes} páginas)");

        var resumo = tela.Secoes.Single(s => s.Tipo == TipoDeSecao.Resumo);
        resumo.SubirCommand.Execute(null);
        tela.Secoes[0].Should().BeSameAs(resumo);
        resumo.SubirCommand.Execute(null);
        tela.Secoes[0].Should().BeSameAs(resumo, "a primeira não sobe mais");

        tela.AdicionarSecaoCommand.Execute(TipoDeSecao.Texto);
        var texto = tela.Secoes[^1];
        (texto.Tipo, texto.Aberta, texto.TemTexto, texto.TemFotos).Should().Be((TipoDeSecao.Texto, true, true, false));
        texto.Texto = "A concretagem da laje ficou para a próxima semana.";
        texto.PaginaNova = true;
        await Esperar(tela);
        tela.TotalDePaginas.Should().Be(2);
        tela.ModeloAtual().Secoes[^1].Should().Be(new SecaoDoRelatorio(TipoDeSecao.Texto)
            { Texto = "A concretagem da laje ficou para a próxima semana.", PaginaNova = true });

        await tela.ProximaPaginaCommand.ExecuteAsync(null);
        (tela.PaginaAtual, tela.TextoDaPagina, tela.ProximaPaginaCommand.CanExecute(null)).Should().Be((1, "Página 2 de 2", false));
        texto.RemoverCommand.Execute(null);
        await Esperar(tela);
        (tela.TotalDePaginas, tela.PaginaAtual).Should().Be((1, 0), "a página que sumiu volta para a última que existe");

        tela.AdicionarSecaoCommand.Execute(TipoDeSecao.Capa);
        tela.Secoes[0].Tipo.Should().Be(TipoDeSecao.Capa, "a capa entra no começo");
        await Esperar(tela);
        tela.TotalDePaginas.Should().Be(2, "a capa numa página e os números na outra");
    }

    [Fact]
    public async Task OPeriodo_ACor_EOQueNaoVale()
    {
        var tela = await Abrir();

        tela.EstaSemanaCommand.Execute(null);
        (tela.De, tela.Ate).Should().Be(((DateTime?)new DateTime(2026, 9, 28), (DateTime?)new DateTime(2026, 9, 28)));
        tela.DesdeOInicioCommand.Execute(null);
        tela.De.Should().Be(new DateTime(2026, 8, 3));

        tela.EscolherCorCommand.Execute("#C62828");
        tela.Paisagem = true;
        await Esperar(tela);
        (tela.ModeloAtual().Tema.Cor, tela.ModeloAtual().Tema.Paisagem, tela.Erro).Should().Be(("#C62828", true, ""));
        using (var previa = SKBitmap.Decode(tela.Previa))
            (previa.Width > previa.Height).Should().BeTrue("paisagem");

        tela.Cor = "vermelho";
        await Esperar(tela);
        (tela.Erro, tela.Previa, tela.GerarPdfCommand.CanExecute(null)).Should().Be(("A cor do relatório precisa ser como #1565C0.", (byte[]?)null, false));
        tela.Cor = "#2E7D32";
        foreach (var secao in tela.Secoes)
            secao.Ativa = false;
        await Esperar(tela);
        tela.Erro.Should().Be("Ligue pelo menos uma seção.");
    }

    [Fact]
    public async Task SalvarComoMeuModelo_ElePassaAAbrir_EExcluir()
    {
        var tela = await Abrir();
        tela.Titulo = "Relatório para a prefeitura";
        tela.Rodape = "Construtora Silva · CNPJ 00.000.000/0001-00";
        tela.Secoes.Single(s => s.Tipo == TipoDeSecao.Gantt).Ativa = false;

        _ambiente.Dialogos.TextoDigitado = "Relatório mensal ao cliente";
        await tela.SalvarComoCommand.ExecuteAsync(null);
        tela.Mensagem.Should().Be("\"Relatório mensal ao cliente\" é o nome de um modelo pronto: escolha outro nome.");

        _ambiente.Dialogos.TextoDigitado = "Prefeitura";
        await tela.SalvarModeloCommand.ExecuteAsync(null); // num pronto, salvar é "salvar como"
        _ambiente.Dialogos.TextosPedidos[^1].ValorInicial.Should().Be("Relatório mensal ao cliente (meu)");
        var salvo = _ambiente.ModelosDeRelatorio.Salvos;
        (salvo.Ultimo, salvo.Modelos.Single().Nome, salvo.Modelos.Single().Titulo, salvo.Modelos.Single().Tema.Rodape)
            .Should().Be(("Prefeitura", "Prefeitura", "Relatório para a prefeitura", "Construtora Silva · CNPJ 00.000.000/0001-00"));
        salvo.Modelos.Single().Secoes.Single(s => s.Tipo == TipoDeSecao.Gantt).Ativa.Should().BeFalse();
        (tela.ModeloEscolhido!.Nome, tela.ModeloEscolhido.Pronto, tela.ExcluirModeloCommand.CanExecute(null)).Should().Be(("Prefeitura", false, true));
        tela.Modelos.Should().HaveCount(5);

        var outra = await Abrir();
        (outra.ModeloEscolhido!.Nome, outra.Titulo, outra.Rodape).Should().Be(("Prefeitura", "Relatório para a prefeitura", "Construtora Silva · CNPJ 00.000.000/0001-00"));

        await outra.ExcluirModeloCommand.ExecuteAsync(null);
        _ambiente.Dialogos.Confirmacoes[^1].Should().Be("Excluir o modelo \"Prefeitura\"? Os relatórios já gerados não mudam.");
        (_ambiente.ModelosDeRelatorio.Salvos.Modelos.Count, outra.ModeloEscolhido!.Nome, outra.Modelos.Count).Should().Be((0, "Relatório mensal ao cliente", 4));
    }

    [Fact]
    public async Task GerarPdf_SalvaOArquivo_ComONomeSugerido()
    {
        var tela = await Abrir();
        _ambiente.Dialogos.ArquivoDoRelatorio = Path.Combine(_pasta, "relatorio.pdf");

        await tela.GerarPdfCommand.ExecuteAsync(null);

        File.ReadAllBytes(Path.Combine(_pasta, "relatorio.pdf")).Take(5).Should().Equal("%PDF-"u8.ToArray());
        _ambiente.Dialogos.RelatoriosSugeridos[^1].Should().Be(($"Relatório mensal de obra - {_vm.Cronograma.Nome} - 2026-08-31", "pdf"));
        tela.Mensagem.Should().StartWith($"Relatório salvo: {Path.Combine(_pasta, "relatorio.pdf")} (").And.EndWith(" páginas).");
        _ambiente.ModelosDeRelatorio.Salvos.Ultimo.Should().Be("Relatório mensal ao cliente", "o próximo relatório abre no mesmo modelo");
    }

    [Fact]
    public void OArquivoDosModelos_GuardaELe_EIgnoraOQueNaoVale()
    {
        var caminho = Path.Combine(_pasta, "modelos-de-relatorio.json");
        var arquivo = new ArquivoModelosDeRelatorio(caminho);
        arquivo.Carregar().Should().Be(ModelosSalvos.Vazios);

        var meu = ModelosProntos.Semanal with { Nome = "Semanal da Ana", Tema = new TemaDoRelatorio { Cor = "#AD1457", Rodape = "Ana Obras" } };
        arquivo.Salvar(new ModelosSalvos([meu], "Semanal da Ana"));
        var lido = arquivo.Carregar();

        (lido.Ultimo, lido.Modelos.Single().Nome, lido.Modelos.Single().Tema, lido.Modelos.Single().Periodo)
            .Should().Be(("Semanal da Ana", "Semanal da Ana", meu.Tema, PeriodoPadrao.EstaSemana));
        lido.Modelos.Single().Secoes.Should().Equal(meu.Secoes);
        File.ReadAllText(caminho).Should().Contain("\"tipo\": \"Capa\"", "os tipos por nome, para o arquivo continuar valendo se a lista mudar")
            .And.NotContain("ativas").And.NotContain("tituloOuPadrao");

        File.WriteAllText(caminho, """{"modelos":[{"nome":"","secoes":[]},{"nome":"Bom","titulo":"T","secoes":[{"tipo":"Resumo"}]}],"ultimo":"Bom"}""");
        arquivo.Carregar().Modelos.Select(m => m.Nome).Should().Equal(["Bom"], "o que não vale fica de fora");
        File.WriteAllText(caminho, "{ quebrado");
        arquivo.Carregar().Should().Be(ModelosSalvos.Vazios);
    }
}
