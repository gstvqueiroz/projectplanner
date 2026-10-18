using System.Text;
using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.Relatorios;

namespace ProjectPlanner.App.Tests;

// Bloco 27: Arquivo > Relatório fotográfico… e a curadoria das fotos na galeria ("hoje" é seg 28/09/2026).
public sealed class RelatorioFotograficoTests : TesteDeFotos, IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-relatorio-" + Guid.NewGuid().ToString("N"));
    private readonly PreferenciasRelatorioEmMemoria _preferencias = new();
    private readonly MainWindowViewModel _vm;

    public RelatorioFotograficoTests()
    {
        Directory.CreateDirectory(_pasta);
        _vm = new MainWindowViewModel(Ambiente.Repositorio, Ambiente.Calculadora, Ambiente.Dialogos, Ambiente.Relogio, Ambiente.Preferencias,
            Ambiente.Assistente, Ambiente.PreferenciasInterface, Ambiente.Processador, Ambiente.ConfiguracaoAssistente, _preferencias);
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private async Task<(TarefaViewModel Fundacao, TarefaViewModel Estrutura)> Obra()
    {
        Datas(("a.jpg", 8, 0), ("b.jpg", 10, 0), ("c.jpg", 9, 0));
        var fundacao = Ambiente.AdicionarTarefa(_vm, "Fundação", 3);
        var estrutura = Ambiente.AdicionarTarefa(_vm, "Estrutura", 5, "1");
        _vm.GerenciarFotosCommand.Execute(fundacao);
        await Ambiente.Dialogos.Galerias.Last().AdicionarArquivosAsync(["/fotos/a.jpg", "/fotos/b.jpg"]);
        _vm.GerenciarFotosCommand.Execute(estrutura);
        await Ambiente.Dialogos.Galerias.Last().AdicionarArquivosAsync(["/fotos/c.jpg"]);
        return (fundacao, estrutura);
    }

    // As fotos de hoje (o processador falso as data de 29/09 por padrão).
    private void Datas(params (string Nome, int Hora, int Minuto)[] fotos)
    {
        foreach (var (nome, hora, minuto) in fotos)
            Ambiente.Processador.Datas[nome] = Ambiente.Segunda.AddHours(hora).AddMinutes(minuto);
    }

    private RelatorioFotograficoViewModel Abrir()
    {
        _vm.RelatorioFotograficoCommand.Execute(null);
        return Ambiente.Dialogos.Relatorios.Last();
    }

    // Bloco 37: a página de indicadores no fim do período.
    [Fact]
    public async Task ComIndicadores_UmaPaginaAMais()
    {
        await Obra();
        var tela = Abrir();
        tela.Previa.Should().Be("Relatório nº 1 · 3 fotos · 4 páginas", "capa, uma página por tarefa e o índice");

        tela.ComIndicadores = true;

        (tela.Previa, tela.Erro).Should().Be(("Relatório nº 1 · 3 fotos · 5 páginas", ""));
    }

    [Fact]
    public async Task Abrir_PeriodoDesdeOInicio_EPrevia()
    {
        await Obra();
        _preferencias.Atuais = new PreferenciasRelatorio("Eng. Ana", "CREA 1", null, 4, AgrupamentoDoRelatorio.PorData);

        var tela = Abrir();

        (tela.De, tela.Ate).Should().Be((Ambiente.Segunda, Ambiente.Segunda));
        (tela.Responsavel, tela.Registro, tela.FotosPorPaginaEscolhida.Valor, tela.AgrupamentoEscolhido.Valor)
            .Should().Be(("Eng. Ana", "CREA 1", 4, AgrupamentoDoRelatorio.PorData));
        tela.Previa.Should().Be("Relatório nº 1 · 3 fotos · 3 páginas");
        (tela.Erro, tela.GerarPdfCommand.CanExecute(null)).Should().Be(("", true));
    }

    [Fact]
    public async Task SemFotosNoPeriodo_ErroENaoGera()
    {
        await Obra();
        var tela = Abrir();

        tela.De = tela.Ate = new DateTime(2026, 10, 5);

        (tela.GerarPdfCommand.CanExecute(null), tela.GerarWordCommand.CanExecute(null)).Should().Be((false, false));
        // Bloco 42: a mensagem diz onde estão as fotos (fora do período, pela data da câmera) e o botão as inclui.
        tela.Erro.Should().StartWith("Nenhuma foto no período com essas escolhas. ").And.Contain("fora dele").And.EndWith("use Incluir todas as fotos.");
        tela.TemFotosForaDoPeriodo.Should().BeTrue();
        tela.IncluirTodasAsFotosCommand.Execute(null);
        (tela.Erro, tela.TemFotosForaDoPeriodo, tela.GerarPdfCommand.CanExecute(null)).Should().Be(("", false, true));
        tela.De = tela.Ate = new DateTime(2026, 10, 5);
        tela.Ate = new DateTime(2026, 10, 1);
        tela.Erro.Should().Be("O período termina antes de começar.");
    }

    [Fact]
    public async Task FiltrosETagsMudamAPrevia_ERamoSoComUmResumo()
    {
        var (fundacao, _) = await Obra();
        var tela = Abrir();

        tela.TarefasEscolhidas = tela.OpcoesDeTarefas.Single(o => o.Valor == TarefasDoRelatorio.DeUmRamo);
        (tela.EscolheRamo, tela.Ramos.Count, tela.Erro).Should().Be((true, 0, "Escolha o resumo do ramo."));

        tela.TarefasEscolhidas = tela.OpcoesDeTarefas[0];
        tela.Tags = "inexistente";
        tela.Previa.Should().Be("Relatório nº 1 · 0 fotos · 2 páginas");
    }

    [Fact]
    public async Task DadosDaObra_VaoParaOProjeto_EMarcamAlteracao()
    {
        await Obra();
        Ambiente.Dialogos.ArquivoParaSalvar = "/obra.pplan";
        _vm.SalvarProjetoCommand.Execute(null);
        _vm.PossuiAlteracoesNaoSalvas.Should().BeFalse();
        var tela = Abrir();

        tela.Contratante = "Sr. Silva";
        tela.Endereco = "Rua A, 1";

        (_vm.Obra.Contratante, _vm.Obra.Endereco, _vm.PossuiAlteracoesNaoSalvas).Should().Be(("Sr. Silva", "Rua A, 1", true));
        tela.Contrato = new string('x', DadosDaObra.TamanhoMaximo + 1);
        tela.Erro.Should().StartWith("O contrato tem 201 caracteres");
        _vm.Obra.Contrato.Should().BeNull();
    }

    [Fact]
    public async Task GerarPdfEWord_UmNumeroSo_NoHistorico_EPreferenciasGuardadas()
    {
        await Obra();
        var tela = Abrir();
        tela.Responsavel = "Eng. Ana";
        Ambiente.Dialogos.ArquivoDoRelatorio = Path.Combine(_pasta, "rel.pdf");

        await tela.GerarPdfCommand.ExecuteAsync(null);
        await tela.GerarWordCommand.ExecuteAsync(null);

        Ambiente.Dialogos.RelatoriosSugeridos.Should().Equal(("Relatório fotográfico 01 - Projeto sem título", "pdf"), ("Relatório fotográfico 01 - Projeto sem título", "docx"));
        Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(_pasta, "rel.pdf")), 0, 5).Should().Be("%PDF-");
        File.ReadAllBytes(Path.Combine(_pasta, "rel.docx"))[..2].Should().Equal((byte)'P', (byte)'K');
        _vm.RelatoriosGerados.Should().ContainSingle().Which.Should().Match<RelatorioGerado>(r => r.Numero == 1 && r.Fotos == 3);
        (tela.Mensagem, tela.UltimoArquivo).Should().Be(("Relatório nº 1 salvo em rel.docx.", Path.Combine(_pasta, "rel.docx")));
        _preferencias.Atuais.Responsavel.Should().Be("Eng. Ana");
        _vm.PossuiAlteracoesNaoSalvas.Should().BeTrue();

        await tela.AbrirUltimoCommand.ExecuteAsync(null);
        Ambiente.Dialogos.ArquivosAbertos.Should().Equal(Path.Combine(_pasta, "rel.docx"));
        Abrir().Previa.Should().StartWith("Relatório nº 2", "o próximo relatório ganha o número seguinte");
    }

    [Fact]
    public async Task GerarCancelado_OuPastaQueNaoExiste_NadaNoHistorico()
    {
        await Obra();
        var tela = Abrir();

        await tela.GerarPdfCommand.ExecuteAsync(null);
        tela.Mensagem.Should().BeEmpty("cancelado na escolha do arquivo");

        Ambiente.Dialogos.ArquivoDoRelatorio = Path.Combine(_pasta, "nao-existe", "rel.pdf");
        await tela.GerarPdfCommand.ExecuteAsync(null);

        tela.Mensagem.Should().StartWith("Não foi possível salvar o relatório:");
        (_vm.RelatoriosGerados.Count, tela.UltimoArquivo, tela.Gerando).Should().Be((0, (string?)null, false));
    }

    [Fact]
    public async Task Logotipo_EscolherETirar()
    {
        await Obra();
        var logo = Path.Combine(_pasta, "logo.png");
        File.WriteAllBytes(logo, [0x89, 0x50, 0x4E, 0x47]);
        var tela = Abrir();
        Ambiente.Dialogos.ImagemEscolhida = logo;

        await tela.EscolherLogotipoCommand.ExecuteAsync(null);
        (tela.TemLogotipo, _preferencias.Atuais.Logotipo).Should().Be((true, logo));

        tela.RemoverLogotipoCommand.Execute(null);
        (tela.TemLogotipo, _preferencias.Atuais.Logotipo).Should().Be((false, (string?)null));

        Ambiente.Dialogos.ImagemEscolhida = Path.Combine(_pasta, "sumiu.png");
        await tela.EscolherLogotipoCommand.ExecuteAsync(null);
        (tela.TemLogotipo, tela.Mensagem).Should().Be((false, $"Não foi possível usar o logotipo: o arquivo não é uma imagem que dê para ler."));
    }

    [Fact]
    public async Task FotosParecidas_Avisa()
    {
        Datas(("a.jpg", 8, 0), ("b.jpg", 8, 1));
        var fundacao = Ambiente.AdicionarTarefa(_vm, "Fundação", 3);
        _vm.GerenciarFotosCommand.Execute(fundacao);
        await Ambiente.Dialogos.Galerias.Last().AdicionarArquivosAsync(["/fotos/a.jpg", "/fotos/b.jpg"]);

        Abrir().AvisoDeParecidas.Should().StartWith("As fotos 1 e 2 são da mesma tarefa, com poucos minutos de diferença");
    }

    // ---- Curadoria na galeria ----

    [Fact]
    public async Task Galeria_TirarDoRelatorioEDestaque_ValemNaHora_SemPerderAEdicao()
    {
        var (fundacao, _) = await Obra();
        _vm.GerenciarFotosCommand.Execute(fundacao);
        var galeria = Ambiente.Dialogos.Galerias.Last();
        var item = Item(galeria, "a.jpg");
        galeria.Selecionar(item, false);
        galeria.EdicaoTitulo = "Título em edição";

        galeria.DestaqueNoRelatorio = true;
        (item.Foto.NoRelatorio, item.Foto.Destaque, item.EhDestaque).Should().Be((true, true, true));
        galeria.NoRelatorio = false;

        (item.Foto.Destaque, item.ForaDoRelatorio, galeria.DestaqueNoRelatorio).Should().Be((false, true, false), "fora do relatório não há destaque");
        fundacao.Tarefa.Foto(item.Foto.Id)!.NoRelatorio.Should().BeFalse();
        (galeria.EdicaoTitulo, galeria.EdicaoAlterada).Should().Be(("Título em edição", true));
        _vm.PossuiAlteracoesNaoSalvas.Should().BeTrue();
        Abrir().Previa.Should().StartWith("Relatório nº 1 · 2 fotos", "a tirada do relatório não entra");

        galeria.DestaqueNoRelatorio = true;
        (item.Foto.NoRelatorio, item.Foto.Destaque).Should().Be((true, true), "marcar o destaque põe a foto de volta");
    }
}

// O relatorio.json de verdade.
public sealed class ArquivoPreferenciasRelatorioTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-relatorio-json-" + Guid.NewGuid().ToString("N"));
    private string Caminho => Path.Combine(_pasta, "relatorio.json");

    public ArquivoPreferenciasRelatorioTests() => Directory.CreateDirectory(_pasta);

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    [Fact]
    public void SemArquivo_Padrao()
    {
        new ArquivoPreferenciasRelatorio(Caminho).Carregar().Should().Be(new PreferenciasRelatorio());
    }

    [Fact]
    public void LogotipoCopiadoParaAPasta_ESalvarECarregar()
    {
        var original = Path.Combine(_pasta, "minha-empresa.PNG");
        File.WriteAllBytes(original, [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(_pasta, "logotipo.jpg"), [9]);
        var arquivo = new ArquivoPreferenciasRelatorio(Caminho);

        var copia = arquivo.GuardarLogotipo(original);
        arquivo.Salvar(new PreferenciasRelatorio("Eng. Ana", "CREA 1", copia, 6, AgrupamentoDoRelatorio.PorLocal));

        copia.Should().Be(Path.Combine(_pasta, "logotipo.png"));
        File.Exists(Path.Combine(_pasta, "logotipo.jpg")).Should().BeFalse("o logotipo anterior sai");
        new ArquivoPreferenciasRelatorio(Caminho).Carregar().Should().Be(new PreferenciasRelatorio("Eng. Ana", "CREA 1", copia, 6, AgrupamentoDoRelatorio.PorLocal));
    }

    [Theory]
    [InlineData("isto não é json")]
    [InlineData("{\"versao\": 2, \"responsavel\": \"x\"}")]
    public void Invalido_Padrao(string conteudo)
    {
        File.WriteAllText(Caminho, conteudo);

        new ArquivoPreferenciasRelatorio(Caminho).Carregar().Should().Be(new PreferenciasRelatorio());
    }

    [Fact]
    public void ValoresForaDoEsperado_ViramOPadrao_ELogotipoQueSumiu_Nulo()
    {
        File.WriteAllText(Caminho, """{"versao": 1, "responsavel": "  ", "logotipo": "/nao/existe.png", "fotosPorPagina": 3, "agrupamento": "voando"}""");

        new ArquivoPreferenciasRelatorio(Caminho).Carregar().Should().Be(new PreferenciasRelatorio());
    }
}
