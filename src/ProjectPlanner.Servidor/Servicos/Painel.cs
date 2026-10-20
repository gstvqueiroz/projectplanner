using Microsoft.EntityFrameworkCore;
using ProjectPlanner.Servidor.Dados;

namespace ProjectPlanner.Servidor.Servicos;

public sealed record LinhaDoDia(DateOnly Dia, int Pedidos, long Tokens, decimal CustoEmReais);

public sealed record LinhaDoModelo(string Modelo, string Nome, string Nivel, int Pedidos, long TokensDeEntrada, long TokensDeSaida, decimal CustoEmReais,
    decimal CustoMedioEmReais, decimal CreditosMedios);

public sealed record LinhaDoPlano(string Plano, int Usuarios, int UsuariosAtivos, int Pedidos, decimal CustoEmReais, decimal ReceitaEmReais,
    decimal MargemEmReais);

public sealed record LinhaDoUsuario(string Email, string Plano, int Pedidos, decimal CustoEmReais, decimal CreditosUsados, long CreditosDoPlano);

public sealed record ResumoDoPainel(int Pedidos, long TokensDeEntrada, long TokensDeSaida, decimal CustoEmDolares, decimal CustoEmReais,
    decimal ReceitaEstimadaEmReais, decimal ImpostosETaxasEmReais, decimal MargemEmReais, decimal? MargemPercentual, int Usuarios,
    int UsuariosAtivos, int Profissionais, int NovosCadastros, decimal CustoMedioPorPedidoEmReais);

public sealed record AlertaDoPainel(decimal CustoDoMesEmReais, decimal LimiteEmReais, bool Passou, DateTime? AlertaEm, string? EmailEnviadoPara);

/// <summary>Bloco 48: tudo o que o painel financeiro mostra num período.</summary>
public sealed record DadosDoPainel(DateOnly De, DateOnly Ate, ResumoDoPainel Resumo, IReadOnlyList<LinhaDoDia> PorDia, IReadOnlyList<LinhaDoModelo> PorModelo,
    IReadOnlyList<LinhaDoPlano> PorPlano, IReadOnlyList<LinhaDoUsuario> QuemMaisGasta, AlertaDoPainel Alerta, OpcoesFinanceiras Premissas);

/// <summary>
/// Bloco 48: o painel financeiro. Junta os pedidos de IA de um período (o custo real de cada um, gravado desde o Bloco 46)
/// com as contas e os planos, e estima a receita e a margem pelo preço do Profissional (até os pagamentos do Bloco 47).
/// </summary>
public static class Painel
{
    public const int MaximoDeDias = 366;
    public const int UsuariosNoTopo = 10;

    /// <exception cref="ArgumentException">Período invertido ou maior que um ano.</exception>
    public static async Task<DadosDoPainel> MontarAsync(BancoDoServidor banco, CatalogoDeModelos catalogo, OpcoesDoServidor opcoes, DateOnly de, DateOnly ate,
        DateTime agora, CancellationToken c)
    {
        if (ate < de)
            throw new ArgumentException("O fim do período vem antes do começo.");
        if (ate.DayNumber - de.DayNumber + 1 > MaximoDeDias)
            throw new ArgumentException($"O período pode ter até {MaximoDeDias} dias.");
        var financeiro = opcoes.Financeiro;
        var planos = opcoes.Planos;
        var (inicio, fim) = (de.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), ate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        var usos = await banco.UsosDeIa.AsNoTracking().Where(u => u.Quando >= inicio && u.Quando < fim)
            .Select(u => new { u.UsuarioId, u.Quando, u.Modelo, u.TokensDeEntrada, u.TokensDeSaida, u.TokensTotais, u.CustoEmMicrodolares })
            .ToListAsync(c);
        var usuarios = await banco.Usuarios.AsNoTracking().Select(u => new { u.Id, u.Email, u.Plano, u.CriadoEm, u.EmailConfirmado }).ToListAsync(c);
        var porUsuario = usuarios.ToDictionary(u => u.Id);
        decimal Reais(long micro) => Math.Round(financeiro.Reais(micro), 2);

        // A receita estimada: o preço do Profissional, proporcional aos dias do período (30 dias = 1 mês).
        var dias = ate.DayNumber - de.DayNumber + 1;
        var fracaoDoMes = dias / 30m;
        decimal ReceitaDoPlano(string plano, int contas) => plano == Planos.Profissional ? Math.Round(contas * financeiro.PrecoProfissionalEmReais * fracaoDoMes, 2) : 0;
        decimal Liquida(decimal receita) => receita * (1 - financeiro.PercentualDeImpostosETaxas / 100);

        var porDia = Enumerable.Range(0, dias).Select(i => de.AddDays(i)).Select(dia =>
        {
            var doDia = usos.Where(u => DateOnly.FromDateTime(u.Quando) == dia).ToList();
            return new LinhaDoDia(dia, doDia.Count, doDia.Sum(u => (long)u.TokensTotais), Reais(doDia.Sum(u => u.CustoEmMicrodolares)));
        }).ToList();

        var porModelo = usos.GroupBy(u => u.Modelo).Select(g =>
        {
            var modelo = catalogo.Achar(g.Key);
            var custo = g.Sum(u => u.CustoEmMicrodolares);
            return new LinhaDoModelo(g.Key, modelo?.Nome ?? g.Key, modelo?.Nivel ?? "", g.Count(), g.Sum(u => (long)u.TokensDeEntrada), g.Sum(u => (long)u.TokensDeSaida),
                Reais(custo), Math.Round(financeiro.Reais(custo) / g.Count(), 4), Math.Round(planos.Creditos(custo) / g.Count(), 1));
        }).OrderByDescending(l => l.CustoEmReais).ToList();

        var porPlano = Planos.Todos.Select(plano =>
        {
            var contas = usuarios.Where(u => u.Plano == plano && u.EmailConfirmado).ToList();
            var ids = contas.Select(u => u.Id).ToHashSet();
            var deles = usos.Where(u => ids.Contains(u.UsuarioId)).ToList();
            var custo = Reais(deles.Sum(u => u.CustoEmMicrodolares));
            var receita = ReceitaDoPlano(plano, contas.Count);
            return new LinhaDoPlano(plano, contas.Count, deles.Select(u => u.UsuarioId).Distinct().Count(), deles.Count, custo, receita,
                Math.Round(Liquida(receita) - custo, 2));
        }).ToList();

        var quemMaisGasta = usos.GroupBy(u => u.UsuarioId).Select(g =>
        {
            var usuario = porUsuario.GetValueOrDefault(g.Key);
            var plano = usuario?.Plano ?? Planos.Gratis;
            var custo = g.Sum(u => u.CustoEmMicrodolares);
            return new LinhaDoUsuario(usuario?.Email ?? "(conta apagada)", plano, g.Count(), Reais(custo), Math.Round(planos.Creditos(custo), 1),
                planos.CreditosPorMes(plano));
        }).OrderByDescending(l => l.CustoEmReais).Take(UsuariosNoTopo).ToList();

        var custoTotal = usos.Sum(u => u.CustoEmMicrodolares);
        var receitaTotal = porPlano.Sum(p => p.ReceitaEmReais);
        var impostos = Math.Round(receitaTotal * financeiro.PercentualDeImpostosETaxas / 100, 2);
        var margem = Math.Round(receitaTotal - impostos - Reais(custoTotal), 2);
        var resumo = new ResumoDoPainel(usos.Count, usos.Sum(u => (long)u.TokensDeEntrada), usos.Sum(u => (long)u.TokensDeSaida),
            Math.Round(custoTotal / 1_000_000m, 4), Reais(custoTotal), receitaTotal, impostos, margem,
            receitaTotal > 0 ? Math.Round(margem / receitaTotal * 100, 1) : null,
            usuarios.Count(u => u.EmailConfirmado), usos.Select(u => u.UsuarioId).Distinct().Count(),
            usuarios.Count(u => u.Plano == Planos.Profissional && u.EmailConfirmado),
            usuarios.Count(u => u.CriadoEm >= inicio && u.CriadoEm < fim && u.EmailConfirmado),
            usos.Count == 0 ? 0 : Math.Round(financeiro.Reais(custoTotal) / usos.Count, 4));

        return new DadosDoPainel(de, ate, resumo, porDia, porModelo, porPlano, quemMaisGasta, await AlertaAsync(banco, financeiro, agora, c), financeiro);
    }

    /// <summary>O custo de IA do mês corrente (UTC), de todas as contas, em milionésimos de dólar.</summary>
    public static Task<long> CustoDoMesAsync(BancoDoServidor banco, DateTime agora, CancellationToken c)
    {
        var inicio = new DateTime(agora.Year, agora.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return banco.UsosDeIa.Where(u => u.Quando >= inicio).SumAsync(u => u.CustoEmMicrodolares, c);
    }

    public static string Mes(DateTime agora) => agora.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<AlertaDoPainel> AlertaAsync(BancoDoServidor banco, OpcoesFinanceiras financeiro, DateTime agora, CancellationToken c)
    {
        var custo = Math.Round(financeiro.Reais(await CustoDoMesAsync(banco, agora, c)), 2);
        var mes = Mes(agora);
        var enviado = await banco.AlertasDeCusto.AsNoTracking().FirstOrDefaultAsync(a => a.Mes == mes, c);
        return new AlertaDoPainel(custo, financeiro.AlertaMensalEmReais, financeiro.AlertaMensalEmReais > 0 && custo >= financeiro.AlertaMensalEmReais,
            enviado?.EnviadoEm, enviado?.EnviadoPara);
    }

    /// <summary>
    /// Depois de cada pedido de IA: se o custo do mês passou do limite e o alerta deste mês ainda não foi, manda o e-mail ao
    /// admin (uma vez por mês). Uma falha no e-mail não atrapalha o pedido.
    /// </summary>
    public static async Task ConferirAlertaAsync(BancoDoServidor banco, OpcoesDoServidor opcoes, IEnviadorDeEmail email, DateTime agora, ILogger log,
        CancellationToken c)
    {
        var financeiro = opcoes.Financeiro;
        if (financeiro.AlertaMensalEmReais <= 0)
            return;
        var mes = Mes(agora);
        if (await banco.AlertasDeCusto.AnyAsync(a => a.Mes == mes, c))
            return;
        var micro = await CustoDoMesAsync(banco, agora, c);
        var reais = financeiro.Reais(micro);
        if (reais < financeiro.AlertaMensalEmReais)
            return;
        var para = string.IsNullOrWhiteSpace(opcoes.Admin.Email) ? null : opcoes.Admin.Email.Trim();
        var registro = new AlertaDeCusto { Mes = mes, EnviadoEm = agora, CustoEmMicrodolares = micro, EnviadoPara = para };
        banco.AlertasDeCusto.Add(registro);
        try
        {
            await banco.SaveChangesAsync(c);
        }
        catch (DbUpdateException)
        {
            return; // outro pedido, ao mesmo tempo, já registrou o alerta deste mês
        }
        var brasil = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        log.LogWarning("O custo de IA de {Mes} passou de R$ {Limite}: R$ {Custo}", mes, financeiro.AlertaMensalEmReais, Math.Round(reais, 2));
        if (para is null)
            return;
        try
        {
            await email.EnviarAsync(para, $"ProjectPlanner: custo de IA de {mes} passou de {financeiro.AlertaMensalEmReais.ToString("C", brasil)}",
                $"O custo de IA do mês chegou a {reais.ToString("C", brasil)} (US$ {micro / 1_000_000m:0.00}), passando do limite de " +
                $"{financeiro.AlertaMensalEmReais.ToString("C", brasil)}.\n\nVeja quem está gastando no painel: /admin\n\n" +
                "Este aviso é mandado uma vez por mês. O limite fica na variável Financeiro__AlertaMensalEmReais.", c);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Não deu para mandar o alerta de custo por e-mail");
            registro.EnviadoPara = null; // o painel não diz que foi
            await banco.SaveChangesAsync(CancellationToken.None);
        }
    }
}
