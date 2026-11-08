using ProjectPlanner.Core;
using ProjectPlanner.Core.Diario;

namespace ProjectPlanner.Data.Tests;

// Bloco 81: o Diário de Obra no .pplan: volta igual (o clima, o efetivo, os equipamentos, os serviços, as ocorrências e o
// fechamento); sem dias, não grava linha; um formato desconhecido é recusado.
public sealed class DiarioNoArquivoTests : TesteComArquivo
{
    [Fact]
    public void Diario_VoltaIgual()
    {
        var c = new Cronograma("Obra", Segunda);
        var escavacao = new Tarefa("Escavação", Segunda, 3);
        c.AdicionarTarefa(escavacao);
        var aberto = c.Diario.Abrir(new DiaDaObra(DateOnly.FromDateTime(Segunda))
        {
            Manha = new ClimaDoPeriodo(TempoNoPeriodo.Nublado),
            Tarde = new ClimaDoPeriodo(TempoNoPeriodo.Chuva, Praticavel: false),
            Efetivo = [new EfetivoNoDia("Pedreiro", 3), new EfetivoNoDia("Servente", 2, "Empreiteira X")],
            Equipamentos = [new EquipamentoNoDia("Betoneira", 1)],
            Atividades = [new AtividadeNoDia(escavacao.Id, "1 Escavação", 40), new AtividadeNoDia(null, "Limpeza do canteiro")],
            Ocorrencias = "Chuva forte às 14h.\nVisita da fiscalização.",
        });
        c.Diario.Abrir(new DiaDaObra(DateOnly.FromDateTime(Segunda).AddDays(1)));
        c.Diario.Fechar(aberto.Data, new DateTime(2026, 9, 28, 18, 30, 0), "Eng. Ana");
        Repositorio.Salvar(c, Caminho);

        var lido = Repositorio.Abrir(Caminho).Diario;

        lido.Dias.Should().HaveCount(2);
        var dia = lido.Dias[0];
        (dia.Manha, dia.Tarde, dia.Ocorrencias).Should().Be((new ClimaDoPeriodo(TempoNoPeriodo.Nublado), new ClimaDoPeriodo(TempoNoPeriodo.Chuva, false),
            "Chuva forte às 14h.\nVisita da fiscalização."));
        dia.Efetivo.Should().Equal(c.Diario.Dias[0].Efetivo);
        dia.Equipamentos.Should().Equal(c.Diario.Dias[0].Equipamentos);
        dia.Atividades.Should().Equal(c.Diario.Dias[0].Atividades);
        dia.Fechamento.Should().Be(new FechamentoDoDia(new DateTime(2026, 9, 28, 18, 30, 0), "Eng. Ana"));
        Versao("DiarioDaObra").Should().Be(1);
    }

    [Fact]
    public void SemDias_NaoGravaLinha_EFormatoDesconhecidoERecusado()
    {
        var c = new Cronograma("Obra", Segunda);
        Repositorio.Salvar(c, Caminho);
        Contar("DiarioDaObra").Should().Be(0);
        Repositorio.Abrir(Caminho).Diario.Vazio.Should().BeTrue();

        c.Diario.Abrir(new DiaDaObra(DateOnly.FromDateTime(Segunda)));
        Repositorio.Salvar(c, Caminho);
        Executar("UPDATE DiarioDaObra SET Versao = 9");
        var abrir = () => Repositorio.Abrir(Caminho);
        abrir.Should().Throw<InvalidDataException>().WithMessage("*Diário de Obra num formato desconhecido*");
    }
}
