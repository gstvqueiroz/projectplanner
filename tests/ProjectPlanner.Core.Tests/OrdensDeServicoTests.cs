using ProjectPlanner.Core.OrdensDeServico;
using ProjectPlanner.Core.Orcamento;

namespace ProjectPlanner.Core.Tests;

// Bloco 58: a ordem de serviço: os dados, o fluxo (emitir, aceitar, recusar, iniciar, concluir, reprovar, conferir, cancelar),
// a lista do projeto e o rascunho montado das tarefas e do orçamento.
public class OrdensDeServicoTests
{
    private static readonly DateTime Seg = new(2026, 9, 28);
    private static readonly DateTime Agora = new(2026, 9, 28, 10, 0, 0);

    private static DadosDaOrdem Dados(string? executante = "Construtora Silva", int itens = 1, DateOnly? prazo = null) =>
        new("Alvenaria do térreo", TipoDeExecutante.Terceiro, executante, null, "11 99999-0000", null,
            Enumerable.Range(1, itens).Select(i => new ItemDaOrdem(null, $"Item {i}", "m²", 10, 25.5m)),
            new DateOnly(2026, 10, 1), prazo ?? new DateOnly(2026, 10, 10), FormaDePagamentoDaOrdem.Empreitada, null,
            [new ItemDeVerificacao("Limpeza feita")]);

    private static OrdemDeServico Nova(DadosDaOrdem? dados = null) => OrdemDeServico.Nova(Guid.NewGuid(), 1, dados ?? Dados(), Agora);

    [Fact]
    public void Item_TotalArredondado_EValidacoes()
    {
        new ItemDaOrdem(null, " Reboco ", " m² ", 3.333m, 10.005m).Should().Match<ItemDaOrdem>(i =>
            i.Descricao == "Reboco" && i.Unidade == "m²" && i.PrecoUnitario == 10.01m && i.Total == 33.36m);
        FluentActions.Invoking(() => new ItemDaOrdem(null, "", "m²", 1, 1)).Should().Throw<DomainValidationException>();
        FluentActions.Invoking(() => new ItemDaOrdem(null, "x", "", 1, 1)).Should().Throw<DomainValidationException>().WithMessage("*unidade*");
        FluentActions.Invoking(() => new ItemDaOrdem(null, "x", "m", 0, 1)).Should().Throw<DomainValidationException>().WithMessage("*quantidade*");
        FluentActions.Invoking(() => new ItemDaOrdem(null, "x", "m", 1, -1)).Should().Throw<DomainValidationException>().WithMessage("*negativo*");
    }

    [Fact]
    public void Dados_TituloObrigatorio_PrazoDepoisDoInicio_ETotal()
    {
        Dados(itens: 3).Total.Should().Be(765);
        FluentActions.Invoking(() => new DadosDaOrdem(" ", TipoDeExecutante.Terceiro, null, null, null, null, null, null, null,
            FormaDePagamentoDaOrdem.Empreitada, null, null)).Should().Throw<DomainValidationException>().WithMessage("*título*");
        FluentActions.Invoking(() => Dados(prazo: new DateOnly(2026, 9, 1))).Should().Throw<DomainValidationException>().WithMessage("*prazo*");
    }

    [Fact]
    public void Fluxo_DoRascunhoAPaga_ComOHistorico()
    {
        var os = Nova();
        (os.Codigo, os.Situacao, os.PodeEditar).Should().Be(("OS-001", SituacaoDaOrdem.Rascunho, true));

        os = os.Emitir(Agora.AddHours(1));
        (os.Situacao, os.Emissao, os.PodeEditar).Should().Be((SituacaoDaOrdem.Emitida, new DateOnly(2026, 9, 28), false));
        os.Invoking(o => o.Editar(Dados())).Should().Throw<DomainValidationException>().WithMessage("Não dá para editar a OS-001: ela está \"emitida\".");

        os = os.Aceitar(Agora.AddHours(2), "João").Iniciar(Agora.AddDays(1)).MarcarVerificacao(0, true).Concluir(new DateTime(2026, 10, 9, 17, 0, 0));
        (os.Situacao, os.Conclusao, os.Dados.Verificacao[0].Feito).Should().Be((SituacaoDaOrdem.Concluida, new DateOnly(2026, 10, 9), true));

        os = os.Reprovar(Agora.AddDays(12), "Falta o rejunte");
        (os.Situacao, os.Conclusao).Should().Be((SituacaoDaOrdem.EmExecucao, (DateOnly?)null));
        os = os.Concluir(Agora.AddDays(13)).Conferir(Agora.AddDays(14), "Eng. Ana").MarcarComoPaga(Agora.AddDays(20));

        os.Situacao.Should().Be(SituacaoDaOrdem.Paga);
        os.Historico.Select(e => (e.Situacao, e.Descricao, e.Autor)).Should().Equal(
            (SituacaoDaOrdem.Rascunho, "Criada", null), (SituacaoDaOrdem.Emitida, "Emitida", null), (SituacaoDaOrdem.Aceita, "Aceita", "João"),
            (SituacaoDaOrdem.EmExecucao, "Iniciada", null), (SituacaoDaOrdem.Concluida, "Concluída", null),
            (SituacaoDaOrdem.EmExecucao, "Reprovada na conferência: Falta o rejunte", null), (SituacaoDaOrdem.Concluida, "Concluída", null),
            (SituacaoDaOrdem.Conferida, "Conferida e aprovada", "Eng. Ana"), (SituacaoDaOrdem.Paga, "Paga", null));
        os.PodeIrPara(SituacaoDaOrdem.Cancelada).Should().BeFalse("paga não se cancela");
    }

    [Fact]
    public void Emitir_PedeExecutanteEItens()
    {
        Nova(Dados(executante: null)).Invoking(o => o.Emitir(Agora)).Should().Throw<DomainValidationException>().WithMessage("*executante*");
        Nova(Dados(itens: 0)).Invoking(o => o.Emitir(Agora)).Should().Throw<DomainValidationException>().WithMessage("*pelo menos um item*");
    }

    [Fact]
    public void Recusada_VoltaARascunho_EIniciarDeEmitidaValeComoAceita()
    {
        var recusada = Nova().Emitir(Agora).Recusar(Agora, "  preço baixo ", "Zé");
        (recusada.Situacao, recusada.Historico[^1].Descricao, recusada.Historico[^1].Autor).Should().Be((SituacaoDaOrdem.Recusada, "Recusada: preço baixo", "Zé"));
        var corrigida = recusada.VoltarARascunho(Agora);
        (corrigida.Situacao, corrigida.Emissao, corrigida.PodeEditar).Should().Be((SituacaoDaOrdem.Rascunho, (DateOnly?)null, true));

        Nova().Emitir(Agora).Iniciar(Agora).Historico[^1].Descricao.Should().Be("Aceita e iniciada");
        Nova().Invoking(o => o.Concluir(Agora)).Should().Throw<DomainValidationException>();
        Nova().Emitir(Agora).Cancelar(Agora, null).Situacao.Should().Be(SituacaoDaOrdem.Cancelada);
    }

    [Fact]
    public void Vencida_SoEmAndamentoDepoisDoPrazo()
    {
        var hoje = new DateOnly(2026, 10, 11);
        Nova().Vencida(hoje).Should().BeFalse("rascunho não vence");
        Nova().Emitir(Agora).Vencida(hoje).Should().BeTrue();
        Nova().Emitir(Agora).Vencida(new DateOnly(2026, 10, 10)).Should().BeFalse("vence depois do dia do prazo");
        Nova().Emitir(Agora).Iniciar(Agora).Concluir(Agora).Vencida(hoje).Should().BeFalse("concluída espera a conferência");
    }

    [Fact]
    public void Lista_NumeroSeguinte_ExcluirSoORascunho_PeloCodigo_EDaTarefa_ECopia()
    {
        var c = new Cronograma("Obra", Seg);
        var a = new Tarefa("Alvenaria", Seg, 5);
        c.AdicionarTarefa(a);
        var lista = c.OrdensDeServico;
        lista.ProximoNumero.Should().Be(1);
        var primeira = OrdemDeServico.Nova(Guid.NewGuid(), lista.ProximoNumero, Dados(), Agora);
        lista.Adicionar(primeira);
        var dados = new DadosDaOrdem("Alvenaria", TipoDeExecutante.EquipePropria, "Equipe", null, null, [a.Id], [new ItemDaOrdem(a.Id, "Alvenaria", "m²", 1, 1)],
            null, null, FormaDePagamentoDaOrdem.Diaria, null, null);
        var segunda = OrdemDeServico.Nova(Guid.NewGuid(), lista.ProximoNumero, dados, Agora).Emitir(Agora);
        lista.Adicionar(segunda);

        (lista.ProximoNumero, lista.PeloCodigo("os-2")?.Id, lista.PeloCodigo("OS-002")?.Id, lista.PeloCodigo("7")).Should().Be((3, segunda.Id, segunda.Id, null));
        lista.DaTarefa(a.Id).Should().Equal(segunda);
        lista.Invoking(l => l.Remover(segunda.Id)).Should().Throw<DomainValidationException>().WithMessage("*cancele*");
        lista.Invoking(l => l.Adicionar(OrdemDeServico.Nova(Guid.NewGuid(), 2, Dados(), Agora))).Should().Throw<DomainValidationException>().WithMessage("Já existe a OS-002.");

        var copia = c.Copiar();
        lista.Remover(primeira.Id);
        lista.Substituir(segunda.Cancelar(Agora, "x"));
        (lista.Todas.Count, lista.DaTarefa(a.Id).Count, lista.ProximoNumero).Should().Be((1, 0, 3));
        copia.OrdensDeServico.Todas.Should().Equal(primeira, segunda);
    }

    [Fact]
    public void Montagem_DaEtapa_ItensComQuantidadeEPrecoDaComposicao_EDatasDoCronograma()
    {
        var c = new Cronograma("Obra", Seg);
        var etapa = new Tarefa("Alvenaria do térreo", Seg, 1);
        c.AdicionarTarefa(etapa);
        var parede = new Tarefa("Parede de blocos", Seg, 5);
        var verga = new Tarefa("Vergas", Seg, 2);
        var limpeza = new Tarefa("Limpeza", Seg, 1);
        foreach (var t in new[] { parede, verga, limpeza })
            c.AdicionarTarefa(t, etapa.Id);
        c.AdicionarPredecessora(new Predecessora(parede.Id, verga.Id));
        parede.DefinirUnidade("m²");
        parede.DefinirQuantidade(120);
        c.Orcamento.Associar(parede, ComposicaoAberta.Estimativa("Alvenaria", "m²", 40, 30));
        c.Orcamento.Associar(verga, ComposicaoAberta.Estimativa("Verga", "m", 20, 15));
        c.Orcamento.EstimarQuantidade(verga, 18, "m");
        limpeza.DefinirValor(500);
        CalculadoraCronograma.Calcular(c);

        var soMaoDeObra = MontagemDaOrdem.Rascunho(c, [etapa.Id], PrecoDosItens.MaoDeObra);
        var completo = MontagemDaOrdem.Rascunho(c, [parede.Id, verga.Id], PrecoDosItens.ServicoCompleto);

        soMaoDeObra.Titulo.Should().Be("Alvenaria do térreo");
        soMaoDeObra.Tarefas.Should().Equal(parede.Id, verga.Id, limpeza.Id);
        soMaoDeObra.Itens.Select(i => (i.TarefaId, i.Descricao, i.Unidade, i.Quantidade, i.PrecoUnitario)).Should().Equal(
            (parede.Id, "Parede de blocos", "m²", 120m, 30m), (verga.Id, "Vergas", "m", 18m, 15m), (limpeza.Id, "Limpeza", "vb", 1m, 500m));
        (soMaoDeObra.InicioPrevisto, soMaoDeObra.Prazo).Should().Be((new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 6)));
        completo.Titulo.Should().Be("Parede de blocos e mais 1");
        completo.Itens.Select(i => i.PrecoUnitario).Should().Equal(70m, 35m);
        FluentActions.Invoking(() => MontagemDaOrdem.Rascunho(c, [], PrecoDosItens.MaoDeObra)).Should().Throw<DomainValidationException>();
    }
}
