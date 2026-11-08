using ProjectPlanner.Core.Edicao;
using ProjectPlanner.Core.Recursos;

namespace ProjectPlanner.Core.Kanban;

/// <summary>Bloco 78: uma raia do quadro: a chave ("resumo:…", "recurso:…", "sem-resumo", "sem-responsavel") e o nome.</summary>
public sealed record RaiaDoQuadro(string Chave, string Nome);

/// <summary>Bloco 78: o que mudou ao levar cards para outra raia (para o aviso com o Desfazer).</summary>
public sealed record ResultadoDaRaia(string Texto, bool MudouAEstrutura);

/// <summary>
/// Bloco 78 (o plano "Delete, arrastar e Kanban", K6): as raias do quadro. Por resumo, uma raia para cada resumo que tem tarefas
/// (o resumo direto, o mesmo da etiqueta do card) e "Sem resumo" para as do primeiro nível; por responsável, uma para cada
/// profissional do projeto (o primeiro da tarefa) e "Sem responsável". Levar um card para outra raia muda o resumo da tarefa (ela
/// vai para o fim daquele grupo no cronograma; um resumo que fica vazio vira tarefa comum, com as datas que tinha) ou o
/// responsável (o profissional da raia entra no lugar do primeiro; em "Sem responsável", saem os profissionais).
/// </summary>
public static class RegrasDasRaias
{
    public const string SemResumo = "sem-resumo";
    public const string SemResponsavel = "sem-responsavel";

    /// <summary>Muda como o quadro agrupa os cards.</summary>
    public static void DefinirModo(Cronograma cronograma, ModoDasRaias modo)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        cronograma.Quadro.DefinirRaias(modo);
    }

    /// <summary>As raias do modo, de cima para baixo ("Sem resumo" e "Sem responsável" no fim); vazio sem raias.</summary>
    public static IReadOnlyList<RaiaDoQuadro> Raias(Cronograma cronograma, ModoDasRaias modo)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        switch (modo)
        {
            case ModoDasRaias.PorResumo:
                var edt = cronograma.NumeracaoEdt();
                var comFolhas = cronograma.Tarefas.Where(t => !t.EhResumo && t.TarefaPaiId is not null).Select(t => t.TarefaPaiId!.Value).ToHashSet();
                return [.. cronograma.Tarefas.Where(t => comFolhas.Contains(t.Id)).Select(t => new RaiaDoQuadro(Chave("resumo", t.Id), $"{edt[t.Id]} {t.Nome}")),
                    new RaiaDoQuadro(SemResumo, "Sem resumo")];
            case ModoDasRaias.PorResponsavel:
                return [.. cronograma.Recursos.Lista.Where(r => r.Tipo == TipoDeRecurso.Profissional).Select(r => new RaiaDoQuadro(Chave("recurso", r.Id), r.Nome)),
                    new RaiaDoQuadro(SemResponsavel, "Sem responsável")];
            default:
                return [];
        }
    }

    /// <summary>A raia da tarefa no modo: a do resumo direto, ou a do primeiro profissional.</summary>
    public static string RaiaDe(Cronograma cronograma, ModoDasRaias modo, Tarefa tarefa)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(tarefa);
        return modo switch
        {
            ModoDasRaias.PorResumo => tarefa.TarefaPaiId is { } pai ? Chave("resumo", pai) : SemResumo,
            ModoDasRaias.PorResponsavel => PrimeiroProfissional(cronograma, tarefa) is { } a ? Chave("recurso", a.RecursoId) : SemResponsavel,
            _ => "",
        };
    }

    /// <summary>Leva as tarefas para a raia: muda o resumo ou o responsável de cada uma que ainda não está nela.</summary>
    /// <exception cref="DomainValidationException">A raia não existe mais, ou o domínio recusa a mudança.</exception>
    public static ResultadoDaRaia MoverParaRaia(Cronograma cronograma, ModoDasRaias modo, IEnumerable<Guid> tarefas, string chave)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(tarefas);
        var raia = Raias(cronograma, modo).FirstOrDefault(r => r.Chave == chave)
                   ?? throw new DomainValidationException(nameof(QuadroDoProjeto.Raias), "A raia não existe mais no quadro.");
        var pedidas = tarefas.ToHashSet();
        var mudam = cronograma.Tarefas.Where(t => pedidas.Contains(t.Id) && !t.EhResumo && RaiaDe(cronograma, modo, t) != chave).ToList();
        if (mudam.Count == 0)
            return new ResultadoDaRaia("", false);
        var nome = mudam.Count == 1 ? $"\"{mudam[0].Nome}\"" : $"{mudam.Count} tarefas";
        var nomeDaRaia = raia.Nome;

        if (modo == ModoDasRaias.PorResumo)
        {
            var resumos = ResumosAntesDaMudanca.Guardar(cronograma);
            Guid? pai = chave == SemResumo ? null : IdDe(chave);
            foreach (var tarefa in mudam)
                cronograma.DefinirPai(tarefa.Id, pai);
            var viraram = resumos.Aplicar(cronograma);
            var texto = pai is null
                ? $"{nome} {(mudam.Count == 1 ? "saiu" : "saíram")} dos resumos: {(mudam.Count == 1 ? "está" : "estão")} no fim do cronograma, no primeiro nível."
                : $"{nome} {(mudam.Count == 1 ? "agora está" : "agora estão")} em \"{cronograma.Tarefas.First(t => t.Id == pai).Nome}\" (no fim do grupo).";
            if (viraram.Count > 0)
                texto += viraram.Count == 1 ? $" \"{viraram[0]}\" virou tarefa comum." : $" {string.Join(", ", viraram.Select(v => $"\"{v}\""))} viraram tarefas comuns.";
            return new ResultadoDaRaia(texto, true);
        }

        var recursos = cronograma.Recursos;
        Guid? novo = chave == SemResponsavel ? null : IdDe(chave);
        var antes = mudam.Select(t => PrimeiroProfissional(cronograma, t) is { } a ? recursos.Obter(a.RecursoId)?.Nome : null).Distinct().ToList();
        var novas = mudam.ToDictionary(t => t.Id, t => NovasAtribuicoes(cronograma, t, novo));
        foreach (var tarefa in mudam)
            recursos.Atribuir(tarefa, novas[tarefa.Id]);
        var era = antes is [{ } unico] ? $" (era {unico})" : "";
        return new ResultadoDaRaia(novo is null
            ? $"{nome} {(mudam.Count == 1 ? "ficou" : "ficaram")} sem responsável{era}: os profissionais saíram da tarefa."
            : $"{nome} agora {(mudam.Count == 1 ? "é" : "são")} de {nomeDaRaia}{era}.", false);
    }

    // O profissional da raia entra no lugar do primeiro (com a quantidade dele, ou a que já tinha na tarefa); sem raia, saem todos.
    private static List<AtribuicaoDeRecurso> NovasAtribuicoes(Cronograma cronograma, Tarefa tarefa, Guid? novo)
    {
        var primeiro = PrimeiroProfissional(cronograma, tarefa);
        var demais = tarefa.Atribuicoes.Where(a => a != primeiro && a.RecursoId != novo).ToList();
        if (novo is not { } id)
            return [.. demais.Where(a => cronograma.Recursos.Obter(a.RecursoId)?.Tipo != TipoDeRecurso.Profissional)];
        var quantidade = tarefa.Atribuicoes.FirstOrDefault(a => a.RecursoId == id)?.Quantidade ?? primeiro?.Quantidade ?? 1;
        return [new AtribuicaoDeRecurso(id, quantidade), .. demais];
    }

    private static AtribuicaoDeRecurso? PrimeiroProfissional(Cronograma cronograma, Tarefa tarefa) =>
        tarefa.Atribuicoes.FirstOrDefault(a => cronograma.Recursos.Obter(a.RecursoId)?.Tipo == TipoDeRecurso.Profissional);

    private static string Chave(string tipo, Guid id) => $"{tipo}:{id}";

    private static Guid IdDe(string chave) =>
        Guid.TryParse(chave[(chave.IndexOf(':') + 1)..], out var id) ? id : throw new DomainValidationException(nameof(QuadroDoProjeto.Raias), "A raia não existe mais no quadro.");
}
