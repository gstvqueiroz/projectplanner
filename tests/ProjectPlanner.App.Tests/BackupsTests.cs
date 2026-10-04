using ProjectPlanner.App.Services;
using ProjectPlanner.Core;
using ProjectPlanner.Data;

namespace ProjectPlanner.App.Tests;

// Bloco 25 (R2): as cópias de segurança a cada salvamento, com o repositório SQLite de verdade.
public sealed class BackupsTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pp-backups-" + Guid.NewGuid().ToString("N"));
    private readonly RelogioAndando _relogio = new(new DateTimeOffset(2026, 10, 4, 15, 30, 0, TimeSpan.Zero));
    private readonly BackupsDeProjetos _backups;
    private readonly SqliteProjetoRepository _repositorio;

    public BackupsTests()
    {
        Directory.CreateDirectory(Path.Combine(_pasta, "obras"));
        _backups = new BackupsDeProjetos(Path.Combine(_pasta, "backups"), _relogio);
        _repositorio = new SqliteProjetoRepository(_relogio, caminho => _backups.Guardar(caminho));
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private string Obra(string nome = "obra.pplan") => Path.Combine(_pasta, "obras", nome);

    private static Cronograma Com(params string[] tarefas)
    {
        var cronograma = new Cronograma("Obra", new DateTime(2026, 9, 28));
        foreach (var nome in tarefas)
            cronograma.AdicionarTarefa(new Tarefa(nome, new DateTime(2026, 9, 28), 2));
        return cronograma;
    }

    [Fact]
    public void PrimeiroSalvamento_NadaParaGuardar()
    {
        _repositorio.Salvar(Com("A"), Obra());

        _backups.CopiasDo(Obra()).Should().BeEmpty();
    }

    [Fact]
    public void SalvarDeNovo_GuardaAVersaoAnterior_QueAbreComoProjeto()
    {
        _repositorio.Salvar(Com("A"), Obra());
        _relogio.Andar(TimeSpan.FromMinutes(5));

        _repositorio.Salvar(Com("A", "B"), Obra());

        var copia = _backups.CopiasDo(Obra()).Should().ContainSingle().Subject;
        Path.GetFileName(copia).Should().Be("obra-20261004-153500-000.pplan");
        _repositorio.Abrir(copia).Tarefas.Select(t => t.Nome).Should().Equal("A");
        _repositorio.Abrir(Obra()).Tarefas.Select(t => t.Nome).Should().Equal("A", "B");
    }

    [Fact]
    public void SoAsCincoMaisRecentesFicam()
    {
        for (var i = 1; i <= 8; i++)
        {
            _repositorio.Salvar(Com($"versão {i}"), Obra());
            _relogio.Andar(TimeSpan.FromSeconds(1));
        }

        var copias = _backups.CopiasDo(Obra());
        copias.Should().HaveCount(BackupsDeProjetos.Quantidade);
        copias.Select(c => _repositorio.Abrir(c).Tarefas.Single().Nome).Should().Equal("versão 7", "versão 6", "versão 5", "versão 4", "versão 3");
    }

    [Fact]
    public void MesmoNomeEmPastasDiferentes_NaoSeMisturam()
    {
        Directory.CreateDirectory(Path.Combine(_pasta, "obras", "outra"));
        var outra = Path.Combine(_pasta, "obras", "outra", "obra.pplan");
        _repositorio.Salvar(Com("A"), Obra());
        _repositorio.Salvar(Com("B"), outra);

        _repositorio.Salvar(Com("A2"), Obra());

        (_backups.PastaDo(Obra()) == _backups.PastaDo(outra)).Should().BeFalse();
        (_backups.CopiasDo(Obra()).Count, _backups.CopiasDo(outra).Count).Should().Be((1, 0));
        Path.GetFileName(_backups.PastaDo(Obra())).Should().MatchRegex("^obra-[0-9a-f]{8}$");
    }

    [Fact]
    public void SalvarQueFalha_NaoGuardaCopia()
    {
        _repositorio.Salvar(Com("A"), Obra());
        var invalido = Com("B");
        invalido.Tarefas[0].AdicionarFoto(new FotoTarefa(Guid.NewGuid(), invalido.Tarefas[0].Id,
            new DadosDaImagem("sem-imagem.jpg", 10, 10, 10, new string('a', 64)), new MetadadosFoto(new DateTime(2026, 9, 28)),
            new DateTime(2026, 9, 28)));

        var salvar = () => _repositorio.Salvar(invalido, Obra()); // uma foto sem imagem: nada é salvo

        salvar.Should().Throw<IOException>();
        _backups.CopiasDo(Obra()).Should().BeEmpty("a cópia só é feita quando o arquivo novo ficou pronto");
    }

    [Fact]
    public void GanchoQueLanca_NaoImpedeDeSalvar()
    {
        var repositorio = new SqliteProjetoRepository(_relogio, _ => throw new IOException("disco de backup cheio"));
        repositorio.Salvar(Com("A"), Obra());

        repositorio.Salvar(Com("B"), Obra());

        repositorio.Abrir(Obra()).Tarefas.Single().Nome.Should().Be("B");
    }

    [Fact]
    public void BackupQueFalha_NaoImpedeDeSalvar()
    {
        File.WriteAllText(Path.Combine(_pasta, "backups"), "um arquivo no lugar da pasta");
        _repositorio.Salvar(Com("A"), Obra());

        _repositorio.Salvar(Com("B"), Obra());

        _repositorio.Abrir(Obra()).Tarefas.Single().Nome.Should().Be("B");
        _backups.Guardar(Obra()).Should().BeNull();
    }

    [Fact]
    public void Argumentos_EComandoDeAbrirAPasta()
    {
        var semPasta = () => new BackupsDeProjetos(" ", _relogio);
        semPasta.Should().Throw<ArgumentException>();
        BackupsDeProjetos.PastaPadrao().Should().EndWith(Path.Combine("ProjectPlanner", "backups"));

        var ambiente = new Ambiente();
        var vm = ambiente.CriarViewModel();
        vm.AbrirBackupsCommand.Execute(null);
        ambiente.Dialogos.ArquivoParaSalvar = "/projetos/obra.pplan";
        vm.SalvarProjetoCommand.Execute(null);
        vm.AbrirBackupsCommand.Execute(null);
        ambiente.Dialogos.PastasAbertas.Should().HaveCount(2);
        ambiente.Dialogos.PastasAbertas[0].Should().Be(BackupsDeProjetos.PastaPadrao());
        ambiente.Dialogos.PastasAbertas[1].Should().StartWith(Path.Combine(BackupsDeProjetos.PastaPadrao(), "obra-"));
    }

    private sealed class RelogioAndando(DateTimeOffset inicio) : TimeProvider
    {
        private DateTimeOffset _agora = inicio;

        public void Andar(TimeSpan quanto) => _agora += quanto;

        public override DateTimeOffset GetUtcNow() => _agora;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
