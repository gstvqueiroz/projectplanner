using ProjectPlanner.Core.Fotos;
using static ProjectPlanner.Core.Tests.Datas;

namespace ProjectPlanner.Core.Tests;

// Bloco 22: os metadados da foto (D2) e as regras dos arquivos aceitos (D5).
public class FotoTarefaTests
{
    private static readonly DadosDaImagem Imagem = new("IMG_0001.JPG", 1600, 1200, 350_000, FotosDeTeste.Hash("a"));

    [Fact]
    public void Foto_ComTudo_GuardaOsValoresLimpos()
    {
        var metadados = new MetadadosFoto(new DateTime(2026, 9, 29, 10, 30, 0), "  Concretagem  ", "Laje do 2º pavimento",
            "Gustavo", "Bloco A", 40, ["depois", " problema ", "", "Depois"]);

        var foto = new FotoTarefa(Guid.NewGuid(), Guid.NewGuid(), Imagem, metadados, new DateTime(2026, 10, 1));

        (foto.Titulo, foto.Descricao, foto.Autor, foto.Local, foto.PercentualConcluidoNaData)
            .Should().Be(("Concretagem", "Laje do 2º pavimento", "Gustavo", "Bloco A", 40));
        foto.Tags.Should().Equal("depois", "problema");
        foto.Metadados.TextoDasTags.Should().Be("depois, problema");
        foto.DataFoto.Should().Be(new DateTime(2026, 9, 29, 10, 30, 0));
    }

    [Fact]
    public void Metadados_TextosEmBrancoViramNulos_ETagsDoTexto()
    {
        var m = new MetadadosFoto(Segunda, " ", "", null, "  ", tags: MetadadosFoto.LerTags("antes; durante ,, depois"));

        (m.Titulo, m.Descricao, m.Autor, m.Local).Should().Be(((string?)null, (string?)null, (string?)null, (string?)null));
        m.Tags.Should().Equal("antes", "durante", "depois");
    }

    [Fact]
    public void Metadados_IguaisPeloConteudo_InclusiveAsTags()
    {
        new MetadadosFoto(Segunda, "a", tags: ["x"]).Should().Be(new MetadadosFoto(Segunda, "a", tags: ["x"]));
        new MetadadosFoto(Segunda, "a", tags: ["x"]).Should().NotBe(new MetadadosFoto(Segunda, "a", tags: ["y"]));
    }

    [Theory]
    [InlineData(1, 1, 1)]          // default: sem data
    [InlineData(1899, 12, 31)]
    [InlineData(2101, 1, 1)]
    public void Metadados_DataForaDoIntervalo_Recusada(int ano, int mes, int dia)
    {
        var criar = () => new MetadadosFoto(new DateTime(ano, mes, dia));

        criar.Should().Throw<DomainValidationException>().Which.Propriedade.Should().Be(nameof(MetadadosFoto.DataFoto));
    }

    [Fact]
    public void Metadados_LimitesDeData_Aceitos()
    {
        new MetadadosFoto(MetadadosFoto.DataMinima).DataFoto.Should().Be(new DateTime(1900, 1, 1));
        new MetadadosFoto(MetadadosFoto.DataMaxima).DataFoto.Year.Should().Be(2100);
    }

    [Theory]
    [InlineData(nameof(MetadadosFoto.Titulo), 121)]
    [InlineData(nameof(MetadadosFoto.Descricao), 2001)]
    [InlineData(nameof(MetadadosFoto.Autor), 121)]
    [InlineData(nameof(MetadadosFoto.Local), 201)]
    public void Metadados_TextoLongoDemais_Recusado(string campo, int tamanho)
    {
        var texto = new string('x', tamanho);
        var criar = () => campo switch
        {
            nameof(MetadadosFoto.Titulo) => new MetadadosFoto(Segunda, titulo: texto),
            nameof(MetadadosFoto.Descricao) => new MetadadosFoto(Segunda, descricao: texto),
            nameof(MetadadosFoto.Autor) => new MetadadosFoto(Segunda, autor: texto),
            _ => new MetadadosFoto(Segunda, local: texto),
        };

        criar.Should().Throw<DomainValidationException>().Which.Propriedade.Should().Be(campo);
    }

    [Fact]
    public void Metadados_NoLimiteDoTexto_Aceitos()
    {
        new MetadadosFoto(Segunda, titulo: new string('x', 120), descricao: new string('y', 2000)).Titulo!.Length.Should().Be(120);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Metadados_PercentualForaDe0a100_Recusado(int percentual)
    {
        var criar = () => new MetadadosFoto(Segunda, percentualConcluidoNaData: percentual);

        criar.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Metadados_TagsDemaisOuLongas_Recusadas()
    {
        var demais = () => new MetadadosFoto(Segunda, tags: Enumerable.Range(1, 21).Select(i => $"t{i}"));
        var longa = () => new MetadadosFoto(Segunda, tags: [new string('x', 41)]);

        demais.Should().Throw<DomainValidationException>().WithMessage("São 21 tags; o máximo é 20.");
        longa.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void DadosDaImagem_GuardaSoONomeDoArquivo_EHashEmMinusculas()
    {
        var dados = new DadosDaImagem("/Users/x/Fotos/IMG_1.JPG", 1600, 900, 10, FotosDeTeste.Hash("b").ToUpperInvariant());

        (dados.NomeArquivoOriginal, dados.HashOriginal).Should().Be(("IMG_1.JPG", FotosDeTeste.Hash("b")));
    }

    [Theory]
    [InlineData("", 10, 10, 10, true)]
    [InlineData("a.jpg", 0, 10, 10, true)]
    [InlineData("a.jpg", 10, 10, 0, true)]
    [InlineData("a.jpg", 10, 10, 10, false)]
    public void DadosDaImagem_Invalidos_Recusados(string nome, int largura, int altura, long tamanho, bool hashValido)
    {
        var criar = () => new DadosDaImagem(nome, largura, altura, tamanho, hashValido ? FotosDeTeste.Hash("c") : "xyz");

        criar.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Foto_SemIdTarefaOuCadastro_Recusada()
    {
        var m = new MetadadosFoto(Segunda);

        FluentActions.Invoking(() => new FotoTarefa(Guid.Empty, Guid.NewGuid(), Imagem, m, Segunda)).Should().Throw<DomainValidationException>();
        FluentActions.Invoking(() => new FotoTarefa(Guid.NewGuid(), Guid.Empty, Imagem, m, Segunda)).Should().Throw<DomainValidationException>();
        FluentActions.Invoking(() => new FotoTarefa(Guid.NewGuid(), Guid.NewGuid(), Imagem, m, default)).Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Foto_ComImagemGirada_MantemMetadadosEHash()
    {
        var foto = new FotoTarefa(Guid.NewGuid(), Guid.NewGuid(), Imagem, new MetadadosFoto(Segunda, "t"), Segunda);

        var girada = foto.ComImagem(new DadosDaImagem(Imagem.NomeArquivoOriginal, 1200, 1600, 340_000, Imagem.HashOriginal));

        (girada.Id, girada.Titulo, girada.Imagem.Largura, girada.Imagem.HashOriginal).Should().Be((foto.Id, "t", 1200, Imagem.HashOriginal));
    }
}

public class RegrasDeFotoTests
{
    [Theory]
    [InlineData("a.jpg")]
    [InlineData("A.JPEG")]
    [InlineData("planta.png")]
    [InlineData("x.webp")]
    public void Arquivo_Aceito(string nome)
    {
        FluentActions.Invoking(() => RegrasDeFoto.ValidarArquivo(nome, 4_000_000)).Should().NotThrow();
    }

    [Theory]
    [InlineData("IMG_1.HEIC", "IMG_1.HEIC: fotos HEIC ainda não são aceitas*Mais compatível*")]
    [InlineData("x.heif", "*HEIC*")]
    [InlineData("doc.pdf", "doc.pdf: formato não aceito. Use fotos JPG, PNG ou WebP.")]
    [InlineData("semextensao", "*formato não aceito*")]
    public void Arquivo_FormatoRecusado_ComOrientacao(string nome, string mensagem)
    {
        FluentActions.Invoking(() => RegrasDeFoto.ValidarArquivo(nome, 1000))
            .Should().Throw<ImagemRecusadaException>().WithMessage(mensagem);
    }

    [Fact]
    public void Arquivo_VazioOuGrandeDemais_Recusado()
    {
        FluentActions.Invoking(() => RegrasDeFoto.ValidarArquivo("a.jpg", 0)).Should().Throw<ImagemRecusadaException>().WithMessage("*vazio*");
        FluentActions.Invoking(() => RegrasDeFoto.ValidarArquivo("a.jpg", RegrasDeFoto.TamanhoMaximoDoArquivo)).Should().NotThrow();
        FluentActions.Invoking(() => RegrasDeFoto.ValidarArquivo("a.jpg", 62_400_000))
            .Should().Throw<ImagemRecusadaException>().WithMessage("a.jpg: o arquivo tem 59,5 MB; o máximo é 50 MB.");
    }

    [Fact]
    public void Dimensoes_AcimaDe100Megapixels_Recusadas()
    {
        FluentActions.Invoking(() => RegrasDeFoto.ValidarDimensoes("a.jpg", 10_000, 10_000)).Should().NotThrow();
        FluentActions.Invoking(() => RegrasDeFoto.ValidarDimensoes("a.jpg", 12_000, 10_000))
            .Should().Throw<ImagemRecusadaException>().WithMessage("a.jpg: a imagem tem 120 megapixels; o máximo é 100.");
    }

    [Theory]
    [InlineData(4000, 3000, 1600, 1200)]
    [InlineData(3000, 4000, 1200, 1600)]
    [InlineData(800, 600, 800, 600)]     // pequena: não amplia
    [InlineData(10000, 10, 1600, 2)]
    public void Reduzir_PeloLadoMaior(int largura, int altura, int esperadaL, int esperadaA)
    {
        RegrasDeFoto.Reduzir(largura, altura, 1600).Should().Be((esperadaL, esperadaA));
    }
}

public class DataDaFotoTests
{
    private static readonly DateTime Modificacao = new(2026, 10, 2, 8, 0, 0);

    [Fact]
    public void ExifOriginal_TemPrioridade()
    {
        DataDaFoto.Escolher(new DateTime(2026, 9, 28, 7, 15, 0), new DateTime(2026, 9, 29), new DateTime(2026, 9, 30), Modificacao)
            .Should().Be((new DateTime(2026, 9, 28, 7, 15, 0), OrigemDaData.ExifOriginal));
    }

    [Fact]
    public void SemOriginal_UsaDigitalizacao_DepoisImagem()
    {
        DataDaFoto.Escolher(null, new DateTime(2026, 9, 29), null, Modificacao).Origem.Should().Be(OrigemDaData.ExifDigitalizacao);
        DataDaFoto.Escolher(null, null, new DateTime(2026, 9, 30), Modificacao).Origem.Should().Be(OrigemDaData.ExifImagem);
    }

    [Fact]
    public void SemExif_OuComDataInvalida_UsaADataDoArquivo()
    {
        DataDaFoto.Escolher(null, null, null, Modificacao).Should().Be((Modificacao, OrigemDaData.ArquivoModificado));
        DataDaFoto.Escolher(new DateTime(1, 1, 1), new DateTime(1601, 1, 1), null, Modificacao)
            .Should().Be((Modificacao, OrigemDaData.ArquivoModificado), "câmera sem relógio acertado");
    }

    [Fact]
    public void DataDoArquivoForaDoIntervalo_FicaNoLimite()
    {
        DataDaFoto.Escolher(null, null, null, new DateTime(1601, 1, 1)).Data.Should().Be(MetadadosFoto.DataMinima);
    }
}
