using ProjectPlanner.Core;

namespace ProjectPlanner.Core.Tests;

// Bloco 71: o calendário de trabalho do projeto: os dias da semana, os feriados nacionais (com os móveis da Páscoa), os do
// projeto e as exceções, e a aritmética dos dias úteis, que no padrão precisa dar exatamente o que dava a conta de antes.
public class CalendarioDoProjetoTests
{
    private static DateTime D(string data) => DateTime.Parse(data);

    private static DateOnly O(string data) => DateOnly.Parse(data);

    private static readonly DayOfWeek[] SegundaASabado =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];

    [Fact]
    public void Padrao_DaOMesmoQueAContaDeAntes_EmMilharesDeCasos()
    {
        var sorteio = new Random(71);
        var padrao = CalendarioDoProjeto.Padrao;
        for (var i = 0; i < 4000; i++)
        {
            var dia = DiasUteis.ProximoOuMesmo(new DateTime(2000, 1, 1).AddDays(sorteio.Next(0, 20000)));
            var quantidade = sorteio.Next(-3000, 3000);
            padrao.Adicionar(dia, quantidade).Should().Be(DiasUteis.Adicionar(dia, quantidade), $"{dia:d} + {quantidade}");
            var outro = DiasUteis.ProximoOuMesmo(dia.AddDays(sorteio.Next(-2000, 2000)));
            padrao.Indice(dia, outro).Should().Be(DiasUteis.Indice(dia, outro));
            var qualquer = dia.AddDays(sorteio.Next(-10, 10));
            (padrao.ProximoOuMesmo(qualquer), padrao.AnteriorOuMesmo(qualquer)).Should().Be((DiasUteis.ProximoOuMesmo(qualquer), DiasUteis.AnteriorOuMesmo(qualquer)));
            padrao.ContarDiasUteis(qualquer, outro).Should().Be(CalendarioDeTrabalho.ContarDiasUteis(qualquer, outro));
        }
    }

    [Fact]
    public void Padrao_NasPontasDasDatas_ENumaQuantidadeEnorme()
    {
        var padrao = CalendarioDoProjeto.Padrao;
        padrao.Adicionar(new DateTime(9999, 12, 27), 4).Should().Be(new DateTime(9999, 12, 31));
        padrao.Adicionar(new DateTime(9999, 12, 27), 5).Should().BeNull();
        padrao.Adicionar(new DateTime(1, 1, 1), -1).Should().BeNull();
        padrao.Adicionar(new DateTime(2026, 9, 28), int.MaxValue).Should().BeNull();
        CalendarioDoProjeto.Brasil.Adicionar(new DateTime(2026, 9, 28), int.MaxValue).Should().BeNull("com feriados também, sem andar dia a dia");
        CalendarioDoProjeto.Brasil.Adicionar(new DateTime(2026, 9, 28), int.MinValue + 1).Should().BeNull();
    }

    [Fact]
    public void Pascoa_EOsFeriadosMoveis()
    {
        FeriadosDoBrasil.Pascoa(2026).Should().Be(O("2026-04-05"));
        FeriadosDoBrasil.Pascoa(2027).Should().Be(O("2027-03-28"));
        FeriadosDoBrasil.Pascoa(2024).Should().Be(O("2024-03-31"));
        var feriados = FeriadosDoBrasil.Do(2026);
        feriados.Select(f => (f.Data, f.Facultativo)).Should().Contain([(O("2026-02-16"), true), (O("2026-02-17"), true), (O("2026-04-03"), false),
            (O("2026-06-04"), true), (O("2026-11-20"), false), (O("2026-10-12"), false)]);
        feriados.Should().HaveCount(13).And.BeInAscendingOrder(f => f.Data);
        FeriadosDoBrasil.Do(2023).Should().NotContain(f => f.Nome.Contains("Zumbi"), "o 20 de novembro é nacional desde 2024");
    }

    [Fact]
    public void Brasil_PulaOsFeriados()
    {
        var brasil = CalendarioDoProjeto.Brasil;

        brasil.EhDiaUtil(O("2026-10-12")).Should().BeFalse("Nossa Senhora Aparecida, segunda-feira");
        brasil.EhDiaUtil(O("2026-10-13")).Should().BeTrue();
        brasil.MotivoSemTrabalho(O("2026-04-03")).Should().Be("Sexta-feira Santa");
        brasil.MotivoSemTrabalho(O("2026-10-11")).Should().Be("domingo");
        // A partir de sexta 09/10: segunda é feriado, então o próximo dia útil é terça 13/10.
        brasil.Adicionar(D("2026-10-09"), 1).Should().Be(D("2026-10-13"));
        brasil.Adicionar(D("2026-10-13"), -1).Should().Be(D("2026-10-09"));
        brasil.ProximoOuMesmo(D("2026-10-10")).Should().Be(D("2026-10-13"));
        brasil.AnteriorOuMesmo(D("2026-10-12")).Should().Be(D("2026-10-09"));
        // Novembro de 2026: 21 dias de segunda a sexta, menos Finados (seg 02) e Consciência Negra (sex 20); 15/11 é domingo.
        brasil.ContarDiasUteis(D("2026-11-01"), D("2026-11-30")).Should().Be(19);
        brasil.Indice(D("2026-11-03"), D("2026-11-23")).Should().Be(13);
        brasil.DiasSemTrabalho(O("2026-11-01"), O("2026-11-30")).Select(d => d.Motivo).Should().Equal("Finados", "Dia Nacional de Zumbi e da Consciência Negra");
    }

    [Fact]
    public void SegundaASabado_EOsFeriadosDoProjeto_EAsExcecoes()
    {
        var calendario = CalendarioDoProjeto.Criar(SegundaASabado, true, false,
            [new FeriadoDoCalendario(O("2026-01-20"), "São Sebastião", TodoAno: true), new FeriadoDoCalendario(O("2026-10-15"), "Dia do Professor (municipal)")],
            [new ExcecaoDoCalendario(O("2026-10-05"), O("2026-10-07"), "Chuva", Trabalha: false),
             new ExcecaoDoCalendario(O("2026-10-11"), O("2026-10-12"), "Recuperar a chuva", Trabalha: true)]);

        calendario.TextoDosDias.Should().Be("segunda a sábado");
        calendario.Descricao.Should().Be("Segunda a sábado · feriados nacionais · 2 feriados do projeto · 1 período sem trabalho · 1 período de trabalho extra");
        calendario.EhDiaUtil(O("2026-10-10")).Should().BeTrue("sábado é dia de trabalho");
        calendario.EhDiaUtil(O("2026-10-06")).Should().BeFalse("chuva");
        calendario.EhDiaUtil(O("2026-10-11")).Should().BeTrue("o domingo trabalhado");
        calendario.EhDiaUtil(O("2026-10-12")).Should().BeTrue("o feriado trabalhado vale mais que o feriado");
        calendario.EhDiaUtil(O("2026-10-15")).Should().BeFalse();
        calendario.EhDiaUtil(O("2027-01-20")).Should().BeFalse("São Sebastião todo ano");
        calendario.MotivoSemTrabalho(O("2026-10-06")).Should().Be("Chuva");
        // 01/10 a 17/10: 15 dias de segunda a sábado, menos 3 de chuva e o 15/10, mais o domingo 11 (o 12 já era de trabalho).
        calendario.ContarDiasUteis(D("2026-10-01"), D("2026-10-17")).Should().Be(15 - 3 - 1 + 1);
        calendario.DiasExtras(O("2026-10-01"), O("2026-10-31")).Select(d => d.Dia).Should().Equal(O("2026-10-11"), O("2026-10-12"));
        calendario.Adicionar(D("2026-10-03"), 1).Should().Be(D("2026-10-08"), "do sábado 03, pula o domingo e os 3 dias de chuva");
    }

    [Fact]
    public void Aritmetica_ConfereComAContaDiaADia_EmCalendariosVariados()
    {
        var sorteio = new Random(1971);
        var calendarios = new[]
        {
            CalendarioDoProjeto.Brasil,
            CalendarioDoProjeto.Criar([DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday], true, true,
                [new FeriadoDoCalendario(O("2026-03-19"), "São José", true)],
                [new ExcecaoDoCalendario(O("2026-12-21"), O("2027-01-08"), "Recesso", false), new ExcecaoDoCalendario(O("2027-01-09"), O("2027-01-10"), "Mutirão", true)]),
            CalendarioDoProjeto.Criar(CalendarioDoProjeto.Semana, false, false, [], [new ExcecaoDoCalendario(O("2026-07-01"), O("2026-07-31"), "Férias", false)]),
        };
        foreach (var calendario in calendarios)
            for (var i = 0; i < 300; i++)
            {
                var dia = calendario.ProximoOuMesmo(new DateTime(2025, 6, 1).AddDays(sorteio.Next(0, 900)));
                var quantidade = sorteio.Next(-250, 250);
                var esperado = dia;
                for (var falta = Math.Abs(quantidade); falta > 0;)
                {
                    esperado = esperado.AddDays(Math.Sign(quantidade));
                    if (calendario.EhDiaUtil(esperado))
                        falta--;
                }
                calendario.Adicionar(dia, quantidade).Should().Be(esperado, $"{calendario.Descricao}: {dia:d} {quantidade:+0;-0}");
                calendario.Indice(dia, esperado).Should().Be(quantidade);
                var contagem = 0;
                for (var d = dia; d <= dia.AddDays(60); d = d.AddDays(1))
                    if (calendario.EhDiaUtil(d))
                        contagem++;
                calendario.ContarDiasUteis(dia, dia.AddDays(60)).Should().Be(contagem);
            }
    }

    [Theory]
    [InlineData("segunda a sexta", "seg,ter,qua,qui,sex")]
    [InlineData("Segunda a Sábado", "seg,ter,qua,qui,sex,sab")]
    [InlineData("de segunda-feira a sábado", "seg,ter,qua,qui,sex,sab")]
    [InlineData("seg-sab", "seg,ter,qua,qui,sex,sab")]
    [InlineData("segunda, quarta e sexta", "seg,qua,sex")]
    [InlineData("segunda a sexta e sábado", "seg,ter,qua,qui,sex,sab")]
    [InlineData("todos os dias", "seg,ter,qua,qui,sex,sab,dom")]
    [InlineData("sábados", "sab")]
    [InlineData("qualquer coisa", null)]
    public void LerDias(string texto, string? esperado)
    {
        var abreviado = CalendarioDoProjeto.LerDias(texto)?.Select(d => CalendarioDoProjeto.NomeDoDia(d)[..3].Replace("sáb", "sab").Replace("ter", "ter"));
        (abreviado is null ? null : string.Join(",", abreviado)).Should().Be(esperado);
    }

    [Fact]
    public void Criar_RecusaOQueNaoFazSentido_EOrdenaEIgualaPeloConteudo()
    {
        var semDias = () => CalendarioDoProjeto.Criar([], true, true);
        semDias.Should().Throw<DomainValidationException>().WithMessage("Marque pelo menos um dia de trabalho na semana.");
        var invertida = () => CalendarioDoProjeto.Padrao.ComExcecao(new ExcecaoDoCalendario(O("2026-10-10"), O("2026-10-01"), "x", false));
        invertida.Should().Throw<DomainValidationException>().WithMessage("O período de 10/10/2026 a 01/10/2026 termina antes de começar.");
        var longa = () => CalendarioDoProjeto.Padrao.ComExcecao(new ExcecaoDoCalendario(O("2026-01-01"), O("2040-01-01"), "x", false));
        longa.Should().Throw<DomainValidationException>().WithMessage("*longo demais*");

        var a = CalendarioDoProjeto.Padrao.ComFeriado(new FeriadoDoCalendario(O("2026-12-08"), "  Imaculada  ")).ComFeriado(new FeriadoDoCalendario(O("2026-01-20"), ""));
        var b = CalendarioDoProjeto.Padrao.ComFeriado(new FeriadoDoCalendario(O("2026-01-20"), "Feriado")).ComFeriado(new FeriadoDoCalendario(O("2026-12-08"), "Imaculada"));
        a.Should().Be(b);
        a.Feriados.Select(f => f.Nome).Should().Equal("Feriado", "Imaculada");
        a.SemFeriado(a.Feriados[0]).SemFeriado(a.Feriados[1]).EhOPadrao.Should().BeTrue();
        CalendarioDoProjeto.Brasil.EhOPadrao.Should().BeFalse();
        CalendarioDoProjeto.Brasil.ComFeriadosNacionais(false, false).Should().Be(CalendarioDoProjeto.Padrao);
        CalendarioDoProjeto.Padrao.ComDias([DayOfWeek.Monday, DayOfWeek.Wednesday]).TextoDosDias.Should().Be("segunda e quarta");
    }
}
