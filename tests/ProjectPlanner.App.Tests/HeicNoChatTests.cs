using System.Diagnostics;
using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;

namespace ProjectPlanner.App.Tests;

// 0.47.2: as fotos HEIC anexadas ao chat viram JPG (os níveis com Claude não leem HEIC).
public sealed class HeicNoChatTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-heic-" + Guid.NewGuid().ToString("N"));

    public HeicNoChatTests() => Directory.CreateDirectory(_pasta);

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private sealed class ConversorFalso(string pasta) : IConversorDeHeic
    {
        public List<string> Pedidos { get; } = [];
        public bool Falhar { get; set; }

        public string? ParaJpeg(string caminho)
        {
            Pedidos.Add(Path.GetFileName(caminho));
            if (Falhar)
                return null;
            var destino = Path.Combine(pasta, Path.GetFileNameWithoutExtension(caminho) + ".jpg");
            File.WriteAllBytes(destino, [0xFF, 0xD8, 0xFF]);
            return destino;
        }
    }

    private string Arquivo(string nome)
    {
        var caminho = Path.Combine(_pasta, nome);
        File.WriteAllBytes(caminho, [1, 2, 3]);
        return caminho;
    }

    [Fact]
    public void Anexar_HeicViraJpg_OsOutrosSeguem_ESemConversorOuComFalha_OHeicSegue()
    {
        var ambiente = new Ambiente();
        var vm = ambiente.CriarViewModel();
        var conversor = new ConversorFalso(_pasta);
        vm.Assistente.ConversorDeHeic = conversor;
        ambiente.Dialogos.DocumentosParaAnexar.AddRange([Arquivo("laje.HEIC"), Arquivo("planta.pdf")]);

        vm.Assistente.AnexarCommand.Execute(null);

        vm.Assistente.AnexosPendentes.Select(a => a.Nome).Should().Equal("laje.jpg", "planta.pdf");
        conversor.Pedidos.Should().Equal("laje.HEIC");
        vm.Assistente.AvisoDeAnexo.Should().Be("Convertida para JPG (todos os níveis leem): laje.HEIC.");

        vm.Assistente.AnexosPendentes.Clear();
        conversor.Falhar = true;
        ambiente.Dialogos.DocumentosParaAnexar.Clear();
        ambiente.Dialogos.DocumentosParaAnexar.Add(Arquivo("fachada.heif"));
        vm.Assistente.AnexarCommand.Execute(null);
        (vm.Assistente.AnexosPendentes.Single().Nome, vm.Assistente.AvisoDeAnexo).Should().Be(("fachada.heif", ""));

        vm.Assistente.ConversorDeHeic = null;
        vm.Assistente.AnexosPendentes.Clear();
        vm.Assistente.AnexarCommand.Execute(null);
        vm.Assistente.AnexosPendentes.Single().Nome.Should().Be("fachada.heif", "no Windows (sem conversor) o HEIC segue como está");
    }

    [Fact]
    public void Sips_NoMac_ConverteDeVerdade()
    {
        if (ConversorDeHeicPeloSips.DoSistema() is null)
            return; // só no Mac
        // Uma imagem PNG de 1×1, convertida para HEIC pelo próprio sips, e depois de volta para JPG pelo conversor.
        var png = Path.Combine(_pasta, "ponto.png");
        File.WriteAllBytes(png, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
        var heic = Path.Combine(_pasta, "ponto.heic");
        using (var p = Process.Start(new ProcessStartInfo(ConversorDeHeicPeloSips.Sips, ["-s", "format", "heic", png, "--out", heic])
               { RedirectStandardOutput = true, RedirectStandardError = true }))
            p!.WaitForExit(30_000);
        if (!File.Exists(heic))
            return; // este Mac não grava HEIC: nada a conferir

        var jpg = new ConversorDeHeicPeloSips(_pasta).ParaJpeg(heic);

        jpg.Should().NotBeNull().And.EndWith("ponto.jpg");
        File.ReadAllBytes(jpg!).Take(2).Should().Equal(0xFF, 0xD8);
        new ConversorDeHeicPeloSips(_pasta).ParaJpeg(Path.Combine(_pasta, "nao-existe.heic")).Should().BeNull();
    }
}
