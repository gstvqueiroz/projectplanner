namespace ProjectPlanner.App.Services.Cofre;

/// <summary>
/// O cofre de senhas do sistema (Bloco 23): Chaves do macOS ou Gerenciador de Credenciais do Windows. Guarda segredos
/// (a chave de API do assistente) fora de qualquer arquivo do programa, protegidos pela conta do usuário.
/// </summary>
public interface ICofreDeSegredos
{
    /// <summary>Nome para a tela: "Chaves do macOS", "Gerenciador de Credenciais do Windows".</summary>
    string Nome { get; }

    /// <summary>Falso num sistema sem cofre conhecido (Linux): nada é gravado.</summary>
    bool Disponivel { get; }

    /// <summary>O segredo guardado com esse nome; nulo se não há.</summary>
    /// <exception cref="CofreException">O cofre recusou o acesso (bloqueado, permissão negada).</exception>
    string? Ler(string nome);

    /// <summary>Guarda (ou substitui) o segredo.</summary>
    /// <exception cref="CofreException">O cofre não aceitou.</exception>
    void Gravar(string nome, string segredo);

    /// <summary>Apaga o segredo; sem erro se ele não existe.</summary>
    /// <exception cref="CofreException">O cofre não deixou apagar.</exception>
    void Remover(string nome);
}

/// <summary>Uma falha do cofre do sistema, com a mensagem para o usuário.</summary>
public sealed class CofreException(string mensagem) : Exception(mensagem);

/// <summary>O cofre do sistema em que o programa está rodando.</summary>
public static class CofreDoSistema
{
    /// <summary>O nome do serviço (macOS) ou o prefixo do alvo (Windows) dos segredos do programa.</summary>
    public const string Servico = "ProjectPlanner";

    public static ICofreDeSegredos Criar()
    {
        if (OperatingSystem.IsMacOS())
            return new CofreMacOS(Servico);
        if (OperatingSystem.IsWindows())
            return new CofreWindows(Servico);
        return new CofreIndisponivel();
    }
}

/// <summary>Sistema sem cofre conhecido: não guarda nada (a chave só pode vir da variável de ambiente).</summary>
public sealed class CofreIndisponivel : ICofreDeSegredos
{
    public string Nome => "cofre do sistema";

    public bool Disponivel => false;

    public string? Ler(string nome) => null;

    public void Gravar(string nome, string segredo) =>
        throw new CofreException("Este sistema não tem um cofre de senhas que o ProjectPlanner saiba usar. Use a variável de ambiente GEMINI_API_KEY.");

    public void Remover(string nome)
    {
    }
}
