using System.Security.Cryptography;
using System.Text;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Fotos;
using ProjectPlanner.Core.Relatorios;

namespace ProjectPlanner.Data.Tests;

// Bloco 27: a curadoria das fotos, os dados da obra e o histórico dos relatórios no .pplan.
public sealed class RelatorioNoArquivoTests : TesteComArquivo
{
    private static FotoTarefa Foto(Tarefa tarefa, string nome, bool noRelatorio, bool destaque) =>
        new FotoTarefa(Guid.NewGuid(), tarefa.Id,
                new DadosDaImagem(nome, 1600, 1200, 1000, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(nome)))),
                new MetadadosFoto(Segunda.AddHours(9)), Segunda)
            .ComCuradoria(noRelatorio, destaque);

    private static ImagensParaSalvar Imagens(params FotoTarefa[] fotos) =>
        new(null, fotos.ToDictionary(f => f.Id, f => new ConteudoDeFoto([1, 2, 3], [4, 5], 1600, 1200)));

    [Fact]
    public void Curadoria_DasFotos_VoltaComoFoiSalva()
    {
        var obra = new Cronograma("Obra", Segunda);
        var tarefa = new Tarefa("Fundação", Segunda, 3);
        obra.AdicionarTarefa(tarefa);
        var fotos = new[] { Foto(tarefa, "a.jpg", true, false), Foto(tarefa, "b.jpg", false, false), Foto(tarefa, "c.jpg", true, true) };
        foreach (var foto in fotos)
            tarefa.AdicionarFoto(foto);

        Repositorio.Salvar(obra, Caminho, Imagens(fotos));
        var aberta = Repositorio.Abrir(Caminho).Tarefas[0];

        fotos.Select(f => (aberta.Foto(f.Id)!.NoRelatorio, aberta.Foto(f.Id)!.Destaque))
            .Should().Equal((true, false), (false, false), (true, true));
    }

    [Fact]
    public void DadosDaObraEHistorico_VoltamIguais_EVazioNaoGrava()
    {
        var obra = new Cronograma("Obra", Segunda);
        Repositorio.Salvar(obra, Caminho);
        Contar("RelatorioDoProjeto").Should().Be(0);

        obra.DefinirObra(new DadosDaObra("Sr. Silva", "C-2026-01", "Rua das Flores, 100"));
        obra.RegistrarRelatorio(new RelatorioGerado(1, new DateTime(2026, 10, 4, 18, 0, 0), new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 4), 12));
        obra.RegistrarRelatorio(new RelatorioGerado(2, new DateTime(2026, 10, 11, 18, 0, 0), new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 11), 9));
        Repositorio.Salvar(obra, Caminho);
        var aberta = Repositorio.Abrir(Caminho);

        aberta.Obra.Should().Be(obra.Obra);
        aberta.Relatorios.Should().BeEquivalentTo(obra.Relatorios, o => o.WithStrictOrdering());
        aberta.ProximoNumeroDeRelatorio.Should().Be(3);
    }

    [Theory]
    [InlineData(1, "{ ruim")]
    [InlineData(1, """{"relatorios":[{"numero":0,"geradoEm":"2026-10-04T18:00:00","de":"2026-09-28","ate":"2026-10-04","fotos":1}]}""")]
    [InlineData(5, """{"contratante":"x"}""")]
    public void DadosDoRelatorioComDefeito_ArquivoRecusado(int versao, string dados)
    {
        var obra = new Cronograma("Obra", Segunda);
        obra.DefinirObra(new DadosDaObra("Sr. Silva", null, null));
        Repositorio.Salvar(obra, Caminho);
        Executar("UPDATE RelatorioDoProjeto SET Versao = $p0, Dados = $p1", versao, dados);

        var abrir = () => Repositorio.Abrir(Caminho);

        abrir.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ArquivoDa0240_AsFotosEntramNoRelatorio_SemDestaque()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Arquivos", "obra-0.24.0.pplan"), Caminho);

        var aberto = Repositorio.Abrir(Caminho);

        aberto.Tarefas.SelectMany(t => t.Fotos).Should().HaveCount(3).And.OnlyContain(f => f.NoRelatorio && !f.Destaque);
        (aberto.Obra.Vazio, aberto.Relatorios.Count).Should().Be((true, 0));
    }
}
