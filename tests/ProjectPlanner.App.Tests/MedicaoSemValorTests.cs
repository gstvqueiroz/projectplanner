using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core.Medicoes;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Tests;

// Correção 0.41.2: num projeto sem Valor nas tarefas (o contrato sem itens), "Sugerir pelo % concluído" e o chat não mediam
// nada e a mensagem não dizia por quê. "Hoje" é seg 28/09/2026: 1 Limpeza (5 d, 150 m²) → 2 Locação (3 d).
public sealed class MedicaoSemValorTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;

    public MedicaoSemValorTests()
    {
        _vm = _ambiente.CriarViewModel();
        var limpeza = Ambiente.AdicionarTarefa(_vm, "Limpeza do terreno", 5);
        limpeza.Quantidade = "150";
        limpeza.Unidade = "m²";
        Ambiente.AdicionarTarefa(_vm, "Locação da obra", 3, "1");
    }

    private MedicoesViewModel AbrirComMedicao()
    {
        _vm.MedicoesCommand.Execute(null);
        var tela = _ambiente.Dialogos.Medicoes.Last();
        tela.NovaMedicaoCommand.Execute(null);
        return tela;
    }

    [Fact]
    public void SemValor_AvisoNaTela_ESugerirDizComoResolver()
    {
        _vm.Cronograma.Tarefas[0].AtualizarPercentualConcluido(100);
        var tela = AbrirComMedicao();

        tela.SemItensNoContrato.Should().BeTrue();
        tela.SugerirPeloPercentualConcluidoCommand.Execute(null);

        tela.Mensagem.Should().Be(MedicoesViewModel.TextoSemItens);
        MedicoesViewModel.TextoSemItens.Should().Contain("coluna Valor").And.Contain("Projeto > Orçamento…").And.Contain("Importar valores");
    }

    [Fact]
    public void ComValor_SemPercentual_DizParaAtualizarOPercentual_EComPercentualMede()
    {
        _vm.Tarefas[0].Valor = "3.000";
        var tela = AbrirComMedicao();
        tela.SemItensNoContrato.Should().BeFalse();

        tela.SugerirPeloPercentualConcluidoCommand.Execute(null);
        tela.Mensagem.Should().Be("Nada a sugerir: nenhuma tarefa com Valor tem % concluído. Atualize o % concluído (na grid ou pelo chat) e sugira de novo.");

        _vm.Cronograma.Tarefas[0].AtualizarPercentualConcluido(100);
        tela.SugerirPeloPercentualConcluidoCommand.Execute(null);
        tela.Mensagem.Should().StartWith("1 item medido pelo % concluído");
        _vm.Cronograma.Contrato.Boletim(1).Resumo.MedidoNoPeriodo.Should().Be(3000m);
    }

    [Fact]
    public void MedirTarefaSemValor_ExplicaComoResolver()
    {
        var c = _vm.Cronograma;
        c.Contrato.NovaMedicao(new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 28));

        ((Action)(() => c.Contrato.Medir(1, c.Tarefas[0].Id, 10))).Should().Throw<Core.DomainValidationException>()
            .WithMessage("\"Limpeza do terreno\" não tem Valor, então não é item do contrato: preencha a coluna Valor (ou aplique o orçamento em Projeto > Orçamento…) para medi-la.");
        c.Contrato.TemItens.Should().BeFalse();
    }

    [Fact]
    public async Task Chat_SabeQueNaoHaItens_EOCartaoExplica()
    {
        _ambiente.Assistente.ResponderComAlteracoes = _ => new RespostaIA("Proponho medir.")
        {
            Atualizacoes = [new AtualizacaoDaObra("medicao", "1", Quantidade: 150)],
        };
        _vm.Assistente.TextoDigitado = "mede a limpeza toda";
        await _vm.Assistente.EnviarCommand.ExecuteAsync(null);

        var obra = _ambiente.Assistente.Pedidos.Single().Contexto!.Obra!;
        obra.Should().Contain(l => l.StartsWith("Itens do contrato: nenhuma tarefa tem Valor"))
            .And.Contain("Tarefa 1 Limpeza do terreno: sem valor (não se mede); quantidade 150 m².");
        var item = _vm.Assistente.Mensagens.Last().Cartao!.Itens.Single();
        item.Problema.Should().StartWith("Não dá: \"Limpeza do terreno\" não tem Valor, então não é item do contrato");
        InstrucoesDoAssistente.Obra.Should().Contain("Só se medem tarefas com Valor");
    }
}
