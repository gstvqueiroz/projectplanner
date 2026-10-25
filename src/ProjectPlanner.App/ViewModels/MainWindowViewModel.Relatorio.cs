using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;

namespace ProjectPlanner.App.ViewModels;

// Bloco 27: Arquivo > Relatório fotográfico… (as preferências da empresa ficam em relatorio.json).
public sealed partial class MainWindowViewModel
{
    private readonly IPreferenciasRelatorio _preferenciasRelatorio;

    /// <summary>Contratante, contrato e endereço da obra (a capa do relatório).</summary>
    public Core.Relatorios.DadosDaObra Obra => _cronograma.Obra;

    /// <summary>Os relatórios fotográficos já gerados neste projeto.</summary>
    public IReadOnlyList<Core.Relatorios.RelatorioGerado> RelatoriosGerados => _cronograma.Relatorios;

    [RelayCommand]
    private Task RelatorioFotografico() => _dialogos.MostrarRelatorioFotograficoAsync(NovoRelatorioFotografico());

    /// <summary>O relatório fotográfico com o cronograma recalculado (as datas, a situação e o andamento vêm do cálculo).</summary>
    internal RelatorioFotograficoViewModel NovoRelatorioFotografico()
    {
        Recalcular();
        return new RelatorioFotograficoViewModel(_cronograma, _fotos, _preferenciasRelatorio, _dialogos, _relogio, MarcarSemRecalcular, _analista);
    }
}
