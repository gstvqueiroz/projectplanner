using ProjectPlanner.Core;
using ProjectPlanner.Data;

namespace ProjectPlanner.App.Services;

/// <summary>
/// Bloco 71: o calendário de trabalho dos projetos novos (o Arquivo > Novo). Sem escolha salva, vale o
/// <see cref="CalendarioDoProjeto.Brasil"/> (segunda a sexta, com os feriados nacionais, o Carnaval e o Corpus Christi).
/// </summary>
public interface IPreferenciasDoCalendario
{
    /// <summary>O calendário salvo; nulo se ainda não há ou se não deu para ler.</summary>
    CalendarioDoProjeto? Carregar();

    /// <summary>Grava o calendário. Uma falha ao gravar não interrompe o uso do programa.</summary>
    void Salvar(CalendarioDoProjeto calendario);
}

/// <summary>Bloco 71: o calendário dos projetos novos em <c>calendario.json</c>, ao lado do interface.json.</summary>
public sealed class ArquivoPreferenciasDoCalendario(string caminho) : IPreferenciasDoCalendario
{
    public ArquivoPreferenciasDoCalendario() : this(CaminhoPadrao())
    {
    }

    public string Caminho { get; } = caminho;

    public static string CaminhoPadrao() =>
        Path.Combine(Path.GetDirectoryName(ArquivoPreferenciasColunas.CaminhoPadrao())!, "calendario.json");

    public CalendarioDoProjeto? Carregar()
    {
        try
        {
            return File.Exists(Caminho) ? FormatoDoCalendario.Ler(File.ReadAllText(Caminho)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    public void Salvar(CalendarioDoProjeto calendario)
    {
        var temporario = Caminho + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Caminho)!);
            File.WriteAllText(temporario, FormatoDoCalendario.Serializar(calendario));
            File.Move(temporario, Caminho, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporario); } catch (Exception) { /* nada a fazer */ }
        }
    }
}

/// <summary>Bloco 71: sem arquivo (os testes): guarda na memória.</summary>
public sealed class PreferenciasDoCalendarioEmMemoria : IPreferenciasDoCalendario
{
    public CalendarioDoProjeto? Salvo { get; set; }

    public CalendarioDoProjeto? Carregar() => Salvo;

    public void Salvar(CalendarioDoProjeto calendario) => Salvo = calendario;
}
