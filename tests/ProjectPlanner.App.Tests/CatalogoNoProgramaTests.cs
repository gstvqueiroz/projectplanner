using ProjectPlanner.App.Services;
using ProjectPlanner.App.ViewModels;
using ProjectPlanner.Core;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Tests;

// Bloco 68: todo tipo e toda ação do catálogo da IA têm como ser aplicados no programa (um tipo novo que só entrou no
// catálogo cai aqui, e não no cartão do usuário como "tipo de atualização desconhecido").
public sealed class CatalogoNoProgramaTests
{
    private readonly Ambiente _ambiente = new();
    private readonly MainWindowViewModel _vm;

    public CatalogoNoProgramaTests()
    {
        _vm = _ambiente.CriarViewModel();
        Ambiente.AdicionarTarefa(_vm, "Alvenaria", 5);
    }

    [Fact]
    public void TodoTipoDoCatalogo_TemCaminhoNoCartao()
    {
        foreach (var tipo in CatalogoDaIa.Tipos)
        {
            var item = _vm.ConferirAtualizacoes([new AtualizacaoDaObra(tipo.Nome, "1")], _vm.RetratoParaOAssistente()).Single();
            item.Problema.Should().NotBe("tipo de atualização desconhecido", tipo.Nome);
        }
    }

    [Fact]
    public void TodaAcaoDasOS_ChegaAOrdem()
    {
        _vm.ConferirAtualizacoes([new AtualizacaoDaObra("os", Titulo: "Alvenaria", Executante: "Equipe A", Tarefas: "1")], _vm.RetratoParaOAssistente())
            .Single().Operacao!.Aplicar(_vm.Cronograma, Ambiente.Segunda);

        foreach (var acao in TiposDeAtualizacao.AcoesDaOs.Where(a => a != "medir"))
        {
            var copia = _vm.Cronograma.Copiar();
            var aplicar = () => new MudarSituacaoDaOrdem(1, acao, "motivo").Aplicar(copia, Ambiente.Segunda);
            // Uma ação fora de hora é recusada pelo domínio (a OS está em rascunho); o que não pode é a ação ser desconhecida.
            try
            {
                aplicar();
            }
            catch (DomainValidationException ex)
            {
                ex.Message.Should().NotStartWith("Ação desconhecida", acao);
            }
        }
        var medir = _vm.ConferirAtualizacoes([new AtualizacaoDaObra("situacaoOs", Percentual: 50, Os: 1, Acao: "medir")], _vm.RetratoParaOAssistente()).Single();
        medir.Operacao.Should().BeOfType<MedirAOrdem>();
    }

    [Fact]
    public void TodaAcaoDasMedicoesDoContrato_EhAceita()
    {
        foreach (var acao in TiposDeAtualizacao.AcoesDaMedicao)
            _vm.ConferirAtualizacoes([new AtualizacaoDaObra("medicaoContrato", Medicao: 1, Acao: acao)], _vm.RetratoParaOAssistente())
                .Single().Problema.Should().Be("não há a medição nº 01", "a ação é conhecida; só falta a medição");
    }
}
