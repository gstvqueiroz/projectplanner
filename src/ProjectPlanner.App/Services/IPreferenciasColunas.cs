using ProjectPlanner.App.Models;

namespace ProjectPlanner.App.Services;

/// <summary>
/// Onde fica o layout das colunas da grid entre uma sessão e outra. É preferência de interface do
/// usuário, não dado do projeto: não vai para o .pplan.
/// </summary>
public interface IPreferenciasColunas
{
    /// <summary>O layout salvo; nulo se ainda não há nenhum ou se não deu para ler.</summary>
    LayoutColunas? Carregar();

    /// <summary>Grava o layout. Uma falha ao gravar não interrompe o uso do programa.</summary>
    void Salvar(LayoutColunas layout);
}
