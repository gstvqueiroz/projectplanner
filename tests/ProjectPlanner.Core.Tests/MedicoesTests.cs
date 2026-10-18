using ProjectPlanner.Core.Edicao;
using ProjectPlanner.Core.Medicoes;
using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.Core.Tests;

// Bloco 36: contrato, medições, boletim, retenção, reajuste, glosas, aditivos e plano de medições. Projeto na seg 28/09/2026:
// Fundação (resumo) > Escavação (100 m³, R$ 5.000: R$ 50/m³; 28/09–02/10) → Sapatas (sem quantidade, R$ 10.000: medida em %;
// 05/10–16/10); Estrutura (40 m³, R$ 20.000: R$ 500/m³; 19/10–30/10); Entrega (marco, sem valor).
public class MedicoesTests
{
    private static readonly DateTime Seg = new(2026, 9, 28);
    private static readonly DateTime Agora = new(2026, 10, 20, 9, 0, 0);

    private static DateOnly D(int dia, int mes = 10) => new(2026, mes, dia);

    private static (Cronograma C, Dictionary<string, Tarefa> T) Obra()
    {
        var c = new Cronograma("Residência", Seg);
        var t = new Dictionary<string, Tarefa>
        {
            ["Fundação"] = new("Fundação", Seg, 1),
            ["Escavação"] = new("Escavação", Seg, 5),
            ["Sapatas"] = new("Sapatas", Seg, 10),
            ["Estrutura"] = new("Estrutura", Seg, 10),
            ["Entrega"] = new("Entrega", Seg, 0),
        };
        c.AdicionarTarefa(t["Fundação"]);
        c.AdicionarTarefa(t["Escavação"], t["Fundação"].Id);
        c.AdicionarTarefa(t["Sapatas"], t["Fundação"].Id);
        c.AdicionarTarefa(t["Estrutura"]);
        c.AdicionarTarefa(t["Entrega"]);
        c.AdicionarPredecessora(new Predecessora(t["Escavação"].Id, t["Sapatas"].Id));
        c.AdicionarPredecessora(new Predecessora(t["Sapatas"].Id, t["Estrutura"].Id));
        c.AdicionarPredecessora(new Predecessora(t["Estrutura"].Id, t["Entrega"].Id));
        t["Escavação"].DefinirValor(5000);
        t["Escavação"].DefinirUnidade("m³");
        t["Escavação"].DefinirQuantidade(100);
        t["Sapatas"].DefinirValor(10000);
        t["Estrutura"].DefinirValor(20000);
        t["Estrutura"].DefinirUnidade("m³");
        t["Estrutura"].DefinirQuantidade(40);
        CalculadoraCronograma.Calcular(c);
        return (c, t);
    }

    // A medição nº 1 (28/09 a 16/10): 40 m³ de escavação (R$ 2.000) e 30% das sapatas (R$ 3.000).
    private static (Cronograma C, Dictionary<string, Tarefa> T) ComPrimeiraMedicao(decimal retencao = 0)
    {
        var (c, t) = Obra();
        c.Contrato.DefinirConfiguracao(new ConfiguracaoDoContrato { PercentualDeRetencao = retencao });
        c.Contrato.NovaMedicao(D(28, 9), D(16));
        c.Contrato.Medir(1, t["Escavação"].Id, 40);
        c.Contrato.Medir(1, t["Sapatas"].Id, 30);
        return (c, t);
    }

    // ---- Unidade e quantidade na tarefa ----

    [Fact]
    public void Tarefa_UnidadeQuantidadeEPrecoUnitario()
    {
        var tarefa = new Tarefa("Alvenaria", Seg, 5);
        tarefa.DefinirValor(1234.5m);
        tarefa.DefinirUnidade("  m² ");
        tarefa.DefinirQuantidade(10.123456m);

        (tarefa.Unidade, tarefa.Quantidade).Should().Be(("m²", 10.1235m));
        tarefa.PrecoUnitario.Should().BeApproximately(121.9440m, 0.0001m);
        tarefa.DefinirUnidade(" ");
        tarefa.DefinirQuantidade(null);
        (tarefa.Unidade, tarefa.Quantidade, tarefa.PrecoUnitario).Should().Be((null, null, null));
    }

    [Fact]
    public void Tarefa_QuantidadeEUnidadeInvalidas_ENoResumoSomem()
    {
        var c = new Cronograma("P", Seg);
        var etapa = new Tarefa("Etapa", Seg, 2);
        c.AdicionarTarefa(etapa);
        etapa.DefinirUnidade("m");
        etapa.DefinirQuantidade(3);

        ((Action)(() => etapa.DefinirQuantidade(0))).Should().Throw<DomainValidationException>().WithMessage("*maior que zero*");
        ((Action)(() => etapa.DefinirQuantidade(-1))).Should().Throw<DomainValidationException>();
        ((Action)(() => etapa.DefinirUnidade("metro quadrado"))).Should().Throw<DomainValidationException>().WithMessage("*14 caracteres*12*");
        c.AdicionarTarefa(new Tarefa("Sub", Seg, 1), etapa.Id);
        (etapa.Unidade, etapa.Quantidade).Should().Be((null, null), "um resumo não tem unidade nem quantidade");
        ((Action)(() => etapa.DefinirQuantidade(1))).Should().Throw<DomainValidationException>().WithMessage("*resumo*");
    }

    // ---- Itens e configuração ----

    [Fact]
    public void Itens_SaoAsTarefasComValor_SemQuantidadeEmPercentual()
    {
        var (c, _) = Obra();

        c.Contrato.Itens().Select(i => (i.Edt, i.Nome, i.Nivel, i.Unidade, i.PorPercentual, i.QuantidadeContratada, i.ValorContratado)).Should().Equal(
            ("1.1", "Escavação", 1, "m³", false, 100m, 5000m),
            ("1.2", "Sapatas", 1, "%", true, 100m, 10000m),
            ("2", "Estrutura", 0, "m³", false, 40m, 20000m));
        c.Contrato.Itens()[0].PrecoUnitario.Should().Be(50);
        (c.Contrato.ValorContratado, c.Contrato.Vazio, c.Contrato.Travado).Should().Be((35000m, true, false));
    }

    [Fact]
    public void Configuracao_ValidaELimpa()
    {
        var (c, _) = Obra();

        c.Contrato.DefinirConfiguracao(new ConfiguracaoDoContrato
        {
            PercentualDeRetencao = 5, NomeDoIndice = "  INCC-DI ", IndiceBase = 1000, Periodicidade = Periodicidade.DatasDeCorte,
            Cortes = [D(31), D(15), D(31)],
        });

        (c.Contrato.Configuracao.NomeDoIndice, c.Contrato.Vazio).Should().Be(("INCC-DI", false));
        c.Contrato.Configuracao.Cortes.Should().Equal(D(15), D(31));
        ((Action)(() => c.Contrato.DefinirConfiguracao(new ConfiguracaoDoContrato { PercentualDeRetencao = 31 })))
            .Should().Throw<DomainValidationException>().WithMessage("A retenção vai de 0% a 30%.");
        ((Action)(() => c.Contrato.DefinirConfiguracao(new ConfiguracaoDoContrato { IndiceBase = 0 })))
            .Should().Throw<DomainValidationException>().WithMessage("O índice base precisa ser maior que zero.");
        c.Contrato.Configuracao.PercentualDeRetencao.Should().Be(5, "a recusada não muda nada");
    }

    // ---- Períodos ----

    [Fact]
    public void ProximoPeriodo_PeloPlano_EDepoisDaUltima()
    {
        var (c, t) = Obra();
        c.Contrato.ProximoPeriodo().Should().Be((D(28, 9), D(30, 9)), "mensal: do início do projeto ao fim do mês");

        c.Contrato.DefinirConfiguracao(new ConfiguracaoDoContrato { Periodicidade = Periodicidade.Quinzenal });
        c.Contrato.ProximoPeriodo().Should().Be((D(28, 9), D(30, 9)));
        c.Contrato.NovaMedicao(D(28, 9), D(30, 9));
        c.Contrato.Medir(1, t["Escavação"].Id, 10);
        c.Contrato.Aprovar(1, Agora);
        c.Contrato.ProximoPeriodo().Should().Be((D(1), D(15)));

        c.Contrato.DefinirConfiguracao(new ConfiguracaoDoContrato { Periodicidade = Periodicidade.DatasDeCorte, Cortes = [D(9), D(20)] });
        c.Contrato.ProximoPeriodo().Should().Be((D(1), D(9)));
    }

    [Fact]
    public void NovaMedicao_SoDepoisDeAprovarAAnterior_ESemSobreporOPeriodo()
    {
        var (c, t) = ComPrimeiraMedicao();

        ((Action)(() => c.Contrato.NovaMedicao(D(17), D(31)))).Should().Throw<DomainValidationException>()
            .WithMessage("Aprove a medição nº 01 antes de abrir outra.");
        c.Contrato.Aprovar(1, Agora);
        ((Action)(() => c.Contrato.NovaMedicao(D(16), D(31)))).Should().Throw<DomainValidationException>()
            .WithMessage("A medição nº 02 precisa começar depois do fim da nº 01 (16/10/2026).");
        ((Action)(() => c.Contrato.NovaMedicao(D(31), D(17)))).Should().Throw<DomainValidationException>().WithMessage("*termina antes de começar*");

        var segunda = c.Contrato.NovaMedicao(D(17), D(31));
        (segunda.Numero, segunda.Rotulo, segunda.Periodo, segunda.TextoDaSituacao).Should().Be((2, "Medição nº 02", "17/10/2026 a 31/10/2026", "Rascunho"));
        c.Contrato.DefinirPeriodo(2, D(17), D(30));
        c.Contrato.ObterMedicao(2).Ate.Should().Be(D(30));
        ((Action)(() => c.Contrato.DefinirPeriodo(1, D(1), D(2)))).Should().Throw<DomainValidationException>().WithMessage("*aprovada e não muda mais*");
        ((Action)(() => c.Contrato.ObterMedicao(9))).Should().Throw<DomainValidationException>().WithMessage("Não existe a medição nº 09.");
    }

    // ---- Medir e o boletim ----

    [Fact]
    public void Boletim_GruposEItens_ValoresEPercentuais()
    {
        var (c, _) = ComPrimeiraMedicao(retencao: 5);

        var boletim = c.Contrato.Boletim(1);

        boletim.Linhas.Select(l => (l.Edt, l.EhGrupo, l.Unidade, l.QuantidadeNoPeriodo, l.ValorContratado, l.ValorNoPeriodo)).Should().Equal(
            ("1", true, "", 0m, 15000m, 5000m),
            ("1.1", false, "m³", 40m, 5000m, 2000m),
            ("1.2", false, "%", 30m, 10000m, 3000m),
            ("2", false, "m³", 0m, 20000m, 0m));
        var escavacao = boletim.Linhas[1];
        (escavacao.PrecoUnitario, escavacao.QuantidadeSaldo, escavacao.ValorSaldo, escavacao.PercentualAcumulado).Should().Be((50m, 60m, 3000m, 40.0));
        var r = boletim.Resumo;
        (r.ValorOriginal, r.ValorContratado, r.MedidoAnterior, r.MedidoNoPeriodo, r.MedidoAcumulado, r.Saldo).Should().Be((35000m, 35000m, 0m, 5000m, 5000m, 30000m));
        (r.Bruto, r.Retencao, r.Liquido, r.RetidoAcumulado, r.Reajuste, r.DescricaoDoReajuste).Should().Be((5000m, 250m, 4750m, 250m, 0m, null));
        r.PercentualMedido.Should().BeApproximately(14.2857, 0.0001);
        r.Fisico.Should().Be(0, "nenhuma tarefa tem % concluído");
    }

    [Fact]
    public void Medir_AcimaDoSaldo_Negativo_ForaDoContrato_EZeroTira()
    {
        var (c, t) = ComPrimeiraMedicao();

        ((Action)(() => c.Contrato.Medir(1, t["Escavação"].Id, 100.5m))).Should().Throw<DomainValidationException>()
            .WithMessage("\"Escavação\": 100,50 m³ passa do saldo do contrato (100,00 m³). Para medir mais, registre um aditivo.");
        ((Action)(() => c.Contrato.Medir(1, t["Sapatas"].Id, 101))).Should().Throw<DomainValidationException>()
            .WithMessage("\"Sapatas\": 101% passa do saldo do contrato (100%).*");
        ((Action)(() => c.Contrato.Medir(1, t["Sapatas"].Id, -1))).Should().Throw<DomainValidationException>().WithMessage("O medido não pode ser negativo.");
        ((Action)(() => c.Contrato.Medir(1, t["Entrega"].Id, 1))).Should().Throw<DomainValidationException>().WithMessage("\"Entrega\" não tem Valor, então não é item do contrato*"); // 0.41.2: a mensagem diz como resolver
        ((Action)(() => c.Contrato.Medir(1, t["Fundação"].Id, 1))).Should().Throw<DomainValidationException>();

        c.Contrato.Medir(1, t["Sapatas"].Id, 0);
        c.Contrato.Medir(1, t["Escavação"].Id, 12.34567m);
        c.Contrato.ObterMedicao(1).Itens.Should().Equal(new ItemMedido(t["Escavação"].Id, 12.3457m));
    }

    [Fact]
    public void Aprovar_TravaOBoletim_EOContrato()
    {
        var (c, t) = ComPrimeiraMedicao();

        var aprovado = c.Contrato.Aprovar(1, Agora);

        (aprovado.Medicao.Situacao, aprovado.Medicao.TextoDaSituacao, c.Contrato.Travado, c.Contrato.MedidoAprovado)
            .Should().Be((SituacaoDaMedicao.Aprovada, "Aprovada em 20/10/2026", true, 5000m));
        ((Action)(() => c.Contrato.Medir(1, t["Escavação"].Id, 1))).Should().Throw<DomainValidationException>()
            .WithMessage("A medição nº 01 está aprovada e não muda mais. Para corrigir, reabra-a.");
        ((Action)(() => c.Contrato.VerificarSePodeMudar(t["Escavação"]))).Should().Throw<DomainValidationException>()
            .WithMessage(ContratoDoProjeto.MotivoDoTravamento);
        c.Contrato.VerificarSePodeMudar(t["Entrega"], soAUnidade: true); // ainda não é item: a unidade pode mudar
        ((Action)(() => c.Contrato.VerificarSePodeMudar(t["Entrega"]))).Should().Throw<DomainValidationException>();

        // Mudar a tarefa depois não muda o boletim aprovado.
        t["Escavação"].DefinirValor(9999);
        c.Contrato.Boletim(1).Linhas[1].ValorNoPeriodo.Should().Be(2000);
        c.Contrato.Boletim(1).Resumo.MedidoNoPeriodo.Should().Be(5000);
    }

    [Fact]
    public void Aprovar_SemNadaMedido_OuAcimaDoContratado_ERecusado()
    {
        var (c, t) = Obra();
        c.Contrato.NovaMedicao(D(28, 9), D(16));
        ((Action)(() => c.Contrato.Aprovar(1, Agora))).Should().Throw<DomainValidationException>().WithMessage("A medição não tem nada medido*");

        c.Contrato.Medir(1, t["Escavação"].Id, 80);
        t["Escavação"].DefinirQuantidade(50); // o contrato mudou antes da aprovação (ainda não travado)
        ((Action)(() => c.Contrato.Aprovar(1, Agora))).Should().Throw<DomainValidationException>()
            .WithMessage("O medido passa do contratado em 1.1 Escavação. Ajuste as quantidades ou registre um aditivo.");
        c.Contrato.Travado.Should().BeFalse();
    }

    [Fact]
    public void SegundaMedicao_AnteriorAcumuladoESaldo_EFechaOItemSemSobraDeCentavos()
    {
        var (c, t) = ComPrimeiraMedicao();
        t["Escavação"].DefinirValor(1000);
        t["Escavação"].DefinirQuantidade(3); // R$ 333,33… por m³
        c.Contrato.Medir(1, t["Escavação"].Id, 1);
        c.Contrato.Boletim(1).Linhas[1].ValorNoPeriodo.Should().Be(333.33m);
        c.Contrato.Aprovar(1, Agora);
        c.Contrato.NovaMedicao(D(17), D(31));

        c.Contrato.Medir(2, t["Escavação"].Id, 2);
        c.Contrato.Medir(2, t["Sapatas"].Id, 70);

        var linhas = c.Contrato.Boletim(2).Itens.ToList();
        (linhas[0].QuantidadeAnterior, linhas[0].ValorAnterior, linhas[0].ValorNoPeriodo, linhas[0].QuantidadeSaldo, linhas[0].ValorSaldo)
            .Should().Be((1m, 333.33m, 666.67m, 0m, 0m), "a última medição leva o que falta do valor");
        (linhas[1].QuantidadeAnterior, linhas[1].ValorNoPeriodo, linhas[1].PercentualAcumulado).Should().Be((30m, 7000m, 100.0));
        ((Action)(() => c.Contrato.Medir(2, t["Sapatas"].Id, 71))).Should().Throw<DomainValidationException>().WithMessage("*saldo do contrato (70%)*");
        c.Contrato.Boletim(2).Resumo.MedidoAnterior.Should().Be(3333.33m);
    }

    [Fact]
    public void SugerirPeloPercentualConcluido_DescontaOJaMedido()
    {
        var (c, t) = ComPrimeiraMedicao();
        c.Contrato.Aprovar(1, Agora);
        c.Contrato.NovaMedicao(D(17), D(31));
        t["Escavação"].AtualizarPercentualConcluido(55);
        t["Sapatas"].AtualizarPercentualConcluido(20); // abaixo do já medido (30%): nada a medir
        t["Estrutura"].AtualizarPercentualConcluido(100);

        c.Contrato.SugerirPeloPercentualConcluido(2).Should().Be(2);

        c.Contrato.ObterMedicao(2).Itens.Should().BeEquivalentTo([new ItemMedido(t["Escavação"].Id, 15), new ItemMedido(t["Estrutura"].Id, 40)]);
    }

    // ---- Retenção, reajuste e glosas ----

    [Fact]
    public void ReajusteGlosasERetencao_NoValorAFaturar()
    {
        var (c, _) = ComPrimeiraMedicao();
        c.Contrato.DefinirConfiguracao(new ConfiguracaoDoContrato { PercentualDeRetencao = 5, NomeDoIndice = "INCC-DI", IndiceBase = 1000 });
        c.Contrato.DefinirIndice(1, 1100);
        c.Contrato.DefinirGlosas(1, [new GlosaDaMedicao("Limpeza do terreno não feita", 100)]);

        var r = c.Contrato.Boletim(1).Resumo;

        (r.Reajuste, r.DescricaoDoReajuste).Should().Be((500m, "INCC-DI: 1.100 ÷ 1.000 = 1,1000"));
        (r.Glosas, r.Bruto, r.Retencao, r.Liquido).Should().Be((100m, 5400m, 270m, 5130m));
        ((Action)(() => c.Contrato.DefinirIndice(1, 0))).Should().Throw<DomainValidationException>();
        ((Action)(() => new GlosaDaMedicao(" ", 10))).Should().Throw<DomainValidationException>().WithMessage("Escreva o motivo da glosa.");
        ((Action)(() => new GlosaDaMedicao("x", 0))).Should().Throw<DomainValidationException>();

        c.Contrato.DefinirGlosas(1, [new GlosaDaMedicao("Tudo", 6000)]);
        ((Action)(() => c.Contrato.Aprovar(1, Agora))).Should().Throw<DomainValidationException>()
            .WithMessage("As glosas (R$ 6.000,00) passam do valor medido com o reajuste (R$ 5.500,00).");
    }

    [Fact]
    public void DevolucaoDaRetencao_NaUltima_EAprovaMesmoSemMedido()
    {
        var (c, t) = ComPrimeiraMedicao(retencao: 10);
        c.Contrato.Aprovar(1, Agora);
        c.Contrato.NovaMedicao(D(17), D(31));
        c.Contrato.Medir(2, t["Escavação"].Id, 60);
        c.Contrato.Boletim(2).Resumo.RetidoAnterior.Should().Be(500);
        c.Contrato.Aprovar(2, Agora); // R$ 3.000, retém 300: retido 800
        c.Contrato.NovaMedicao(D(1, 11), D(5, 11));
        c.Contrato.DefinirDevolucaoDaRetencao(3, true);

        var r = c.Contrato.Aprovar(3, Agora).Resumo;

        (r.MedidoNoPeriodo, r.Retencao, r.Devolucao, r.Liquido, r.RetidoAcumulado).Should().Be((0m, 0m, 800m, 800m, 0m));
    }

    // ---- Situação: reabrir, pagar e remover ----

    [Fact]
    public void Reabrir_Pagar_ERemover()
    {
        var (c, t) = ComPrimeiraMedicao();
        c.Contrato.Aprovar(1, Agora);
        c.Contrato.MarcarPaga(1, D(5, 11));
        c.Contrato.ObterMedicao(1).TextoDaSituacao.Should().Be("Paga em 05/11/2026");
        ((Action)(() => c.Contrato.Reabrir(1))).Should().Throw<DomainValidationException>().WithMessage("*desmarque o pagamento*");
        ((Action)(() => c.Contrato.MarcarPaga(1, D(6, 11)))).Should().Throw<DomainValidationException>().WithMessage("*já está paga*");

        c.Contrato.DesmarcarPaga(1);
        c.Contrato.NovaMedicao(D(17), D(31));
        ((Action)(() => c.Contrato.Reabrir(1))).Should().Throw<DomainValidationException>().WithMessage("Só a última medição pode voltar a rascunho.");
        ((Action)(() => c.Contrato.RemoverMedicao(1))).Should().Throw<DomainValidationException>().WithMessage("*reabra*");
        ((Action)(() => c.Contrato.MarcarPaga(2, D(1, 11)))).Should().Throw<DomainValidationException>().WithMessage("Aprove a medição antes*");
        c.Contrato.RemoverMedicao(2);

        c.Contrato.Reabrir(1);
        var reaberta = c.Contrato.ObterMedicao(1);
        (reaberta.Situacao, reaberta.Linhas, reaberta.Resumo, reaberta.Itens.Count, c.Contrato.Travado).Should().Be((SituacaoDaMedicao.Rascunho, null, null, 2, false));
        ((Action)(() => c.Contrato.Reabrir(1))).Should().Throw<DomainValidationException>().WithMessage("A medição nº 01 já está em rascunho.");
        c.Contrato.RemoverMedicao(1);
        c.Contrato.Medicoes.Should().BeEmpty();
        t["Escavação"].Valor.Should().Be(5000);
    }

    [Fact]
    public void SoAUltimaVoltaARascunho()
    {
        var (c, t) = ComPrimeiraMedicao();
        c.Contrato.Aprovar(1, Agora);
        c.Contrato.NovaMedicao(D(17), D(31));
        c.Contrato.Medir(2, t["Estrutura"].Id, 1);
        c.Contrato.Aprovar(2, Agora);

        ((Action)(() => c.Contrato.Reabrir(1))).Should().Throw<DomainValidationException>().WithMessage("Só a última medição pode voltar a rascunho.");
    }

    // ---- Aditivos ----

    [Fact]
    public void Aditivo_AcrescimoEItemNovo_MudamAsTarefas_EOOriginalFica()
    {
        var (c, t) = ComPrimeiraMedicao();
        c.Contrato.Aprovar(1, Agora);

        var aditivo = c.Contrato.RegistrarAditivo(D(25), " Ampliação da escavação ", [
            new AlteracaoDoAditivo(t["Escavação"].Id, 20, 1000),
            new AlteracaoDoAditivo(t["Entrega"].Id, 0, 500),
        ]);

        (aditivo.Numero, aditivo.Valor, aditivo.Rotulo).Should().Be((1, 1500m, "Aditivo nº 1 · 25/10/2026 · Ampliação da escavação · + R$ 1.500,00"));
        (t["Escavação"].Quantidade, t["Escavação"].Valor, t["Entrega"].Valor).Should().Be((120m, 6000m, 500m));
        c.Contrato.NovaMedicao(D(17), D(31));
        var r = c.Contrato.Boletim(2).Resumo;
        (r.ValorOriginal, r.ValorDosAditivos, r.QuantidadeDeAditivos, r.ValorContratado).Should().Be((35000m, 1500m, 1, 36500m));
        c.Contrato.Itens().Select(i => i.Nome).Should().Contain("Entrega", "o item novo entra no contrato, medido em %");
    }

    [Fact]
    public void Aditivo_SupressaoAbaixoDoMedido_QuantidadeEmItemPercentual_ESemMudanca_SaoRecusados()
    {
        var (c, t) = ComPrimeiraMedicao();
        c.Contrato.Aprovar(1, Agora); // 40 m³ e R$ 2.000 de escavação; 30% (R$ 3.000) das sapatas

        ((Action)(() => c.Contrato.RegistrarAditivo(D(25), "Redução", [new AlteracaoDoAditivo(t["Escavação"].Id, -70, 0)])))
            .Should().Throw<DomainValidationException>().WithMessage("\"Escavação\": o aditivo deixa a quantidade em 30,00, menos do que já foi medido (40,00).");
        ((Action)(() => c.Contrato.RegistrarAditivo(D(25), "Redução", [new AlteracaoDoAditivo(t["Sapatas"].Id, 0, -7500)])))
            .Should().Throw<DomainValidationException>().WithMessage("*R$ 2.500,00, menos do que já foi medido (R$ 3.000,00)*");
        ((Action)(() => c.Contrato.RegistrarAditivo(D(25), "Quantidade", [new AlteracaoDoAditivo(t["Sapatas"].Id, 10, 0)])))
            .Should().Throw<DomainValidationException>().WithMessage("*é medida em % (sem quantidade)*");
        ((Action)(() => c.Contrato.RegistrarAditivo(D(25), "Nada", [new AlteracaoDoAditivo(t["Sapatas"].Id, 0, 0)])))
            .Should().Throw<DomainValidationException>().WithMessage("O aditivo não muda nenhum item*");
        ((Action)(() => c.Contrato.RegistrarAditivo(D(25), " ", [new AlteracaoDoAditivo(t["Sapatas"].Id, 0, 1)])))
            .Should().Throw<DomainValidationException>().WithMessage("Escreva o motivo do aditivo.");
        ((Action)(() => c.Contrato.RegistrarAditivo(D(25), "Resumo", [new AlteracaoDoAditivo(t["Fundação"].Id, 0, 1)])))
            .Should().Throw<DomainValidationException>().WithMessage("*é um resumo*");
        // Uma recusa no segundo item não muda o primeiro.
        ((Action)(() => c.Contrato.RegistrarAditivo(D(25), "Dois", [
                new AlteracaoDoAditivo(t["Estrutura"].Id, 0, 100), new AlteracaoDoAditivo(t["Sapatas"].Id, 0, -9000)])))
            .Should().Throw<DomainValidationException>();
        (t["Estrutura"].Valor, c.Contrato.Aditivos.Count).Should().Be((20000m, 0));
    }

    [Fact]
    public void RemoverAditivo_SoOUltimo_EDesfazAsMudancas()
    {
        var (c, t) = Obra();
        c.Contrato.RegistrarAditivo(D(1), "Primeiro", [new AlteracaoDoAditivo(t["Estrutura"].Id, 10, 5000)]);
        c.Contrato.RegistrarAditivo(D(2), "Segundo", [new AlteracaoDoAditivo(t["Estrutura"].Id, -5, -2500)]);

        ((Action)(() => c.Contrato.RemoverAditivo(1))).Should().Throw<DomainValidationException>().WithMessage("Só o último aditivo pode ser removido.");
        c.Contrato.RemoverAditivo(2);
        (t["Estrutura"].Quantidade, t["Estrutura"].Valor).Should().Be((50m, 25000m));
        c.Contrato.RemoverAditivo(1);
        (t["Estrutura"].Quantidade, t["Estrutura"].Valor, c.Contrato.Aditivos.Count).Should().Be((40m, 20000m, 0));
    }

    // ---- Itens que saem do cronograma e cópia ----

    [Fact]
    public void ItemRemovidoDepoisDeMedido_ContinuaNoBoletim()
    {
        var (c, t) = ComPrimeiraMedicao();
        c.Contrato.Aprovar(1, Agora);
        c.RemoverTarefa(t["Fundação"].Id);
        c.Contrato.NovaMedicao(D(17), D(31));

        var linhas = c.Contrato.Boletim(2).Linhas;

        linhas.Select(l => (l.Descricao, l.EhGrupo, l.Removida, l.ValorContratado, l.ValorAnterior)).Should().Equal(
            ("Estrutura", false, false, 20000m, 0m),
            ("Itens que saíram do cronograma", true, true, 0m, 5000m),
            ("Escavação", false, true, 0m, 2000m),
            ("Sapatas", false, true, 0m, 3000m));
        c.Contrato.Boletim(2).Resumo.Saldo.Should().Be(15000);
    }

    [Fact]
    public void Copia_TemOMesmoContrato_MasIndependente()
    {
        var (c, t) = ComPrimeiraMedicao(retencao: 5);
        var copia = c.Copiar();

        copia.Contrato.Aprovar(1, Agora);

        (c.Contrato.Travado, copia.Contrato.Travado, copia.Contrato.Configuracao.PercentualDeRetencao).Should().Be((false, true, 5m));
        copia.Contrato.Itens().Select(i => i.TarefaId).Should().Contain(t["Escavação"].Id);
    }

    [Fact]
    public void Desfazer_VoltaAMedicao()
    {
        var (c, _) = ComPrimeiraMedicao();
        var historico = new HistoricoDeAlteracoes(c);
        c.Contrato.Aprovar(1, Agora);
        historico.Registrar("Aprovar a medição nº 01", c);

        var (antes, _) = historico.Desfazer(c);

        antes.Contrato.ObterMedicao(1).Situacao.Should().Be(SituacaoDaMedicao.Rascunho);
    }

    // ---- Plano, importação e cópia de tarefas ----

    [Fact]
    public void Plano_PrevistoMensalEMedidoPelasAprovadas()
    {
        var (c, _) = ComPrimeiraMedicao();
        c.Contrato.Aprovar(1, Agora);

        var plano = PlanoDeMedicoes.Montar(c);

        plano.Base.Should().Be("Cronograma atual");
        plano.Periodos.Select(p => (p.Periodo.Rotulo, p.Previsto, p.Medido, p.MedidoAcumulado)).Should().Equal(
            ("28/09–30/09", 3000m, 0m, 0m), ("01/10–30/10", 32000m, 5000m, 5000m));
        plano.Periodos[1].Medicoes.Should().Equal(1);
        plano.Periodos[1].Diferenca.Should().Be(-30000);
    }

    [Fact]
    public void ImportarValores_ComUnidadeEQuantidade_ETravadoRecusa()
    {
        var (c, t) = Obra();

        var resultado = ImportacaoDeValores.Aplicar(c, [
            ["Item", "Descrição", "Unid.", "Quant.", "Preço total"],
            ["1.1", "Escavação", "m³", "120,5", "6.025,00"],
            ["1.2", "Sapatas", "", "", "10.000"],
        ]);

        resultado.Resumo.Should().Be("2 valores importados (com unidade e quantidade).");
        (t["Escavação"].Unidade, t["Escavação"].Quantidade, t["Escavação"].Valor).Should().Be(("m³", 120.5m, 6025m));
        (t["Sapatas"].Unidade, t["Sapatas"].Quantidade).Should().Be((null, null));

        c.Contrato.NovaMedicao(D(28, 9), D(2));
        c.Contrato.Medir(1, t["Sapatas"].Id, 1);
        c.Contrato.Aprovar(1, Agora);
        ((Action)(() => ImportacaoDeValores.Aplicar(c, [["1.1", "1"]]))).Should().Throw<DomainValidationException>()
            .WithMessage(ContratoDoProjeto.MotivoDoTravamento);
    }

    [Fact]
    public void ColarTarefas_ComOContratoTravado_VemSemValorNemQuantidade()
    {
        var (c, t) = ComPrimeiraMedicao();
        var trecho = CopiaDeTarefas.Copiar(c, [t["Escavação"].Id]);
        var livre = CopiaDeTarefas.Colar(c, trecho, null, 0)[0];
        (livre.Unidade, livre.Quantidade, livre.Valor).Should().Be(("m³", 100m, 5000m));

        c.Contrato.Aprovar(1, Agora);
        var travada = CopiaDeTarefas.Colar(c, trecho, null, 0)[0];

        (travada.Unidade, travada.Quantidade, travada.Valor).Should().Be(("m³", null, null));
    }

    [Fact]
    public void Restaurar_ValidaComoAsCriadasNoPrograma()
    {
        var resumo = new ResumoDoBoletim(0, 0, 100, 0, 100, 0, null, 0, 0, 0, 0, 0, null);
        var aprovada = Medicao.Restaurar(1, D(1), D(10), SituacaoDaMedicao.Aprovada, [new ItemMedido(Guid.NewGuid(), 1)], [], null, false, "  obs ",
            Agora, D(30), [], resumo);

        (aprovada.Observacoes, aprovada.PagaEm, aprovada.Resumo).Should().Be(("obs", null, resumo), "a data de pagamento só vale numa paga");
        ((Action)(() => Medicao.Restaurar(1, D(10), D(1), SituacaoDaMedicao.Rascunho, [], [], null, false, null, null, null, null, null)))
            .Should().Throw<DomainValidationException>().WithMessage("*termina antes de começar*");
        ((Action)(() => Medicao.Restaurar(1, D(1), D(2), SituacaoDaMedicao.Aprovada, [], [], null, false, null, null, null, null, null)))
            .Should().Throw<DomainValidationException>().WithMessage("*sem o boletim guardado*");
        ((Action)(() => Medicao.Restaurar(0, D(1), D(2), SituacaoDaMedicao.Rascunho, [], [], null, false, null, null, null, null, null)))
            .Should().Throw<DomainValidationException>().WithMessage("O número da medição começa em 1.");
        var id = Guid.NewGuid();
        ((Action)(() => Medicao.Restaurar(1, D(1), D(2), SituacaoDaMedicao.Rascunho, [new(id, 1), new(id, 2)], [], null, false, null, null, null, null, null)))
            .Should().Throw<DomainValidationException>().WithMessage("*medido duas vezes*");
    }
}
