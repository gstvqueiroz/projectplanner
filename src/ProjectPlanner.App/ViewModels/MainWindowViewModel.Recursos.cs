using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;

namespace ProjectPlanner.App.ViewModels;

// Bloco 32: Projeto > Recursos… e Tarefa > Recursos da tarefa…; Bloco 34: Projeto > Uso de recursos….
public sealed partial class MainWindowViewModel
{
    private readonly ICatalogoDeRecursos _catalogoDeRecursos;

    [RelayCommand]
    private Task RecursosDoProjeto() =>
        _dialogos.MostrarRecursosAsync(new RecursosViewModel(_cronograma, _dialogos, _catalogoDeRecursos, descricao => MarcarAlteracao(descricao)));

    /// <summary>Os recursos da tarefa selecionada (num resumo, avisa que eles vão nas tarefas dentro dele).</summary>
    [RelayCommand]
    private Task RecursosDaTarefa()
    {
        if (TarefaSelecionada is not { } linha)
        {
            Mensagem = "Selecione uma tarefa para ver os recursos dela.";
            return Task.CompletedTask;
        }
        if (linha.EhResumo)
        {
            Mensagem = $"\"{linha.Nome}\" é um resumo: os recursos vão nas tarefas dentro dele.";
            return Task.CompletedTask;
        }
        return _dialogos.MostrarRecursosDaTarefaAsync(new RecursosDaTarefaViewModel(_cronograma, linha.Tarefa, descricao => MarcarAlteracao(descricao)));
    }

    [RelayCommand]
    private Task UsoDeRecursos()
    {
        Recalcular(); // o uso vem das datas calculadas
        return _dialogos.MostrarUsoDeRecursosAsync(new UsoDeRecursosViewModel(_cronograma, _dialogos, descricao => MarcarAlteracao(descricao)));
    }
}
