using ProjectPlanner.Core.Fotos;

namespace ProjectPlanner.Core.Tests;

// Bloco 22 (D3): filtros, agrupamento por dia e ordenação da galeria, sem tela.
public class FiltroDeFotosTests
{
    private static readonly Guid Tarefa = Guid.NewGuid();
    private static readonly DateOnly Hoje = new(2026, 10, 15);

    private static FotoTarefa F(int dia, int hora, string? legenda = null, string? titulo = null, string? local = null,
        string? autor = null, params string[] tags) =>
        FotosDeTeste.Nova(Tarefa, new DateTime(2026, 10, dia, hora, 0, 0), legenda, titulo, autor, local, tags);

    private readonly FotoTarefa _escavacao = F(1, 8, "Escavação das sapatas", tags: "antes");
    private readonly FotoTarefa _armacao = F(9, 10, "Armação", titulo: "Ferragem", tags: ["durante", "Problema"]);
    private readonly FotoTarefa _concretagem = F(9, 15, "Concretagem da laje", local: "Bloco A", tags: "durante");
    private readonly FotoTarefa _cura = F(15, 9, "Cura", autor: "João", tags: "depois");
    private readonly FotoTarefa _mesmoInstante = F(9, 15, "Segunda foto da concretagem");

    private List<FotoTarefa> Todas => [_escavacao, _armacao, _concretagem, _cura, _mesmoInstante];

    private IReadOnlyList<FotoTarefa> Filtrar(CriteriosDeFotos c) => FiltroDeFotos.Filtrar(Todas, c, Hoje);

    [Fact]
    public void SemFiltro_TodasEmOrdemDeData_EmpatesNaOrdemDeCadastro()
    {
        Filtrar(CriteriosDeFotos.Nenhum).Should().Equal(_escavacao, _armacao, _concretagem, _mesmoInstante, _cura);
    }

    [Fact]
    public void MaisRecentesPrimeiro_InverteTudo()
    {
        Filtrar(new CriteriosDeFotos { MaisRecentesPrimeiro = true })
            .Should().Equal(_cura, _mesmoInstante, _concretagem, _armacao, _escavacao);
    }

    [Fact]
    public void Agrupar_PorDia_ComAsFotosDeCadaDia()
    {
        var grupos = FiltroDeFotos.Agrupar(Todas, CriteriosDeFotos.Nenhum, Hoje);

        grupos.Select(g => (g.Dia, g.Fotos.Count)).Should().Equal(
            (new DateOnly(2026, 10, 1), 1), (new DateOnly(2026, 10, 9), 3), (new DateOnly(2026, 10, 15), 1));
        grupos[1].Fotos.Should().Equal(_armacao, _concretagem, _mesmoInstante);
    }

    [Fact]
    public void Agrupar_MaisRecentesPrimeiro_DiasEFotosInvertidos()
    {
        FiltroDeFotos.Agrupar(Todas, new CriteriosDeFotos { MaisRecentesPrimeiro = true }, Hoje)
            .Select(g => g.Dia.Day).Should().Equal(15, 9, 1);
    }

    [Fact]
    public void DataExata()
    {
        Filtrar(new CriteriosDeFotos { Dia = new DateOnly(2026, 10, 9) }).Should().Equal(_armacao, _concretagem, _mesmoInstante);
        Filtrar(new CriteriosDeFotos { Dia = new DateOnly(2026, 10, 2) }).Should().BeEmpty();
    }

    [Fact]
    public void Intervalo_InclusiveNasPontas()
    {
        Filtrar(new CriteriosDeFotos { De = new DateOnly(2026, 10, 9), Ate = new DateOnly(2026, 10, 15) })
            .Should().Equal(_armacao, _concretagem, _mesmoInstante, _cura);
        Filtrar(new CriteriosDeFotos { Ate = new DateOnly(2026, 10, 1) }).Should().Equal(_escavacao);
        Filtrar(new CriteriosDeFotos { De = new DateOnly(2026, 10, 15) }).Should().Equal(_cura);
    }

    [Fact]
    public void IntervaloInvertido_Recusado()
    {
        var filtrar = () => Filtrar(new CriteriosDeFotos { De = new DateOnly(2026, 10, 9), Ate = new DateOnly(2026, 10, 1) });

        filtrar.Should().Throw<DomainValidationException>().WithMessage("O início do período (09/10/2026) é depois do fim (01/10/2026).");
    }

    [Theory]
    [InlineData(AtalhoDePeriodo.Hoje, 1)]
    [InlineData(AtalhoDePeriodo.Ultimos7Dias, 4)] // 9 a 15
    [InlineData(AtalhoDePeriodo.EsteMes, 5)]
    [InlineData(AtalhoDePeriodo.Tudo, 5)]
    public void Atalhos(AtalhoDePeriodo atalho, int quantidade)
    {
        Filtrar(new CriteriosDeFotos { Atalho = atalho }).Should().HaveCount(quantidade);
    }

    [Fact]
    public void Ultimos7Dias_SaoHojeEOsSeisAnteriores()
    {
        FiltroDeFotos.Periodo(AtalhoDePeriodo.Ultimos7Dias, Hoje).Should().Be((new DateOnly(2026, 10, 9), Hoje));
        FiltroDeFotos.Periodo(AtalhoDePeriodo.EsteMes, new DateOnly(2026, 2, 10)).Should().Be((new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)));
    }

    [Theory]
    [InlineData("concretagem", 2)]   // legenda, sem diferenciar maiúsculas
    [InlineData("ESCAVACAO", 1)]     // sem acentos
    [InlineData("ferragem", 1)]      // título
    [InlineData("bloco a", 1)]       // local
    [InlineData("joão", 1)]          // autor
    [InlineData("inexistente", 0)]
    [InlineData("   ", 5)]           // em branco = sem filtro
    public void Texto(string texto, int quantidade)
    {
        Filtrar(new CriteriosDeFotos { Texto = texto }).Should().HaveCount(quantidade);
    }

    [Fact]
    public void Tag_SemDiferenciarMaiusculas()
    {
        Filtrar(new CriteriosDeFotos { Tag = "durante" }).Should().Equal(_armacao, _concretagem);
        Filtrar(new CriteriosDeFotos { Tag = "problema" }).Should().Equal(_armacao);
    }

    [Fact]
    public void Criterios_SeCombinam()
    {
        Filtrar(new CriteriosDeFotos { Dia = new DateOnly(2026, 10, 9), Tag = "durante", Texto = "laje" }).Should().Equal(_concretagem);
    }

    [Fact]
    public void PorTarefa_ParaAVisaoDoProjeto()
    {
        var outra = FotosDeTeste.Nova(Guid.NewGuid(), new DateTime(2026, 10, 9));

        FiltroDeFotos.Filtrar([.. Todas, outra], new CriteriosDeFotos { TarefaId = Tarefa }, Hoje).Should().NotContain(outra);
    }

    [Fact]
    public void Tags_SemRepetir_EmOrdemAlfabetica()
    {
        FiltroDeFotos.Tags(Todas).Should().Equal("antes", "depois", "durante", "Problema");
    }

    [Fact]
    public void SemFotos_SemGrupos()
    {
        FiltroDeFotos.Agrupar([], CriteriosDeFotos.Nenhum, Hoje).Should().BeEmpty();
    }
}
