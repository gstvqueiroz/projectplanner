using ProjectPlanner.Core;
using ProjectPlanner.Core.Diario;
using ProjectPlanner.Core.Recursos;

namespace ProjectPlanner.Core.Tests;

// Bloco 81: o Diário de Obra (RDO): os dias em ordem de data, numerados pela posição; um dia fechado não muda e os números dos
// fechados nunca mudam; o dia sugerido pelo cronograma; o % levado ao cronograma; o dia parado no calendário.
public class DiarioDaObraTests
{
    private static readonly DateOnly D28 = new(2026, 9, 28), D29 = new(2026, 9, 29), D30 = new(2026, 9, 30), D01 = new(2026, 10, 1);

    private readonly DiarioDaObra _diario = new();

    [Fact]
    public void Dias_EmOrdemDeData_NumeradosPelaPosicao_ELimpos()
    {
        _diario.Abrir(new DiaDaObra(D30));
        var dia = _diario.Abrir(new DiaDaObra(D28)
        {
            Efetivo = [new EfetivoNoDia(" Pedreiro ", 3, " "), new EfetivoNoDia("", 2), new EfetivoNoDia("Servente", 2, "Empreiteira X")],
            Atividades = [new AtividadeNoDia(null, "  Limpeza do terreno ")],
            Ocorrencias = "   ",
        });

        _diario.Dias.Select(d => (d.Data, _diario.NumeroDe(d.Data))).Should().Equal((D28, 1), (D30, 2));
        dia.Efetivo.Should().Equal(new EfetivoNoDia("Pedreiro", 3), new EfetivoNoDia("Servente", 2, "Empreiteira X"));
        (dia.TotalDoEfetivo, dia.Atividades.Single().Descricao, dia.Ocorrencias).Should().Be((5, "Limpeza do terreno", null));
        FluentActions.Invoking(() => _diario.Abrir(new DiaDaObra(D28))).Should().Throw<DomainValidationException>()
            .WithMessage("O diário já tem o dia 28/09/2026.");
        FluentActions.Invoking(() => _diario.Salvar(dia with { Efetivo = [new EfetivoNoDia("Pedreiro", 0)] }))
            .Should().Throw<DomainValidationException>().WithMessage("A quantidade de \"Pedreiro\" precisa ir de 1 a 999.");
        FluentActions.Invoking(() => _diario.Salvar(dia with { Atividades = [new AtividadeNoDia(null, "Reboco", 120)] }))
            .Should().Throw<DomainValidationException>().WithMessage("O % de \"Reboco\" precisa ir de 0 a 100.");
    }

    [Fact]
    public void Fechado_NaoMuda_ENumerosDosFechadosNuncaMudam()
    {
        _diario.Abrir(new DiaDaObra(D28));
        _diario.Abrir(new DiaDaObra(D30));
        _diario.Fechar(D30, new DateTime(2026, 9, 30, 18, 0, 0), " Eng. Ana ");

        _diario.Dia(D30)!.Fechamento.Should().Be(new FechamentoDoDia(new DateTime(2026, 9, 30, 18, 0, 0), "Eng. Ana"));
        FluentActions.Invoking(() => _diario.Salvar(new DiaDaObra(D30) { Ocorrencias = "x" })).Should().Throw<DomainValidationException>()
            .WithMessage("O dia 30/09/2026 está fechado (assinado): reabra-o para mudar.");
        FluentActions.Invoking(() => _diario.Abrir(new DiaDaObra(D29))).Should().Throw<DomainValidationException>()
            .WithMessage("O dia 29/09/2026 ficaria antes do último dia fechado (RDO nº 2, 30/09/2026): os números dos dias fechados não podem mudar.");
        FluentActions.Invoking(() => _diario.Remover(D28)).Should().Throw<DomainValidationException>().WithMessage("Há dias fechados depois de 28/09/2026*");
        _diario.Abrir(new DiaDaObra(D01)).Should().NotBeNull("depois do último fechado pode");

        _diario.Reabrir(D30);
        _diario.Salvar(new DiaDaObra(D30) { Ocorrencias = "Visita da fiscalização." });
        _diario.Remover(D01);
        _diario.Dias.Select(d => d.Data).Should().Equal(D28, D30);
        _diario.Copiar().Dias.Should().Equal(_diario.Dias);
    }

    [Fact]
    public void Sugerido_PeloCronograma_ServicosEfetivoEquipamentos_EClimaPeloCalendario()
    {
        var c = new Cronograma("Obra", Datas.Segunda);
        var escavacao = new Tarefa("Escavação", Datas.Segunda, 3);
        var formas = new Tarefa("Formas", Datas.Segunda, 2);
        var pintura = new Tarefa("Pintura", Datas.Segunda, 2);
        foreach (var t in new[] { escavacao, formas, pintura })
            c.AdicionarTarefa(t);
        pintura.AtualizarPercentualConcluido(100);
        var pedreiro = new Recurso(Guid.NewGuid(), "Pedreiro", TipoDeRecurso.Profissional, "dia", 200, fornecedor: "Empreiteira X");
        var betoneira = new Recurso(Guid.NewGuid(), "Betoneira", TipoDeRecurso.Equipamento, "dia", 80);
        var cimento = new Recurso(Guid.NewGuid(), "Cimento", TipoDeRecurso.Material, "sc", 40);
        foreach (var r in new[] { pedreiro, betoneira, cimento })
            c.Recursos.Adicionar(r);
        c.Recursos.Atribuir(escavacao, [new AtribuicaoDeRecurso(pedreiro.Id, 2), new AtribuicaoDeRecurso(cimento.Id, 10)]);
        c.Recursos.Atribuir(formas, [new AtribuicaoDeRecurso(pedreiro.Id, 1.5m), new AtribuicaoDeRecurso(betoneira.Id, 1)]);
        escavacao.AtualizarPercentualConcluido(30);
        CalculadoraCronograma.Calcular(c);

        var dia = RegrasDoDiario.Sugerir(c, D29);

        dia.Atividades.Should().Equal(new AtividadeNoDia(escavacao.Id, "1 Escavação", 30), new AtividadeNoDia(formas.Id, "2 Formas", 0));
        dia.Efetivo.Should().Equal([new EfetivoNoDia("Pedreiro", 4, "Empreiteira X")], "2 + 1,5 por dia, arredondado para cima; material não entra");
        dia.Equipamentos.Should().Equal(new EquipamentoNoDia("Betoneira", 1));
        (dia.Manha, dia.Tarde).Should().Be((ClimaDoPeriodo.Bom, ClimaDoPeriodo.Bom));
        RegrasDoDiario.Sugerir(c, D30).Atividades.Select(a => a.Descricao).Should().Equal("1 Escavação");

        c.DefinirCalendario(c.Calendario.ComExcecao(new ExcecaoDoCalendario(D01, D01, "Chuva forte", false)));
        var parado = RegrasDoDiario.Sugerir(c, D01);
        (parado.Impraticavel, parado.Manha.Tempo, parado.Ocorrencias).Should().Be((true, TempoNoPeriodo.Chuva, "Sem trabalho no calendário: Chuva forte."));
    }

    [Fact]
    public void LevarOPercentual_EMarcarSemTrabalho()
    {
        var c = new Cronograma("Obra", Datas.Segunda);
        var escavacao = new Tarefa("Escavação", Datas.Segunda, 3);
        c.AdicionarTarefa(escavacao);
        CalculadoraCronograma.Calcular(c);
        c.Diario.Abrir(new DiaDaObra(D28) { Atividades = [new AtividadeNoDia(escavacao.Id, "1 Escavação", 40), new AtividadeNoDia(null, "Limpeza", 100)] });
        c.Diario.Abrir(new DiaDaObra(D29) { Manha = new ClimaDoPeriodo(TempoNoPeriodo.Chuva, false), Tarde = new ClimaDoPeriodo(TempoNoPeriodo.Chuva, false) });

        RegrasDoDiario.LevarOPercentual(c, D28).Should().Equal("1 Escavação: 0% → 40%");
        escavacao.PercentualConcluido.Should().Be(40);
        RegrasDoDiario.LevarOPercentual(c, D28).Should().BeEmpty("já está no cronograma");

        RegrasDoDiario.MarcarSemTrabalho(c, D29);
        c.Calendario.EhDiaUtil(D29).Should().BeFalse();
        c.Calendario.MotivoSemTrabalho(D29).Should().Be("Chuva (RDO nº 2)");
        FluentActions.Invoking(() => RegrasDoDiario.MarcarSemTrabalho(c, D29)).Should().Throw<DomainValidationException>()
            .WithMessage("29/09/2026 já não é dia de trabalho no calendário.");
    }
}
