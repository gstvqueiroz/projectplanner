using System.ComponentModel;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;

namespace ProjectPlanner.App.Tests;

// Bloco 17: abrir a galeria de qualquer tarefa (com ou sem fotos) e adicionar a primeira foto por ela.
public class GerenciarFotosTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly TarefaViewModel _a, _b;

    public GerenciarFotosTests()
    {
        _vm = _ambiente.CriarViewModel();
        _a = Ambiente.AdicionarTarefa(_vm, "Fundação", 3);
        _b = Ambiente.AdicionarTarefa(_vm, "Estrutura", 2);
        _b.Tarefa.AdicionarFoto(new FotoTarefa(_b.Tarefa.Id, Ambiente.Segunda, "exemplo://1", "Formas"));
        _vm.RecalcularCronogramaCommand.Execute(null); // como ao abrir um arquivo que já tinha a foto
        _ambiente.Dialogos.ArquivoParaSalvar = "/projetos/obra.pplan";
        _vm.SalvarProjetoCommand.Execute(null);
    }

    private GaleriaFotosViewModel Galeria => _ambiente.Dialogos.Galerias.Last();

    [Fact]
    public void GerenciarFotos_TarefaSemFotos_AbreAGaleriaVaziaSemErro()
    {
        _vm.TarefaSelecionada = _a;

        var abrir = () => _vm.GerenciarFotosCommand.Execute(null);

        abrir.Should().NotThrow();
        (Galeria.Titulo, Galeria.Vazia, Galeria.Resumo, Galeria.Fotos.Count)
            .Should().Be(("Fotos — Fundação", true, GaleriaFotosViewModel.SemFotos, 0));
        _vm.PossuiAlteracoesNaoSalvas.Should().BeFalse("só abrir não muda o projeto");
    }

    [Fact]
    public void GerenciarFotos_TarefaComFotos_MostraAsFotos()
    {
        _vm.GerenciarFotosCommand.Execute(_b); // pela linha (menus de contexto), mesmo com outra selecionada

        (Galeria.Titulo, Galeria.Vazia, Galeria.Resumo).Should().Be(("Fotos — Estrutura", false, "1 foto"));
        Galeria.Fotos.Single().Descricao.Should().Be("Formas");
    }

    [Fact]
    public void GerenciarFotos_SemTarefaSelecionada_Desabilitado_ComADica()
    {
        _vm.TarefaSelecionada = null;

        _vm.GerenciarFotosCommand.CanExecute(null).Should().BeFalse();
        _vm.DicaGerenciarFotos.Should().Be("Selecione uma tarefa para gerenciar suas fotos");
        _vm.GerenciarFotosCommand.CanExecute(_a).Should().BeTrue("com a linha informada (menu de contexto), dá");

        _vm.TarefaSelecionada = _a;
        _vm.GerenciarFotosCommand.CanExecute(null).Should().BeTrue();
        _vm.DicaGerenciarFotos.Should().NotContain("Selecione");
    }

    [Fact]
    public void AdicionarFoto_NaGaleriaVazia_CriaUmRegistroDeExemploNaTarefaCerta()
    {
        _vm.GerenciarFotosCommand.Execute(_a);

        Galeria.AdicionarFotoCommand.Execute(null);

        var foto = _a.Tarefa.Fotos.Should().ContainSingle().Subject;
        (foto.TarefaId, foto.DataRegistro, foto.Descricao).Should().Be((_a.Tarefa.Id, new DateTime(2026, 9, 28), "Foto de exemplo"));
        foto.CaminhoArquivo.Should().StartWith("exemplo://");
        _b.Tarefa.Fotos.Should().ContainSingle("a outra tarefa não muda");
        (Galeria.Vazia, Galeria.Resumo, Galeria.Fotos.Count).Should().Be((false, "1 foto", 1));
        _vm.PossuiAlteracoesNaoSalvas.Should().BeTrue();
    }

    [Fact]
    public void AdicionarFoto_VariasVezes_CadaCliqueUmaFotoNova()
    {
        _vm.GerenciarFotosCommand.Execute(_b);

        Galeria.AdicionarFotoCommand.Execute(null);
        Galeria.AdicionarFotoCommand.Execute(null);

        _b.Tarefa.Fotos.Should().HaveCount(3).And.OnlyHaveUniqueItems(f => f.Id);
        Galeria.Resumo.Should().Be("3 fotos, da mais antiga para a mais recente");
    }

    [Fact]
    public void Indicador_ApareceNaGridENoGanttDepoisDaPrimeiraFoto()
    {
        var avisos = new List<string?>();
        ((INotifyPropertyChanged)_a).PropertyChanged += (_, e) => avisos.Add(e.PropertyName);
        (_a.TemFotos, _a.QuantidadeDeFotos, _b.TemFotos).Should().Be((false, 0, true));
        _vm.Gantt.TarefasComIconeDeFotos.Should().Equal(_b.Tarefa.Id);

        _vm.GerenciarFotosCommand.Execute(_a);
        Galeria.AdicionarFotoCommand.Execute(null);

        (_a.TemFotos, _a.QuantidadeDeFotos, _a.DicaDasFotos).Should().Be((true, 1, "1 foto — abrir a galeria"));
        avisos.Should().Contain([nameof(TarefaViewModel.TemFotos), nameof(TarefaViewModel.QuantidadeDeFotos)]);
        _vm.Gantt.TarefasComIconeDeFotos.Should().Equal(_a.Tarefa.Id, _b.Tarefa.Id);
    }

    [Fact]
    public void Indicador_CliqueAbreAGaleriaDaquelaLinha()
    {
        _vm.TarefaSelecionada = _a;

        _b.GerenciarFotosCommand.Execute(null);

        Galeria.Titulo.Should().Be("Fotos — Estrutura");
    }

    [Fact]
    public void Galeria_AvisoDeProvisorio()
    {
        GaleriaFotosViewModel.AvisoProvisorio.Should().Contain("ainda será implementado").And.Contain("registro de exemplo");
    }

    [Fact]
    public void Galeria_ArgumentosNulos_Recusados()
    {
        var semTarefa = () => new GaleriaFotosViewModel(null!, _ => { }, () => DateTime.Today);
        var semAcao = () => new GaleriaFotosViewModel(_a.Tarefa, null!, () => DateTime.Today);

        semTarefa.Should().Throw<ArgumentNullException>();
        semAcao.Should().Throw<ArgumentNullException>();
    }
}
