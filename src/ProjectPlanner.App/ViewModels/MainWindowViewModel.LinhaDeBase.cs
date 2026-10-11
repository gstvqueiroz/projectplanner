using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Planejamento;

namespace ProjectPlanner.App.ViewModels;

// Bloco 31: Projeto > Salvar linha de base… e Linhas de base… (a em uso é a que a grid, o Gantt e a Curva S comparam).
public sealed partial class MainWindowViewModel
{
    /// <summary>As linhas de base salvas no projeto.</summary>
    public IReadOnlyList<LinhaDeBase> LinhasDeBase => _cronograma.LinhasDeBase;

    public LinhaDeBase? LinhaDeBaseEmUso => _cronograma.LinhaDeBaseEmUso;

    /// <summary>
    /// Guarda o cronograma de agora (datas calculadas, durações e valores) como uma linha de base nova, com um nome
    /// opcional. Ela passa a ser a em uso. Pode ser desfeita (Editar > Desfazer).
    /// </summary>
    [RelayCommand]
    private async Task SalvarLinhaDeBase()
    {
        var proximo = _cronograma.LinhasDeBase.Count == 0 ? 0 : _cronograma.LinhasDeBase.Max(l => l.Numero) + 1;
        var pergunta = _cronograma.LinhasDeBase.Count == 0
            ? $"Guardar o cronograma de agora como a linha de base LB{proximo}, para comparar o previsto com o atual.\nNome (opcional, por exemplo, Contrato assinado):"
            : $"Guardar o cronograma de agora como LB{proximo}. A LB{_cronograma.LinhasDeBase[^1].Numero} continua guardada.\nNome (opcional, por exemplo, Aditivo 1):";
        if (await _dialogos.PedirTextoAsync("Salvar linha de base", pergunta) is not { } nome)
            return;
        Recalcular();
        LinhaDeBase linha;
        try
        {
            linha = _cronograma.SalvarLinhaDeBase(nome, _relogio.GetLocalNow().DateTime);
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return;
        }
        MarcarAlteracao($"Salvar a linha de base LB{linha.Numero}");
        LinhaDeBaseMudou();
        Mensagem = $"Linha de base salva: {linha.Descricao}. Ligue as colunas de linha de base e desvio em Exibir > Colunas.";
    }

    /// <summary>Projeto > Linhas de base…: escolher a em uso e remover as antigas.</summary>
    [RelayCommand]
    private Task GerenciarLinhasDeBase() => _dialogos.MostrarLinhasDeBaseAsync(new LinhasDeBaseViewModel(_cronograma, _dialogos, descricao =>
    {
        MarcarAlteracao(descricao);
        LinhaDeBaseMudou();
    }));

    private void LinhaDeBaseMudou()
    {
        OnPropertyChanged(nameof(LinhasDeBase));
        OnPropertyChanged(nameof(LinhaDeBaseEmUso));
        foreach (var linha in Tarefas)
            linha.AtualizarTela();
    }
}
