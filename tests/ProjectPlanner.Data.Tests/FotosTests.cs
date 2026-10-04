using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Fotos;

namespace ProjectPlanner.Data.Tests;

// Bloco 22: fotos de verdade no .pplan (metadados + imagem e miniatura em tabelas próprias).
public sealed class FotosTests : TesteComArquivo
{
    private static string Hash(string semente) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(semente)));

    private static FotoTarefa Foto(Tarefa tarefa, DateTime data, string nome, string? legenda = null, IEnumerable<string>? tags = null) =>
        new(Guid.NewGuid(), tarefa.Id, new DadosDaImagem(nome, 1600, 1200, 350_000, Hash(nome)),
            new MetadadosFoto(data, "Título " + nome, legenda, "Gustavo", "Bloco A", 40, tags), new DateTime(2026, 10, 1, 9, 0, 0));

    // Bytes de mentira bastam: o repositório guarda e devolve, quem entende de imagem é o processador.
    private static ConteudoDeFoto Conteudo(string nome) =>
        new(Encoding.UTF8.GetBytes("imagem de " + nome), Encoding.UTF8.GetBytes("miniatura de " + nome), 1600, 1200);

    private sealed record Obra(Cronograma Projeto, Tarefa Fundacao, Tarefa Estrutura, FotoTarefa[] Fotos);

    private static Obra MontarObra()
    {
        var projeto = new Cronograma("Obra", Segunda);
        var fundacao = new Tarefa("Fundação", Segunda, 3);
        var estrutura = new Tarefa("Estrutura", Segunda, 2);
        projeto.AdicionarTarefa(fundacao);
        projeto.AdicionarTarefa(estrutura);
        var fotos = new[]
        {
            Foto(fundacao, Segunda.AddDays(2), "b.jpg", "Armadura", ["durante", "problema"]),
            Foto(fundacao, Segunda.AddHours(8), "a.jpg", "Escavação", ["antes"]),
            Foto(estrutura, Segunda.AddDays(4), "c.jpg"),
        };
        fundacao.AdicionarFoto(fotos[0]);
        fundacao.AdicionarFoto(fotos[1]);
        estrutura.AdicionarFoto(fotos[2]);
        return new Obra(projeto, fundacao, estrutura, fotos);
    }

    private static ImagensParaSalvar Novas(params FotoTarefa[] fotos) =>
        new(null, fotos.ToDictionary(f => f.Id, f => Conteudo(f.Imagem.NomeArquivoOriginal)));

    [Fact]
    public void SalvarEAbrir_MetadadosCompletos_ImagemEMiniatura()
    {
        var obra = MontarObra();

        Repositorio.Salvar(obra.Projeto, Caminho, Novas(obra.Fotos));
        var aberto = Repositorio.Abrir(Caminho);

        var fotos = aberto.Tarefas[0].Fotos;
        fotos.Select(f => f.Imagem.NomeArquivoOriginal).Should().Equal("a.jpg", "b.jpg");
        fotos[1].Should().BeEquivalentTo(obra.Fotos[0], o => o.ComparingByMembers<FotoTarefa>());
        fotos[1].Tags.Should().Equal("durante", "problema");
        aberto.Tarefas[0].FotosNaOrdemDeCadastro.Select(f => f.Imagem.NomeArquivoOriginal).Should().Equal("b.jpg", "a.jpg");
        aberto.FotosDescartadasAoAbrir.Should().Be(0);

        Repositorio.LerMiniaturas(Caminho, obra.Fotos.Select(f => f.Id).ToList()).Values
            .Select(Encoding.UTF8.GetString).Should().BeEquivalentTo(["miniatura de a.jpg", "miniatura de b.jpg", "miniatura de c.jpg"]);
        Encoding.UTF8.GetString(Repositorio.LerImagem(Caminho, obra.Fotos[2].Id)!).Should().Be("imagem de c.jpg");
    }

    [Fact]
    public void SalvarDeNovo_ImagensQueJaEstavamNoArquivo_SaoCopiadasDele()
    {
        var obra = MontarObra();
        Repositorio.Salvar(obra.Projeto, Caminho, Novas(obra.Fotos));
        obra.Fundacao.Renomear("Fundação profunda");

        Repositorio.Salvar(obra.Projeto, Caminho, new ImagensParaSalvar(Caminho, new Dictionary<Guid, ConteudoDeFoto>()));

        Repositorio.Abrir(Caminho).Tarefas[0].Nome.Should().Be("Fundação profunda");
        (Contar("FotosImagens"), Contar("FotosMiniaturas")).Should().Be((3, 3));
        Encoding.UTF8.GetString(Repositorio.LerImagem(Caminho, obra.Fotos[0].Id)!).Should().Be("imagem de b.jpg");
    }

    [Fact]
    public void SalvarComo_LevaAsFotosParaOArquivoNovo_EOAntigoContinuaIntacto()
    {
        var obra = MontarObra();
        Repositorio.Salvar(obra.Projeto, Caminho, Novas(obra.Fotos));
        var copia = Path.Combine(Pasta, "copia.pplan");
        var nova = Foto(obra.Estrutura, Segunda.AddDays(5), "d.jpg");
        obra.Estrutura.AdicionarFoto(nova);

        Repositorio.Salvar(obra.Projeto, copia, new ImagensParaSalvar(Caminho, Novas(nova).Novas));

        (Contar("FotosImagens", copia), Contar("FotosImagens")).Should().Be((4, 3));
        Encoding.UTF8.GetString(Repositorio.LerImagem(copia, obra.Fotos[1].Id)!).Should().Be("imagem de a.jpg");
        Encoding.UTF8.GetString(Repositorio.LerImagem(copia, nova.Id)!).Should().Be("imagem de d.jpg");
    }

    [Fact]
    public void FotoAlteradaNaMemoria_GanhaDaCopiaDoArquivo()
    {
        var obra = MontarObra();
        Repositorio.Salvar(obra.Projeto, Caminho, Novas(obra.Fotos));

        // Girar gera uma imagem nova: ela vai como "nova" e vale mais que a do arquivo de origem.
        var girada = new Dictionary<Guid, ConteudoDeFoto> { [obra.Fotos[0].Id] = Conteudo("b girada") };
        Repositorio.Salvar(obra.Projeto, Caminho, new ImagensParaSalvar(Caminho, girada));

        Encoding.UTF8.GetString(Repositorio.LerImagem(Caminho, obra.Fotos[0].Id)!).Should().Be("imagem de b girada");
        Contar("FotosImagens").Should().Be(3);
    }

    [Fact]
    public void FotoRemovida_SomeDoArquivo_ComAImagem()
    {
        var obra = MontarObra();
        Repositorio.Salvar(obra.Projeto, Caminho, Novas(obra.Fotos));
        obra.Fundacao.RemoverFoto(obra.Fotos[0].Id);

        Repositorio.Salvar(obra.Projeto, Caminho, new ImagensParaSalvar(Caminho, new Dictionary<Guid, ConteudoDeFoto>()));

        (Contar("FotosTarefa"), Contar("FotosImagens"), Contar("FotosMiniaturas")).Should().Be((2, 2, 2));
        Repositorio.LerImagem(Caminho, obra.Fotos[0].Id).Should().BeNull();
    }

    [Fact]
    public void OrigemSumiu_SalvarFalha_SemTocarNoDestino()
    {
        var obra = MontarObra();
        Repositorio.Salvar(obra.Projeto, Caminho, Novas(obra.Fotos));
        var antes = File.ReadAllBytes(Caminho);

        var salvar = () => Repositorio.Salvar(obra.Projeto, Caminho,
            new ImagensParaSalvar(Path.Combine(Pasta, "sumiu.pplan"), new Dictionary<Guid, ConteudoDeFoto>()));

        salvar.Should().Throw<IOException>().WithMessage("3 foto(s) ficariam sem imagem*Nada foi salvo.");
        File.ReadAllBytes(Caminho).Should().Equal(antes);
        Directory.GetFiles(Pasta, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void ImagemNovaDeFotoQueNaoEstaNoProjeto_Ignorada()
    {
        var obra = MontarObra();
        var sobra = new Dictionary<Guid, ConteudoDeFoto>(Novas(obra.Fotos).Novas) { [Guid.NewGuid()] = Conteudo("sobra") };

        Repositorio.Salvar(obra.Projeto, Caminho, new ImagensParaSalvar(null, sobra));

        Contar("FotosImagens").Should().Be(3);
    }

    [Fact]
    public void Banco_ApagarATarefa_ApagaFotosImagensEMiniaturasEmCascata()
    {
        var obra = MontarObra();
        Repositorio.Salvar(obra.Projeto, Caminho, Novas(obra.Fotos));

        Executar("DELETE FROM Tarefas WHERE Id = $p0", obra.Fundacao.Id.ToString().ToUpperInvariant());

        (Contar("FotosTarefa"), Contar("FotosImagens"), Contar("FotosMiniaturas")).Should().Be((1, 1, 1));
    }

    [Fact]
    public void Banco_RecusaPercentualForaDe0a100()
    {
        var obra = MontarObra();
        Repositorio.Salvar(obra.Projeto, Caminho, Novas(obra.Fotos));

        var invalido = () => Executar("UPDATE FotosTarefa SET PercentualConcluidoNaData = 150");

        invalido.Should().Throw<SqliteException>().WithMessage("*CK_FotosTarefa_Percentual*");
    }

    [Fact]
    public void Abrir_NaoLeOsBlobs_FotoRegistroSoTemMetadados()
    {
        using var contexto = ProjectPlannerDbContext.Criar(Caminho, criarSeNaoExistir: true);

        contexto.Model.FindEntityType(typeof(FotoRegistro))!.GetProperties()
            .Should().NotContain(p => p.ClrType == typeof(byte[]), "imagens e miniaturas ficam em tabelas próprias, lidas sob demanda");
    }

    [Fact]
    public void LerMiniaturas_SoAsPedidasEQueExistem()
    {
        var obra = MontarObra();
        Repositorio.Salvar(obra.Projeto, Caminho, Novas(obra.Fotos));

        Repositorio.LerMiniaturas(Caminho, [obra.Fotos[2].Id, Guid.NewGuid()]).Keys.Should().Equal(obra.Fotos[2].Id);
        Repositorio.LerMiniaturas(Caminho, []).Should().BeEmpty();
        Repositorio.LerImagem(Caminho, Guid.NewGuid()).Should().BeNull();
    }

    [Fact]
    public void LerDeArquivoQueNaoExiste_Recusado()
    {
        var ler = () => Repositorio.LerImagem(Path.Combine(Pasta, "nao.pplan"), Guid.NewGuid());

        ler.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void ArquivoDaVersao0210_AbreComTarefasIntactas_EDescartaAsFotosDeExemplo()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Arquivos", "obra-0.21.0.pplan"), Caminho);

        var aberto = Repositorio.Abrir(Caminho);

        aberto.Tarefas.Select(t => $"{aberto.EdtDe(t.Id)} {t.Nome}").Should().Equal("1 Fundação", "1.1 Escavar", "1.2 Armar", "2 Estrutura");
        aberto.Predecessoras.Should().HaveCount(2);
        aberto.Tarefas[1].Notas.Should().Be("Solo argiloso");
        aberto.Tarefas.SelectMany(t => t.Fotos).Should().BeEmpty();
        aberto.FotosDescartadasAoAbrir.Should().Be(3);
        Tabelas().Should().Contain(["FotosImagens", "FotosMiniaturas"]);
        Contar("FotosTarefa").Should().Be(3, "abrir só migra o schema; os registros somem ao salvar");

        Repositorio.Salvar(aberto, Caminho, new ImagensParaSalvar(Caminho, new Dictionary<Guid, ConteudoDeFoto>()));

        Contar("FotosTarefa").Should().Be(0);
        Repositorio.Abrir(Caminho).FotosDescartadasAoAbrir.Should().Be(0);
    }
}
