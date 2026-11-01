using Microsoft.Data.Sqlite;
using ProjectPlanner.Core;

namespace ProjectPlanner.Data.Tests;

// Bloco 71: o calendário de trabalho no .pplan: volta igual, com o início num sábado de trabalho e a restrição de início
// intactos (ele é lido antes das tarefas); o padrão não grava linha, e um arquivo de antes abre com o padrão.
public sealed class CalendarioNoArquivoTests : TesteComArquivo
{
    private static readonly DateTime Sabado = new(2026, 10, 3);

    private static CalendarioDoProjeto SegundaASabado() => CalendarioDoProjeto.Criar(
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday], true, false,
        [new FeriadoDoCalendario(new DateOnly(2026, 1, 20), "São Sebastião", TodoAno: true), new FeriadoDoCalendario(new DateOnly(2026, 10, 15), "Municipal")],
        [new ExcecaoDoCalendario(new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 21), "Chuva", false),
         new ExcecaoDoCalendario(new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 25), "Mutirão", true)]);

    [Fact]
    public void Calendario_VoltaIgual_EOSabadoDeTrabalhoFica()
    {
        var c = new Cronograma("Obra", Sabado);
        c.DefinirCalendario(SegundaASabado());
        c.AlterarDataInicio(Sabado);
        var a = new Tarefa("Alvenaria", Sabado, 5);
        c.AdicionarTarefa(a);
        a.DefinirRestricaoDeInicio(new DateTime(2026, 10, 10)); // outro sábado
        Repositorio.Salvar(c, Caminho);

        var aberto = Repositorio.Abrir(Caminho);

        aberto.Calendario.Should().Be(c.Calendario);
        (aberto.DataInicio, aberto.Tarefas[0].NaoIniciarAntesDe, aberto.Tarefas[0].DataInicio).Should().Be((Sabado, new DateTime(2026, 10, 10), new DateTime(2026, 10, 10)));
        aberto.Tarefas[0].Calendario.Should().BeSameAs(aberto.Calendario);
        Versao("CalendarioDoProjeto").Should().Be(1);
    }

    [Fact]
    public void Padrao_NaoGravaLinha_EUmArquivoSemOCalendarioAbreComOPadrao()
    {
        var c = new Cronograma("Obra", Segunda);
        c.AdicionarTarefa(new Tarefa("A", Segunda, 3));
        Repositorio.Salvar(c, Caminho);

        Contar("CalendarioDoProjeto").Should().Be(0);
        Repositorio.Abrir(Caminho).Calendario.Should().BeSameAs(CalendarioDoProjeto.Padrao);

        c.DefinirCalendario(CalendarioDoProjeto.Brasil);
        Repositorio.Salvar(c, Caminho);
        Contar("CalendarioDoProjeto").Should().Be(1);
        Repositorio.Abrir(Caminho).Calendario.Should().Be(CalendarioDoProjeto.Brasil);
    }

    [Fact]
    public void FormatoDesconhecido_ERecusado()
    {
        var c = new Cronograma("Obra", Segunda);
        c.DefinirCalendario(CalendarioDoProjeto.Brasil);
        Repositorio.Salvar(c, Caminho);
        using (var conexao = new SqliteConnection($"Data Source={Caminho};Pooling=False"))
        {
            conexao.Open();
            using var comando = conexao.CreateCommand();
            comando.CommandText = "UPDATE CalendarioDoProjeto SET Versao = 9";
            comando.ExecuteNonQuery();
        }

        var abrir = () => Repositorio.Abrir(Caminho);

        abrir.Should().Throw<InvalidDataException>().WithMessage("*calendário de trabalho num formato desconhecido*");
        FormatoDoCalendario.Ler(FormatoDoCalendario.Serializar(SegundaASabado())).Should().Be(SegundaASabado());
        var defeito = () => FormatoDoCalendario.Ler("""{"dias":[],"feriadosNacionais":true}""");
        defeito.Should().Throw<InvalidDataException>().WithMessage("*Marque pelo menos um dia*");
    }
}
