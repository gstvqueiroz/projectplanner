using ProjectPlanner.Core;
using ProjectPlanner.Core.OrdensDeServico;

namespace ProjectPlanner.Data.Tests;

// Bloco 58: as ordens de serviço no .pplan.
public sealed class OrdensNoArquivoTests : TesteComArquivo
{
    [Fact]
    public void Ordens_VoltamIguais_ComHistoricoEVerificacao_ESemOrdensNaoGrava()
    {
        var c = new Cronograma("Obra", Segunda);
        var a = new Tarefa("Alvenaria", Segunda, 5);
        c.AdicionarTarefa(a);
        Repositorio.Salvar(c, Caminho);
        Contar("OrdensDeServicoDoProjeto").Should().Be(0);

        var agora = new DateTime(2026, 9, 28, 9, 30, 0);
        var dados = new DadosDaOrdem("Alvenaria", TipoDeExecutante.Terceiro, "Construtora Silva", null, "11 9999", [a.Id],
            [new ItemDaOrdem(a.Id, "Alvenaria de blocos", "m²", 120.5m, 32.1m), new ItemDaOrdem(null, "Andaime", "vb", 1, 300)],
            new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 2), FormaDePagamentoDaOrdem.PorMedicao, "Usar EPI.\nMaterial da obra.",
            [new ItemDeVerificacao("Prumo conferido"), new ItemDeVerificacao("Limpeza")]);
        var os = OrdemDeServico.Nova(Guid.NewGuid(), 1, dados, agora).Emitir(agora).Aceitar(agora.AddHours(1), "João").MarcarVerificacao(1, true).MandadaAoTelegram();
        c.OrdensDeServico.Adicionar(os);
        c.OrdensDeServico.Adicionar(OrdemDeServico.Nova(Guid.NewGuid(), 2, new DadosDaOrdem("Pintura", TipoDeExecutante.EquipePropria, null, null, null,
            null, null, null, null, FormaDePagamentoDaOrdem.Diaria, null, null), agora));
        Repositorio.Salvar(c, Caminho);

        var aberta = Repositorio.Abrir(Caminho).OrdensDeServico.Todas;

        aberta.Should().HaveCount(2);
        var lida = aberta[0];
        (lida.Id, lida.Numero, lida.Situacao, lida.Emissao, lida.Total).Should().Be((os.Id, 1, SituacaoDaOrdem.Aceita, new DateOnly(2026, 9, 28), os.Total));
        lida.Dados.Should().BeEquivalentTo(os.Dados);
        lida.Historico.Should().Equal(os.Historico);
        aberta[1].Dados.Executante.Should().BeNull();
        (lida.NoTelegram, aberta[1].NoTelegram).Should().Be((true, false));
    }

    [Theory]
    [InlineData(1, "{ ruim")]
    [InlineData(1, """{"ordens":[{"id":"6f9619ff-8b86-d011-b42d-00cf4fc964ff","numero":0,"situacao":"Rascunho","titulo":"x","tipo":"Terceiro","formaDePagamento":"Empreitada"}]}""")]
    [InlineData(1, """{"ordens":[{"id":"6f9619ff-8b86-d011-b42d-00cf4fc964ff","numero":1,"situacao":"Voando","titulo":"x","tipo":"Terceiro","formaDePagamento":"Empreitada"}]}""")]
    [InlineData(2, """{"ordens":[]}""")]
    public void OrdensComDefeito_ArquivoRecusado(int versao, string dados)
    {
        var c = new Cronograma("Obra", Segunda);
        c.OrdensDeServico.Adicionar(OrdemDeServico.Nova(Guid.NewGuid(), 1, new DadosDaOrdem("x", TipoDeExecutante.Terceiro, null, null, null, null, null,
            null, null, FormaDePagamentoDaOrdem.Empreitada, null, null), DateTime.Now));
        Repositorio.Salvar(c, Caminho);
        Executar("UPDATE OrdensDeServicoDoProjeto SET Versao = $p0, Dados = $p1", versao, dados);

        var abrir = () => Repositorio.Abrir(Caminho);

        abrir.Should().Throw<InvalidDataException>();
    }
}
