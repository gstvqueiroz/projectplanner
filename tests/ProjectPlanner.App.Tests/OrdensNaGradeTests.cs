using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;
using ProjectPlanner.Core.OrdensDeServico;

namespace ProjectPlanner.App.Tests;

// Bloco 61: as OS à vista: a marca "OS" na linha da tarefa (laranja se venceu), que abre a OS; o aviso ao abrir a obra; as OS na
// situação da obra que vai ao assistente.
public sealed class OrdensNaGradeTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;
    private readonly TarefaViewModel _alvenaria, _reboco;

    public OrdensNaGradeTests()
    {
        _vm = _ambiente.CriarViewModel();
        _alvenaria = Ambiente.AdicionarTarefa(_vm, "Alvenaria", 5);
        _alvenaria.Valor = "3.000";
        _reboco = Ambiente.AdicionarTarefa(_vm, "Reboco", 4, "1");
    }

    // Cria e emite uma OS da tarefa dentro da tela (como a pessoa faria), com o prazo dado.
    private void EmitirOS(TarefaViewModel tarefa, DateTime prazo, bool emitir = true)
    {
        _ambiente.Dialogos.AoMostrarOrdensDeServico = tela =>
        {
            tela.NovaOrdemCommand.Execute(null);
            tela.Executante = "Construtora Silva";
            (tela.InicioPrevisto, tela.Prazo) = (prazo.AddDays(-3), prazo);
            if (emitir)
                tela.EmitirCommand.Execute(null);
            else
                tela.SalvarCommand.Execute(null);
            return Task.CompletedTask;
        };
        _vm.TarefaSelecionada = tarefa;
        _vm.OrdensDeServicoCommand.Execute(null);
        _ambiente.Dialogos.AoMostrarOrdensDeServico = null;
    }

    [Fact]
    public void MarcaNaLinha_DepoisDeFecharAsOS_ELaranjaQuandoVence()
    {
        (_alvenaria.TemOrdens, _reboco.TemOrdens).Should().Be((false, false));

        EmitirOS(_alvenaria, new DateTime(2026, 10, 2));
        EmitirOS(_reboco, new DateTime(2026, 9, 25));

        (_alvenaria.TemOrdens, _alvenaria.OrdemVencida, _reboco.OrdemVencida).Should().Be((true, false, true));
        _alvenaria.DicaDasOrdens.Should().Be("OS-001 · Emitida (prazo 02/10) · Construtora Silva\n— abrir as ordens de serviço");
        _reboco.OrdensDaTarefa.Should().Be("OS-002 · Emitida (vencida em 25/09) · Construtora Silva");

        while (_vm.TextoDesfazer.Contains("OS-002")) // nova, as alterações da ficha e a emissão
            _vm.DesfazerAlteracaoCommand.Execute(null);
        _vm.Tarefas.Single(t => t.Nome == "Reboco").TemOrdens.Should().BeFalse("o Desfazer tirou a OS do reboco, e a linha acompanha");
        _vm.Tarefas.Single(t => t.Nome == "Alvenaria").TemOrdens.Should().BeTrue();
    }

    [Fact]
    public void ClicarNaMarca_AbreAsOS_NaDaTarefa()
    {
        EmitirOS(_alvenaria, new DateTime(2026, 10, 2));
        EmitirOS(_reboco, new DateTime(2026, 10, 9));

        _reboco.AbrirOrdensCommand.Execute(null);

        var tela = _ambiente.Dialogos.OrdensDeServico.Last();
        (tela.Ordem!.Codigo, _vm.TarefaSelecionada).Should().Be(("OS-002", _reboco));
    }

    [Fact]
    public async Task AoAbrirAObra_AvisaAsVencidasEAsQueEsperamConferencia()
    {
        var c = new Cronograma("Obra", Ambiente.Segunda);
        var agora = new DateTime(2026, 9, 1);
        DadosDaOrdem Dados(DateOnly prazo) => new("Serviço", TipoDeExecutante.Terceiro, "Equipe", null, null, null, [new ItemDaOrdem(null, "x", "vb", 1, 10)],
            null, prazo, FormaDePagamentoDaOrdem.Empreitada, null, null);
        c.OrdensDeServico.Adicionar(OrdemDeServico.Nova(Guid.NewGuid(), 1, Dados(new DateOnly(2026, 9, 20)), agora).Emitir(agora));
        c.OrdensDeServico.Adicionar(OrdemDeServico.Nova(Guid.NewGuid(), 2, Dados(new DateOnly(2026, 9, 21)), agora).Emitir(agora).Iniciar(agora).Concluir(agora));
        var ambiente = new Ambiente();
        var vm = ambiente.CriarViewModel();
        ambiente.Repositorio.AoAbrir = _ => c;
        ambiente.Dialogos.ArquivoParaAbrir = "/obras/obra.pplan";

        await vm.AbrirProjetoCommand.ExecuteAsync(null);

        vm.Mensagem.Should().Be("Projeto aberto: /obras/obra.pplan Ordens de serviço: 1 vencida, 1 esperando a conferência (Projeto > Ordens de serviço…).");
    }

    [Fact]
    public void Assistente_VeAsOS_NaSituacaoDaObra()
    {
        EmitirOS(_alvenaria, new DateTime(2026, 9, 25));
        EmitirOS(_reboco, new DateTime(2026, 10, 9), emitir: false);

        var linhas = _vm.RetratoParaOAssistente().Obra!.Where(l => l.Contains("OS")).ToList();

        linhas[0].Should().StartWith("Ordens de serviço: 2; em andamento 1, vencidas 1, esperando conferência 0; medido a pagar R$ 0,00; por medir R$ 3.000,00");
        linhas[1].Should().Be("OS-001 · Emitida (vencida em 25/09) · Construtora Silva: Alvenaria; terceiro (empreiteiro); empreitada (preço fechado); " +
                              "valor R$ 3.000,00, medido R$ 0,00, pago R$ 0,00; tarefas 1.");
        linhas[2].Should().StartWith("OS-002 · Rascunho · Construtora Silva: Reboco");
    }
}
