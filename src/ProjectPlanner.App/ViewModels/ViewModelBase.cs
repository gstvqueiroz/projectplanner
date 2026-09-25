using System.Collections;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ProjectPlanner.Core;

namespace ProjectPlanner.App.ViewModels;

/// <summary>
/// Base dos ViewModels. Além de notificar mudanças, guarda os erros de validação por propriedade
/// (<see cref="INotifyDataErrorInfo"/>), que o Avalonia mostra com borda vermelha no campo.
/// </summary>
public abstract class ViewModelBase : ObservableObject, INotifyDataErrorInfo
{
    private readonly Dictionary<string, string> _erros = [];
    private readonly Dictionary<string, object?> _valoresRecusados = [];

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public bool HasErrors => _erros.Count > 0;

    public IEnumerable GetErrors(string? propertyName) =>
        propertyName is not null && _erros.TryGetValue(propertyName, out var erro) ? new[] { erro } : Array.Empty<string>();

    public string? ErroDe(string propriedade) => _erros.GetValueOrDefault(propriedade);

    /// <summary>
    /// O valor a mostrar: o último que o usuário digitou e o domínio recusou, se houver; senão, o atual.
    /// Assim o campo inválido continua visível, com a borda vermelha, até ser corrigido.
    /// </summary>
    protected T ValorExibido<T>(string propriedade, T valorAtual) =>
        _valoresRecusados.TryGetValue(propriedade, out var recusado) ? (T)recusado! : valorAtual;

    /// <summary>
    /// Aplica uma alteração no domínio. Se ela violar uma regra (ou o texto digitado não puder ser
    /// interpretado), a mensagem vira o erro da propriedade, em vez de a exceção chegar à UI.
    /// Digitar de novo o valor atual desfaz uma recusa anterior sem alterar nada.
    /// </summary>
    /// <returns>Se o domínio foi alterado.</returns>
    protected bool AlterarDominio<T>(string propriedade, T valor, bool igualAoAtual, Action alteracao)
    {
        if (igualAoAtual)
        {
            if (_valoresRecusados.Remove(propriedade))
                AtualizarErro(propriedade, null);
            return false;
        }

        try
        {
            alteracao();
            _valoresRecusados.Remove(propriedade);
            AtualizarErro(propriedade, null);
            return true;
        }
        catch (Exception ex) when (ex is DomainValidationException or FormatException)
        {
            _valoresRecusados[propriedade] = valor;
            AtualizarErro(propriedade, ex.Message);
            return false;
        }
    }

    protected void LimparRecusas()
    {
        foreach (var propriedade in _erros.Keys.Union(_valoresRecusados.Keys).ToList())
        {
            _valoresRecusados.Remove(propriedade);
            AtualizarErro(propriedade, null);
        }
    }

    private void AtualizarErro(string propriedade, string? erro)
    {
        if (erro is null)
            _erros.Remove(propriedade);
        else
            _erros[propriedade] = erro;

        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propriedade));
        OnPropertyChanged(propriedade);
        OnPropertyChanged(nameof(HasErrors));
    }
}
