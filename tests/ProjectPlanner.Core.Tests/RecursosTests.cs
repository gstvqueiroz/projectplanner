using ProjectPlanner.Core.Edicao;
using ProjectPlanner.Core.Recursos;

namespace ProjectPlanner.Core.Tests;

// Bloco 32: recursos (profissionais, materiais e equipamentos), atribuições, custo orçado e importação. Projeto na seg
// 28/09/2026: Alvenaria (resumo) > Parede A (5 d) e Parede B (3 d); Pintura (2 d).
public class RecursosTests
{
    private static readonly DateTime Seg = new(2026, 9, 28);

    private static readonly Recurso Pedreiro = new(Guid.NewGuid(), "Pedreiro", TipoDeRecurso.Profissional, null, 280, 2, "Equipe própria");
    private static readonly Recurso Servente = new(Guid.NewGuid(), "Servente", TipoDeRecurso.Profissional, null, 180);
    private static readonly Recurso Tijolo = new(Guid.NewGuid(), "Tijolo cerâmico", TipoDeRecurso.Material, "mil", 950.5m);
    private static readonly Recurso Betoneira = new(Guid.NewGuid(), "Betoneira 400 L", TipoDeRecurso.Equipamento, "x", 90, 1);

    private static (Cronograma C, Tarefa Alvenaria, Tarefa A, Tarefa B, Tarefa Pintura) Obra()
    {
        var c = new Cronograma("Casa", Seg);
        var alvenaria = new Tarefa("Alvenaria", Seg, 1);
        var a = new Tarefa("Parede A", Seg, 5);
        var b = new Tarefa("Parede B", Seg, 3);
        var pintura = new Tarefa("Pintura", Seg, 2);
        c.AdicionarTarefa(alvenaria);
        c.AdicionarTarefa(a, alvenaria.Id);
        c.AdicionarTarefa(b, alvenaria.Id);
        c.AdicionarTarefa(pintura);
        foreach (var r in new[] { Pedreiro, Servente, Tijolo, Betoneira })
            c.Recursos.Adicionar(r);
        return (c, alvenaria, a, b, pintura);
    }

    [Fact]
    public void Recurso_PorDiaTemUnidadeDia_MaterialTemAUnidadeDele_EValida()
    {
        (Pedreiro.Unidade, Pedreiro.PorDia, Betoneira.Unidade, Tijolo.Unidade, Tijolo.PorDia, Tijolo.Disponivel).Should()
            .Be(("dia", true, "dia", "mil", false, null));
        new Recurso(Guid.NewGuid(), "Areia", TipoDeRecurso.Material, " ", 10, 5).Should().Match<Recurso>(r => r.Unidade == "un" && r.Disponivel == null);
        ((Action)(() => new Recurso(Guid.NewGuid(), " ", TipoDeRecurso.Material, "m³", 1))).Should().Throw<DomainValidationException>().WithMessage("Escreva o nome do recurso.");
        ((Action)(() => new Recurso(Guid.NewGuid(), "x", TipoDeRecurso.Material, "m³", -1))).Should().Throw<DomainValidationException>().WithMessage("*negativo*");
        ((Action)(() => new Recurso(Guid.NewGuid(), "x", TipoDeRecurso.Profissional, null, 1, 0))).Should().Throw<DomainValidationException>().WithMessage("*maior que zero*");
        ((Action)(() => new AtribuicaoDeRecurso(Guid.NewGuid(), 0))).Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Atribuir_TotalECusto_PorDiaVezesOsDias_MaterialPeloTotal()
    {
        var (c, alvenaria, a, b, pintura) = Obra();

        c.Recursos.Atribuir(a, [new(Pedreiro.Id, 2), new(Servente.Id, 1), new(Tijolo.Id, 3.5m)]);
        c.Recursos.Atribuir(b, [new(Pedreiro.Id, 1)]);

        (c.Recursos.Total(a, a.Atribuicoes[0]), c.Recursos.Total(a, a.Atribuicoes[2])).Should().Be((10m, 3.5m), "2 por dia × 5 dias; o tijolo pelo total");
        c.Recursos.CustoOrcado(a).Should().Be(10 * 280 + 5 * 180 + 3326.75m);
        c.Recursos.CustoOrcado(alvenaria).Should().Be(7026.75m + 3 * 280, "o resumo soma as tarefas dentro dele");
        (c.Recursos.CustoOrcado(pintura), c.Recursos.CustoTotal).Should().Be((null, 7866.75m));
        c.Recursos.Texto(a).Should().Be("2 Pedreiro; 1 Servente; 3,50 mil Tijolo cerâmico");
        c.Recursos.Uso(Pedreiro.Id).Should().Be((13m, 3640m));
        c.Recursos.TarefasCom(Pedreiro.Id).Should().Equal(a, b);

        a.AlterarDuracao(10);
        c.Recursos.Total(a, a.Atribuicoes[0]).Should().Be(20, "o total acompanha a duração");
        c.Recursos.Atribuir(a, []);
        a.Atribuicoes.Should().BeEmpty();
    }

    [Fact]
    public void Atribuir_Recusas()
    {
        var (c, alvenaria, a, _, _) = Obra();

        ((Action)(() => c.Recursos.Atribuir(alvenaria, [new(Pedreiro.Id, 1)]))).Should().Throw<DomainValidationException>().WithMessage("*é um resumo*");
        ((Action)(() => c.Recursos.Atribuir(a, [new(Guid.NewGuid(), 1)]))).Should().Throw<DomainValidationException>().WithMessage("*não está no projeto*");
        ((Action)(() => c.Recursos.Atribuir(a, [new(Pedreiro.Id, 1), new(Pedreiro.Id, 2)]))).Should().Throw<DomainValidationException>()
            .WithMessage("\"Pedreiro\" aparece duas vezes na tarefa.");
        ((Action)(() => c.Recursos.Atribuir(new Tarefa("Fora", Seg, 1), []))).Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Lista_NomeRepetido_RemoverEmUso_ETrocarTipoEmUso()
    {
        var (c, _, a, _, _) = Obra();
        c.Recursos.Atribuir(a, [new(Pedreiro.Id, 1), new(Tijolo.Id, 1)]);

        ((Action)(() => c.Recursos.Adicionar(new Recurso(Guid.NewGuid(), "pedreiro", TipoDeRecurso.Profissional, null, 1)))).Should()
            .Throw<DomainValidationException>().WithMessage("Já existe o recurso \"pedreiro\" (profissional).");
        c.Recursos.Adicionar(new Recurso(Guid.NewGuid(), "Pedreiro", TipoDeRecurso.Material, "un", 1)); // outro tipo: pode
        ((Action)(() => c.Recursos.Remover(Pedreiro.Id))).Should().Throw<DomainValidationException>()
            .WithMessage("\"Pedreiro\" está em 1 tarefa (1.1 Parede A): tire-o delas antes de remover.");
        ((Action)(() => c.Recursos.Substituir(new Recurso(Tijolo.Id, "Tijolo cerâmico", TipoDeRecurso.Equipamento, null, 1)))).Should()
            .Throw<DomainValidationException>().WithMessage("*mudar entre material*");

        c.Recursos.Substituir(new Recurso(Pedreiro.Id, "Pedreiro", TipoDeRecurso.Profissional, null, 300, 3));
        c.Recursos.Obter(Pedreiro.Id)!.CustoUnitario.Should().Be(300);
        c.Recursos.Remover(Betoneira.Id);
        c.Recursos.Lista.Should().NotContain(Betoneira);
    }

    [Fact]
    public void ViraResumo_PerdeOsRecursos_ECopiaEIndependente()
    {
        var (c, _, a, _, pintura) = Obra();
        c.Recursos.Atribuir(pintura, [new(Servente.Id, 2)]);
        var copia = c.Copiar();

        c.AdicionarTarefa(new Tarefa("Massa", Seg, 1), pintura.Id);
        copia.Recursos.Remover(Betoneira.Id);

        (pintura.Atribuicoes.Count, copia.Tarefas.Single(t => t.Id == pintura.Id).Atribuicoes.Count).Should().Be((0, 1));
        (c.Recursos.Lista.Count, copia.Recursos.Lista.Count).Should().Be((4, 3));
        a.Atribuicoes.Should().BeEmpty();
    }

    [Fact]
    public void ColarTarefas_LevaOsRecursos_EmOutroProjetoSoOsQueExistem()
    {
        var (c, _, a, _, _) = Obra();
        c.Recursos.Atribuir(a, [new(Pedreiro.Id, 2), new(Tijolo.Id, 3)]);
        var trecho = CopiaDeTarefas.Copiar(c, [a.Id]);

        var colada = CopiaDeTarefas.Colar(c, trecho, null, 0)[0];
        var outro = new Cronograma("Outro", Seg);
        outro.Recursos.Adicionar(Pedreiro);
        var noOutro = CopiaDeTarefas.Colar(outro, trecho, null, 0)[0];

        colada.Atribuicoes.Should().Equal(a.Atribuicoes);
        noOutro.Atribuicoes.Should().Equal(new AtribuicaoDeRecurso(Pedreiro.Id, 2));
    }

    [Fact]
    public void Importar_NovosAtualizadosEInvalidos_TipoPelaUnidade()
    {
        var (c, _, _, _, _) = Obra();

        var resultado = ImportacaoDeRecursos.Ler([
            ["Insumo", "Unidade", "Preço unitário", "Disponível", "Fornecedor"],
            ["Pedreiro", "dia", "310,00", "3", "Equipe"],
            ["Cimento CP-II", "sc", "38,90", "", "Depósito Silva"],
            ["Carpinteiro", "h", "35", "", ""],
            ["", "un", "1", "", ""],
            ["Areia", "m³", "abc", "", ""],
        ], c.Recursos.Lista);

        resultado.Resumo.Should().Be("2 recursos novos, 1 atualizado. 2 linhas inválidas: linha 5: sem nome, linha 6: \"abc\" não é um valor em reais (use, por exemplo, 1.234,56).");
        resultado.Atualizados.Single().Should().Match<Recurso>(r => r.Id == Pedreiro.Id && r.CustoUnitario == 310 && r.Disponivel == 3);
        resultado.Novos.Select(r => (r.Nome, r.Tipo, r.Unidade)).Should().Equal(("Cimento CP-II", TipoDeRecurso.Material, "sc"),
            ("Carpinteiro", TipoDeRecurso.Profissional, "dia"));
        ((Action)(() => ImportacaoDeRecursos.Ler([["a", "b"]], []))).Should().Throw<DomainValidationException>().WithMessage("*Nome e Custo*");
        ImportacaoDeRecursos.Tabela([Tijolo])[1].Should().Equal("Tijolo cerâmico", "Material", "mil", "950,50", "", "");
    }
}
