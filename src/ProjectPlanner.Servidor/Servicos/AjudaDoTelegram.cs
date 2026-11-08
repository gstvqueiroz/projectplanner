namespace ProjectPlanner.Servidor.Servicos;

/// <summary>
/// Bloco 83: a ajuda do bot. /ajuda mostra o começo com um botão por assunto; tocar num assunto troca a mesma mensagem pelos
/// exemplos dele (com o botão de voltar), e os atalhos de baixo respondem os comandos que não usam a IA. Também o menu de
/// comandos que o Telegram mostra ao digitar "/" (registrado ao subir, um para os chats privados e outro para os grupos).
/// </summary>
public static class AjudaDoTelegram
{
    /// <summary>Os dados dos botões de assunto ("aj:diario"; "aj:menu" volta ao começo).</summary>
    public const string PrefixoDoAssunto = "aj:";

    /// <summary>Os dados dos atalhos para os comandos ("cmd:resumo").</summary>
    public const string PrefixoDoComando = "cmd:";

    public const string Menu = "menu";

    /// <summary>Um assunto: a chave (nos dados do botão) e o rótulo do botão.</summary>
    public sealed record Assunto(string Chave, string Botao);

    public static IReadOnlyList<Assunto> Assuntos { get; } =
    [
        new("andamento", "📈 Andamento"),
        new("diario", "📒 Diário (RDO)"),
        new("fotos", "📷 Fotos"),
        new("custos", "💰 Custos e medições"),
        new("os", "📋 Ordens de serviço"),
        new("kanban", "🗂 Kanban"),
        new("grupo", "👷 Equipe no grupo"),
        new("comandos", "⌨️ Comandos"),
    ];

    /// <summary>Os comandos que os atalhos da ajuda respondem (sem a IA).</summary>
    public static IReadOnlyList<string> Atalhos { get; } = ["resumo", "hoje", "atrasadas"];

    public static IReadOnlyList<ComandoDoBot> ComandosDoPrivado { get; } =
    [
        new("ajuda", "Como usar o bot, com exemplos"),
        new("resumo", "A obra em números"),
        new("hoje", "O que está em andamento hoje e o RDO do dia"),
        new("atrasadas", "As tarefas atrasadas"),
        new("caixa", "O que espera você aplicar no programa"),
        new("desfazer", "Tira da caixa a última coisa que você mandou"),
        new("obra", "Mostra e troca a obra"),
        new("sair", "Desliga este chat da sua conta"),
    ];

    public static IReadOnlyList<ComandoDoBot> ComandosDoGrupo { get; } =
    [
        new("ajuda", "Como usar o bot no grupo, com exemplos"),
        new("resumo", "A obra em números"),
        new("hoje", "O que está em andamento hoje e o RDO do dia"),
        new("atrasadas", "As tarefas atrasadas"),
        new("os", "As ordens de serviço abertas"),
        new("recusar", "Recusa uma OS: /recusar 14 e o motivo"),
        new("caixa", "O que o grupo mandou e espera o programa"),
        new("desfazer", "Tira da caixa a última coisa que você mandou"),
        new("equipe", "A equipe do grupo e os papéis"),
        new("papel", "Muda o papel (respondendo à mensagem da pessoa)"),
        new("obra", "Liga o grupo a uma obra (só o dono)"),
        new("sair", "Desliga o grupo (só o dono)"),
    ];

    /// <summary>O começo da ajuda: o que o bot faz e onde as coisas vão parar.</summary>
    public static string Inicio(bool grupo, string? obra, string bot)
    {
        var daObra = obra is null ? "" : $"\nObra: {obra}" + (grupo ? "" : " (/obra troca)");
        return grupo
            ? $"👷 Assistente da obra neste grupo{daObra}\n\n" +
              $"O bot só lê o que é para ele: fotos, mensagens com #atualizacao, mensagens que o marcam ({Arroba(bot)}) e respostas a ele " +
              "(também em áudio). " +
              "A conversa da equipe ele não lê. O que muda a obra vai para a caixa, e o dono confere e aplica no programa.\n\n" +
              "Toque num assunto para ver exemplos:"
            : $"👷 Assistente da obra no Telegram{daObra}\n\n" +
              "Escreva (ou mande um áudio) como falaria com o engenheiro: eu respondo e anoto. O que muda a obra fica na caixa de entrada, e você confere e " +
              "aplica no programa (Conta > Buscar do Telegram).\n\n" +
              "Toque num assunto para ver exemplos:";
    }

    /// <summary>Os botões do começo: dois assuntos por linha e, na última, os atalhos.</summary>
    public static IReadOnlyList<BotaoDoTelegram> BotoesDoMenu { get; } =
    [
        .. Assuntos.Select((a, i) => new BotaoDoTelegram(a.Botao, PrefixoDoAssunto + a.Chave, i / 2)),
        new("📊 Resumo", PrefixoDoComando + "resumo", 99),
        new("📅 Hoje", PrefixoDoComando + "hoje", 99),
        new("⚠️ Atrasadas", PrefixoDoComando + "atrasadas", 99),
    ];

    public static IReadOnlyList<BotaoDoTelegram> BotoesDoAssunto { get; } = [new("⬅️ Voltar aos assuntos", PrefixoDoAssunto + Menu)];

    /// <summary>Os exemplos de um assunto; nulo se a chave não existe.</summary>
    public static string? Texto(string chave, bool grupo, string bot)
    {
        var noGrupo = grupo ? $"\n\nNo grupo, comece a mensagem com #atualizacao ou marque {Arroba(bot)}." : "";
        return chave switch
        {
            "andamento" =>
                "📈 Andamento das tarefas\n\n" +
                "Conte o que foi feito, do jeito que falaria:\n" +
                "• \"a alvenaria do térreo está em 60%\"\n" +
                "• \"terminamos a fundação\"\n" +
                "• \"fizemos 20 m² de reboco hoje\": eu calculo o % pelo total da tarefa (20 de 200 m² são 10% a mais)\n" +
                "• \"o contrapiso já tem 120 m² prontos\" (o total feito até agora)\n" +
                "• \"a pintura só começa depois do dia 20\"\n\n" +
                "Para o % sair da quantidade, a tarefa precisa ter a quantidade total no programa (colunas Unidade e Quantidade); se " +
                "não tiver, eu pergunto o total.\n\n" +
                "Perguntas também valem: \"o que está atrasado?\", \"quando termina a estrutura?\", \"quanto falta do reboco?\".\n\n" +
                "Se houver duas tarefas parecidas, eu pergunto qual é antes de anotar." + noGrupo,
            "diario" =>
                "📒 Diário de obra (RDO)\n\n" +
                "Conte o dia, que eu anoto no RDO de hoje:\n" +
                "• \"manhã de sol; choveu à tarde e paramos às 14h\"\n" +
                "• \"hoje vieram 4 pedreiros e 3 serventes da Construtora Silva\"\n" +
                "• \"usamos a betoneira e o caminhão munck\"\n" +
                "• \"entregaram 5 m³ de areia\", \"visita do fiscal às 10h\"\n\n" +
                "Para outro dia, diga a data: \"no RDO de ontem, faltou o eletricista\". Fechar (assinar) o RDO é no programa." + noGrupo,
            "fotos" =>
                "📷 Fotos\n\n" +
                "Mande as fotos da obra aqui, como foto (não como arquivo), com uma legenda se quiser: \"laje do 2º piso concretada\".\n\n" +
                "Elas ficam na caixa da obra. No programa, ao buscar do Telegram, a IA sugere a tarefa, a legenda e o local de cada foto, e " +
                "você confere antes de importar.\n\n" +
                "#os14 na legenda liga a foto à OS 14. Com #atualizacao na legenda, eu também anoto o que ela conta (\"#atualizacao laje do " +
                "2º piso concretada\").",
            "custos" =>
                "💰 Custos e medições\n\n" +
                "• \"gastei R$ 1.200 de cimento na fundação\"\n" +
                "• \"paguei R$ 3.500 ao empreiteiro do reboco, nota 123\"\n" +
                "• \"mede 30 m² de contrapiso na medição\"\n" +
                "• \"quanto já medi?\", \"como está a margem?\"\n\n" +
                "O custo entra nos custos realizados, e a medição na medição em rascunho, quando você aplicar no programa." + noGrupo,
            "os" =>
                "📋 Ordens de serviço (OS)\n\n" +
                "• \"abre uma OS do reboco para o João, por empreitada\"\n" +
                "• \"quais OS estão vencidas?\"\n" +
                "• \"a OS 3 está em 60%\", \"cancela a OS 5\"\n\n" +
                "No grupo da obra, a OS aparece com botões: o executante toca em Aceitar, Comecei e Terminei. /os lista as abertas, " +
                "/os 14 manda a OS de novo e /recusar 14 e o motivo recusa uma." + noGrupo,
            "kanban" =>
                "🗂 Quadro Kanban\n\n" +
                "Cada tarefa é um card, e o % concluído decide a coluna:\n" +
                "• \"move a pintura para Impedida\"\n" +
                "• \"põe no checklist do reboco: comprar argamassa; chamar o eletricista\"\n" +
                "• \"marca comprar argamassa como feito\"\n" +
                "• \"etiqueta Urgente na impermeabilização\"\n" +
                "• \"arquiva o card da limpeza\"" + noGrupo,
            "grupo" =>
                "👷 A equipe no grupo da obra\n\n" +
                $"1. Crie um grupo no Telegram e adicione o bot ({Arroba(bot)}).\n" +
                "2. O dono da conta manda /obra no grupo e escolhe a obra.\n\n" +
                $"No grupo, o bot só lê fotos, mensagens com #atualizacao, mensagens que o marcam ({Arroba(bot)}) e respostas a ele. A " +
                "conversa da equipe ele não lê, e ela não gasta créditos.\n\n" +
                "Papéis: acompanhar (só pergunta), campo (manda fotos e atualizações) e engenharia (também muda os papéis). /equipe mostra " +
                "a equipe; para mudar o papel de alguém, responda a uma mensagem da pessoa com /papel campo.\n\n" +
                "Os créditos de IA são da conta do dono.",
            "comandos" => grupo
                ? "⌨️ Comandos do grupo (toque em / para ver a lista)\n\n" +
                  "/resumo — a obra em números\n" +
                  "/hoje — o que está em andamento hoje e o RDO do dia\n" +
                  "/atrasadas — as tarefas atrasadas\n" +
                  "/caixa — o que este grupo mandou e espera o programa\n" +
                  "/desfazer — tira da caixa a última coisa que você mandou\n" +
                  "/os — as ordens de serviço abertas; /recusar 14 e o motivo\n" +
                  "/equipe e /papel — a equipe e os papéis\n" +
                  "/obra e /sair — liga e desliga o grupo (só o dono)\n\n" +
                  "Os comandos respondem na hora e não gastam créditos de IA. Eles usam os dados da última vez que a obra foi salva no programa."
                : "⌨️ Comandos (toque em / ou no botão Menu para ver a lista)\n\n" +
                  "/resumo — a obra em números\n" +
                  "/hoje — o que está em andamento hoje e o RDO do dia\n" +
                  "/atrasadas — as tarefas atrasadas\n" +
                  "/caixa — o que espera você aplicar no programa\n" +
                  "/desfazer — tira da caixa a última coisa que você mandou\n" +
                  "/obra — mostra e troca a obra\n" +
                  "/ajuda — esta ajuda\n" +
                  "/sair — desliga este chat da sua conta\n\n" +
                  "Os comandos respondem na hora e não gastam créditos de IA. Eles usam os dados da última vez que a obra foi salva no programa.",
            _ => null,
        };
    }

    private static string Arroba(string bot) => bot.Length == 0 ? "@ do bot" : "@" + bot;
}
