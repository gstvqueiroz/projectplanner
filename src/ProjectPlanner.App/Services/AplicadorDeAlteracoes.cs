using System.Globalization;
using ProjectPlanner.Core;
using ProjectPlanner.IA;

namespace ProjectPlanner.App.Services;

/// <summary>O que um lote de alterações do assistente fez, para a conversa ("12 tarefas criadas · 15 vínculos criados").</summary>
public sealed record ResumoDasAlteracoes(int Criadas, int Alteradas, int Removidas, int VinculosCriados, int VinculosRemovidos,
    int Movidas, bool ProjetoAlterado)
{
    public bool Vazio => Criadas + Alteradas + Removidas + VinculosCriados + VinculosRemovidos + Movidas == 0 && !ProjetoAlterado;

    public string Texto
    {
        get
        {
            var partes = new List<string>();
            Contar(partes, Criadas, "tarefa criada", "tarefas criadas");
            Contar(partes, Alteradas, "tarefa alterada", "tarefas alteradas");
            Contar(partes, Removidas, "tarefa removida", "tarefas removidas");
            Contar(partes, Movidas, "tarefa movida", "tarefas movidas");
            Contar(partes, VinculosCriados, "vínculo criado", "vínculos criados");
            Contar(partes, VinculosRemovidos, "vínculo removido", "vínculos removidos");
            if (ProjetoAlterado)
                partes.Add("projeto alterado");
            return partes.Count == 0 ? "Nenhuma mudança." : string.Join(" · ", partes);
        }
    }

    private static void Contar(List<string> partes, int quantidade, string uma, string varias)
    {
        if (quantidade > 0)
            partes.Add(quantidade == 1 ? $"1 {uma}" : $"{quantidade} {varias}");
    }
}

/// <summary>Uma alteração do assistente que não pôde ser aplicada (e por isso nenhuma foi).</summary>
public sealed class AlteracaoRecusadaException(int numero, AlteracaoDoCronograma alteracao, string motivo)
    : Exception($"Alteração {numero} ({alteracao}): {motivo}")
{
    /// <summary>A posição da alteração na lista, a partir de 1.</summary>
    public int Numero { get; } = numero;

    public AlteracaoDoCronograma Alteracao { get; } = alteracao;

    public string Motivo { get; } = motivo;
}

/// <summary>
/// Aplica as alterações do assistente num cronograma (Bloco 24), na ordem, pelos comandos do domínio e com as regras
/// dele. Quem chama passa uma cópia: se uma alteração falhar, a cópia é jogada fora e o cronograma aberto não muda.
/// </summary>
/// <remarks>
/// As tarefas que já existiam são achadas pelo Nº do retrato mandado com o pedido (ou pela EDT, "1.2"); as criadas,
/// pela referência que o assistente deu a elas. Tarefas novas começam no início do projeto: as datas saem do cálculo.
/// </remarks>
public static class AplicadorDeAlteracoes
{
    /// <exception cref="AlteracaoRecusadaException">Uma alteração não vale; o cronograma pode ter ficado pela metade.</exception>
    public static ResumoDasAlteracoes Aplicar(Cronograma cronograma, IReadOnlyList<AlteracaoDoCronograma> alteracoes, ContextoDoProjeto retrato)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(alteracoes);
        ArgumentNullException.ThrowIfNull(retrato);
        var contexto = new Contexto(cronograma, retrato);
        for (var i = 0; i < alteracoes.Count; i++)
        {
            var alteracao = alteracoes[i];
            try
            {
                Aplicar(contexto, alteracao);
            }
            catch (DomainValidationException ex)
            {
                throw new AlteracaoRecusadaException(i + 1, alteracao, ex.Message);
            }
            catch (RecusaException ex)
            {
                throw new AlteracaoRecusadaException(i + 1, alteracao, ex.Message);
            }
        }
        return contexto.Resumo();
    }

    private static void Aplicar(Contexto c, AlteracaoDoCronograma a)
    {
        switch (a.Acao)
        {
            case AcoesNoCronograma.Criar:
                Criar(c, a);
                break;
            case AcoesNoCronograma.Alterar:
                Alterar(c, a);
                break;
            case AcoesNoCronograma.Remover:
                var removida = c.Tarefa(a.Tarefa, "tarefa");
                c.Removidas += 1 + c.Cronograma.DescendentesDe(removida.Id).Count;
                c.Cronograma.RemoverTarefa(removida.Id);
                break;
            case AcoesNoCronograma.Vincular:
                Vincular(c, a);
                break;
            case AcoesNoCronograma.Desvincular:
                var (de, para) = (c.Tarefa(a.De, "de"), c.Tarefa(a.Para, "para"));
                c.Cronograma.RemoverPredecessora(de.Id, para.Id);
                c.VinculosRemovidos++;
                break;
            case AcoesNoCronograma.Mover:
                c.Cronograma.DefinirPai(c.Tarefa(a.Tarefa, "tarefa").Id, c.Pai(a.Pai)?.Id);
                c.Movidas++;
                break;
            case AcoesNoCronograma.Projeto:
                Projeto(c, a);
                break;
            case AcoesNoCronograma.Limpar:
                foreach (var tarefa in c.Cronograma.Tarefas.Where(t => t.TarefaPaiId is null).ToList())
                {
                    c.Removidas += 1 + c.Cronograma.DescendentesDe(tarefa.Id).Count;
                    c.Cronograma.RemoverTarefa(tarefa.Id);
                }
                break;
            default:
                throw new RecusaException(
                    $"a ação \"{a.Acao}\" não existe; use uma destas: {string.Join(", ", AcoesNoCronograma.Todas)}.");
        }
    }

    private static void Criar(Contexto c, AlteracaoDoCronograma a)
    {
        var referencia = Limpar(a.Ref) ?? throw new RecusaException("falta a referência (ref) da tarefa nova.");
        if (c.Existe(referencia))
            throw new RecusaException($"a referência \"{referencia}\" já está em uso; escolha outra (como n1, n2...).");
        if (string.IsNullOrWhiteSpace(a.Nome))
            throw new RecusaException("falta o nome da tarefa.");
        var tarefa = new Tarefa(a.Nome, c.Cronograma.DataInicio, a.Duracao ?? 1);
        c.Cronograma.AdicionarTarefa(tarefa, c.Pai(a.Pai)?.Id);
        c.Novas[referencia] = tarefa.Id;
        if (a.Percentual is { } percentual)
            tarefa.AtualizarPercentualConcluido(percentual);
        if (!string.IsNullOrWhiteSpace(a.Notas))
            tarefa.AlterarNotas(a.Notas);
        c.Criadas++;
    }

    private static void Alterar(Contexto c, AlteracaoDoCronograma a)
    {
        var tarefa = c.Tarefa(a.Tarefa, "tarefa");
        if (a.Nome is null && a.Duracao is null && a.Percentual is null && a.Notas is null)
            throw new RecusaException("não diz o que mudar (nome, duracao, percentual ou notas).");
        if (!string.IsNullOrWhiteSpace(a.Nome))
            tarefa.Renomear(a.Nome);
        if (a.Duracao is { } duracao && duracao != tarefa.Duracao)
            tarefa.AlterarDuracao(duracao);
        if (a.Percentual is { } percentual && percentual != tarefa.PercentualConcluido)
            tarefa.AtualizarPercentualConcluido(percentual);
        if (a.Notas is not null)
            tarefa.AlterarNotas(a.Notas);
        c.Alteradas++;
    }

    private static void Vincular(Contexto c, AlteracaoDoCronograma a)
    {
        var (de, para) = (c.Tarefa(a.De, "de"), c.Tarefa(a.Para, "para"));
        var vinculo = new Predecessora(de.Id, para.Id, TipoDoVinculo(a.Tipo), a.Defasagem ?? 0);
        // Um vínculo que já existe entre as duas é trocado pelo novo (outro tipo ou defasagem).
        if (c.Cronograma.PredecessorasDe(para.Id).Any(p => p.TarefaOrigemId == de.Id))
            c.Cronograma.RemoverPredecessora(de.Id, para.Id);
        c.Cronograma.AdicionarPredecessora(vinculo);
        c.VinculosCriados++;
    }

    private static void Projeto(Contexto c, AlteracaoDoCronograma a)
    {
        if (string.IsNullOrWhiteSpace(a.Nome) && string.IsNullOrWhiteSpace(a.Inicio))
            throw new RecusaException("não diz o que mudar no projeto (nome ou inicio).");
        if (!string.IsNullOrWhiteSpace(a.Nome))
            c.Cronograma.Renomear(a.Nome);
        if (!string.IsNullOrWhiteSpace(a.Inicio))
        {
            if (!DateTime.TryParseExact(a.Inicio.Trim(), ["yyyy-MM-dd", "dd/MM/yyyy"], CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var inicio))
                throw new RecusaException($"a data \"{a.Inicio}\" não está no formato AAAA-MM-DD.");
            c.Cronograma.AlterarDataInicio(inicio);
        }
        c.ProjetoAlterado = true;
    }

    private static TipoDependencia TipoDoVinculo(string? tipo) => (tipo ?? "TI").Trim().ToUpperInvariant() switch
    {
        "" or "TI" or "FS" => TipoDependencia.FS,
        "II" or "SS" => TipoDependencia.SS,
        "TT" or "FF" => TipoDependencia.FF,
        "IT" or "SF" => TipoDependencia.SF,
        _ => throw new RecusaException($"o tipo de vínculo \"{tipo}\" não existe; use TI, II, TT ou IT."),
    };

    // "#3", " 3 ", "n1" → "3", "n1"; vazio → nulo.
    private static string? Limpar(string? referencia)
    {
        var limpa = (referencia ?? "").Trim().TrimStart('#').Trim();
        return limpa.Length == 0 ? null : limpa;
    }

    private sealed class RecusaException(string mensagem) : Exception(mensagem);

    private sealed class Contexto(Cronograma cronograma, ContextoDoProjeto retrato)
    {
        public Cronograma Cronograma { get; } = cronograma;
        public Dictionary<string, Guid> Novas { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int Criadas, Alteradas, Removidas, VinculosCriados, VinculosRemovidos, Movidas;
        public bool ProjetoAlterado;

        public bool Existe(string referencia) => Novas.ContainsKey(referencia) || DoRetrato(referencia) is not null;

        /// <summary>O resumo onde a tarefa fica; nulo (primeiro nível) para vazio, "raiz" ou "0".</summary>
        public Tarefa? Pai(string? referencia)
        {
            var limpa = Limpar(referencia);
            return limpa is null || limpa is "0" || limpa.Equals("raiz", StringComparison.OrdinalIgnoreCase) ? null : Tarefa(limpa, "pai");
        }

        public Tarefa Tarefa(string? referencia, string campo)
        {
            var limpa = Limpar(referencia) ?? throw new RecusaException($"falta a tarefa (campo {campo}).");
            Guid id;
            if (Novas.TryGetValue(limpa, out var nova))
                id = nova;
            else if (DoRetrato(limpa) is { } existente)
                id = existente.Id;
            else
                throw new RecusaException($"a tarefa \"{limpa}\" (campo {campo}) não existe: use o Nº de uma linha do retrato " +
                                          "ou a referência de uma tarefa criada antes nesta resposta.");
            return Cronograma.Tarefas.FirstOrDefault(t => t.Id == id)
                   ?? throw new RecusaException($"a tarefa \"{limpa}\" (campo {campo}) já foi removida.");
        }

        // O Nº do retrato ("3") ou, com ponto, a EDT ("1.2").
        private TarefaNoContexto? DoRetrato(string referencia) =>
            int.TryParse(referencia, NumberStyles.None, CultureInfo.InvariantCulture, out var numero)
                ? retrato.Tarefas.FirstOrDefault(t => t.Numero == numero)
                : referencia.Contains('.') ? retrato.Tarefas.FirstOrDefault(t => t.Edt == referencia) : null;

        public ResumoDasAlteracoes Resumo() =>
            new(Criadas, Alteradas, Removidas, VinculosCriados, VinculosRemovidos, Movidas, ProjetoAlterado);
    }
}
