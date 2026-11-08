using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.ViewModels;

// Bloco 81: Projeto > Diário de obra… (o RDO). Bloco 82: o diário no retrato da IA.
public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private Task DiarioDeObra() => _dialogos.MostrarDiarioDeObraAsync(NovoDiario());

    // O dia novo vem do cronograma calculado.
    private DiarioDeObraViewModel NovoDiario()
    {
        Recalcular();
        return new DiarioDeObraViewModel(_cronograma, _dialogos, _relogio, descricao => MarcarAlteracao(descricao), CabecalhoDosRelatorios,
            fotos => _fotos.Miniaturas(fotos));
    }

    /// <summary>
    /// Bloco 82: o diário para o retrato da IA: o resumo (quantos dias, o último fechado) e os dias mais recentes, um por linha
    /// ("RDO nº 012 · 28/09/2026 (seg) · aberto · manhã: Bom, praticável; tarde: Chuva, impraticável · efetivo 9: Pedreiro 4
    /// (Construtora Alfa), Servente 5 · ..."). Nulo sem diário.
    /// </summary>
    private IReadOnlyList<string>? DiarioParaOAssistente()
    {
        var diario = _cronograma.Diario;
        if (diario.Vazio)
            return null;
        static string Data(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var total = diario.Dias.Count;
        var resumo = (total == 1 ? "1 dia" : $"{total} dias") +
                     (diario.UltimoFechado is { } ultimo
                         ? $"; o último fechado (assinado: não muda, e não há dia novo antes dele) é o RDO nº {diario.NumeroDe(ultimo.Data):000}, de {Data(ultimo.Data)}"
                         : "; nenhum fechado") +
                     (total > ContextoDoProjeto.DiasDoDiario ? $"; abaixo, os {ContextoDoProjeto.DiasDoDiario} mais recentes" : "") + ".";
        return [resumo, .. diario.Dias.TakeLast(ContextoDoProjeto.DiasDoDiario).Select(d =>
        {
            var partes = new List<string>
            {
                $"RDO nº {diario.NumeroDe(d.Data):000} · {Data(d.Data)} ({Core.CalendarioDoProjeto.NomeDoDia(d.Data.DayOfWeek)[..3]}) · {(d.Fechado ? "fechado" : "aberto")}",
                $"manhã: {d.Manha.Texto}; tarde: {d.Tarde.Texto}",
                d.Efetivo.Count == 0 ? "efetivo: nenhum" : $"efetivo {d.TotalDoEfetivo}: " + string.Join(", ", d.Efetivo.Select(e =>
                    $"{e.Funcao} {e.Quantidade}{(e.Empresa is { } empresa ? $" ({empresa})" : "")}")),
            };
            if (d.Equipamentos.Count > 0)
                partes.Add("equipamentos: " + string.Join(", ", d.Equipamentos.Select(e => $"{e.Nome} {e.Quantidade}")));
            if (d.Atividades.Count > 0)
                partes.Add("serviços: " + string.Join("; ", d.Atividades.Select(a => a.Descricao +
                    (a.Quantidade is { } q ? $" {q.ToString("#,##0.####", CultureInfo.GetCultureInfo("pt-BR"))} {a.Unidade}".TrimEnd() : "") + // Bloco 84
                    (a.Percentual is { } p ? $" {p}%" : ""))));
            if (d.Ocorrencias is { } ocorrencias)
                partes.Add("ocorrências: " + (ocorrencias.Length > 300 ? ocorrencias[..300] + "…" : ocorrencias));
            return string.Join(" · ", partes);
        })];
    }
}
