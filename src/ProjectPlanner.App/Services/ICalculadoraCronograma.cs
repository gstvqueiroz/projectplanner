using ProjectPlanner.Core;

namespace ProjectPlanner.App.Services;

/// <summary>
/// Acesso ao cálculo do cronograma (Bloco 3) por injeção de dependência. A
/// <see cref="CalculadoraCronograma"/> é estática, então esta interface só a embrulha, para que os
/// ViewModels a recebam pelo construtor e os testes possam observá-la.
/// </summary>
public interface ICalculadoraCronograma
{
    ResultadoCronograma Calcular(Cronograma cronograma);
}

public sealed class CalculadoraCronogramaServico : ICalculadoraCronograma
{
    public ResultadoCronograma Calcular(Cronograma cronograma) => CalculadoraCronograma.Calcular(cronograma);
}
