using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

/// <summary>Base dos testes de fotos do App (Bloco 22): galeria aberta pelo comando, fotos pelo processador falso.</summary>
public abstract class TesteDeFotos
{
    protected readonly Ambiente Ambiente = new();
    protected readonly MainWindowViewModel Vm;

    protected TesteDeFotos() => Vm = Ambiente.CriarViewModel();

    protected GaleriaFotosViewModel AbrirGaleria(TarefaViewModel linha)
    {
        Vm.GerenciarFotosCommand.Execute(linha);
        return Ambiente.Dialogos.Galerias.Last();
    }

    /// <summary>Abre a galeria da tarefa e adiciona as fotos (os nomes viram "/fotos/nome").</summary>
    protected async Task<GaleriaFotosViewModel> ComFotos(TarefaViewModel linha, params string[] arquivos)
    {
        var galeria = AbrirGaleria(linha);
        await galeria.AdicionarArquivosAsync(arquivos.Select(a => "/fotos/" + a));
        return galeria;
    }

    protected static FotoNaGaleria Item(GaleriaFotosViewModel galeria, string arquivo) =>
        galeria.Grupos.SelectMany(g => g.Fotos).Single(f => f.Foto.Imagem.NomeArquivoOriginal == arquivo);
}
