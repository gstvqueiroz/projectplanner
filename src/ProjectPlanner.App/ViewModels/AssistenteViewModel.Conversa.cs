using ProjectPlanner.Core.Assistente;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.ViewModels;

// Bloco 70 (saiu de AssistenteViewModel.cs): a conversa guardada no projeto.
public sealed partial class AssistenteViewModel
{
    // ---- Bloco 26: a conversa guardada no projeto ----

    // Acrescenta a mensagem no painel e, se não for um aviso local, na conversa do projeto.
    private void Acrescentar(MensagemChat mensagem, AlteracoesNaConversa? alteracoes = null, CartaoDeAtualizacoes? cartao = null, TelaParaAbrir? tela = null)
    {
        int? indice = null;
        if (!mensagem.EhErro)
        {
            _conversa.Acrescentar(ParaAConversa(mensagem, alteracoes) with
            {
                ResultadoDasAlteracoes = TextoGuardado(alteracoes, cartao),
            });
            indice = _conversa.Mensagens.Count - 1;
        }
        Mensagens.Add(new MensagemNaTela(mensagem, alteracoes) { IndiceNaConversa = indice, Cartao = cartao, Tela = tela });
        AtualizarEstudo();
    }

    // Mostra a conversa guardada no projeto (as alterações de sessões anteriores aparecem como texto, sem Desfazer).
    private void MostrarConversa()
    {
        Mensagens.Clear();
        for (var i = 0; i < _conversa.Mensagens.Count; i++)
        {
            var m = _conversa.Mensagens[i];
            var chat = new MensagemChat(m.Autor == AutorNaConversa.Usuario ? AutorMensagem.Usuario : AutorMensagem.Assistente,
                m.Texto, m.DataHora, [.. m.Anexos.Select(a => new AnexoIA(a.Caminho, a.Tamanho))], Resposta: DaConversa(m));
            var alteracoes = m.ResultadoDasAlteracoes is { } texto ? AlteracoesNaConversa.Salvas(texto, m.AlteracoesAplicadas) : null;
            Mensagens.Add(new MensagemNaTela(chat, alteracoes) { IndiceNaConversa = i });
        }
        AtualizarFicha();
        AtualizarEstudo();
    }

    private static MensagemDaConversa ParaAConversa(MensagemChat m, AlteracoesNaConversa? alteracoes)
    {
        var r = m.Resposta;
        return new MensagemDaConversa(
            m.Autor == AutorMensagem.Usuario ? AutorNaConversa.Usuario : AutorNaConversa.Assistente,
            m.Texto,
            m.DataHora,
            r?.Tipo switch
            {
                TipoDeResposta.EscolherModo => TipoDaMensagem.EscolhaDeModo,
                TipoDeResposta.Perguntas => TipoDaMensagem.Perguntas,
                TipoDeResposta.Plano => TipoDaMensagem.Plano,
                _ => TipoDaMensagem.Texto,
            },
            alteracoes?.Texto,
            alteracoes?.Aplicadas ?? false)
        {
            Anexos = [.. m.Anexos.Select(a => new AnexoNaConversa(a.CaminhoLocal, a.TamanhoEmBytes))],
            Perguntas = [.. (r?.Perguntas ?? []).Select(p => new PerguntaNaConversa(p.Texto, p.Opcoes))],
            Plano = r?.Plano is { } plano
                ? new PlanoNaConversa([.. plano.Etapas.Select(e => new EtapaDoPlano(e.Nome, e.Duracao, e.Observacao))], plano.Termino, plano.Premissas)
                : null,
        };
    }

    private static RespostaIA? DaConversa(MensagemDaConversa m) => m.Tipo switch
    {
        TipoDaMensagem.EscolhaDeModo => new RespostaIA(m.Texto) { Tipo = TipoDeResposta.EscolherModo },
        TipoDaMensagem.Perguntas => new RespostaIA(m.Texto)
        {
            Tipo = TipoDeResposta.Perguntas,
            Perguntas = [.. m.Perguntas.Select(p => new PerguntaIA(p.Texto, p.Opcoes))],
        },
        TipoDaMensagem.Plano when m.Plano is { } plano => new RespostaIA(m.Texto)
        {
            Tipo = TipoDeResposta.Plano,
            Plano = new PlanoIA([.. plano.Etapas.Select(e => new EtapaIA(e.Nome, e.DuracaoEmDias, e.Observacao))], plano.TerminoEstimado, plano.Premissas),
        },
        _ => null,
    };
}
