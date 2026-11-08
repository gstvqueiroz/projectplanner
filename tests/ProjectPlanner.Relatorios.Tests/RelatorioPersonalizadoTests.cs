using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Diario;
using ProjectPlanner.Core.Indicadores;
using ProjectPlanner.Core.OrdensDeServico;
using ProjectPlanner.Core.Relatorios;
using ProjectPlanner.Relatorios.Personalizado;
using SkiaSharp;

namespace ProjectPlanner.Relatorios.Tests;

// Bloco 88: o relatório personalizado: as seções do modelo, na ordem, com a aparência dele (a cor, o papel, a orientação), em PDF e
// na prévia da tela. A obra: 1 Fundação (1.1 Escavação e 1.2 Sapatas, prontas), 2 Estrutura (2.1 Pilares 60%, 2.2 Vigas 20%),
// 3 Alvenaria e o marco 4 Laje pronta; 7 fotos dos pilares em setembro; 3 dias no diário; a medição 01 aprovada; 3 custos; 1 OS.
public sealed class RelatorioPersonalizadoTests
{
    public static readonly DateTime Inicio = new(2026, 8, 3);
    public static readonly PeriodoDoRelatorio Setembro = new(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

    private static byte[] Jpeg(SKColor cor, int largura = 160, int altura = 120)
    {
        using var bitmap = new SKBitmap(largura, altura);
        bitmap.Erase(cor);
        using var imagem = SKImage.FromBitmap(bitmap);
        return imagem.Encode(SKEncodedImageFormat.Jpeg, 80).ToArray();
    }

    public static (Cronograma Obra, Dictionary<Guid, byte[]> Imagens) Obra(int fotos = 7)
    {
        var c = new Cronograma("Casa da Ana", Inicio);
        c.DefinirObra(new DadosDaObra("Ana Souza", "Contrato 12/2026", "Rua das Flores, 100"));
        Tarefa Nova(string nome, int duracao, Tarefa? pai = null, int feito = 0, decimal? valor = null)
        {
            var t = new Tarefa(nome, Inicio, duracao);
            c.AdicionarTarefa(t, pai?.Id);
            if (duracao > 0 && valor is not null)
                t.DefinirValor(valor);
            if (feito > 0)
                t.AtualizarPercentualConcluido(feito);
            return t;
        }
        var fundacao = Nova("Fundação", 1);
        var escavacao = Nova("Escavação", 5, fundacao, 100, 8_000);
        var sapatas = Nova("Sapatas", 5, fundacao, 100, 12_000);
        var estrutura = Nova("Estrutura", 1);
        var pilares = Nova("Pilares", 10, estrutura, 60, 20_000);
        var vigas = Nova("Vigas", 8, estrutura, 20, 15_000);
        var alvenaria = Nova("Alvenaria", 15, null, 0, 25_000);
        var laje = Nova("Laje pronta", 0);
        c.AdicionarPredecessora(new Predecessora(escavacao.Id, sapatas.Id, TipoDependencia.FS, 0));
        c.AdicionarPredecessora(new Predecessora(sapatas.Id, pilares.Id, TipoDependencia.FS, 0));
        c.AdicionarPredecessora(new Predecessora(pilares.Id, vigas.Id, TipoDependencia.FS, 0));
        c.AdicionarPredecessora(new Predecessora(vigas.Id, alvenaria.Id, TipoDependencia.FS, 0));
        c.AdicionarPredecessora(new Predecessora(vigas.Id, laje.Id, TipoDependencia.FS, 0));
        CalculadoraCronograma.Calcular(c);

        var imagens = new Dictionary<Guid, byte[]>();
        for (var i = 0; i < fotos; i++)
        {
            var nome = $"IMG_{i}.jpg";
            var foto = new FotoTarefa(Guid.NewGuid(), pilares.Id,
                new DadosDaImagem(nome, 1600, 1200, 1000, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(nome)))),
                new MetadadosFoto(new DateTime(2026, 9, 2 + 3 * i, 9, 0, 0), titulo: $"Pilar P{i + 1} concretado", local: "Bloco A"),
                Inicio);
            pilares.AdicionarFoto(foto);
            imagens[foto.Id] = Jpeg(new SKColor((byte)(30 * i), 140, 200));
        }

        c.Diario.Abrir(new DiaDaObra(new DateOnly(2026, 9, 14))
        {
            Efetivo = [new EfetivoNoDia("Pedreiro", 4), new EfetivoNoDia("Servente", 3)],
            Atividades = [new AtividadeNoDia(pilares.Id, "2.1 Pilares", 50, 6, "un")],
        });
        c.Diario.Abrir(new DiaDaObra(new DateOnly(2026, 9, 15)) { Tarde = new ClimaDoPeriodo(TempoNoPeriodo.Chuva, Praticavel: false), Ocorrencias = "Chuva forte às 14h." });
        c.Diario.Abrir(new DiaDaObra(new DateOnly(2026, 9, 16)) { Efetivo = [new EfetivoNoDia("Armador", 2)] });

        var medicao = c.Contrato.NovaMedicao(new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 31));
        c.Contrato.Medir(medicao.Numero, escavacao.Id, 100);
        c.Contrato.Aprovar(medicao.Numero, new DateTime(2026, 9, 1));

        c.Custos.Adicionar(new LancamentoDeCusto(Guid.NewGuid(), new DateOnly(2026, 9, 3), pilares.Id, CategoriaDeCusto.Material, "Concreto usinado", "NF 12", 9_800));
        c.Custos.Adicionar(new LancamentoDeCusto(Guid.NewGuid(), new DateOnly(2026, 9, 10), null, CategoriaDeCusto.Administracao, "Aluguel do container", null, 650));
        c.Custos.Adicionar(new LancamentoDeCusto(Guid.NewGuid(), new DateOnly(2026, 9, 20), vigas.Id, CategoriaDeCusto.MaoDeObra, "Armação das vigas", null, 4_200));

        var agora = new DateTime(2026, 9, 1);
        c.OrdensDeServico.Adicionar(OrdemDeServico.Nova(Guid.NewGuid(), 1, new DadosDaOrdem("Alvenaria do térreo", TipoDeExecutante.Terceiro, "Construtora Silva", null, null,
            [alvenaria.Id], [new ItemDaOrdem(alvenaria.Id, "Alvenaria", "m²", 120, 60)], null, new DateOnly(2026, 10, 15), FormaDePagamentoDaOrdem.Empreitada, null, null), agora)
            .Emitir(agora));
        CalculadoraCronograma.Calcular(c);
        return (c, imagens);
    }

    public static readonly CabecalhoDoRelatorio Cabecalho =
        new("Casa da Ana", new DadosDaObra("Ana Souza", "Contrato 12/2026", "Rua das Flores, 100"), "Eng. Bia Lima", "CREA 12345", null);

    private static RelatorioPersonalizado Montar(ModeloDeRelatorio modelo, PeriodoDoRelatorio? periodo = null, int fotos = 7)
    {
        var (obra, imagens) = Obra(fotos);
        return GeradorRelatorioPersonalizado.Montar(modelo, obra, Cabecalho, periodo ?? Setembro, id => imagens.GetValueOrDefault(id), new DateTime(2026, 10, 1, 9, 0, 0));
    }

    private static ModeloDeRelatorio Com(params SecaoDoRelatorio[] secoes) => new() { Nome = "Teste", Titulo = "Relatório de teste", Secoes = secoes };

    private static byte[] Pdf(RelatorioPersonalizado relatorio)
    {
        using var destino = new MemoryStream();
        relatorio.GerarPdf(destino);
        return destino.ToArray();
    }

    private static int PaginasDoPdf(byte[] pdf) => Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page\b").Count;

    [Fact]
    public void OsModelosProntos_ValemEMontam_NoPdfENaPrevia()
    {
        foreach (var modelo in ModelosProntos.Todos)
        {
            using var relatorio = Montar(modelo);
            var pdf = Pdf(relatorio);
            Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-", modelo.Nome);
            PaginasDoPdf(pdf).Should().Be(relatorio.Paginas, modelo.Nome);
            relatorio.Paginas.Should().Be(modelo == ModelosProntos.Executivo ? 1 : relatorio.Paginas, "o resumo executivo cabe numa página");
            relatorio.Paginas.Should().BeGreaterThan(modelo == ModelosProntos.Executivo ? 0 : 1, modelo.Nome);
            using var previa = SKBitmap.Decode(relatorio.Previa(relatorio.Paginas - 1, 400));
            (previa.Width, previa.Height).Should().Be((400, 566), $"{modelo.Nome}: A4 em retrato");
        }
    }

    [Fact]
    public void ACapa_TemAFaixaNaCorDoTema_ESemCabecalho()
    {
        var vermelho = Com(new SecaoDoRelatorio(TipoDeSecao.Capa)) with { Tema = new TemaDoRelatorio { Cor = "#C62828" } };

        using var relatorio = Montar(vermelho);

        relatorio.Paginas.Should().Be(1);
        using var capa = SKBitmap.Decode(relatorio.Previa(0, 300));
        capa.GetPixel(150, 10).Should().Be(new SKColor(0xC6, 0x28, 0x28), "a faixa do alto da capa");
        capa.GetPixel(150, 400).Should().NotBe(new SKColor(0xC6, 0x28, 0x28));
        relatorio.NomeSugerido.Should().Be("Relatório de teste - Casa da Ana - 2026-09-30");
    }

    [Fact]
    public void Fotos_NaGradeEscolhida_ComOMaximo_ESemFotosNoPeriodoAvisa()
    {
        using var quatro = Montar(Com(new SecaoDoRelatorio(TipoDeSecao.Fotos) { FotosPorPagina = 4, MaximoDeFotos = 6 }));
        quatro.Paginas.Should().Be(2, "6 das 7 fotos, 4 por página");
        quatro.Avisos.Should().BeEmpty();

        using var duas = Montar(Com(new SecaoDoRelatorio(TipoDeSecao.Fotos) { FotosPorPagina = 2 }));
        duas.Paginas.Should().Be(4, "7 fotos, 2 por página");

        using var outubro = Montar(Com(new SecaoDoRelatorio(TipoDeSecao.Fotos)), new PeriodoDoRelatorio(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)));
        outubro.Avisos.Should().Equal("Fotos do período: nenhuma foto de 01/10/2026 a 31/10/2026.");
    }

    [Fact]
    public void UmaTabelaLonga_ContinuaNaPaginaSeguinte()
    {
        var (obra, _) = Obra(fotos: 0);
        for (var i = 1; i <= 90; i++)
            obra.AdicionarTarefa(new Tarefa($"Tarefa extra {i}", Inicio, 3));
        CalculadoraCronograma.Calcular(obra);

        using var relatorio = GeradorRelatorioPersonalizado.Montar(Com(new SecaoDoRelatorio(TipoDeSecao.Tarefas)), obra, Cabecalho, Setembro, _ => null, DateTime.Now);

        relatorio.Paginas.Should().BeGreaterThan(2);
    }

    [Fact]
    public void Paisagem_ECarta_MudamAFolha()
    {
        var modelo = Com(new SecaoDoRelatorio(TipoDeSecao.Resumo), new SecaoDoRelatorio(TipoDeSecao.Gantt) { Nivel = 3 })
            with { Tema = new TemaDoRelatorio { Papel = PapelDoRelatorio.Carta, Paisagem = true } };

        using var relatorio = Montar(modelo);

        (relatorio.Largura, relatorio.Altura).Should().Be((792f, 612f));
        Encoding.Latin1.GetString(Pdf(relatorio)).Should().Contain("/MediaBox [0 0 792 612]");
    }

    [Fact]
    public void SecaoEmPaginaNova_SemDeixarUmaPaginaEmBranco_EAsDesligadasNaoSaem()
    {
        var texto = new SecaoDoRelatorio(TipoDeSecao.Texto) { Texto = "Primeiro parágrafo.\n\nSegundo parágrafo.", PaginaNova = true };

        using (var duas = Montar(Com(new SecaoDoRelatorio(TipoDeSecao.Resumo), texto)))
            duas.Paginas.Should().Be(2);
        using (var uma = Montar(Com(texto)))
            uma.Paginas.Should().Be(1, "a primeira seção já começa numa página nova");
        using (var semAsDesligadas = Montar(Com(new SecaoDoRelatorio(TipoDeSecao.Resumo), texto with { Ativa = false })))
            semAsDesligadas.Paginas.Should().Be(1);
    }

    [Fact]
    public void TodasAsSecoes_JuntasNumRelatorio()
    {
        var todas = Com([.. Enum.GetValues<TipoDeSecao>().Select(t => new SecaoDoRelatorio(t) { Texto = t == TipoDeSecao.Texto ? "Sem pendências." : null })]);

        using var relatorio = Montar(todas);

        relatorio.Paginas.Should().BeGreaterThanOrEqualTo(6);
        relatorio.Avisos.Should().BeEmpty();
        PaginasDoPdf(Pdf(relatorio)).Should().Be(relatorio.Paginas);
    }

    [Fact]
    public void UmModeloQueNaoVale_ERecusado()
    {
        var (obra, _) = Obra(fotos: 0);
        RelatorioPersonalizado Gerar(ModeloDeRelatorio m) => GeradorRelatorioPersonalizado.Montar(m, obra, Cabecalho, Setembro, _ => null, DateTime.Now);

        FluentActions.Invoking(() => Gerar(Com())).Should().Throw<DomainValidationException>().WithMessage("Ligue pelo menos uma seção.");
        FluentActions.Invoking(() => Gerar(Com(new SecaoDoRelatorio(TipoDeSecao.Resumo)) with { Tema = new TemaDoRelatorio { Cor = "azul" } }))
            .Should().Throw<DomainValidationException>().WithMessage("A cor do relatório precisa ser como #1565C0.");
        FluentActions.Invoking(() => Gerar(Com(new SecaoDoRelatorio(TipoDeSecao.Fotos) { FotosPorPagina = 3 })))
            .Should().Throw<DomainValidationException>().WithMessage("As fotos vão 2, 4 ou 6 por página.");
    }

    [Fact]
    public void Descartado_NoMeioDoPdf_OPdfTerminaInteiro_EDepoisNaoDesenhaMais()
    {
        var relatorio = Montar(ModelosProntos.Mensal);
        var paginas = relatorio.Paginas;
        using var destino = new DescartaAoEscrever(relatorio);

        relatorio.GerarPdf(destino);

        destino.Descartou.Should().BeTrue("o descarte veio com o PDF em curso");
        var pdf = destino.ToArray();
        PaginasDoPdf(pdf).Should().Be(paginas);
        Encoding.Latin1.GetString(pdf).TrimEnd().Should().EndWith("%%EOF");
        relatorio.Invoking(r => r.Previa(0, 200)).Should().Throw<ObjectDisposedException>();
        relatorio.Invoking(r => r.GerarPdf(Stream.Null)).Should().Throw<ObjectDisposedException>();
        relatorio.Invoking(r => r.Dispose()).Should().NotThrow("descartar de novo não faz nada");
    }

    // Descarta o relatório na primeira escrita do PDF, como a tela faz ao trocar a prévia enquanto o PDF é gerado.
    private sealed class DescartaAoEscrever(IDisposable relatorio) : MemoryStream
    {
        public bool Descartou { get; private set; }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Descartar();
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Descartar();
            base.Write(buffer);
        }

        private void Descartar()
        {
            if (Descartou)
                return;
            Descartou = true;
            relatorio.Dispose();
        }
    }
}
