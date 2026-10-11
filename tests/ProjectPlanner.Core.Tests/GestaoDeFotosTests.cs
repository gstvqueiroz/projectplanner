using ProjectPlanner.Core.Fotos;

namespace ProjectPlanner.Core.Tests;

// Bloco 28: GPS, locais padronizados, tarefa sugerida pela data da foto e alerta de tarefa sem foto recente.
// Projeto começando na seg 21/09/2026: A (5 d: 21/09–25/09, 100%), B (10 d: 28/09–09/10), C (4 d: 12/10–15/10),
// Marco 1 (25/09), dentro do resumo Fase (A e B).
public class GestaoDeFotosTests
{
    private static readonly DateTime Inicio = new(2026, 9, 21);

    private static (Cronograma Cronograma, Dictionary<string, Tarefa> Tarefas) Obra(params (string Nome, int Duracao, string Depois)[] extras)
    {
        var c = new Cronograma("Residência", Inicio);
        var t = new Dictionary<string, Tarefa>();
        void Nova(string nome, int duracao, string? pai = null)
        {
            t[nome] = new Tarefa(nome, Inicio, duracao);
            c.AdicionarTarefa(t[nome], pai is null ? null : t[pai].Id);
        }
        Nova("Fase", 1); Nova("A", 5, "Fase"); Nova("B", 10, "Fase"); Nova("C", 4); Nova("Marco 1", 0);
        c.AdicionarPredecessora(new Predecessora(t["A"].Id, t["B"].Id));
        c.AdicionarPredecessora(new Predecessora(t["B"].Id, t["C"].Id));
        c.AdicionarPredecessora(new Predecessora(t["A"].Id, t["Marco 1"].Id));
        foreach (var (nome, duracao, depois) in extras)
        {
            Nova(nome, duracao);
            c.AdicionarPredecessora(new Predecessora(t[depois].Id, t[nome].Id));
        }
        t["A"].AtualizarPercentualConcluido(100);
        CalculadoraCronograma.Calcular(c);
        return (c, t);
    }

    private static void Foto(Tarefa tarefa, DateTime data, string? local = null) =>
        tarefa.AdicionarFoto(FotosDeTeste.Nova(tarefa.Id, data, local: local));

    // ---- Coordenadas ----

    [Fact]
    public void Coordenadas_TextoEMapa()
    {
        var gps = new Coordenadas(-22.905810, -47.062630);

        gps.ToString().Should().Be("-22.905810, -47.062630");
        gps.Mapa().ToString().Should().Be("https://www.openstreetmap.org/?mlat=-22.90581&mlon=-47.06263#map=18/-22.90581/-47.06263");
        new Coordenadas(90, 180).Should().Be(new Coordenadas(90, 180));
        new Coordenadas(-90, -180).Latitude.Should().Be(-90);
    }

    [Theory]
    [InlineData(90.0001, 0)]
    [InlineData(-90.0001, 0)]
    [InlineData(0, 180.0001)]
    [InlineData(0, -180.0001)]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.NaN)]
    public void Coordenadas_ForaDoIntervalo_Recusadas(double latitude, double longitude)
    {
        var criar = () => new Coordenadas(latitude, longitude);

        criar.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Gps_SobreviveAEdicaoAoGiroEACuradoria()
    {
        var foto = FotosDeTeste.Nova(Guid.NewGuid(), Inicio) with { Gps = new Coordenadas(-22.9, -47.06) };

        foto.ComMetadados(new MetadadosFoto(Inicio, "t")).Gps.Should().Be(foto.Gps);
        foto.ComImagem(new DadosDaImagem("x.jpg", 1200, 1600, 10, FotosDeTeste.Hash("x"))).Gps.Should().Be(foto.Gps);
        foto.ComCuradoria(false, false).Gps.Should().Be(foto.Gps);
        FotosDeTeste.Nova(Guid.NewGuid(), Inicio).Gps.Should().BeNull();
    }

    // ---- Configuração: locais e alerta ----

    [Fact]
    public void Configuracao_LocaisSemVaziosNemRepetidos_NaOrdem()
    {
        var configuracao = new ConfiguracaoDasFotos(["  Bloco A ", "", "Bloco B", "bloco a", "   ", "Fachada"], 10);

        configuracao.Locais.Should().Equal("Bloco A", "Bloco B", "Fachada");
        (configuracao.DiasSemFotoParaAlerta, configuracao.AlertaLigado, configuracao.EhPadrao).Should().Be((10, true, false));
        new ConfiguracaoDasFotos([], 0).AlertaLigado.Should().BeFalse();
        ConfiguracaoDasFotos.Padrao.Should().Be(new ConfiguracaoDasFotos([" "], ConfiguracaoDasFotos.DiasPadrao));
        ConfiguracaoDasFotos.Padrao.EhPadrao.Should().BeTrue();
        new ConfiguracaoDasFotos(["A", "B"], 7).Should().NotBe(new ConfiguracaoDasFotos(["B", "A"], 7), "a ordem da lista é do usuário");
    }

    [Fact]
    public void Configuracao_Limites()
    {
        new ConfiguracaoDasFotos([new string('x', MetadadosFoto.TamanhoMaximoDoLocal)], ConfiguracaoDasFotos.DiasMaximos).Locais.Should().HaveCount(1);
        new ConfiguracaoDasFotos(Enumerable.Range(1, ConfiguracaoDasFotos.QuantidadeMaximaDeLocais).Select(i => $"L{i}"), 1).Locais
            .Should().HaveCount(ConfiguracaoDasFotos.QuantidadeMaximaDeLocais);

        var acoes = new Func<ConfiguracaoDasFotos>[]
        {
            () => new ConfiguracaoDasFotos([new string('x', MetadadosFoto.TamanhoMaximoDoLocal + 1)], 7),
            () => new ConfiguracaoDasFotos(Enumerable.Range(0, ConfiguracaoDasFotos.QuantidadeMaximaDeLocais + 1).Select(i => $"L{i}"), 7),
            () => new ConfiguracaoDasFotos([], -1),
            () => new ConfiguracaoDasFotos([], ConfiguracaoDasFotos.DiasMaximos + 1),
        };
        foreach (var acao in acoes)
            acao.Should().Throw<DomainValidationException>();
        ((Func<ConfiguracaoDasFotos>)(() => new ConfiguracaoDasFotos(null!, 7))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Sugestoes_PadronizadosPrimeiro_DepoisOsUsadosEmOrdemAlfabetica()
    {
        var tarefa = Guid.NewGuid();
        var fotos = new[]
        {
            FotosDeTeste.Nova(tarefa, Inicio, local: "Térreo"), FotosDeTeste.Nova(tarefa, Inicio, local: "bloco a"),
            FotosDeTeste.Nova(tarefa, Inicio, local: "Área externa"), FotosDeTeste.Nova(tarefa, Inicio, local: "térreo"),
            FotosDeTeste.Nova(tarefa, Inicio),
        };

        new ConfiguracaoDasFotos(["Bloco B", "Bloco A"], 7).Sugestoes(fotos).Should().Equal("Bloco B", "Bloco A", "Área externa", "Térreo");
        ConfiguracaoDasFotos.Padrao.Sugestoes([]).Should().BeEmpty();
    }

    [Fact]
    public void Cronograma_GuardaAConfiguracao_EACopiaLevaJunto()
    {
        var c = new Cronograma("Obra", Inicio);
        c.ConfiguracaoDasFotos.Should().Be(ConfiguracaoDasFotos.Padrao);
        var nova = new ConfiguracaoDasFotos(["Bloco A"], 3);

        c.DefinirConfiguracaoDasFotos(nova);

        c.ConfiguracaoDasFotos.Should().Be(nova);
        c.Copiar().ConfiguracaoDasFotos.Should().Be(nova);
        ((Action)(() => c.DefinirConfiguracaoDasFotos(null!))).Should().Throw<ArgumentNullException>();
    }

    // ---- Filtro por local ----

    [Fact]
    public void Filtro_PorLocal_MesmoNomeSemDiferenciarMaiusculasNemAcentos()
    {
        var tarefa = Guid.NewGuid();
        var fotos = new[]
        {
            FotosDeTeste.Nova(tarefa, Inicio, local: "Bloco A — Térreo", arquivo: "1.jpg"),
            FotosDeTeste.Nova(tarefa, Inicio, local: "Bloco A", arquivo: "2.jpg"),
            FotosDeTeste.Nova(tarefa, Inicio, arquivo: "3.jpg"),
        };
        string[] Nomes(string? local) =>
            FiltroDeFotos.Filtrar(fotos, new CriteriosDeFotos { Local = local }, DateOnly.FromDateTime(Inicio)).Select(f => f.Imagem.NomeArquivoOriginal).ToArray();

        Nomes("bloco a — terreo").Should().Equal("1.jpg");
        Nomes("Bloco A").Should().Equal(["2.jpg"], "não é busca por trecho");
        Nomes("  ").Should().Equal("1.jpg", "2.jpg", "3.jpg");
        Nomes(null).Should().HaveCount(3);
        Nomes("Fachada").Should().BeEmpty();
    }

    // ---- Tarefa sugerida pela data ----

    [Theory]
    [InlineData("2026-09-25 17:00", "A")]  // último dia de A (concluída, mas a foto é daquele dia); o marco do dia não conta
    [InlineData("2026-09-21 06:00", "A")]
    [InlineData("2026-09-30 10:00", "B")]
    [InlineData("2026-10-09 23:59", "B")]  // último dia de B, a hora não importa
    [InlineData("2026-10-15 08:00", "C")]
    public void Sugerir_ATarefaPrevistaNoDia(string data, string esperada)
    {
        var (c, t) = Obra();

        SugestaoDeTarefa.Sugerir(c, DateTime.Parse(data, System.Globalization.CultureInfo.InvariantCulture)).Should().BeSameAs(t[esperada]);
    }

    [Theory]
    [InlineData("2026-09-26 10:00")] // sábado entre A e B
    [InlineData("2026-10-10 10:00")] // sábado entre B e C
    [InlineData("2026-09-20 10:00")] // antes do projeto
    [InlineData("2026-12-01 10:00")] // depois
    public void Sugerir_NenhumaTarefaNoDia_Nula(string data)
    {
        var (c, _) = Obra();

        SugestaoDeTarefa.Sugerir(c, DateTime.Parse(data, System.Globalization.CultureInfo.InvariantCulture)).Should().BeNull();
    }

    [Fact]
    public void Sugerir_VariasNoDia_AMaisCurta_ENoEmpateAPrimeiraNaEdt()
    {
        var (c, t) = Obra(("D", 2, "A"), ("E", 2, "A"), ("F", 3, "A"));

        SugestaoDeTarefa.Sugerir(c, new DateTime(2026, 9, 29, 10, 0, 0)).Should().BeSameAs(t["D"], "B, D, E e F estão no dia; D e E são as mais curtas");
        SugestaoDeTarefa.Sugerir(c, new DateTime(2026, 9, 30, 10, 0, 0)).Should().BeSameAs(t["F"]);
        SugestaoDeTarefa.Sugerir(new Cronograma("Vazio", Inicio), Inicio).Should().BeNull();
    }

    // ---- Alerta de tarefa sem foto ----

    private static readonly DateTime Dia8 = new(2026, 10, 8, 15, 0, 0);

    [Fact]
    public void Alerta_TarefaIniciadaSemNenhumaFoto_ContaDesdeOInicio()
    {
        var (c, t) = Obra();

        var alertas = AlertaDeFotos.Calcular(c, Dia8);

        alertas.Should().ContainSingle();
        (alertas[0].Tarefa, alertas[0].Edt, alertas[0].Dias, alertas[0].UltimaFoto).Should().Be((t["B"], "1.2", 10, (DateOnly?)null));
        alertas[0].Descricao.Should().Be("Sem nenhuma foto desde o início, há 10 dias");
    }

    [Theory]
    [InlineData(5, false)] // 3 dias
    [InlineData(1, true)]  // 7 dias: no limite, alerta
    public void Alerta_ContaDesdeAUltimaFoto(int diaDaFoto, bool alerta)
    {
        var (c, t) = Obra();
        Foto(t["B"], new DateTime(2026, 9, 29, 9, 0, 0));
        Foto(t["B"], new DateTime(2026, 10, diaDaFoto, 18, 0, 0));

        var alertas = AlertaDeFotos.Calcular(c, Dia8);

        alertas.Any().Should().Be(alerta);
        if (alerta)
            alertas[0].Descricao.Should().Be("Sem foto há 7 dias (a última é de 01/10/2026)");
    }

    [Fact]
    public void Alerta_FotoDoFuturoOuDeAntesDoInicio_NaoContam()
    {
        var (c, t) = Obra();
        Foto(t["B"], new DateTime(2026, 10, 20));  // data errada na câmera
        Foto(t["B"], new DateTime(2026, 9, 21));   // antes de B começar

        var alerta = AlertaDeFotos.Calcular(c, Dia8).Single();

        (alerta.Dias, alerta.UltimaFoto).Should().Be((10, new DateOnly(2026, 9, 21)));
    }

    [Fact]
    public void Alerta_ConcluidasNaoIniciadasResumosEMarcos_FicamDeFora()
    {
        var (c, t) = Obra();
        t["B"].AtualizarPercentualConcluido(100);
        t["C"].AtualizarPercentualConcluido(10); // adiantada: % > 0 mas o início é depois de hoje

        AlertaDeFotos.Calcular(c, Dia8).Should().BeEmpty();
        AlertaDeFotos.Calcular(c, new DateTime(2026, 10, 30)).Select(a => a.Tarefa).Should().Equal(t["C"]);
    }

    [Fact]
    public void Alerta_NaoIniciadaComPercentual_ContaDesdeOInicio()
    {
        var (c, t) = Obra();

        AlertaDeFotos.Calcular(c, new DateTime(2026, 9, 27)).Should().BeEmpty("B começa amanhã e está em 0%");
        t["B"].AtualizarPercentualConcluido(5);
        AlertaDeFotos.Calcular(c, new DateTime(2026, 9, 27)).Should().BeEmpty("começou antes, mas ainda não passou nenhum dia");
    }

    [Fact]
    public void Alerta_DiasConfiguraveis_EZeroDesliga()
    {
        var (c, t) = Obra();
        Foto(t["B"], new DateTime(2026, 10, 7));

        c.DefinirConfiguracaoDasFotos(new ConfiguracaoDasFotos([], 1));
        AlertaDeFotos.Calcular(c, Dia8).Single().Descricao.Should().Be("Sem foto há 1 dia (a última é de 07/10/2026)");
        Foto(t["B"], new DateTime(2026, 10, 8, 7, 0, 0));
        AlertaDeFotos.Calcular(c, Dia8).Should().BeEmpty("tem foto de hoje");

        c.DefinirConfiguracaoDasFotos(new ConfiguracaoDasFotos([], 0));
        AlertaDeFotos.Calcular(c, new DateTime(2026, 11, 30)).Should().BeEmpty();
    }

    [Fact]
    public void Alerta_NaOrdemDaEdt_ENoResumoDoProjeto()
    {
        var (c, t) = Obra(("D", 2, "A"));
        var hoje = new DateTime(2026, 10, 20);

        var alertas = AlertaDeFotos.Calcular(c, hoje);

        alertas.Select(a => a.Tarefa.Nome).Should().Equal("B", "C", "D");
        ResumoDoProjeto.Calcular(c, AcompanhamentoDoCronograma.Calcular(c, hoje), hoje).SemFotoRecente.Should().Equal(alertas);
        ((Func<IReadOnlyList<TarefaSemFotoRecente>>)(() => AlertaDeFotos.Calcular(null!, hoje))).Should().Throw<ArgumentNullException>();
    }
}
