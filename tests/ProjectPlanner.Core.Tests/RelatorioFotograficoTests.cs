using ProjectPlanner.Core.Relatorios;

namespace ProjectPlanner.Core.Tests;

// Bloco 27: a montagem do relatório fotográfico (o que entra, a ordem, a numeração, as legendas e as páginas).
public partial class RelatorioFotograficoTests
{
    private static readonly DateTime Seg = new(2026, 9, 28);
    private static readonly CabecalhoDoRelatorio Cabecalho = new("Residência Silva", DadosDaObra.Vazios, "Eng. Ana", "CREA 123", null);

    // Fundação (resumo) > Escavação (3 d), Sapatas (5 d, após Escavação); Cobertura (2 d, sem vínculo, com folga).
    private sealed class Obra
    {
        public Cronograma Cronograma { get; } = new("Residência Silva", Seg);
        public Tarefa Fundacao { get; } = new("Fundação", Seg, 1);
        public Tarefa Escavacao { get; } = new("Escavação", Seg, 3);
        public Tarefa Sapatas { get; } = new("Sapatas", Seg, 5);
        public Tarefa Cobertura { get; } = new("Cobertura", Seg, 2);

        public Obra()
        {
            Cronograma.AdicionarTarefa(Fundacao);
            Cronograma.AdicionarTarefa(Escavacao, Fundacao.Id);
            Cronograma.AdicionarTarefa(Sapatas, Fundacao.Id);
            Cronograma.AdicionarTarefa(Cobertura);
            Cronograma.AdicionarPredecessora(new Predecessora(Escavacao.Id, Sapatas.Id));
        }

        public FotoTarefa Foto(Tarefa tarefa, DateTime data, string? local = null, string? autor = null, int? percentual = null,
            IEnumerable<string>? tags = null, bool noRelatorio = true, bool destaque = false, string? titulo = null)
        {
            var foto = new FotoTarefa(Guid.NewGuid(), tarefa.Id,
                    new DadosDaImagem($"IMG_{Guid.NewGuid():N}.jpg", 1600, 1200, 300_000, FotosDeTeste.Hash(Guid.NewGuid().ToString())),
                    new MetadadosFoto(data, titulo, null, autor, local, percentual, tags), new DateTime(2026, 10, 1))
                .ComCuradoria(noRelatorio, destaque);
            tarefa.AdicionarFoto(foto);
            return foto;
        }

        public RelatorioFotografico Montar(OpcoesDoRelatorio opcoes)
        {
            CalculadoraCronograma.Calcular(Cronograma);
            return MontadorDoRelatorio.Montar(Cronograma, opcoes, Cabecalho, new DateTime(2026, 10, 11, 10, 0, 0));
        }
    }

    private static OpcoesDoRelatorio Outubro => new(new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 31));

    // ---- Curadoria e dados da obra ----

    [Fact]
    public void Curadoria_PorPadraoEntra_SemDestaque_EAsCopiasMantem()
    {
        var obra = new Obra();
        var foto = obra.Foto(obra.Escavacao, Seg);

        (foto.NoRelatorio, foto.Destaque).Should().Be((true, false));
        var destacada = foto.ComCuradoria(true, true);
        destacada.ComMetadados(new MetadadosFoto(Seg, "Novo título")).Should().Match<FotoTarefa>(f => f.NoRelatorio && f.Destaque);
        destacada.ComImagem(foto.Imagem).Destaque.Should().BeTrue();
        foto.ComCuradoria(false, true).Should().Match<FotoTarefa>(f => !f.NoRelatorio && !f.Destaque, "fora do relatório não há destaque");
    }

    [Fact]
    public void DadosDaObra_TextosLimpos_ELimite()
    {
        new DadosDaObra("  Sr. Silva ", "", null).Should().Match<DadosDaObra>(d => d.Contratante == "Sr. Silva" && d.Contrato == null && !d.Vazio);
        DadosDaObra.Vazios.Vazio.Should().BeTrue();
        var longo = () => new DadosDaObra(null, null, new string('x', DadosDaObra.TamanhoMaximo + 1));
        longo.Should().Throw<DomainValidationException>().WithMessage("O endereço tem 201 caracteres*");
    }

    [Fact]
    public void Historico_NumeracaoERepetido_ECopia()
    {
        var obra = new Obra();
        obra.Cronograma.ProximoNumeroDeRelatorio.Should().Be(1);

        obra.Cronograma.RegistrarRelatorio(new RelatorioGerado(1, Seg, new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 4), 12));
        obra.Cronograma.RegistrarRelatorio(new RelatorioGerado(3, Seg, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 11), 7));

        obra.Cronograma.ProximoNumeroDeRelatorio.Should().Be(4);
        obra.Cronograma.Invoking(c => c.RegistrarRelatorio(new RelatorioGerado(3, Seg, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5), 0)))
            .Should().Throw<DomainValidationException>().WithMessage("Já existe o relatório nº 3.");
        obra.Cronograma.DefinirObra(new DadosDaObra("Silva", "C-01", "Rua A"));
        var copia = obra.Cronograma.Copiar();
        (copia.Relatorios.Count, copia.Obra.Contrato).Should().Be((2, "C-01"));
    }

    [Theory]
    [InlineData(0, 2026, 10, 1, 0, "O número do relatório começa em 1.")]
    [InlineData(1, 2026, 9, 1, 0, "O período do relatório termina antes de começar.")]
    [InlineData(1, 2026, 10, 1, -1, "A quantidade de fotos não pode ser negativa.")]
    public void RelatorioGerado_Invalido(int numero, int ano, int mes, int dia, int fotos, string mensagem)
    {
        var criar = () => new RelatorioGerado(numero, Seg, new DateOnly(2026, 9, 28), new DateOnly(ano, mes, dia), fotos);

        criar.Should().Throw<DomainValidationException>().WithMessage(mensagem);
    }

    [Fact]
    public void DesdeOUltimo_DoInicioDoProjeto_EDepoisDoUltimo()
    {
        var obra = new Obra();
        var hoje = new DateOnly(2026, 10, 11);

        MontadorDoRelatorio.DesdeOUltimo(obra.Cronograma, hoje).Should().Be((new DateOnly(2026, 9, 28), hoje));
        obra.Cronograma.RegistrarRelatorio(new RelatorioGerado(1, Seg, new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 4), 3));
        MontadorDoRelatorio.DesdeOUltimo(obra.Cronograma, hoje).Should().Be((new DateOnly(2026, 10, 5), hoje));
        obra.Cronograma.RegistrarRelatorio(new RelatorioGerado(2, Seg, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 11), 3));
        MontadorDoRelatorio.DesdeOUltimo(obra.Cronograma, hoje).Should().Be((hoje, hoje), "o último já chega até hoje");
    }

    // ---- Opções ----

    [Theory]
    [InlineData(3, TarefasDoRelatorio.Todas, false, "As fotos por página precisam ser 1, 2, 4 ou 6.")]
    [InlineData(2, TarefasDoRelatorio.DeUmRamo, false, "Escolha o resumo do ramo.")]
    [InlineData(2, TarefasDoRelatorio.Todas, true, "O período termina antes de começar.")]
    public void Opcoes_Invalidas(int porPagina, TarefasDoRelatorio tarefas, bool invertido, string mensagem)
    {
        var opcoes = (invertido ? new OpcoesDoRelatorio(new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 1)) : Outubro)
            with { FotosPorPagina = porPagina, Tarefas = tarefas };

        opcoes.Invoking(o => o.Validar()).Should().Throw<DomainValidationException>().WithMessage(mensagem);
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 1, 2)]
    [InlineData(4, 2, 2)]
    [InlineData(6, 2, 3)]
    public void Opcoes_Grade(int porPagina, int colunas, int linhas) =>
        (Outubro with { FotosPorPagina = porPagina }).Grade.Should().Be((colunas, linhas));

    // ---- O que entra e a ordem ----

    [Fact]
    public void PorTarefa_NaOrdemDaEdt_DestaquePrimeiro_ENumeracaoSeguida()
    {
        var obra = new Obra();
        obra.Foto(obra.Cobertura, Seg.AddDays(10).AddHours(9));
        var s1 = obra.Foto(obra.Sapatas, Seg.AddDays(4).AddHours(8));
        var s2 = obra.Foto(obra.Sapatas, Seg.AddDays(5).AddHours(8), destaque: true);
        var e1 = obra.Foto(obra.Escavacao, Seg.AddHours(10), local: "Bloco A", autor: "Gustavo", percentual: 40);

        var relatorio = obra.Montar(Outubro);

        relatorio.Grupos.Select(g => g.Titulo).Should().Equal("1.1 Escavação", "1.2 Sapatas", "2 Cobertura");
        relatorio.Grupos[1].Fotos.Select(f => f.Foto.Id).Should().Equal(s2.Id, s1.Id);
        relatorio.Fotos.Select(f => f.Numero).Should().Equal(1, 2, 3, 4);
        relatorio.Grupos[0].Fotos.Single().Legenda.Should().Be("Foto 1 · 28/09/2026 10:00 · 1.1 Escavação · 40% concluído · Bloco A · Gustavo");
        relatorio.Grupos[0].Detalhe.Should().StartWith("Previsto 28/09/2026 a 30/09/2026 · 0% concluído");
        (relatorio.Numero, relatorio.TotalDeFotos, relatorio.Titulo).Should().Be((1, 4, "Relatório fotográfico nº 1 · 28/09/2026 a 31/10/2026"));
        relatorio.Fotos.Should().NotContain(f => f.Foto.Id == e1.Id && f.Numero != 1);
    }

    [Fact]
    public void Periodo_TiradasDoRelatorio_ETags_FicamDeFora()
    {
        var obra = new Obra();
        obra.Foto(obra.Escavacao, Seg.AddDays(-1));                      // antes do período
        obra.Foto(obra.Escavacao, new DateTime(2026, 11, 1, 8, 0, 0));   // depois
        obra.Foto(obra.Escavacao, Seg.AddHours(8), noRelatorio: false);  // tirada do relatório
        var antes = obra.Foto(obra.Escavacao, Seg.AddHours(9), tags: ["Antes"]);
        var ultimoDia = obra.Foto(obra.Sapatas, new DateTime(2026, 10, 31, 23, 59, 0), tags: ["depois"]);

        obra.Montar(Outubro).Fotos.Select(f => f.Foto.Id).Should().Equal(antes.Id, ultimoDia.Id);
        obra.Montar(Outubro with { Tags = ["antes"] }).Fotos.Select(f => f.Foto.Id).Should().Equal(antes.Id);
    }

    [Fact]
    public void FiltroDeTarefas_CriticasRamoEEmExecucao()
    {
        var obra = new Obra();
        var escavacao = obra.Foto(obra.Escavacao, Seg.AddHours(8));
        obra.Foto(obra.Cobertura, Seg.AddHours(9));
        var sapatas = obra.Foto(obra.Sapatas, Seg.AddDays(20));

        obra.Montar(Outubro with { Tarefas = TarefasDoRelatorio.SoCriticas }).Fotos.Select(f => f.Foto.Id).Should().Equal(escavacao.Id, sapatas.Id);
        obra.Montar(Outubro with { Tarefas = TarefasDoRelatorio.DeUmRamo, Ramo = obra.Fundacao.Id }).TotalDeFotos.Should().Be(2);
        obra.Montar(new OpcoesDoRelatorio(new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 31)) with { Tarefas = TarefasDoRelatorio.EmExecucaoNoPeriodo })
            .Grupos.Select(g => g.Titulo).Should().Equal(["1.2 Sapatas"], "a escavação e a cobertura acabaram antes de 06/10");
    }

    [Fact]
    public void PorDataEPorLocal()
    {
        var obra = new Obra();
        obra.Foto(obra.Sapatas, Seg.AddDays(1).AddHours(8), local: "Bloco B");
        obra.Foto(obra.Escavacao, Seg.AddHours(8), local: "bloco A");
        obra.Foto(obra.Cobertura, Seg.AddHours(9));

        obra.Montar(Outubro with { Agrupamento = AgrupamentoDoRelatorio.PorData }).Grupos.Select(g => (g.Titulo, g.Fotos.Count))
            .Should().Equal(("Segunda-feira, 28/09/2026", 2), ("Terça-feira, 29/09/2026", 1));
        obra.Montar(Outubro with { Agrupamento = AgrupamentoDoRelatorio.PorLocal }).Grupos.Select(g => g.Titulo)
            .Should().Equal("bloco A", "Bloco B", "Sem local informado");
    }

    [Fact]
    public void SemFotos_RelatorioVazio_SoCapaEIndice()
    {
        var relatorio = new Obra().Montar(Outubro);

        (relatorio.TotalDeFotos, relatorio.Paginas.Count, relatorio.TotalDePaginas).Should().Be((0, 0, 2));
        relatorio.Resumo.Should().NotBeNull();
        new Obra().Montar(Outubro with { ComResumo = false, ComIndice = false, ComAssinaturas = false })
            .Should().Match<RelatorioFotografico>(r => r.Resumo == null && r.TotalDePaginas == 1);
    }

    // ---- Páginas ----

    [Fact]
    public void Paginas_DuasPorPagina_CadaGrupoComecaNumaPagina()
    {
        var obra = new Obra();
        for (var i = 0; i < 3; i++)
            obra.Foto(obra.Escavacao, Seg.AddHours(8 + i));
        obra.Foto(obra.Sapatas, Seg.AddDays(5));

        var paginas = obra.Montar(Outubro).Paginas;

        paginas.Select(p => (p.Grupo.Titulo, p.Continuacao, p.Fotos.Count)).Should().Equal(
            ("1.1 Escavação", false, 2), ("1.1 Escavação", true, 1), ("1.2 Sapatas", false, 1));
    }

    [Fact]
    public void Paginas_QuatroPorPagina_DestaqueOcupaALinhaInteira()
    {
        var obra = new Obra();
        obra.Foto(obra.Escavacao, Seg.AddHours(8), destaque: true);
        for (var i = 0; i < 4; i++)
            obra.Foto(obra.Escavacao, Seg.AddHours(9 + i));

        var paginas = obra.Montar(Outubro with { FotosPorPagina = 4 }).Paginas;

        paginas.Select(p => string.Join(",", p.Fotos.Select(f => f.Colunas))).Should().Equal("2,1,1", "1,1");
    }

    [Fact]
    public void Parecidas_MesmaTarefaEmPoucosMinutos()
    {
        var obra = new Obra();
        obra.Foto(obra.Escavacao, Seg.AddHours(8));
        obra.Foto(obra.Escavacao, Seg.AddHours(8).AddMinutes(1));
        obra.Foto(obra.Escavacao, Seg.AddHours(9));
        obra.Foto(obra.Sapatas, Seg.AddHours(8).AddMinutes(1), noRelatorio: true);

        obra.Montar(Outubro).Parecidas.Select(p => (p.Primeira.Numero, p.Segunda.Numero)).Should().Equal((1, 2));
    }
}
