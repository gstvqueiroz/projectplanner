using ProjectPlanner.Core.Fotos;
using ProjectPlanner.Core.Relatorios;

namespace ProjectPlanner.Core.Tests;

// Bloco 42: a data da foto contra a tarefa, a pasta da obra na configuração e o primeiro relatório que pega as fotos antigas.
public class FotosComIaCoreTests
{
    private static readonly DateTime Seg = new(2026, 9, 28);

    private static (Cronograma C, Tarefa A, Tarefa B) Obra()
    {
        var c = new Cronograma("Casa", Seg);
        var a = new Tarefa("Fundação", Seg, 3);
        var b = new Tarefa("Alvenaria", Seg, 10);
        c.AdicionarTarefa(a);
        c.AdicionarTarefa(b);
        c.AdicionarPredecessora(new Predecessora(a.Id, b.Id));
        CalculadoraCronograma.Calcular(c);
        return (c, a, b);
    }

    [Fact]
    public void CoerenciaDaData_AntesDoInicio_DepoisDeConcluida_ComFolga()
    {
        var (_, a, b) = Obra(); // b: 01/10 a 14/10

        CoerenciaDaData.Aviso(b, new DateTime(2026, 9, 24)).Should().BeNull("7 dias antes ainda vale");
        CoerenciaDaData.Aviso(b, new DateTime(2026, 9, 23)).Should().Be("A foto é de 23/09/2026, antes do início previsto da tarefa (01/10/2026).");
        CoerenciaDaData.Aviso(a, new DateTime(2026, 12, 1)).Should().BeNull("depois do término, mas a tarefa não está concluída");
        a.AtualizarPercentualConcluido(100);
        CoerenciaDaData.Aviso(a, new DateTime(2026, 12, 1)).Should().Be("A foto é de 01/12/2026, depois do término da tarefa (30/09/2026), que já está concluída.");
    }

    [Fact]
    public void Configuracao_PastaDaObra_NaoEhPadrao()
    {
        var config = new ConfiguracaoDasFotos([], ConfiguracaoDasFotos.DiasPadrao);
        config.EhPadrao.Should().BeTrue();
        (config with { PastaDaObra = "/Fotos/Obra" }).EhPadrao.Should().BeFalse();
    }

    [Fact]
    public void PrimeiroRelatorio_ComecaNaFotoMaisAntiga_QuandoEhAntesDoInicio()
    {
        var (c, a, _) = Obra();
        var hoje = new DateOnly(2026, 10, 18);
        MontadorDoRelatorio.DesdeOUltimo(c, hoje).Should().Be((new DateOnly(2026, 9, 28), hoje));

        a.AdicionarFoto(Foto(a, new DateTime(2026, 9, 5)));
        MontadorDoRelatorio.DesdeOUltimo(c, hoje).Should().Be((new DateOnly(2026, 9, 5), hoje), "a foto é de antes do início do projeto");

        c.RegistrarRelatorio(new RelatorioGerado(1, Seg, new DateOnly(2026, 9, 5), new DateOnly(2026, 10, 4), 1));
        MontadorDoRelatorio.DesdeOUltimo(c, hoje).Should().Be((new DateOnly(2026, 10, 5), hoje), "depois do primeiro, é do dia seguinte ao último");
    }

    [Fact]
    public void PrimeiroRelatorio_FotoForaDoRelatorio_NaoMudaOInicio()
    {
        var (c, a, _) = Obra();
        a.AdicionarFoto(Foto(a, new DateTime(2026, 9, 5)).ComCuradoria(noRelatorio: false, destaque: false));

        MontadorDoRelatorio.DesdeOUltimo(c, new DateOnly(2026, 10, 18)).Item1.Should().Be(new DateOnly(2026, 9, 28));
    }

    private static FotoTarefa Foto(Tarefa tarefa, DateTime data) => new(Guid.NewGuid(), tarefa.Id,
        new DadosDaImagem($"IMG_{Guid.NewGuid():N}.jpg", 1600, 1200, 300_000, FotosDeTeste.Hash(Guid.NewGuid().ToString())), new MetadadosFoto(data),
        new DateTime(2026, 10, 1));
}
