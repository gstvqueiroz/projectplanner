using System.Globalization;
using ProjectPlanner.Core.Relatorios;

namespace ProjectPlanner.Relatorios;

/// <summary>Gera o relatório num formato (PDF ou Word).</summary>
public interface IGeradorDeRelatorio
{
    /// <summary>"pdf" ou "docx".</summary>
    string Extensao { get; }

    /// <param name="imagem">A imagem guardada de uma foto (JPEG), pelo id; nula se não deu para ler (a foto sai como um quadro vazio).</param>
    void Gerar(RelatorioFotografico relatorio, Func<Guid, byte[]?> imagem, Stream destino);
}

/// <summary>Os textos do relatório, iguais no PDF e no Word.</summary>
public static class TextosDoRelatorio
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    public const string Titulo = "RELATÓRIO FOTOGRÁFICO";

    public static string Numero(RelatorioFotografico r) => $"Nº {r.Numero:00}";

    public static string Periodo(RelatorioFotografico r) => $"Período: {r.Opcoes.De:dd/MM/yyyy} a {r.Opcoes.Ate:dd/MM/yyyy}";

    /// <summary>Linha do topo das páginas de fotos: "Residência Silva · Relatório fotográfico nº 03 · 01/10/2026 a 31/10/2026".</summary>
    public static string Cabecalho(RelatorioFotografico r) =>
        $"{r.Cabecalho.Obra} · Relatório fotográfico nº {r.Numero:00} · {r.Opcoes.De:dd/MM/yyyy} a {r.Opcoes.Ate:dd/MM/yyyy}";

    /// <summary>Os dados da capa, só os preenchidos: (rótulo, valor).</summary>
    public static IReadOnlyList<(string Rotulo, string Valor)> DadosDaCapa(RelatorioFotografico r)
    {
        var c = r.Cabecalho;
        var dados = new List<(string, string)>();
        void Se(string rotulo, string? valor)
        {
            if (!string.IsNullOrWhiteSpace(valor))
                dados.Add((rotulo, valor.Trim()));
        }
        Se("Contratante", c.DadosDaObra.Contratante);
        Se("Contrato", c.DadosDaObra.Contrato);
        Se("Endereço da obra", c.DadosDaObra.Endereco);
        Se("Responsável técnico", c.Responsavel is null ? null : c.Registro is null ? c.Responsavel : $"{c.Responsavel} ({c.Registro})");
        Se("Fotos", r.TotalDeFotos == 1 ? "1 foto" : $"{r.TotalDeFotos} fotos");
        Se("Emitido em", r.GeradoEm.ToString("dd/MM/yyyy HH:mm", Brasil));
        return dados;
    }

    /// <summary>O resumo do andamento em linhas curtas; vazio sem resumo.</summary>
    public static IReadOnlyList<string> Andamento(RelatorioFotografico r)
    {
        if (r.Resumo is not { } resumo)
            return [];
        var linhas = new List<string>
        {
            $"Andamento em {r.Opcoes.Ate:dd/MM/yyyy}: {resumo.PercentualConcluido}% concluído (previsto: {resumo.PercentualPrevisto}%)",
            $"Prazo: {resumo.Inicio:dd/MM/yyyy} a {(resumo.Termino is { } t ? t.ToString("dd/MM/yyyy", Brasil) : "—")} ({resumo.DiasUteis} dias úteis)",
            $"Tarefas: {resumo.Concluidas} concluídas · {resumo.EmDia} em dia · {resumo.Atrasadas} atrasadas · {resumo.NaoIniciadas} não iniciadas",
        };
        return linhas;
    }

    public static IReadOnlyList<string> Atrasadas(RelatorioFotografico r, int maximo = 8) =>
        r.Resumo is not { } resumo ? [] :
        [.. resumo.ListaDeAtrasadas.Take(maximo).Select(t => $"{t.Edt} {t.Nome} — {t.PercentualConcluido}% (previsto {t.PercentualPrevisto}%)"),
            .. resumo.ListaDeAtrasadas.Count > maximo ? [$"e mais {resumo.ListaDeAtrasadas.Count - maximo}"] : Array.Empty<string>()];

    public static IReadOnlyList<string> ProximosMarcos(RelatorioFotografico r, int maximo = 5) =>
        r.Resumo is not { } resumo ? [] : [.. resumo.ProximosMarcos.Take(maximo).Select(t => $"{t.Edt} {t.Nome} — {t.Data:dd/MM/yyyy}")];

    /// <summary>"Fundação (continuação)".</summary>
    public static string TituloDaPagina(PaginaDeFotos pagina) => pagina.Continuacao ? $"{pagina.Grupo.Titulo} (continuação)" : pagina.Grupo.Titulo;

    /// <summary>As colunas do índice das fotos.</summary>
    public static readonly string[] ColunasDoIndice = ["Nº", "Data", "Tarefa", "Título"];

    public static string[] LinhaDoIndice(FotoNoRelatorio f) =>
        [f.Numero.ToString(Brasil), f.Foto.DataFoto.ToString("dd/MM/yyyy HH:mm", Brasil), f.Tarefa, f.Titulo ?? f.Foto.Imagem.NomeArquivoOriginal];

    /// <summary>As assinaturas: (cargo, nome), com o responsável técnico e o contratante quando informados.</summary>
    public static IReadOnlyList<(string Cargo, string? Nome)> Assinaturas(RelatorioFotografico r) =>
    [
        ("Responsável técnico", r.Cabecalho.Responsavel is { } nome ? r.Cabecalho.Registro is { } registro ? $"{nome} — {registro}" : nome : null),
        ("Contratante", r.Cabecalho.DadosDaObra.Contratante),
    ];

    public static string Rodape(int pagina, int total) => $"Página {pagina} de {total}";

    // ---- Bloco 29 ----

    public const string TituloDoTexto = "Andamento do período";

    public const string TituloDosPontos = "Pontos de atenção";

    /// <summary>"Antes e depois — Bloco A".</summary>
    public static string TituloAntesEDepois(AntesEDepoisNoRelatorio comparacao) => $"Antes e depois — {comparacao.Local}";
}
