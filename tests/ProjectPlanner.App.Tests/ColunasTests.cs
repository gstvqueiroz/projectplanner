using ProjectPlanner.App.Models;
using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

public class ColunasTests
{
    private readonly PreferenciasEmMemoria _preferencias = new();

    private ColunasViewModel Colunas() => new(_preferencias);

    private static List<string> Ordem(ColunasViewModel colunas) =>
        colunas.Colunas.OrderBy(c => c.Posicao).Select(c => c.Id).ToList();

    [Fact]
    public void SemPreferenciaSalva_UsaOLayoutPadrao()
    {
        var colunas = Colunas();

        Ordem(colunas).Should().Equal(ColunasViewModel.Padrao.Select(p => p.Id));
        // Bloco 31: as colunas de restrição, prazo, linha de base, desvios e valor começam ocultas (e as de unidade, quantidade
        // e preço unitário, do Bloco 36); as demais, visíveis.
        colunas.Colunas.Where(c => !c.Visivel).Select(c => c.Id).Should().BeEquivalentTo(
            ["NaoIniciarAntesDe", "Prazo", "InicioLinhaDeBase", "TerminoLinhaDeBase", "DesvioDoInicio", "DesvioDoTermino", "Valor",
             "Unidade", "Quantidade", "PrecoUnitario"]);
        colunas.Colunas.Where(c => !ColunasViewModel.OcultasPorPadrao.Contains(c.Id)).Should().OnlyContain(c => c.Visivel);
        colunas["Nome"].Largura.Should().Be(220);
        colunas["Notas"].Largura.Should().Be(200);
        _preferencias.Gravacoes.Should().Be(0);
    }

    [Fact]
    public void PadraoTemAsNovasColunasAoLadoDasRelacionadas()
    {
        var ids = ColunasViewModel.Padrao.Select(p => p.Id).ToList();

        ids.IndexOf("Edt").Should().Be(ids.IndexOf("Numero") + 1);
        ids.IndexOf("Sucessoras").Should().Be(ids.IndexOf("Predecessoras") + 1);
        // As dos Blocos 31, 35 e 36 vêm depois das Notas, na ordem em que entraram.
        ids.Skip(ids.IndexOf("Notas") + 1).Should().Equal(
            "NaoIniciarAntesDe", "Prazo", "InicioLinhaDeBase", "TerminoLinhaDeBase", "DesvioDoInicio", "DesvioDoTermino", "Valor",
            "Unidade", "Quantidade", "PrecoUnitario");
        ids.Should().Contain(["Folga", "Critica"]);
    }

    [Fact]
    public void OcultarUmaColuna_AtualizaOEstadoEGrava()
    {
        var colunas = Colunas();

        colunas["Notas"].Visivel = false;

        colunas["Notas"].Visivel.Should().BeFalse();
        _preferencias.Salvo!.Colunas.Single(c => c.Id == "Notas").Visivel.Should().BeFalse();
        _preferencias.Gravacoes.Should().Be(1);
    }

    [Fact]
    public void AColunaNome_NaoPodeSerOcultada()
    {
        var colunas = Colunas();

        colunas["Nome"].PodeOcultar.Should().BeFalse();
        colunas["Nome"].Visivel = false;

        colunas["Nome"].Visivel.Should().BeTrue();
    }

    [Fact]
    public void ReordenarERedimensionarNaGrade_GuardaPosicaoELargura()
    {
        var colunas = Colunas();
        // O usuário arrastou Notas para a 3ª posição e alargou Nome para 300.
        var ordem = ColunasViewModel.Padrao.Select(p => p.Id).Where(id => id != "Notas").ToList();
        ordem.Insert(2, "Notas");
        var daGrade = ordem.Select((id, i) => (id, i, id == "Nome" ? 300d : colunas[id].Largura));

        colunas.RegistrarLayoutDaGrade(daGrade);

        Ordem(colunas).Take(4).Should().Equal("Numero", "Edt", "Notas", "Nome");
        colunas["Nome"].Largura.Should().Be(300);
        _preferencias.Salvo!.Colunas.Single(c => c.Id == "Notas").Posicao.Should().Be(2);
        _preferencias.Salvo.Colunas.Single(c => c.Id == "Nome").Largura.Should().Be(300);
    }

    [Fact]
    public void LayoutIgualAoAtual_NaoGravaDeNovo()
    {
        var colunas = Colunas();

        colunas.RegistrarLayoutDaGrade(colunas.Colunas.Select(c => (c.Id, c.Posicao, c.Largura)));

        _preferencias.Gravacoes.Should().Be(0);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(0)]
    [InlineData(-50)]
    [InlineData(5)]
    public void LarguraInvalidaVindaDaGrade_EIgnorada(double largura)
    {
        var colunas = Colunas();

        colunas.RegistrarLayoutDaGrade([("Nome", colunas["Nome"].Posicao, largura)]);

        colunas["Nome"].Largura.Should().Be(220);
    }

    [Fact]
    public void AoAbrir_AplicaOLayoutSalvo()
    {
        _preferencias.Salvo = new LayoutColunas(ColunasViewModel.Padrao
            .Select((p, i) => new EstadoColuna(p.Id, p.Id != "Folga", p.Id == "Nome" ? 250 : p.Largura,
                p.Id == "Notas" ? -1 : i))
            .ToList());

        var colunas = Colunas();

        Ordem(colunas)[0].Should().Be("Notas");
        colunas["Folga"].Visivel.Should().BeFalse();
        colunas["Nome"].Largura.Should().Be(250);
        colunas.Colunas.Select(c => c.Posicao).Order().Should().Equal(Enumerable.Range(0, colunas.Colunas.Count));
    }

    [Fact]
    public void LayoutSalvoIncompletoOuComLixo_CompletaComOPadrao()
    {
        // Um arquivo antigo, sem EDT/Sucessoras/Notas, com um id que não existe, uma largura inválida,
        // posições repetidas e a coluna Nome oculta à força.
        _preferencias.Salvo = new LayoutColunas(
        [
            new("Nome", false, double.NaN, 0),
            new("Numero", true, 70, 0),
            new("Predecessoras", true, 150, 1),
            new("ColunaQueNaoExiste", true, 100, 2),
        ]);

        var colunas = Colunas();

        colunas["Nome"].Visivel.Should().BeTrue();
        colunas["Nome"].Largura.Should().Be(220);
        colunas["Numero"].Largura.Should().Be(70);
        colunas["Edt"].Visivel.Should().BeTrue();
        colunas.Colunas.Select(c => c.Posicao).Order().Should().Equal(Enumerable.Range(0, colunas.Colunas.Count));
        // EDT fica logo depois do Nº, como no padrão; Sucessoras logo depois das Predecessoras.
        Ordem(colunas).IndexOf("Edt").Should().Be(Ordem(colunas).IndexOf("Numero") + 1);
        Ordem(colunas).IndexOf("Sucessoras").Should().Be(Ordem(colunas).IndexOf("Predecessoras") + 1);
    }

    [Fact]
    public void RestaurarPadrao_DesfazTudoGravaEAvisaATela()
    {
        var colunas = Colunas();
        colunas["Notas"].Visivel = false;
        colunas["Folga"].Visivel = false;
        colunas.RegistrarLayoutDaGrade([("Notas", 0, 400), ("Numero", 1, 65)]);
        var avisou = false;
        colunas.LayoutRestaurado += (_, _) => avisou = true;

        colunas.RestaurarPadraoCommand.Execute(null);

        Ordem(colunas).Should().Equal(ColunasViewModel.Padrao.Select(p => p.Id));
        colunas.Colunas.Should().OnlyContain(c => c.Visivel != ColunasViewModel.OcultasPorPadrao.Contains(c.Id));
        colunas["Notas"].Largura.Should().Be(200);
        avisou.Should().BeTrue();
        _preferencias.Salvo!.Colunas.Should().OnlyContain(c => c.Visivel != ColunasViewModel.OcultasPorPadrao.Contains(c.Id));
    }

    [Fact]
    public void MainWindowViewModel_ExpoeAsColunasLidasDasPreferencias()
    {
        var ambiente = new Ambiente();
        ambiente.Preferencias.Salvo = new LayoutColunas([new("Notas", false, 200, 15)]);

        var vm = ambiente.CriarViewModel();

        vm.Colunas["Notas"].Visivel.Should().BeFalse();
    }
}

public sealed class ArquivoPreferenciasColunasTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "projectplanner-testes", Guid.NewGuid().ToString("N"));

    private string Caminho => Path.Combine(_pasta, "sub", "colunas.json");

    [Fact]
    public void SemArquivo_NaoHaLayout()
    {
        new ArquivoPreferenciasColunas(Caminho).Carregar().Should().BeNull();
    }

    [Fact]
    public void SalvarECarregar_DevolveOMesmoLayoutECriaAPasta()
    {
        var layout = new LayoutColunas([new("Nome", true, 180.5, 0), new("Notas", false, 200, 1)]);
        new ArquivoPreferenciasColunas(Caminho).Salvar(layout);

        var lido = new ArquivoPreferenciasColunas(Caminho).Carregar();

        lido!.Colunas.Should().Equal(layout.Colunas);
        File.ReadAllText(Caminho).Should().Contain("\"versao\": 1").And.Contain("\"id\": \"Nome\"");
    }

    [Theory]
    [InlineData("isto não é json")]
    [InlineData("{\"versao\": 99, \"colunas\": []}")]
    [InlineData("{\"versao\": 1}")]
    [InlineData("[]")]
    [InlineData("")]
    public void ArquivoInvalido_NaoHaLayout(string conteudo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Caminho)!);
        File.WriteAllText(Caminho, conteudo);

        new ArquivoPreferenciasColunas(Caminho).Carregar().Should().BeNull();
    }

    [Fact]
    public void FalhaAoGravar_NaoLancaErro()
    {
        // A "pasta" é um arquivo: não dá para criar colunas.json dentro dela.
        Directory.CreateDirectory(_pasta);
        var arquivoNoLugarDaPasta = Path.Combine(_pasta, "sub");
        File.WriteAllText(arquivoNoLugarDaPasta, "");

        var salvar = () => new ArquivoPreferenciasColunas(Caminho).Salvar(new LayoutColunas([]));

        salvar.Should().NotThrow();
    }

    [Fact]
    public void CaminhoPadrao_FicaNaPastaDeDadosDoUsuario()
    {
        var caminho = ArquivoPreferenciasColunas.CaminhoPadrao();

        caminho.Should().EndWith(Path.Combine("ProjectPlanner", "colunas.json"));
        if (OperatingSystem.IsMacOS())
            caminho.Should().Contain(Path.Combine("Library", "Application Support"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_pasta))
            Directory.Delete(_pasta, recursive: true);
    }
}
