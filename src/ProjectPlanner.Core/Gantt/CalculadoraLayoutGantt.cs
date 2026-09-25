using System.Globalization;

namespace ProjectPlanner.Core.Gantt;

/// <summary>
/// Calcula onde cada elemento do Gantt fica desenhado, a partir de um cronograma já calculado pelo CPM.
/// </summary>
/// <remarks>
/// <para>
/// O eixo de tempo é de dias corridos, sem destaque para fins de semana (até existir o bloco de
/// calendários): uma tarefa de 5 dias úteis que atravessa um fim de semana ocupa 7 dias. Cada dia vai
/// de X = (dia − início do eixo) × escala até o X do dia seguinte. O eixo começa
/// <see cref="DiasAntesDoInicio"/> dias antes do início do projeto (para caber a volta das setas que
/// saem do começo das primeiras barras) e termina no domingo que fica pelo menos
/// <see cref="DiasDepoisDoFim"/> dias depois do fim do projeto. Se isso não preencher a
/// <see cref="OpcoesGantt.LarguraVisivel"/>, o eixo segue até preenchê-la (último dia possivelmente
/// cortado pela borda). Sem tarefas, o fim do projeto é o próprio início, então o eixo é só a área
/// visível a partir de 3 dias antes do início do projeto (que num projeto novo é hoje).
/// </para>
/// <para>
/// Sábados e domingos vêm em <see cref="LayoutGantt.FinsDeSemana"/>, em todo o eixo, inclusive nos dias
/// sem tarefa, para a tela pintar um fundo discreto. É só uma pista visual: o cálculo continua em dias
/// úteis de segunda a sexta, como sempre.
/// </para>
/// <para>
/// As linhas seguem a ordem do cronograma, a mesma da grid, para que a linha N da grid e a barra N
/// fiquem lado a lado. As barras vão do começo do Início cedo ao fim do Término cedo (o cronograma
/// calculado); a folga, quando existe, vai do fim da barra ao fim do Término tarde.
/// </para>
/// <para>
/// Cada seta sai do ponto da predecessora que o tipo do vínculo relaciona e chega no ponto da sucessora,
/// sempre na altura do centro das barras: FS sai do fim e chega no início; SS sai do início e chega no
/// início; FF sai do fim e chega no fim; SF sai do início e chega no fim. A seta sai andando para fora
/// da barra (para a direita do fim, para a esquerda do início) e chega andando para dentro dela; quando
/// o caminho direto não dá, ela contorna pela divisa entre as linhas.
/// </para>
/// </remarks>
public static class CalculadoraLayoutGantt
{
    public const int DiasAntesDoInicio = 3;
    public const int DiasDepoisDoFim = 7;

    /// <summary>A partir desta escala (px/dia), o cabeçalho mostra semanas e dias.</summary>
    public const double EscalaMinimaParaDias = 18;

    /// <summary>A partir desta escala (e abaixo da de dias), mostra meses e semanas; abaixo dela, anos e meses.</summary>
    public const double EscalaMinimaParaSemanas = 6;

    /// <exception cref="InvalidOperationException">Alguma tarefa ainda não foi calculada.</exception>
    public static LayoutGantt Calcular(Cronograma cronograma, ResultadoCronograma resultado, OpcoesGantt opcoes)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(resultado);
        ArgumentNullException.ThrowIfNull(opcoes);
        if (cronograma.Tarefas.Any(t => t.InicioMaisCedo is null))
            throw new InvalidOperationException("Calcule o cronograma antes de montar o Gantt.");

        var inicioEixo = SomarDias(cronograma.DataInicio, -DiasAntesDoInicio);
        var fimEixo = DomingoEmOuDepois(SomarDias(resultado.DataTermino, DiasDepoisDoFim));
        var fimVisivel = SomarDias(inicioEixo, DiasParaPreencher(opcoes.LarguraVisivel, opcoes.PixelsPorDia) - 1);
        if (fimVisivel > fimEixo)
            fimEixo = fimVisivel;
        var eixo = new Eixo(inicioEixo, opcoes.PixelsPorDia);

        var barras = cronograma.Tarefas.Select((tarefa, linha) => Barra(tarefa, linha, eixo, opcoes)).ToList();
        var barraPorTarefa = barras.ToDictionary(b => b.TarefaId);
        var vinculosCriticos = resultado.CaminhosCriticos
            .SelectMany(caminho => caminho.Zip(caminho.Skip(1), (a, b) => (a.Id, b.Id)))
            .ToHashSet();
        var setas = cronograma.Predecessoras
            .Select(p => Seta(p, barraPorTarefa[p.TarefaOrigemId], barraPorTarefa[p.TarefaDestinoId],
                vinculosCriticos.Contains((p.TarefaOrigemId, p.TarefaDestinoId)), opcoes))
            .ToList();

        var finsDeSemana = DiasDeFimDeSemana(inicioEixo, fimEixo)
            .Select(dia => new ColunaDia(dia, eixo.InicioDoDia(dia), opcoes.PixelsPorDia))
            .ToList();

        return new LayoutGantt(inicioEixo, fimEixo, eixo.FimDoDia(fimEixo),
            Math.Max(barras.Count * opcoes.AlturaLinha, opcoes.AlturaVisivel),
            barras, setas, Escala(inicioEixo, fimEixo, eixo, opcoes), finsDeSemana);
    }

    /// <summary>Quantos dias inteiros ou parciais cabem numa largura, nessa escala (pelo menos 1).</summary>
    public static int DiasParaPreencher(double largura, double pixelsPorDia)
    {
        if (!(pixelsPorDia > 0))
            throw new ArgumentOutOfRangeException(nameof(pixelsPorDia), pixelsPorDia, "A escala precisa ser maior que zero.");
        if (!(largura >= 0))
            throw new ArgumentOutOfRangeException(nameof(largura), largura, "A largura não pode ser negativa.");
        return (int)Math.Min(Math.Max(1, Math.Ceiling(largura / pixelsPorDia)), int.MaxValue);
    }

    /// <summary>Os sábados e domingos de <paramref name="de"/> a <paramref name="ate"/> (inclusive), em ordem.</summary>
    public static IEnumerable<DateTime> DiasDeFimDeSemana(DateTime de, DateTime ate)
    {
        for (var dia = de.Date; dia <= ate.Date; dia = dia.AddDays(1))
        {
            if (dia.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                yield return dia;
            if (dia == DateTime.MaxValue.Date)
                yield break;
        }
    }

    private static BarraGantt Barra(Tarefa tarefa, int linha, Eixo eixo, OpcoesGantt opcoes)
    {
        var x = eixo.InicioDoDia(tarefa.InicioMaisCedo!.Value);
        var largura = eixo.FimDoDia(tarefa.TerminoMaisCedo!.Value) - x;
        var y = linha * opcoes.AlturaLinha + (opcoes.AlturaLinha - opcoes.AlturaBarra) / 2;
        var fimDaFolga = tarefa.Folga > 0 ? eixo.FimDoDia(tarefa.TerminoMaisTarde!.Value) : (double?)null;

        return new BarraGantt(tarefa.Id, tarefa.Nome, linha, x, y, largura, opcoes.AlturaBarra,
            largura * tarefa.PercentualConcluido / 100, fimDaFolga, tarefa.EhCritica);
    }

    private static SetaGantt Seta(Predecessora vinculo, BarraGantt origem, BarraGantt destino, bool critica, OpcoesGantt opcoes)
    {
        var (saiDoFim, chegaNoFim) = vinculo.Tipo switch
        {
            TipoDependencia.FS => (true, false),
            TipoDependencia.SS => (false, false),
            TipoDependencia.FF => (true, true),
            _ => (false, true), // SF
        };
        var saida = new PontoGantt(saiDoFim ? origem.Direita : origem.X, origem.CentroY);
        var chegada = new PontoGantt(chegaNoFim ? destino.Direita : destino.X, destino.CentroY);

        // Sai para fora da barra (para a direita do fim, para a esquerda do início) e chega andando
        // para dentro dela (para a direita no início, para a esquerda no fim).
        var xSaida = saida.X + (saiDoFim ? 1 : -1) * opcoes.AfastamentoSeta;
        var sentidoChegada = chegaNoFim ? -1 : 1;
        var xAproximacao = chegada.X - sentidoChegada * opcoes.AfastamentoSeta;
        var caminhoDireto = sentidoChegada > 0 ? xSaida <= xAproximacao : xSaida >= xAproximacao;

        List<PontoGantt> pontos;
        if (caminhoDireto)
        {
            pontos = [saida, new(xSaida, saida.Y), new(xSaida, chegada.Y), chegada];
        }
        else
        {
            // Contorna pela divisa entre as linhas, do lado da sucessora, para não atravessar as barras.
            var yDivisa = destino.Linha > origem.Linha
                ? destino.Linha * opcoes.AlturaLinha
                : (destino.Linha + 1) * opcoes.AlturaLinha;
            pontos =
            [
                saida, new(xSaida, saida.Y), new(xSaida, yDivisa),
                new(xAproximacao, yDivisa), new(xAproximacao, chegada.Y), chegada,
            ];
        }

        var xBasePonta = chegada.X - sentidoChegada * opcoes.TamanhoPontaSeta;
        var meiaAltura = opcoes.TamanhoPontaSeta / 2;
        PontoGantt[] ponta = [chegada, new(xBasePonta, chegada.Y - meiaAltura), new(xBasePonta, chegada.Y + meiaAltura)];

        return new SetaGantt(vinculo.TarefaOrigemId, vinculo.TarefaDestinoId, vinculo.Tipo, pontos, ponta, critica);
    }

    private static EscalaGantt Escala(DateTime inicio, DateTime fim, Eixo eixo, OpcoesGantt opcoes)
    {
        var (superior, inferior) = opcoes.PixelsPorDia switch
        {
            >= EscalaMinimaParaDias => (UnidadeEscala.Semana, UnidadeEscala.Dia),
            >= EscalaMinimaParaSemanas => (UnidadeEscala.Mes, UnidadeEscala.Semana),
            _ => (UnidadeEscala.Ano, UnidadeEscala.Mes),
        };
        return new EscalaGantt(
            superior, Marcas(superior, faixaSuperior: true, inicio, fim, eixo, opcoes.Cultura),
            inferior, Marcas(inferior, faixaSuperior: false, inicio, fim, eixo, opcoes.Cultura));
    }

    private static List<MarcaEscala> Marcas(UnidadeEscala unidade, bool faixaSuperior, DateTime inicio, DateTime fim,
        Eixo eixo, CultureInfo cultura)
    {
        var formato = (unidade, faixaSuperior) switch
        {
            (UnidadeEscala.Dia, _) => "%d",
            (UnidadeEscala.Semana, true) => "dd/MM/yyyy",
            (UnidadeEscala.Semana, false) => "dd/MM",
            (UnidadeEscala.Mes, true) => "MMMM yyyy",
            (UnidadeEscala.Mes, false) => "MMM",
            _ => "yyyy",
        };

        var marcas = new List<MarcaEscala>();
        for (var periodo = InicioDoPeriodo(inicio, unidade); periodo <= fim; )
        {
            var proximo = Proximo(periodo, unidade);
            var de = periodo < inicio ? inicio : periodo;
            var ate = proximo == DateTime.MaxValue || proximo.AddDays(-1) > fim ? fim : proximo.AddDays(-1);
            marcas.Add(new MarcaEscala(periodo, eixo.InicioDoDia(de), eixo.FimDoDia(ate) - eixo.InicioDoDia(de),
                periodo.ToString(formato, cultura)));
            periodo = proximo;
        }
        return marcas;
    }

    private static DateTime InicioDoPeriodo(DateTime dia, UnidadeEscala unidade) => unidade switch
    {
        UnidadeEscala.Dia => dia,
        UnidadeEscala.Semana => SomarDias(dia, -(((int)dia.DayOfWeek + 6) % 7)),
        UnidadeEscala.Mes => new DateTime(dia.Year, dia.Month, 1),
        _ => new DateTime(dia.Year, 1, 1),
    };

    // O período seguinte; DateTime.MaxValue quando ele já não cabe no calendário.
    private static DateTime Proximo(DateTime periodo, UnidadeEscala unidade)
    {
        try
        {
            return unidade switch
            {
                UnidadeEscala.Dia => periodo.AddDays(1),
                UnidadeEscala.Semana => periodo.AddDays(7),
                UnidadeEscala.Mes => periodo.AddMonths(1),
                _ => periodo.AddYears(1),
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTime.MaxValue;
        }
    }

    private static DateTime DomingoEmOuDepois(DateTime dia) =>
        SomarDias(dia, (7 - (int)dia.DayOfWeek) % 7);

    // AddDays sem estourar nas pontas do calendário (0001-01-01 e 9999-12-31).
    private static DateTime SomarDias(DateTime dia, int dias)
    {
        var limite = dias < 0 ? (dia.Date - DateTime.MinValue).Days : (DateTime.MaxValue.Date - dia.Date).Days;
        return dia.Date.AddDays(dias < 0 ? -Math.Min(-dias, limite) : Math.Min(dias, limite));
    }

    // Dias corridos contados a partir do início do eixo.
    private readonly record struct Eixo(DateTime Inicio, double PixelsPorDia)
    {
        public double InicioDoDia(DateTime dia) => (dia.Date - Inicio).Days * PixelsPorDia;

        public double FimDoDia(DateTime dia) => ((dia.Date - Inicio).Days + 1) * PixelsPorDia;
    }
}
