using ProjectPlanner.App.ViewModels;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Tests.Avaliacao;

/// <summary>
/// Bloco 68: a obra em que os pedidos da avaliação são feitos ("hoje" é 28/09/2026). Montada pelos mesmos cartões que a IA
/// usa, para a obra ser a de um usuário de verdade:
/// <list type="bullet">
/// <item>1 Fundação (R$ 18.000, 100%), 2 Alvenaria (120 m², R$ 9.600, 80%), 3 Reboco (240 m², R$ 7.200, 40%), 4 Pintura
/// (240 m², R$ 4.800) e 5 Instalações elétricas (R$ 6.000, 30%), começando em 03/08/2026;</item>
/// <item>a medição 01 (agosto, a fundação) aprovada e a 02 (setembro, 60 m² de alvenaria) em rascunho;</item>
/// <item>o recurso Pedreiro (R$ 280/dia, 4 por dia), 2 na alvenaria;</item>
/// <item>a OS-001 (alvenaria, Construtora Silva, aceita e medida em 30%) e a OS-002 (pintura, Pintores Unidos, em rascunho).</item>
/// </list>
/// </summary>
public static class ObraDaAvaliacao
{
    public static MainWindowViewModel Montar()
    {
        var vm = new Ambiente().CriarViewModel();
        vm.NomeProjeto = "Casa térrea da avaliação";
        vm.InicioProjeto = new DateTime(2026, 8, 3);
        Ambiente.AdicionarTarefa(vm, "Fundação", 10);
        Ambiente.AdicionarTarefa(vm, "Alvenaria", 15, "1");
        Ambiente.AdicionarTarefa(vm, "Reboco", 10, "2");
        Ambiente.AdicionarTarefa(vm, "Pintura", 8, "3");
        Ambiente.AdicionarTarefa(vm, "Instalações elétricas", 10, "2");

        Aplicar(vm,
            new AtualizacaoDaObra("tarefa", "1", Valor: 18000),
            new AtualizacaoDaObra("tarefa", "2", Quantidade: 120, Valor: 9600, Unidade: "m²"),
            new AtualizacaoDaObra("tarefa", "3", Quantidade: 240, Valor: 7200, Unidade: "m²"),
            new AtualizacaoDaObra("tarefa", "4", Quantidade: 240, Valor: 4800, Unidade: "m²"),
            new AtualizacaoDaObra("tarefa", "5", Valor: 6000),
            new AtualizacaoDaObra("obra", Contratante: "Prefeitura Municipal", Contrato: "12/2026", Endereco: "Rua A, 10"),
            new AtualizacaoDaObra("percentual", "1", 100),
            new AtualizacaoDaObra("percentual", "2", 80),
            new AtualizacaoDaObra("percentual", "3", 40),
            new AtualizacaoDaObra("percentual", "5", 30));
        Aplicar(vm, new AtualizacaoDaObra("medicao", "1", 100, De: "2026-08-03", Ate: "2026-08-31"));
        Aplicar(vm, new AtualizacaoDaObra("medicaoContrato", Medicao: 1, Acao: "aprovar"));
        Aplicar(vm, new AtualizacaoDaObra("medicao", "2", Quantidade: 60, De: "2026-09-01", Ate: "2026-09-25"));
        Aplicar(vm,
            new AtualizacaoDaObra("recurso", Valor: 280, Quantidade: 4, Nome: "Pedreiro", TipoDoRecurso: "profissional"),
            new AtualizacaoDaObra("atribuir", "2", Quantidade: 2, Recurso: "Pedreiro"));
        Aplicar(vm, new AtualizacaoDaObra("os", Titulo: "Alvenaria", Executante: "Construtora Silva", TipoDeExecutante: "terceiro", Tarefas: "2",
            Pagamento: "medicao"));
        Aplicar(vm, new AtualizacaoDaObra("situacaoOs", Os: 1, Acao: "emitir"));
        Aplicar(vm, new AtualizacaoDaObra("situacaoOs", Os: 1, Acao: "aceitar"));
        Aplicar(vm, new AtualizacaoDaObra("situacaoOs", Percentual: 30, Os: 1, Acao: "medir"));
        Aplicar(vm, new AtualizacaoDaObra("os", Titulo: "Pintura", Executante: "Pintores Unidos", TipoDeExecutante: "terceiro", Tarefas: "4"));
        return vm;
    }

    private static void Aplicar(MainWindowViewModel vm, params AtualizacaoDaObra[] atualizacoes)
    {
        var itens = vm.ConferirAtualizacoes(atualizacoes, vm.RetratoParaOAssistente());
        if (itens.FirstOrDefault(i => !i.Valido) is { } problema)
            throw new InvalidOperationException($"A obra da avaliação não montou: \"{problema.Texto}\": {problema.Problema}");
        vm.AplicarAtualizacoesAsync([.. itens.Select(i => i.Operacao!)], "Obra da avaliação").GetAwaiter().GetResult();
    }
}
