using ProjectPlanner.Core.Edicao;
using ProjectPlanner.Core.Planejamento;
using static ProjectPlanner.Core.Tests.Datas;

namespace ProjectPlanner.Core.Tests;

// Bloco 84: o andamento por quantidade. A tarefa guarda o executado acumulado e o % concluído sai do executado ÷ a quantidade
// total: os arredondamentos do % inteiro não se acumulam de um apontamento para o outro.
public sealed class ProducaoDaTarefaTests
{
    private static Tarefa Reboco(decimal total = 200, int percentual = 0)
    {
        var tarefa = new Tarefa("Reboco", Segunda, 10);
        tarefa.DefinirUnidade("m²");
        tarefa.DefinirQuantidade(total);
        tarefa.AtualizarPercentualConcluido(percentual);
        return tarefa;
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.1, 1)] // começou: ao menos 1%
    [InlineData(100, 50)]
    [InlineData(199.9, 99)] // 99,95% não vira 100%: só o total conclui
    [InlineData(200, 100)]
    [InlineData(250, 100)] // passou do total
    [InlineData(-5, 0)]
    public void PercentualPeloExecutado_AsRegras(double executado, int esperado) =>
        Tarefa.PercentualPeloExecutado((decimal)executado, 200).Should().Be(esperado);

    [Fact]
    public void Apontar_25PorDiaEm1200_ChegaA100SemPerderNosArredondamentos()
    {
        var alvenaria = Reboco(1200);
        var percentuais = new List<int>();

        for (var dia = 1; dia <= 48; dia++)
        {
            alvenaria.ApontarProducao(25);
            percentuais.Add(alvenaria.PercentualConcluido);
        }

        (percentuais[0], percentuais[23], percentuais[46], percentuais[47]).Should().Be((2, 50, 98, 100),
            "25 de 1.200 são 2,08% por dia: só com o executado guardado os 48 dias dão 100% (somando 2% por dia, seriam 96%)");
        alvenaria.QuantidadeExecutada.Should().Be(1200);
    }

    [Fact]
    public void SemExecutadoGuardado_PartedoPercentualDeAgora()
    {
        var reboco = Reboco(200, 30);
        (reboco.QuantidadeExecutada, reboco.ExecutadoAtual).Should().Be(((decimal?)null, (decimal?)60m));

        reboco.ApontarProducao(20);

        (reboco.QuantidadeExecutada, reboco.PercentualConcluido).Should().Be(((decimal?)80m, 40));
    }

    [Fact]
    public void MudarOPercentualPorOutroCaminho_TiraOExecutado_EAProximaProducaoParteDele()
    {
        var reboco = Reboco();
        reboco.ApontarProducao(81); // 40,5% → 41%

        reboco.AtualizarPercentualConcluido(41);
        reboco.QuantidadeExecutada.Should().Be(81, "o mesmo %: o executado continua valendo");
        reboco.AtualizarPercentualConcluido(50);
        (reboco.QuantidadeExecutada, reboco.ExecutadoAtual).Should().Be(((decimal?)null, (decimal?)100m));

        reboco.ApontarProducao(10);
        (reboco.QuantidadeExecutada, reboco.PercentualConcluido).Should().Be(((decimal?)110m, 55));
    }

    [Fact]
    public void OTotalExecutado_LevaOPercentual_EVazioTiraSoOExecutado()
    {
        var reboco = Reboco(200, 30);

        reboco.DefinirQuantidadeExecutada(120);
        (reboco.QuantidadeExecutada, reboco.PercentualConcluido).Should().Be(((decimal?)120m, 60));

        reboco.DefinirQuantidadeExecutada(null);
        (reboco.QuantidadeExecutada, reboco.PercentualConcluido).Should().Be(((decimal?)null, 60), "o % fica");
    }

    [Fact]
    public void Correcao_Negativa_EOQueNaoVale()
    {
        var reboco = Reboco();
        reboco.ApontarProducao(80);
        reboco.ApontarProducao(-10);
        (reboco.QuantidadeExecutada, reboco.PercentualConcluido).Should().Be(((decimal?)70m, 35), "uma correção de um apontamento a mais");

        reboco.Invoking(t => t.ApontarProducao(-100)).Should().Throw<DomainValidationException>().WithMessage("O executado não pode ficar negativo.");
        reboco.Invoking(t => t.ApontarProducao(0)).Should().Throw<DomainValidationException>().WithMessage("Informe a quantidade executada.");
        reboco.Invoking(t => t.DefinirQuantidadeExecutada(-1)).Should().Throw<DomainValidationException>();
        (reboco.QuantidadeExecutada, reboco.PercentualConcluido).Should().Be(((decimal?)70m, 35), "o que foi recusado não mudou nada");

        var semTotal = new Tarefa("Limpeza", Segunda, 2);
        semTotal.Invoking(t => t.ApontarProducao(5)).Should().Throw<DomainValidationException>().WithMessage(Tarefa.SemQuantidadeTotal);
        semTotal.ExecutadoAtual.Should().BeNull();
    }

    [Fact]
    public void MudarAQuantidadeTotal_RefazOPercentualPeloExecutado()
    {
        var reboco = Reboco();
        reboco.DefinirQuantidadeExecutada(150);
        reboco.PercentualConcluido.Should().Be(75);

        reboco.DefinirQuantidade(250);
        reboco.PercentualConcluido.Should().Be(60, "um aditivo de quantidade: 150 de 250");

        reboco.DefinirQuantidade(null);
        (reboco.QuantidadeExecutada, reboco.PercentualConcluido).Should().Be(((decimal?)null, 60), "sem o total, o executado não vale; o % fica");
    }

    [Fact]
    public void Resumo_NaoTemExecutado_EAoVirarResumoOExecutadoSai()
    {
        var montador = new MontadorCronograma().ComTarefa("Revestimentos", 10).ComTarefa("Reboco interno", 5);
        var revestimentos = montador["Revestimentos"];
        revestimentos.DefinirUnidade("m²");
        revestimentos.DefinirQuantidade(200);
        revestimentos.ApontarProducao(50);

        montador.Cronograma.Rebaixar(montador["Reboco interno"].Id); // o reboco interno entra nos revestimentos, que viram resumo

        (revestimentos.EhResumo, revestimentos.QuantidadeExecutada).Should().Be((true, (decimal?)null));
        revestimentos.Invoking(t => t.ApontarProducao(5)).Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void CopiarOCronograma_ECopiarEColarTarefas_LevamOExecutado()
    {
        var montador = new MontadorCronograma().ComTarefa("Reboco", 10);
        var reboco = montador["Reboco"];
        reboco.DefinirUnidade("m²");
        reboco.DefinirQuantidade(200);
        reboco.ApontarProducao(81);

        montador.Cronograma.Copiar().Tarefas.Single().QuantidadeExecutada.Should().Be(81);

        var trecho = CopiaDeTarefas.Copiar(montador.Cronograma, [reboco.Id]);
        var colada = CopiaDeTarefas.Colar(montador.Cronograma, trecho, null, 1).Single();
        (colada.QuantidadeExecutada, colada.PercentualConcluido).Should().Be(((decimal?)81m, 41));
    }

    [Theory]
    [InlineData("m²", "M2", true)]
    [InlineData("m2", "metros quadrados", true)]
    [InlineData("m³", "m3", true)]
    [InlineData("pç", "un", true)]
    [InlineData("ml", "m", true)]
    [InlineData("m²", "m³", false)]
    [InlineData("kg", "sc", false)]
    public void Unidades_IguaisDoJeitoQueOCanteiroEscreve(string a, string b, bool mesma) => Unidades.Mesma(a, b).Should().Be(mesma);

    [Fact]
    public void Unidades_VaziaNaoDaParaConferir() => (Unidades.Mesma("m²", null), Unidades.Mesma(" ", "m²"), Unidades.Normal("Bloco")).Should().Be(((bool?)null, (bool?)null, "bloco"));
}
