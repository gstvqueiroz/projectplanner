using System.Security.Cryptography;
using System.Text;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Fotos;
using ProjectPlanner.Core.Relatorios;

namespace ProjectPlanner.Data.Tests;

// Bloco 28: o GPS das fotos, os locais padronizados e os dias do alerta no .pplan.
public sealed class GestaoDeFotosNoArquivoTests : TesteComArquivo
{
    private static FotoTarefa Foto(Tarefa tarefa, string nome, Coordenadas? gps) =>
        new FotoTarefa(Guid.NewGuid(), tarefa.Id,
            new DadosDaImagem(nome, 1600, 1200, 1000, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(nome)))),
            new MetadadosFoto(Segunda.AddHours(9)), Segunda) { Gps = gps };

    private static ImagensParaSalvar Imagens(params FotoTarefa[] fotos) =>
        new(null, fotos.ToDictionary(f => f.Id, f => new ConteudoDeFoto([1, 2, 3], [4, 5], 1600, 1200)));

    private (Cronograma Obra, FotoTarefa ComGps, FotoTarefa SemGps) ObraComFotos()
    {
        var obra = new Cronograma("Obra", Segunda);
        var tarefa = new Tarefa("Fundação", Segunda, 3);
        obra.AdicionarTarefa(tarefa);
        var (comGps, semGps) = (Foto(tarefa, "a.jpg", new Coordenadas(-22.905810123, -47.062630456)), Foto(tarefa, "b.jpg", null));
        tarefa.AdicionarFoto(comGps);
        tarefa.AdicionarFoto(semGps);
        Repositorio.Salvar(obra, Caminho, Imagens(comGps, semGps));
        return (obra, comGps, semGps);
    }

    [Fact]
    public void Gps_VoltaComoFoiSalvo_ESemGpsFicaNulo()
    {
        var (_, comGps, semGps) = ObraComFotos();

        var aberta = Repositorio.Abrir(Caminho).Tarefas[0];

        aberta.Foto(comGps.Id)!.Gps.Should().Be(comGps.Gps);
        aberta.Foto(semGps.Id)!.Gps.Should().BeNull();
    }

    [Theory]
    [InlineData("Latitude = 95")]
    [InlineData("Longitude = NULL")]
    [InlineData("Latitude = NULL")]
    public void GpsComDefeito_AbreAFotoSemGps(string defeito)
    {
        var (_, comGps, _) = ObraComFotos();
        Executar($"UPDATE FotosTarefa SET {defeito} WHERE lower(Id) = lower($p0)", comGps.Id.ToString());

        var aberta = Repositorio.Abrir(Caminho).Tarefas[0];

        aberta.Foto(comGps.Id)!.Gps.Should().BeNull();
    }

    [Fact]
    public void LocaisEDiasDoAlerta_VoltamIguais_EOPadraoNaoGrava()
    {
        var obra = new Cronograma("Obra", Segunda);
        Repositorio.Salvar(obra, Caminho);
        Contar("RelatorioDoProjeto").Should().Be(0);

        var configuracao = new ConfiguracaoDasFotos(["Bloco A — Térreo", "Fachada"], 0);
        obra.DefinirConfiguracaoDasFotos(configuracao);
        Repositorio.Salvar(obra, Caminho);
        var aberta = Repositorio.Abrir(Caminho);

        Contar("RelatorioDoProjeto").Should().Be(1);
        aberta.ConfiguracaoDasFotos.Should().Be(configuracao);
        (aberta.Obra.Vazio, aberta.Relatorios.Count).Should().Be((true, 0));
    }

    [Fact]
    public void ArquivoDa027_SemOsCamposNovos_AbreComOPadrao()
    {
        var obra = new Cronograma("Obra", Segunda);
        obra.DefinirObra(new DadosDaObra("Sr. Silva", null, null));
        Repositorio.Salvar(obra, Caminho);
        Executar("UPDATE RelatorioDoProjeto SET Dados = $p0", """{"contratante":"Sr. Silva","contrato":null,"endereco":null,"relatorios":[]}""");

        var aberta = Repositorio.Abrir(Caminho);

        aberta.ConfiguracaoDasFotos.Should().Be(ConfiguracaoDasFotos.Padrao);
        aberta.Obra.Contratante.Should().Be("Sr. Silva");
    }

    [Theory]
    [InlineData("""{"diasSemFoto":400}""")]
    [InlineData("""{"diasSemFoto":-1}""")]
    [InlineData("""{"locais":"Bloco A"}""")]
    public void ConfiguracaoComDefeito_ArquivoRecusado(string dados)
    {
        var obra = new Cronograma("Obra", Segunda);
        obra.DefinirConfiguracaoDasFotos(new ConfiguracaoDasFotos(["A"], 7));
        Repositorio.Salvar(obra, Caminho);
        Executar("UPDATE RelatorioDoProjeto SET Dados = $p0", dados);

        var abrir = () => Repositorio.Abrir(Caminho);

        abrir.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ArquivoDa0240_AbreSemGps_ComAConfiguracaoPadrao()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Arquivos", "obra-0.24.0.pplan"), Caminho);

        var aberto = Repositorio.Abrir(Caminho);

        aberto.Tarefas.SelectMany(t => t.Fotos).Should().HaveCount(3).And.OnlyContain(f => f.Gps == null);
        aberto.ConfiguracaoDasFotos.Should().Be(ConfiguracaoDasFotos.Padrao);
    }
}
