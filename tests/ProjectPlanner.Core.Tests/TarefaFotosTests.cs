using ProjectPlanner.Core.Gantt;
using static ProjectPlanner.Core.Tests.Datas;

namespace ProjectPlanner.Core.Tests;

public class TarefaFotosTests
{
    private readonly Tarefa _tarefa = new("Fundação", Segunda, 3);

    private FotoTarefa Foto(DateTime data, string? descricao = null, string? arquivo = null) =>
        FotosDeTeste.Nova(_tarefa.Id, data, descricao, arquivo: arquivo);

    [Fact]
    public void TarefaNova_NaoTemFotos()
    {
        _tarefa.Fotos.Should().BeEmpty();
    }

    [Fact]
    public void VariasFotos_FicamEmOrdemDeData_EmpatesNaOrdemEmQueEntraram()
    {
        var (terca, segunda1, segunda2) = (Foto(Terca), Foto(Segunda, "primeira"), Foto(Segunda, "segunda"));

        _tarefa.AdicionarFoto(terca);
        _tarefa.AdicionarFoto(segunda1);
        _tarefa.AdicionarFoto(segunda2);

        _tarefa.Fotos.Should().Equal(segunda1, segunda2, terca);
        _tarefa.FotosNaOrdemDeCadastro.Should().Equal(terca, segunda1, segunda2);
    }

    [Fact]
    public void FotoDeOutraTarefa_Recusada()
    {
        var outra = FotosDeTeste.Nova(Guid.NewGuid(), Segunda);

        var adicionar = () => _tarefa.AdicionarFoto(outra);

        adicionar.Should().Throw<DomainValidationException>().WithMessage("A foto é de outra tarefa, não de \"Fundação\".");
        _tarefa.Fotos.Should().BeEmpty();
    }

    [Fact]
    public void MesmaFotoDuasVezes_Recusada()
    {
        var foto = Foto(Segunda);
        _tarefa.AdicionarFoto(foto);

        var repetir = () => _tarefa.AdicionarFoto(foto);

        repetir.Should().Throw<DomainValidationException>();
        _tarefa.Fotos.Should().ContainSingle();
    }

    [Fact]
    public void MesmaImagem_ComOutroId_Recusada_EmOutraTarefaPode()
    {
        _tarefa.AdicionarFoto(Foto(Segunda, arquivo: "obra.jpg"));

        var repetida = () => _tarefa.AdicionarFoto(Foto(Terca, arquivo: "obra.jpg"));
        var outraTarefa = new Tarefa("Estrutura", Segunda, 2);

        repetida.Should().Throw<DomainValidationException>().WithMessage("obra.jpg: esta imagem já é uma foto de \"Fundação\".");
        _tarefa.TemFotoDaMesmaImagem(FotosDeTeste.Hash("obra.jpg")).Should().BeTrue();
        outraTarefa.Invoking(t => t.AdicionarFoto(FotosDeTeste.Nova(t.Id, Segunda, arquivo: "obra.jpg"))).Should().NotThrow();
    }

    [Fact]
    public void RemoverFoto_TiraSoAquela_EFotoInexistenteERecusada()
    {
        var (a, b) = (Foto(Segunda), Foto(Terca));
        _tarefa.AdicionarFoto(a);
        _tarefa.AdicionarFoto(b);

        _tarefa.RemoverFoto(a.Id);
        var removerDeNovo = () => _tarefa.RemoverFoto(a.Id);

        _tarefa.Fotos.Should().Equal(b);
        removerDeNovo.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void SubstituirFoto_MantemOLugar_EFotoDeFora_Recusada()
    {
        var (a, b) = (Foto(Segunda, "a"), Foto(Terca, "b"));
        _tarefa.AdicionarFoto(a);
        _tarefa.AdicionarFoto(b);
        var editada = a.ComMetadados(new MetadadosFoto(Quarta, descricao: "a editada"));

        _tarefa.SubstituirFoto(editada);

        _tarefa.FotosNaOrdemDeCadastro.Should().Equal(editada, b);
        _tarefa.Foto(a.Id)!.Descricao.Should().Be("a editada");
        _tarefa.Invoking(t => t.SubstituirFoto(Foto(Segunda))).Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Gantt_SoAsBarrasDeTarefasComFotosTemOIcone_ForaDasAlcas()
    {
        var projeto = new Cronograma("Obra", Segunda);
        var comFotos = new Tarefa("Com fotos", Segunda, 3);
        var semFotos = new Tarefa("Sem fotos", Segunda, 3);
        projeto.AdicionarTarefa(comFotos);
        projeto.AdicionarTarefa(semFotos);
        comFotos.AdicionarFoto(FotosDeTeste.Nova(comFotos.Id, Segunda));
        comFotos.AdicionarFoto(FotosDeTeste.Nova(comFotos.Id, Terca));

        var layout = CalculadoraLayoutGantt.Calcular(projeto, CalculadoraCronograma.Calcular(projeto), new OpcoesGantt(20));

        layout.Barras.Select(b => (b.Nome, b.QuantidadeDeFotos)).Should().Equal(("Com fotos", 2), ("Sem fotos", 0));
        var barra = layout.Barras[0]; // X 60 a 120, centro 15: ícone de 132 a 144, 9 a 21
        LocalizadorGantt.IconeDeFotos(barra).Should().Be((132.0, 9.0, 12.0));
        LocalizadorGantt.IconeDeFotosEm(layout, 138, 15)!.Nome.Should().Be("Com fotos");
        LocalizadorGantt.IconeDeFotosEm(layout, 138, 45).Should().BeNull(); // a linha de baixo não tem fotos
        // Onde o ícone começa não há alça (a do término vai até 124), e a alça não chega ao ícone.
        LocalizadorGantt.AlcaEm(layout, 130, 15).Should().BeNull();
        LocalizadorGantt.IconeDeFotosEm(layout, 124, 15).Should().BeNull();
    }
}
