namespace ProjectPlanner.Core;

/// <summary>
/// Lançada quando uma regra de negócio do domínio é violada.
/// </summary>
public class DomainValidationException : Exception
{
    public DomainValidationException(string propriedade, string mensagem)
        : base(mensagem)
    {
        Propriedade = propriedade;
    }

    /// <summary>
    /// Nome da propriedade cuja regra foi violada (ex.: <c>nameof(Tarefa.Nome)</c>).
    /// </summary>
    public string Propriedade { get; }
}
