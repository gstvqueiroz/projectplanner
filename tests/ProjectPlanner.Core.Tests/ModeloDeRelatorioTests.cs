using ProjectPlanner.Core.Relatorios;

namespace ProjectPlanner.Core.Tests;

// Bloco 88: o modelo do relatório personalizado (as seções, a aparência e as regras), os modelos prontos e o período com os atalhos.
public sealed class ModeloDeRelatorioTests
{
    private static ModeloDeRelatorio Com(params SecaoDoRelatorio[] secoes) => new() { Nome = "Meu", Titulo = "Relatório", Secoes = secoes };

    [Fact]
    public void OsModelosProntos_Valem_ETemNomesDiferentes()
    {
        foreach (var modelo in ModelosProntos.Todos)
            modelo.Invoking(m => m.Validar()).Should().NotThrow(modelo.Nome);
        ModelosProntos.Todos.Select(m => m.Nome).Should().OnlyHaveUniqueItems();
        ModelosProntos.PeloNome("relatório MENSAL ao cliente").Should().BeSameAs(ModelosProntos.Mensal);
        ModelosProntos.PeloNome("outro").Should().BeNull();
        ModelosProntos.Mensal.Ativas.Select(s => s.Tipo).Should().NotContain(TipoDeSecao.Texto, "o texto vem desligado no mensal");
    }

    [Fact]
    public void Secao_OTituloPadrao_EOProprio()
    {
        new SecaoDoRelatorio(TipoDeSecao.Fotos).TituloOuPadrao.Should().Be("Fotos do período");
        new SecaoDoRelatorio(TipoDeSecao.Fotos) { Titulo = "  Registro fotográfico " }.TituloOuPadrao.Should().Be("Registro fotográfico");
        Enum.GetValues<TipoDeSecao>().Select(SecaoDoRelatorio.TituloPadrao).Should().OnlyHaveUniqueItems();
        Enum.GetValues<TipoDeSecao>().Select(SecaoDoRelatorio.Descricao).Should().OnlyContain(d => d.Length > 20);
    }

    [Theory]
    [InlineData("Ligue pelo menos uma seção.")]
    public void UmModeloSemSecaoLigada_NaoVale(string mensagem) =>
        Com(new SecaoDoRelatorio(TipoDeSecao.Resumo) { Ativa = false }).Invoking(m => m.Validar())
            .Should().Throw<DomainValidationException>().WithMessage(mensagem);

    [Fact]
    public void AsOpcoesForaDoAceito_NaoValem()
    {
        void Recusa(ModeloDeRelatorio modelo, string mensagem) =>
            modelo.Invoking(m => m.Validar()).Should().Throw<DomainValidationException>().WithMessage(mensagem);
        var resumo = new SecaoDoRelatorio(TipoDeSecao.Resumo);

        Recusa(Com(resumo) with { Nome = " " }, "Dê um nome ao modelo.");
        Recusa(Com(resumo) with { Nome = new string('a', 81) }, "O nome do modelo passa de 80 caracteres.");
        Recusa(Com(resumo) with { Titulo = "" }, "Dê um título ao relatório.");
        Recusa(Com(resumo) with { Tema = new TemaDoRelatorio { Cor = "#12345" } }, "A cor do relatório precisa ser como #1565C0.");
        Recusa(Com(resumo) with { Tema = new TemaDoRelatorio { Rodape = new string('x', 161) } }, "O rodapé passa de 160 caracteres.");
        Recusa(Com(new SecaoDoRelatorio(TipoDeSecao.Fotos) { MaximoDeFotos = 0 }), "O máximo de fotos vai de 1 a 200.");
        Recusa(Com(new SecaoDoRelatorio(TipoDeSecao.Proximas) { Dias = 120 }), "As próximas atividades vão de 1 a 90 dias.");
        Recusa(Com(new SecaoDoRelatorio(TipoDeSecao.Gantt) { Nivel = 0 }), "O nível do cronograma vai de 1 a 6.");
        Recusa(Com(new SecaoDoRelatorio(TipoDeSecao.Texto) { Texto = new string('t', 8001) }), "O texto de \"Observações\" passa de 8000 caracteres.");
        Com(resumo, new SecaoDoRelatorio(TipoDeSecao.Fotos) { FotosPorPagina = 6, Ativa = false }).Invoking(m => m.Validar()).Should().NotThrow();
    }

    [Fact]
    public void Periodo_OsAtalhos_EOTexto()
    {
        var quarta = new DateOnly(2026, 9, 16);
        (PeriodoDoRelatorio.EstaSemana(quarta).De, PeriodoDoRelatorio.EstaSemana(quarta).Ate).Should().Be((new DateOnly(2026, 9, 14), quarta));
        PeriodoDoRelatorio.EstaSemana(new DateOnly(2026, 9, 20)).De.Should().Be(new DateOnly(2026, 9, 14), "domingo é o fim da semana que começou na segunda");
        PeriodoDoRelatorio.EsteMes(quarta).Should().Be(new PeriodoDoRelatorio(new DateOnly(2026, 9, 1), quarta));
        PeriodoDoRelatorio.MesPassado(quarta).Should().Be(new PeriodoDoRelatorio(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)));
        PeriodoDoRelatorio.MesPassado(new DateOnly(2026, 1, 10)).Should().Be(new PeriodoDoRelatorio(new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 31)));
        PeriodoDoRelatorio.DesdeOInicio(new DateOnly(2026, 8, 3), quarta).Texto.Should().Be("03/08/2026 a 16/09/2026");
        PeriodoDoRelatorio.DesdeOInicio(new DateOnly(2026, 12, 1), quarta).Texto.Should().Be("01/12/2026", "a obra ainda não começou");
        new PeriodoDoRelatorio(quarta, quarta).Contem(new DateTime(2026, 9, 16, 23, 59, 0)).Should().BeTrue();
        FluentActions.Invoking(() => new PeriodoDoRelatorio(quarta, quarta.AddDays(-1))).Should().Throw<DomainValidationException>()
            .WithMessage("O período termina antes de começar.");
    }
}
