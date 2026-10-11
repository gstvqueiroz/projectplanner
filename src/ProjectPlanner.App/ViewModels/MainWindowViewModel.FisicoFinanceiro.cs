using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Planejamento;
using ProjectPlanner.Relatorios;

namespace ProjectPlanner.App.ViewModels;

// Bloco 35: Projeto > Cronograma físico-financeiro… e Importar valores….
public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private Task FisicoFinanceiro()
    {
        if (!Recalcular())
            return Task.CompletedTask; // a mensagem do cálculo já está na barra
        return _dialogos.MostrarFisicoFinanceiroAsync(new FisicoFinanceiroViewModel(_cronograma, _dialogos, _relogio,
            () => Colunas["Valor"].Visivel = true, ImportarValoresAsync));
    }

    /// <summary>
    /// Lê uma planilha (.xlsx ou .csv) com a EDT e o valor de cada tarefa e põe os valores nas tarefas (um passo do
    /// Desfazer). O resultado (importados, EDTs não encontradas, resumos, valores inválidos) vai para a barra de status.
    /// </summary>
    [RelayCommand]
    private Task ImportarValores() => ImportarValoresAsync();

    private async Task ImportarValoresAsync()
    {
        if (await _dialogos.EscolherPlanilhaAsync() is not { } caminho)
            return;
        IReadOnlyList<IReadOnlyList<string>> linhas;
        try
        {
            linhas = await Task.Run(() => LeitorDePlanilha.Ler(caminho));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Mensagem = $"Não foi possível ler a planilha {Path.GetFileName(caminho)}: {ex.Message}";
            return;
        }
        ResultadoDaImportacaoDeValores resultado;
        try
        {
            resultado = ImportacaoDeValores.Aplicar(_cronograma, linhas);
        }
        catch (DomainValidationException ex)
        {
            Mensagem = ex.Message;
            return;
        }
        if (resultado.Aplicados > 0)
        {
            MarcarAlteracao($"Importar valores de {Path.GetFileName(caminho)}");
            Colunas["Valor"].Visivel = true;
        }
        Mensagem = resultado.Resumo;
    }
}
