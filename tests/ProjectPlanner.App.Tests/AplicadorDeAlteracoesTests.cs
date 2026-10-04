using ProjectPlanner.App.Services;
using ProjectPlanner.Core;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Tests;

// Bloco 24: as alterações do assistente aplicadas no cronograma pelas regras do domínio.
public class AplicadorDeAlteracoesTests
{
    private static readonly DateTime Seg = new(2026, 9, 28);

    private static AlteracaoDoCronograma Criar(string referencia, string nome, int? duracao = null, string? pai = null) =>
        new(AcoesNoCronograma.Criar, Ref: referencia, Nome: nome, Duracao: duracao, Pai: pai);

    private static AlteracaoDoCronograma Vincular(string de, string para, string? tipo = null, int? defasagem = null) =>
        new(AcoesNoCronograma.Vincular, De: de, Para: para, Tipo: tipo, Defasagem: defasagem);

    // O retrato como o App o monta: Nº = posição na lista, EDT e o Id de cada tarefa.
    private static ContextoDoProjeto Retrato(Cronograma cronograma)
    {
        var edt = cronograma.NumeracaoEdt();
        return new ContextoDoProjeto(cronograma.Nome, DateOnly.FromDateTime(cronograma.DataInicio), DateOnly.FromDateTime(cronograma.DataInicio),
            DateOnly.FromDateTime(Seg), cronograma.Tarefas.Select((t, i) => new TarefaNoContexto(i + 1, edt[t.Id], t.Nome, cronograma.NivelDe(t.Id),
                TipoNoContexto.Tarefa, t.Duracao, DateOnly.FromDateTime(t.DataInicio), DateOnly.FromDateTime(t.DataTermino), t.PercentualConcluido,
                "", t.Folga, t.EhCritica, t.Notas, t.Fotos.Count, t.Id)).ToList());
    }

    private static ResumoDasAlteracoes Aplicar(Cronograma cronograma, params AlteracaoDoCronograma[] alteracoes) =>
        AplicadorDeAlteracoes.Aplicar(cronograma, alteracoes, Retrato(cronograma));

    private static Tarefa Tarefa(Cronograma cronograma, string nome) => cronograma.Tarefas.Single(t => t.Nome == nome);

    // 1 Fundação (resumo) > 1.1 Escavar (2 d), 1.2 Armar (3 d, pred 2); 2 Estrutura (4 d, pred 3).
    private static Cronograma Obra()
    {
        var cronograma = new Cronograma("Obra", Seg);
        Aplicar(cronograma,
            Criar("a", "Fundação"), Criar("b", "Escavar", 2, "a"), Criar("c", "Armar", 3, "a"), Criar("d", "Estrutura", 4),
            Vincular("b", "c"), Vincular("c", "d"));
        return cronograma;
    }

    [Fact]
    public void CronogramaInteiro_ResumosTarefasMarcosEVinculos()
    {
        var cronograma = new Cronograma("Novo projeto", Seg);

        var resumo = Aplicar(cronograma,
            new AlteracaoDoCronograma(AcoesNoCronograma.Projeto, Nome: "Casa 100 m²", Inicio: "2026-10-05"),
            Criar("n1", "Serviços preliminares"),
            Criar("n2", "Limpeza do terreno", 2, "n1"),
            Criar("n3", "Locação da obra", 1, "n1"),
            Criar("n4", "Fundação"),
            Criar("n5", "Escavação", 3, "n4"),
            Criar("n6", "Sapatas", 5, "n4"),
            Criar("n7", "Fundação concluída", 0, "n4"),
            Vincular("n2", "n3"),
            Vincular("n3", "n5"),
            Vincular("n5", "n6", "II", 1),
            Vincular("n6", "n7"));

        (cronograma.Nome, cronograma.DataInicio).Should().Be(("Casa 100 m²", new DateTime(2026, 10, 5)));
        cronograma.NumeracaoEdt().Select(e => (Tarefa: cronograma.Tarefas.Single(t => t.Id == e.Key).Nome, e.Value)).Should().Equal(
            ("Serviços preliminares", "1"), ("Limpeza do terreno", "1.1"), ("Locação da obra", "1.2"),
            ("Fundação", "2"), ("Escavação", "2.1"), ("Sapatas", "2.2"), ("Fundação concluída", "2.3"));
        (Tarefa(cronograma, "Fundação").EhResumo, Tarefa(cronograma, "Fundação concluída").EhMarco).Should().Be((true, true));
        cronograma.Predecessoras.Should().ContainSingle(p => p.Tipo == TipoDependencia.SS && p.Lag == 1);
        resumo.Texto.Should().Be("7 tarefas criadas · 4 vínculos criados · projeto alterado");

        var resultado = CalculadoraCronograma.Calcular(cronograma);
        resultado.DataTermino.Should().BeAfter(new DateTime(2026, 10, 5));
    }

    [Fact]
    public void Existentes_PeloNumeroOuPelaEdt_AlterarMoverEDesvincular()
    {
        var cronograma = Obra();

        var resumo = Aplicar(cronograma,
            new AlteracaoDoCronograma(AcoesNoCronograma.Alterar, Tarefa: "3", Nome: "Armação", Duracao: 5, Percentual: 20, Notas: "Aço CA-50"),
            new AlteracaoDoCronograma(AcoesNoCronograma.Desvincular, De: "#3", Para: "4"),
            Vincular("1.1", "4", "TT", -1),
            new AlteracaoDoCronograma(AcoesNoCronograma.Mover, Tarefa: "4", Pai: "1"));

        var armacao = Tarefa(cronograma, "Armação");
        (armacao.Duracao, armacao.PercentualConcluido, armacao.Notas).Should().Be((5, 20, "Aço CA-50"));
        cronograma.PredecessorasDe(Tarefa(cronograma, "Estrutura").Id).Single()
            .Should().Be(new Predecessora(Tarefa(cronograma, "Escavar").Id, Tarefa(cronograma, "Estrutura").Id, TipoDependencia.FF, -1));
        cronograma.FilhasDe(Tarefa(cronograma, "Fundação").Id).Select(t => t.Nome).Should().Equal("Escavar", "Armação", "Estrutura");
        resumo.Texto.Should().Be("1 tarefa alterada · 1 tarefa movida · 1 vínculo criado · 1 vínculo removido");
    }

    [Fact]
    public void Vincular_OQueJaExiste_TrocaOTipo()
    {
        var cronograma = Obra();

        Aplicar(cronograma, Vincular("2", "3", "II", 2));

        cronograma.PredecessorasDe(Tarefa(cronograma, "Armar").Id).Should().ContainSingle()
            .Which.Should().Be(new Predecessora(Tarefa(cronograma, "Escavar").Id, Tarefa(cronograma, "Armar").Id, TipoDependencia.SS, 2));
    }

    [Fact]
    public void Remover_ContaAsSubtarefas_ELimparTiraTudo()
    {
        var cronograma = Obra();

        Aplicar(cronograma, new AlteracaoDoCronograma(AcoesNoCronograma.Remover, Tarefa: "1")).Texto.Should().Be("3 tarefas removidas");
        cronograma.Tarefas.Select(t => t.Nome).Should().Equal("Estrutura");
        cronograma.Predecessoras.Should().BeEmpty();

        var outra = Obra();
        Aplicar(outra, new AlteracaoDoCronograma(AcoesNoCronograma.Limpar), Criar("n1", "Nova")).Texto
            .Should().Be("1 tarefa criada · 4 tarefas removidas");
        outra.Tarefas.Select(t => t.Nome).Should().Equal("Nova");
    }

    [Fact]
    public void Referencias_SaoAsDoRetrato_MesmoDepoisDeRemoverOuCriar()
    {
        var cronograma = Obra();

        // Depois de remover a 2 (Escavar), "4" continua sendo a Estrutura, que era a linha 4 no retrato.
        Aplicar(cronograma,
            new AlteracaoDoCronograma(AcoesNoCronograma.Remover, Tarefa: "2"),
            Criar("n1", "Concretar", 2, "1"),
            Vincular("n1", "4"));

        cronograma.PredecessorasDe(Tarefa(cronograma, "Estrutura").Id).Select(p => p.TarefaOrigemId)
            .Should().Contain(Tarefa(cronograma, "Concretar").Id);
    }

    [Fact]
    public void CriarSemDuracao_UmDia_ComPercentualENotas()
    {
        var cronograma = new Cronograma("Obra", Seg);

        Aplicar(cronograma, new AlteracaoDoCronograma(AcoesNoCronograma.Criar, Ref: "n1", Nome: "  Pintura  ", Percentual: 50, Notas: "Duas demãos"));

        var pintura = cronograma.Tarefas.Single();
        (pintura.Nome, pintura.Duracao, pintura.PercentualConcluido, pintura.Notas, pintura.DataInicio).Should().Be(("Pintura", 1, 50, "Duas demãos", Seg));
    }

    public static TheoryData<AlteracaoDoCronograma, string> Recusas => new()
    {
        { new AlteracaoDoCronograma("teletransportar", Tarefa: "1"), "a ação \"teletransportar\" não existe; use uma destas: criar, alterar, remover, vincular, desvincular, mover, projeto, limpar." },
        { new AlteracaoDoCronograma("", Nome: "x"), "a ação \"\" não existe*" },
        { Criar("", "Sem ref"), "falta a referência (ref) da tarefa nova." },
        { Criar("2", "Ref que é um Nº"), "a referência \"2\" já está em uso; escolha outra (como n1, n2...)." },
        { Criar("n1", "  "), "falta o nome da tarefa." },
        { Criar("n1", "Dentro de quem?", 1, "n9"), "a tarefa \"n9\" (campo pai) não existe: use o Nº de uma linha do retrato ou a referência de uma tarefa criada antes nesta resposta." },
        { Criar("n1", "Negativa", -2), "*duração*" },
        { new AlteracaoDoCronograma(AcoesNoCronograma.Alterar, Tarefa: "1", Duracao: 9), "A duração de um resumo é calculada a partir das subtarefas." },
        { new AlteracaoDoCronograma(AcoesNoCronograma.Alterar, Tarefa: "2"), "não diz o que mudar (nome, duracao, percentual ou notas)." },
        { new AlteracaoDoCronograma(AcoesNoCronograma.Alterar, Tarefa: "3", Percentual: 120), "*percentual*" },
        { new AlteracaoDoCronograma(AcoesNoCronograma.Remover), "falta a tarefa (campo tarefa)." },
        { Vincular("1", "4"), "\"Fundação\" é um resumo, e resumos não têm predecessoras nem sucessoras. Vincule as subtarefas dele." },
        { Vincular("4", "2"), "*dependência circular*" },
        { Vincular("2", "4", "XYZ"), "o tipo de vínculo \"XYZ\" não existe; use TI, II, TT ou IT." },
        { new AlteracaoDoCronograma(AcoesNoCronograma.Desvincular, De: "2", Para: "4"), "Não existe vínculo entre as tarefas informadas." },
        { new AlteracaoDoCronograma(AcoesNoCronograma.Mover, Tarefa: "1", Pai: "2"), "*ciclo na estrutura*" },
        { new AlteracaoDoCronograma(AcoesNoCronograma.Projeto, Inicio: "amanhã"), "a data \"amanhã\" não está no formato AAAA-MM-DD." },
        { new AlteracaoDoCronograma(AcoesNoCronograma.Projeto), "não diz o que mudar no projeto (nome ou inicio)." },
    };

    [Theory]
    [MemberData(nameof(Recusas))]
    public void AlteracaoInvalida_RecusadaComONumeroEOMotivo(AlteracaoDoCronograma invalida, string motivo)
    {
        var cronograma = Obra();

        var aplicar = () => Aplicar(cronograma, Criar("ok", "Antes da inválida", 1), invalida);

        var recusa = aplicar.Should().Throw<AlteracaoRecusadaException>().Which;
        (recusa.Numero, recusa.Alteracao).Should().Be((2, invalida));
        recusa.Motivo.Should().Match(motivo);
        recusa.Message.Should().StartWith($"Alteração 2 ({invalida}): ");
    }

    [Fact]
    public void TarefaJaRemovidaNaMesmaResposta_Recusada()
    {
        var cronograma = Obra();

        var aplicar = () => Aplicar(cronograma,
            new AlteracaoDoCronograma(AcoesNoCronograma.Remover, Tarefa: "1"),
            new AlteracaoDoCronograma(AcoesNoCronograma.Alterar, Tarefa: "2", Nome: "Escavação"));

        aplicar.Should().Throw<AlteracaoRecusadaException>().Which.Motivo.Should().Be("a tarefa \"2\" (campo tarefa) já foi removida.");
    }

    [Fact]
    public void ResumoVazio_EArgumentosNulos()
    {
        var cronograma = Obra();

        Aplicar(cronograma).Should().Match<ResumoDasAlteracoes>(r => r.Vazio && r.Texto == "Nenhuma mudança.");
        var semCronograma = () => AplicadorDeAlteracoes.Aplicar(null!, [], Retrato(cronograma));
        var semLista = () => AplicadorDeAlteracoes.Aplicar(cronograma, null!, Retrato(cronograma));
        var semRetrato = () => AplicadorDeAlteracoes.Aplicar(cronograma, [], null!);
        semCronograma.Should().Throw<ArgumentNullException>();
        semLista.Should().Throw<ArgumentNullException>();
        semRetrato.Should().Throw<ArgumentNullException>();
    }
}
