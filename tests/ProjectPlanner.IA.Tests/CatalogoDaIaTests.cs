using System.Reflection;
using System.Text.Json.Nodes;
using ProjectPlanner.IA.Gemini;

namespace ProjectPlanner.IA.Tests;

// Bloco 68: o vocabulário das atualizações num lugar só: o registro, a leitura, o esquema do Gemini e as instruções saem do
// catálogo, e estes testes pegam o que ficar de fora quando entrar um tipo ou um campo novo.
public sealed class CatalogoDaIaTests
{
    private static readonly ParameterInfo[] Parametros =
        [.. typeof(AtualizacaoDaObra).GetConstructors().Single().GetParameters().Skip(1)];

    private static string Propriedade(string campo) => char.ToUpperInvariant(campo[0]) + campo[1..];

    [Fact]
    public void Catalogo_TemOsMesmosCamposDoRegistro_NaMesmaOrdemEFormato()
    {
        CatalogoDaIa.Campos.Select(c => Propriedade(c.Nome)).Should().Equal(Parametros.Select(p => p.Name));
        foreach (var (campo, parametro) in CatalogoDaIa.Campos.Zip(Parametros))
            parametro.ParameterType.Should().Be(campo.Formato switch
            {
                FormatoDoCampo.Inteiro => typeof(int?),
                FormatoDoCampo.Numero => typeof(decimal?),
                _ => typeof(string),
            }, campo.Nome);
    }

    [Fact]
    public void Montar_LevaCadaCampoParaAPropriedadeDeMesmoNome()
    {
        foreach (var campo in CatalogoDaIa.Campos)
        {
            object valor = campo.Formato switch
            {
                FormatoDoCampo.Inteiro => (int?)7,
                FormatoDoCampo.Numero => (decimal?)7.5m,
                _ => "x",
            };
            var a = CatalogoDaIa.Montar("percentual", new Dictionary<string, object?> { [campo.Nome] = valor });
            foreach (var p in typeof(AtualizacaoDaObra).GetProperties().Where(p => p.Name is not ("Tipo" or "EqualityContract")))
                p.GetValue(a).Should().Be(p.Name == Propriedade(campo.Nome) ? valor : null, $"o campo {campo.Nome} vai só para {Propriedade(campo.Nome)}");
        }
    }

    [Fact]
    public void Tipos_UsamCamposDoCatalogo_ETodoCampoEhUsado()
    {
        var nomes = CatalogoDaIa.Campos.Select(c => c.Nome).ToHashSet();
        CatalogoDaIa.Tipos.SelectMany(t => t.Campos).Should().OnlyContain(c => nomes.Contains(c));
        nomes.Should().OnlyContain(c => CatalogoDaIa.Tipos.Any(t => t.Campos.Contains(c)), "um campo que nenhum tipo usa sobra");
        CatalogoDaIa.Tipos.Select(t => t.Nome).Should().OnlyHaveUniqueItems().And.Equal(TiposDeAtualizacao.Todos);
        CatalogoDaIa.Tipos.Where(t => t.Acoes is not null).Should().OnlyContain(t => t.Campos.Contains("acao"));
        CatalogoDaIa.Campo("acao").Valores.Should().BeEquivalentTo(CatalogoDaIa.Tipos.SelectMany(t => t.Acoes ?? []).Distinct());
    }

    [Fact]
    public void EsquemaDoGemini_SaiDoCatalogo()
    {
        var item = AssistenteGemini.EsquemaDaResposta()["properties"]!["atualizacoes"]!["items"]!["properties"]!.AsObject();

        item.Select(p => p.Key).Should().Equal(["tipo", .. CatalogoDaIa.Campos.Select(c => c.Nome)]);
        item["tipo"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>()).Should().Equal(TiposDeAtualizacao.Todos);
        foreach (var campo in CatalogoDaIa.Campos)
        {
            item[campo.Nome]!["type"]!.GetValue<string>().Should().Be(campo.Formato switch
            {
                FormatoDoCampo.Inteiro => "INTEGER",
                FormatoDoCampo.Numero => "NUMBER",
                _ => "STRING",
            });
            (item[campo.Nome]!["enum"] as JsonArray)?.Select(v => v!.GetValue<string>()).Should().Equal(campo.Valores ?? []);
            if (campo.Valores is null)
                item[campo.Nome]!["enum"].Should().BeNull();
        }
    }

    [Fact]
    public void Leitura_DeCadaTipoComTodosOsCamposDele()
    {
        foreach (var tipo in CatalogoDaIa.Tipos)
        {
            var json = new JsonObject { ["tipo"] = tipo.Nome };
            var esperado = new Dictionary<string, object?>();
            foreach (var nome in tipo.Campos)
            {
                var campo = CatalogoDaIa.Campo(nome);
                (json[nome], esperado[nome]) = campo.Formato switch
                {
                    FormatoDoCampo.Inteiro => ((JsonNode)3, (object?)3),
                    FormatoDoCampo.Numero => ((JsonNode)"12,5", (object?)12.5m), // como os modelos às vezes mandam
                    _ => ((JsonNode)(campo.Valores?[0] ?? $" {nome} "), (object?)(campo.Valores?[0] ?? nome)),
                };
            }
            var resposta = FormatoDaResposta.Interpretar(new JsonObject
            {
                ["mensagem"] = "ok",
                ["alteracoes"] = new JsonArray(),
                ["atualizacoes"] = new JsonArray(json),
            }.ToJsonString());

            resposta.Atualizacoes.Single().Should().Be(CatalogoDaIa.Montar(tipo.Nome, esperado), tipo.Nome);
        }
    }

    [Fact]
    public void Instrucoes_CitamTodoTipoCampoAcaoTelaEAba()
    {
        var texto = InstrucoesDoAssistente.Base;

        foreach (var nome in CatalogoDaIa.Tipos.Select(t => t.Nome).Concat(CatalogoDaIa.Campos.Select(c => c.Nome))
                     .Concat(CatalogoDaIa.Campos.SelectMany(c => c.Valores ?? [])).Concat(TelasDoPrograma.Todas)
                     .Concat(TelasDoPrograma.Abas.Values.SelectMany(a => a)))
            texto.Should().Contain(nome);
        texto.Should().Contain(CatalogoDaIa.Referencia);
        CatalogoDaIa.Referencia.Should().Contain("situacaoOs: os, acao, percentual, descricao, documento")
            .And.Contain("acao de medicaoContrato = aprovar, pagar").And.Contain("tipoDoRecurso = profissional, material, equipamento");
    }

    [Fact]
    public void Instrucoes_SemAContradicaoDosPagamentosDasOS()
    {
        // Até a 0.67 o parágrafo das OS dizia que pagamentos e retenção eram só na tela, e o mapa do controle, que a IA pagava.
        InstrucoesDoAssistente.Obra.Should().NotContain("Pagamentos e a retenção se registram na tela da OS");
        InstrucoesDoAssistente.Obra.Should().Contain("imprimirGantt: A4, A3, A2, A1, A0, Carta, Oficio")
            .And.Contain("relatorioDasOrdens: todas, abertas, encerradas, vencidas");
    }
}
