using CommunityToolkit.Mvvm.Input;
using ProjectPlanner.App.Services;
using ProjectPlanner.Core.OrdensDeServico;
using ProjectPlanner.Relatorios;

namespace ProjectPlanner.App.ViewModels;

// Bloco 70 (saiu de OrdensDeServicoViewModel.cs): o PDF e a impressão da OS.
public sealed partial class OrdensDeServicoViewModel
{
    // ---- PDF ----

    private bool TemSelecionadaParaPdf() => Ordem is not null;

    private CabecalhoDaOrdem CabecalhoDoPdf()
    {
        var c = _cabecalho();
        var responsavel = string.Join(" · ", new[] { c.Responsavel, c.Registro }.Where(t => !string.IsNullOrWhiteSpace(t)));
        return new(_cronograma.Nome, _cronograma.Obra, _nomes, Agora.ToString("dd/MM/yyyy HH:mm", Brasil), responsavel.Length > 0 ? responsavel : null, c.Logotipo);
    }

    private string NomeDoArquivo(OrdemDeServico o) =>
        string.Concat($"{o.Codigo} - {o.Dados.Titulo}".Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    [RelayCommand(CanExecute = nameof(TemSelecionadaParaPdf))]
    private async Task SalvarPdf()
    {
        if (!SalvarSePreciso())
            return;
        var o = Ordem!;
        if (await _dialogos.SalvarComoAsync($"Salvar a {o.Codigo} em PDF", NomeDoArquivo(o), "pdf") is not { } caminho)
            return;
        if (await GravarPdf(o, caminho))
            Mensagem = $"Salvo em {Path.GetFileName(caminho)}.";
    }

    /// <summary>Grava o PDF numa pasta temporária e abre no leitor do sistema, para imprimir.</summary>
    [RelayCommand(CanExecute = nameof(TemSelecionadaParaPdf))]
    private async Task Imprimir()
    {
        if (!SalvarSePreciso())
            return;
        var o = Ordem!;
        var pasta = Path.Combine(Path.GetTempPath(), "ProjectPlanner", "Impressao");
        var caminho = Path.Combine(pasta, $"{NomeDoArquivo(o)} {Agora:yyyyMMdd-HHmmss}.pdf");
        try
        {
            Directory.CreateDirectory(pasta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Mensagem = $"Não foi possível preparar a impressão: {ex.Message}";
            return;
        }
        if (!await GravarPdf(o, caminho))
            return;
        await _dialogos.AbrirArquivoAsync(caminho);
        Mensagem = $"O PDF da {o.Codigo} abriu no leitor do sistema: imprima com {(AtalhosDeTeclado.EhMac ? "⌘P" : "Ctrl+P")}.";
    }

    // Grava num temporário e troca no fim (um PDF aberto no leitor não fica pela metade).
    private async Task<bool> GravarPdf(OrdemDeServico ordem, string caminho)
    {
        var cabecalho = CabecalhoDoPdf();
        var temporario = caminho + ".tmp";
        try
        {
            await Task.Run(() =>
            {
                using (var destino = File.Create(temporario))
                    new GeradorOrdemDeServicoPdf().Gerar(ordem, cabecalho, destino);
                File.Move(temporario, caminho, overwrite: true);
            });
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporario); } catch (Exception) { /* nada a fazer */ }
            Mensagem = $"Não foi possível salvar: {ex.Message}";
            return false;
        }
    }
}
