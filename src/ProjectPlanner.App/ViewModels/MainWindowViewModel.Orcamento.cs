using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.ViewModels;

// Bloco 33: Projeto > Orçamento…; Bloco 38: as sugestões da IA, também pedidas pelo chat do assistente.
public sealed partial class MainWindowViewModel
{
    private readonly IBibliotecaDeReferencias _biblioteca;
    private readonly IConsultorDeOrcamento _consultor;

    private OrcamentoViewModel NovoOrcamento()
    {
        Recalcular(); // o uso dos recursos e as durações vêm das datas calculadas
        return new OrcamentoViewModel(_cronograma, _dialogos, _biblioteca, _relogio, descricao => MarcarAlteracao(descricao), _consultor, SinapiDaCaixa);
    }

    /// <summary>Bloco 40: de onde a SINAPI é baixada (os testes trocam por uma de mentira).</summary>
    internal ISinapiDaCaixa SinapiDaCaixa { get; set; } = Services.SinapiDaCaixa.Padrao;

    [RelayCommand]
    private Task Orcamento() => _dialogos.MostrarOrcamentoAsync(NovoOrcamento());

    /// <summary>
    /// Bloco 38: o assistente do chat pediu sugestões de orçamento: abre Projeto > Orçamento… na aba Sugestões da IA, já
    /// pedindo com o pedido do chat como foco (a lista fica para o usuário aceitar).
    /// </summary>
    public async Task AbrirSugestoesDeOrcamentoAsync(string pedido)
    {
        var orcamento = NovoOrcamento();
        orcamento.Foco = pedido ?? "";
        orcamento.AbaEscolhida = OrcamentoViewModel.AbaDasSugestoes;
        var consulta = orcamento.PedirSugestoesCommand.ExecuteAsync(null);
        await _dialogos.MostrarOrcamentoAsync(orcamento);
        await consulta; // fechar a janela cancela a consulta (ela termina com "Consulta cancelada")
    }
}
