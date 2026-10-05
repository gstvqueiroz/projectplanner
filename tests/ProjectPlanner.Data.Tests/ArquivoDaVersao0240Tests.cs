using Microsoft.EntityFrameworkCore;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Fotos;

namespace ProjectPlanner.Data.Tests;

// Bloco 25 (parte D): um .pplan gravado pela 0.24.0 abre igual na versão atual. O arquivo Arquivos/obra-0.24.0.pplan
// foi gerado com o código do commit 7548784 (Bloco 24, v0.24.0): 3 níveis, marcos, os quatro tipos de vínculo (com
// defasagem positiva e negativa), % concluído, notas com quebra de linha e 3 fotos de verdade (2 JPG e 1 PNG, com
// título, legenda, autor, local e tags). As datas esperadas são as que a 0.24.0 calculou para ele.
public sealed class ArquivoDaVersao0240Tests : TesteComArquivo
{
    private static readonly Guid Escavacao = Id(2);
    private static readonly Guid Sapatas = Id(3);

    private static Guid Id(int numero) => new(numero, 0, 0, new byte[8]);

    private Cronograma AbrirOArquivo()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Arquivos", "obra-0.24.0.pplan"), Caminho);
        return Repositorio.Abrir(Caminho);
    }

    private static IEnumerable<string> Estrutura(Cronograma c) =>
        c.Tarefas.Select(t => $"{c.EdtDe(t.Id)} {t.Nome} | {t.Duracao} d | {t.PercentualConcluido}% | {t.Fotos.Count} fotos");

    private static IEnumerable<string> Vinculos(Cronograma c) =>
        c.Predecessoras.Select(p => $"{c.EdtDe(p.TarefaOrigemId)} -> {c.EdtDe(p.TarefaDestinoId)} {p.Tipo} {p.Lag}");

    [Fact]
    public void Abre_ComTarefasVinculosENotasComoForamSalvos()
    {
        var aberto = AbrirOArquivo();

        // Antes do cálculo, os resumos têm a duração e o % gravados; o cálculo os soma (ver Calculo_DaAsMesmasDatasQueA0240).
        (aberto.Nome, aberto.DataInicio).Should().Be(("Residência Silva — referência 0.24", new DateTime(2026, 9, 7)));
        Estrutura(aberto).Should().Equal(
            "1 Fundação | 1 d | 0% | 0 fotos",
            "1.1 Escavação | 3 d | 100% | 2 fotos",
            "1.2 Sapatas | 5 d | 60% | 1 fotos",
            "1.3 Fundação concluída | 0 d | 0% | 0 fotos",
            "2 Estrutura | 1 d | 0% | 0 fotos",
            "2.1 Pilares | 6 d | 20% | 0 fotos",
            "2.2 Alvenaria | 12 d | 0% | 0 fotos",
            "2.3 Cinta e laje | 1 d | 0% | 0 fotos",
            "2.3.1 Formas | 2 d | 0% | 0 fotos",
            "2.3.2 Concretagem | 1 d | 0% | 0 fotos",
            "3 Revestimento — cerâmica 60×60 «A» | 4 d | 0% | 0 fotos",
            "4 Cobertura | 2 d | 0% | 0 fotos",
            "5 Entrega da obra | 0 d | 0% | 0 fotos");
        aberto.Tarefas.Where(t => t.EhMarco).Select(t => t.Nome).Should().Equal("Fundação concluída", "Entrega da obra");
        aberto.Tarefas.Where(t => t.EhResumo).Select(t => t.Nome).Should().Equal("Fundação", "Estrutura", "Cinta e laje");
        Vinculos(aberto).Should().Equal(
            "1.1 -> 1.2 FS 0", "1.2 -> 1.3 FS 1", "1.3 -> 2.1 FS 0", "2.1 -> 2.2 SS 2", "2.2 -> 2.3.1 FF 0",
            "2.3.1 -> 2.3.2 SF 3", "2.3.2 -> 3 FS -1", "2.2 -> 4 FS 0", "4 -> 5 FS 0", "3 -> 5 FS 0");
        aberto.Tarefas.Single(t => t.Id == Escavacao).Notas.Should().Be("Solo argiloso;\nconferir a sondagem SP-02.");
        aberto.Tarefas.Single(t => t.Nome == "Concretagem").Notas.Should().Be("Concreto fck 25 MPa — slump 10±2");
    }

    [Fact]
    public void Abre_ComAsFotos_MetadadosImagensEMiniaturas()
    {
        var aberto = AbrirOArquivo();

        var fotos = aberto.Tarefas.SelectMany(t => t.Fotos).ToList();
        aberto.FotosDescartadasAoAbrir.Should().Be(0);
        fotos.Select(f => (f.TarefaId, f.Imagem.NomeArquivoOriginal)).Should().BeEquivalentTo(
            [(Escavacao, "IMG_0101.jpg"), (Escavacao, "IMG_0102.jpg"), (Sapatas, "planta.png")]);
        var primeira = fotos.Single(f => f.Imagem.NomeArquivoOriginal == "IMG_0101.jpg");
        (primeira.Titulo, primeira.Descricao, primeira.Autor, primeira.Local, primeira.PercentualConcluidoNaData, primeira.DataFoto)
            .Should().Be(("Início da escavação", "Frente norte", "Gustavo", "Bloco A", 100, new DateTime(2026, 9, 9, 9, 30, 0)));
        fotos.Single(f => f.Imagem.NomeArquivoOriginal == "IMG_0102.jpg").Tags.Should().Equal("durante", "problema");

        Repositorio.LerMiniaturas(Caminho, fotos.Select(f => f.Id).ToList()).Values.Should().HaveCount(3).And.OnlyContain(m => m.Length > 0);
        foreach (var foto in fotos)
            Repositorio.LerImagem(Caminho, foto.Id).Should().NotBeNullOrEmpty($"a imagem de {foto.Imagem.NomeArquivoOriginal} continua no arquivo");
    }

    [Fact]
    public void Calculo_DaAsMesmasDatasQueA0240()
    {
        var aberto = AbrirOArquivo();

        var resultado = CalculadoraCronograma.Calcular(aberto);

        resultado.DataTermino.Should().Be(new DateTime(2026, 10, 13));
        aberto.Tarefas.Select(t => $"{aberto.EdtDe(t.Id)} {t.InicioMaisCedo:yyyy-MM-dd} {t.TerminoMaisCedo:yyyy-MM-dd} folga {t.Folga} {(t.EhCritica ? "crítica" : "-")}")
            .Should().Equal(
                "1 2026-09-07 2026-09-17 folga 0 crítica",
                "1.1 2026-09-07 2026-09-09 folga 0 crítica",
                "1.2 2026-09-10 2026-09-16 folga 0 crítica",
                "1.3 2026-09-17 2026-09-17 folga 0 crítica",
                "2 2026-09-18 2026-10-08 folga 0 crítica",
                "2.1 2026-09-18 2026-09-25 folga 0 crítica",
                "2.2 2026-09-22 2026-10-07 folga 0 crítica",
                "2.3 2026-10-06 2026-10-08 folga 0 crítica",
                "2.3.1 2026-10-06 2026-10-07 folga 0 crítica",
                "2.3.2 2026-10-08 2026-10-08 folga 0 crítica",
                "3 2026-10-08 2026-10-13 folga 0 crítica",
                "4 2026-10-08 2026-10-09 folga 2 -",
                "5 2026-10-13 2026-10-13 folga 0 crítica");
        aberto.Tarefas.Where(t => t.EhResumo).Select(t => $"{t.Nome} {t.Duracao} d {t.PercentualConcluido}%")
            .Should().Equal("Fundação 9 d 75%", "Estrutura 15 d 6%", "Cinta e laje 3 d 0%");
    }

    [Fact]
    public void FormatoNaoMudou_AsMigrationsDoArquivoSaoAsDaVersaoAtual()
    {
        AbrirOArquivo();
        using var contexto = ProjectPlannerDbContext.Criar(Caminho, criarSeNaoExistir: false);

        contexto.Database.GetAppliedMigrations().Should().Equal(contexto.Database.GetMigrations(),
            "a 0.25 não mudou o formato do .pplan: nada a migrar num arquivo da 0.24");
    }

    [Fact]
    public void SalvarComAVersaoAtual_EReabrir_SemPerderNada()
    {
        var aberto = AbrirOArquivo();
        var copia = Path.Combine(Pasta, "obra-salva-na-0.25.pplan");

        Repositorio.Salvar(aberto, copia, new ImagensParaSalvar(Caminho, new Dictionary<Guid, ConteudoDeFoto>()));
        var reaberto = Repositorio.Abrir(copia);

        Estrutura(reaberto).Should().Equal(Estrutura(aberto));
        Vinculos(reaberto).Should().Equal(Vinculos(aberto));
        reaberto.Tarefas.Select(t => t.Notas).Should().Equal(aberto.Tarefas.Select(t => t.Notas));
        foreach (var foto in aberto.Tarefas.SelectMany(t => t.Fotos))
            Repositorio.LerImagem(copia, foto.Id).Should().Equal(Repositorio.LerImagem(Caminho, foto.Id));
    }
}
