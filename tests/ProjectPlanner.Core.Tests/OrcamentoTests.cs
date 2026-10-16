using ProjectPlanner.Core.Medicoes;
using ProjectPlanner.Core.Orcamento;
using ProjectPlanner.Core.Recursos;

namespace ProjectPlanner.Core.Tests;

// Bloco 33: bases de referência (SINAPI), composições abertas, BDI, planilha orçamentária e aplicação às tarefas. Base de
// teste no formato da Caixa: a alvenaria 87495 (m²) usa pedreiro, servente, bloco e a argamassa 87292 (m³, auxiliar).
public class OrcamentoTests
{
    private static readonly DateTime Seg = new(2026, 9, 28);

    private static readonly IReadOnlyList<IReadOnlyList<string>> PlanilhaDeInsumos =
    [
        ["SISTEMA NACIONAL DE PESQUISA DE CUSTOS E ÍNDICES DA CONSTRUÇÃO CIVIL"],
        ["PREÇOS DE INSUMOS", "SP", "09/2026"],
        [],
        ["CODIGO  ", "DESCRICAO DO INSUMO", "UNIDADE DE MEDIDA", "ORIGEM DE PRECO", "PRECO MEDIANO R$"],
        ["88309", "PEDREIRO COM ENCARGOS COMPLEMENTARES", "H", "CR", "28,00"],
        ["88316", "SERVENTE COM ENCARGOS COMPLEMENTARES", "H", "CR", "20,00"],
        ["7271", "BLOCO CERAMICO DE VEDACAO 9X19X19 CM", "UN", "C", "1,20"],
        ["370", "AREIA MEDIA - POSTO JAZIDA", "M3", "C", "120,00"],
        ["1379", "CIMENTO PORTLAND COMPOSTO CP II-32", "KG", "C", "0,80"],
        ["88830", "BETONEIRA 400 L - CHP DIURNO", "CHP", "CR", "2,00"],
        ["9999", "INSUMO COM PRECO RUIM", "UN", "C", "abc"],
    ];

    private static readonly IReadOnlyList<IReadOnlyList<string>> PlanilhaDeComposicoes =
    [
        ["CUSTO DE COMPOSIÇÕES - ANALÍTICO"],
        ["CODIGO DA COMPOSICAO", "DESCRICAO DA COMPOSICAO", "UNIDADE", "TIPO ITEM", "CODIGO ITEM", "DESCRIÇÃO ITEM", "UNIDADE ITEM", "COEFICIENTE", "PRECO UNITARIO"],
        ["87292", "ARGAMASSA TRAÇO 1:2:8 (CIMENTO, CAL E AREIA)", "M3", "", "", "", "", "", ""],
        ["87292", "", "", "INSUMO", "88316", "SERVENTE", "H", "10,0000000", "20,00"],
        ["87292", "", "", "INSUMO", "370", "AREIA", "M3", "1,1000000", "120,00"],
        ["87292", "", "", "INSUMO", "1379", "CIMENTO", "KG", "200,0000000", "0,80"],
        ["87292", "", "", "INSUMO", "88830", "BETONEIRA", "CHP", "1,0000000", "2,00"],
        ["87495", "ALVENARIA DE VEDAÇÃO DE BLOCOS CERÂMICOS FURADOS 9X19X19 CM", "M2", "", "", "", "", "", ""],
        ["87495", "", "", "INSUMO", "88309", "PEDREIRO", "H", "0,8000000", "28,00"],
        ["87495", "", "", "INSUMO", "88316", "SERVENTE", "H", "0,4000000", "20,00"],
        ["87495", "", "", "INSUMO", "7271", "BLOCO", "UN", "13,5000000", "1,20"],
        ["87495", "", "", "COMPOSICAO", "87292", "ARGAMASSA", "M3", "0,0120000", "533,00"],
    ];

    private static readonly BaseDeReferencia Sinapi = new("SINAPI", "SP", "09/2026", false);

    private static TabelaDeReferencia Tabela() =>
        ImportacaoDeReferencia.Montar(Sinapi, [ImportacaoDeReferencia.Ler(PlanilhaDeInsumos), ImportacaoDeReferencia.Ler(PlanilhaDeComposicoes)]);

    private static (Cronograma C, Tarefa Alvenaria, Tarefa Pintura) Obra()
    {
        var c = new Cronograma("Casa", Seg);
        var etapa = new Tarefa("Vedações", Seg, 1);
        var alvenaria = new Tarefa("Alvenaria", Seg, 5);
        var pintura = new Tarefa("Pintura", Seg, 2);
        c.AdicionarTarefa(etapa);
        c.AdicionarTarefa(alvenaria, etapa.Id);
        c.AdicionarTarefa(pintura, etapa.Id);
        alvenaria.DefinirQuantidade(100);
        pintura.DefinirValor(1500);
        CalculadoraCronograma.Calcular(c);
        return (c, alvenaria, pintura);
    }

    // ---- Base de referência ----

    [Fact]
    public void Base_DoNomeDoArquivoDaCaixa_EDescricao()
    {
        BaseDeReferencia.DoNomeDoArquivo("SINAPI_Preco_Ref_Insumos_SP_202609_NaoDesonerado.xlsx").Descricao.Should().Be("SINAPI SP 09/2026 não desonerado");
        BaseDeReferencia.DoNomeDoArquivo("SINAPI_Custo_Ref_Composicoes_Analitico_BA_202512_Desonerado.xlsx").Should()
            .Match<BaseDeReferencia>(b => b.Uf == "BA" && b.Referencia == "12/2025" && b.Desonerado == true && b.Chave == "SINAPI-BA-12-2025-D");
        BaseDeReferencia.DoNomeDoArquivo("minha base.csv").Descricao.Should().Be("SINAPI");
        ((Action)(() => new BaseDeReferencia(" "))).Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Importar_InsumosPeloCabecalho_TipoPelaUnidade_EAvisos()
    {
        var r = ImportacaoDeReferencia.Ler(PlanilhaDeInsumos);

        r.Insumos.Should().HaveCount(6);
        r.Insumos.Select(i => (i.Codigo, i.Tipo)).Should().Contain([("88309", TipoDeInsumo.MaoDeObra), ("88830", TipoDeInsumo.Equipamento), ("370", TipoDeInsumo.Material)]);
        r.Resumo.Should().Be("6 insumos e 0 composições. 1 linha pulada: linha 11 (9999): preço inválido.");
        ((Action)(() => ImportacaoDeReferencia.Ler([["a", "b"]]))).Should().Throw<DomainValidationException>().WithMessage("*não parece uma base de preços*");
    }

    [Fact]
    public void Importar_ComposicoesAnaliticas_ComAAuxiliar()
    {
        var r = ImportacaoDeReferencia.Ler(PlanilhaDeComposicoes);

        r.Composicoes.Select(c => (c.Codigo, c.Unidade, c.Itens.Count)).Should().Equal(("87292", "M3", 4), ("87495", "M2", 4));
        r.Composicoes[1].Itens[3].Should().Be(new ItemDeComposicao(true, "87292", 0.012m));
        r.Insumos.Should().HaveCount(6, "os insumos do analítico (com preço) entram também");
    }

    [Fact]
    public void Abrir_SomaAsAuxiliares_ECustos()
    {
        var aberta = Tabela().Abrir("87495");

        aberta.Insumos.ToDictionary(i => i.Codigo, i => i.Coeficiente).Should().BeEquivalentTo(new Dictionary<string, decimal>
        {
            ["88309"] = 0.8m, ["88316"] = 0.52m, ["7271"] = 13.5m, ["370"] = 0.0132m, ["1379"] = 2.4m, ["88830"] = 0.012m,
        });
        (aberta.CustoUnitario, aberta.CustoDeMateriais, aberta.CustoDeMaoDeObraEEquipamentos).Should().Be((52.53m, 19.704m, 32.824m));
        aberta.Base.Should().Be(Sinapi);
    }

    [Fact]
    public void Abrir_Recusa_CicloEFaltando_EBuscar()
    {
        var ciclo = new TabelaDeReferencia(Sinapi, [], [new Composicao("1", "A", "M2", [new(true, "2", 1)]), new Composicao("2", "B", "M2", [new(true, "1", 1)])]);
        ((Action)(() => ciclo.Abrir("1"))).Should().Throw<DomainValidationException>().WithMessage("*ciclo*");
        var faltando = new TabelaDeReferencia(Sinapi, [], [new Composicao("1", "A", "M2", [new(false, "77", 1)])]);
        ((Action)(() => faltando.Abrir("1"))).Should().Throw<DomainValidationException>().WithMessage("A composição 1 usa o insumo 77, que não está na base.");
        ((Action)(() => faltando.Abrir("9"))).Should().Throw<DomainValidationException>().WithMessage("*não está na base SINAPI SP 09/2026 não desonerado*");

        Tabela().Buscar("alvenaria ceramico").Select(c => c.Codigo).Should().Equal("87495");
        Tabela().Buscar("87292").Single().Descricao.Should().StartWith("ARGAMASSA");
        Tabela().Buscar(" ").Should().BeEmpty();
    }

    // ---- Orçamento ----

    [Fact]
    public void Preco_ComBdiEBdiDeMateriais_EPlanilha()
    {
        var (c, alvenaria, _) = Obra();
        c.Orcamento.DefinirConfiguracao(new ConfiguracaoDoOrcamento { Bdi = 25, BdiDeMateriais = 15 });
        c.Orcamento.Associar(alvenaria, Tabela().Abrir("87495"));

        c.Orcamento.PrecoUnitario(c.Orcamento.ComposicaoDe(alvenaria.Id)!).Should().Be(63.69m, "32,824 × 1,25 + 19,704 × 1,15");
        var p = c.Orcamento.Planilha();
        p.Linhas.Select(l => (l.Edt, l.EhGrupo, l.Codigo, l.Quantidade, l.PrecoUnitario, l.Total, l.SemComposicao)).Should().Equal(
            ("1", true, "", null, null, 7869m, false),
            ("1.1", false, "87495", 100m, 63.69m, 6369m, false),
            ("1.2", false, "", null, null, 1500m, true));
        (p.CustoDireto, p.ValorDoBdi, p.Informado, p.Total, p.Bases.Single()).Should().Be((5253m, 1116m, 1500m, 7869m, "SINAPI SP 09/2026 não desonerado"));
        ((Action)(() => c.Orcamento.DefinirConfiguracao(new ConfiguracaoDoOrcamento { Bdi = 120 }))).Should().Throw<DomainValidationException>();
        ((Action)(() => c.Orcamento.Associar(c.Tarefas[0], Tabela().Abrir("87495")))).Should().Throw<DomainValidationException>().WithMessage("*é um resumo*");
    }

    [Fact]
    public void Aplicar_ValorUnidadeERecursos_PorDiaPelasHoras()
    {
        var (c, alvenaria, pintura) = Obra();
        c.Orcamento.DefinirConfiguracao(new ConfiguracaoDoOrcamento { Bdi = 25, BdiDeMateriais = 15 });
        c.Orcamento.Associar(alvenaria, Tabela().Abrir("87495"));
        c.Orcamento.Associar(pintura, Tabela().Abrir("87292")); // sem quantidade: aviso

        var r = c.Orcamento.Aplicar();

        (r.Tarefas, r.RecursosNovos, r.Atribuicoes).Should().Be((1, 6, 6));
        r.Avisos.Should().Equal("1.2 Pintura: sem quantidade (preencha a coluna Quantidade)");
        (alvenaria.Valor, alvenaria.Unidade).Should().Be((6369m, "M2"));
        var pedreiro = c.Recursos.Lista.Single(x => x.Nome.StartsWith("PEDREIRO"));
        (pedreiro.Tipo, pedreiro.CustoUnitario, pedreiro.Fornecedor).Should().Be((TipoDeRecurso.Profissional, 224m, "SINAPI 88309"));
        alvenaria.Atribuicoes.Single(a => a.RecursoId == pedreiro.Id).Quantidade.Should().Be(2, "80 h ÷ 8 ÷ 5 dias");
        c.Recursos.Total(alvenaria, alvenaria.Atribuicoes.Single(a => c.Recursos.Obter(a.RecursoId)!.Nome.StartsWith("BLOCO"))).Should().Be(1350);
        c.Recursos.CustoOrcado(alvenaria).Should().Be(5252.80m, "o custo dos recursos é o custo direto da composição");

        c.Orcamento.Aplicar().RecursosNovos.Should().Be(0, "aplicar de novo reaproveita os recursos");
        c.Recursos.Lista.Should().HaveCount(6);
    }

    [Fact]
    public void Aplicar_ContratoTravado_Recusa_EUnidadeDiferenteAvisa()
    {
        var (c, alvenaria, pintura) = Obra();
        alvenaria.DefinirUnidade("m");
        c.Orcamento.Associar(alvenaria, Tabela().Abrir("87495"));
        c.Orcamento.Aplicar().Avisos.Should().Equal("1.1 Alvenaria: a unidade da tarefa (m) é diferente da composição (M2)");

        alvenaria.DefinirUnidade("m²");
        c.Orcamento.Aplicar().Avisos.Should().BeEmpty("m² e M2 são a mesma unidade");
        c.Contrato.NovaMedicao(new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 30));
        c.Contrato.Medir(1, pintura.Id, 10);
        c.Contrato.Aprovar(1, Seg);
        ((Action)(() => c.Orcamento.Aplicar())).Should().Throw<DomainValidationException>().WithMessage("*medição aprovada*");
    }

    [Fact]
    public void AtualizarPrecos_PorOutraBase_ECopiaIndependente()
    {
        var (c, alvenaria, pintura) = Obra();
        c.Orcamento.Associar(alvenaria, Tabela().Abrir("87495"));
        c.Orcamento.Associar(pintura, Tabela().Abrir("87292"));
        var copia = c.Copiar();
        var outubro = new BaseDeReferencia("SINAPI", "SP", "10/2026", false);
        var nova = new TabelaDeReferencia(outubro,
            Tabela().Insumos.Select(i => i with { Preco = i.Preco * 1.1m }), Tabela().Composicoes.Where(x => x.Codigo != "87292").Append(
                new Composicao("87292X", "outra", "M3", [new(false, "370", 1)])).Append(Tabela().Composicao("87292")! with { Codigo = "87292" }));
        var semArgamassa = new TabelaDeReferencia(outubro, nova.Insumos, nova.Composicoes.Where(x => x.Codigo is not "87292" and not "87495"));

        var (atualizadas, faltando) = c.Orcamento.AtualizarPrecos(nova);
        (atualizadas, faltando.Count).Should().Be((2, 0));
        c.Orcamento.ComposicaoDe(alvenaria.Id)!.CustoUnitario.Should().Be(57.78m, "10% a mais");
        c.Orcamento.AtualizarPrecos(semArgamassa).NaoEncontradas.Should().BeEquivalentTo(["87292", "87495"]);
        copia.Orcamento.ComposicaoDe(alvenaria.Id)!.Base.Referencia.Should().Be("09/2026", "a cópia (Desfazer) não muda");
        c.RemoverTarefa(alvenaria.Id);
        c.Orcamento.ComposicaoDe(alvenaria.Id).Should().BeNull();
    }
}
