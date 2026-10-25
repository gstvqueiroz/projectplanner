using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ProjectPlanner.Relatorios;

/// <summary>
/// Bloco 35: lê uma planilha em linhas de texto, para importar valores: Excel (.xlsx, a primeira aba) ou CSV (";", "," ou
/// tabulação, o que aparecer mais na primeira linha; UTF-8 ou, se não for, Latin-1, como o Excel antigo grava).
/// </summary>
public static class LeitorDePlanilha
{
    /// <summary>As extensões aceitas.</summary>
    public static readonly IReadOnlyList<string> Extensoes = ["xlsx", "csv", "txt"];

    /// <exception cref="InvalidDataException">O arquivo não é uma planilha que dê para ler.</exception>
    /// <exception cref="IOException">Não deu para abrir o arquivo.</exception>
    public static IReadOnlyList<IReadOnlyList<string>> Ler(string caminho)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caminho);
        return Path.GetExtension(caminho).ToLowerInvariant() switch
        {
            ".xlsx" => LerExcel(caminho),
            ".csv" or ".txt" => LerCsv(File.ReadAllBytes(caminho)),
            var outra => throw new InvalidDataException($"Formato {outra} não aceito. Use uma planilha .xlsx ou .csv."),
        };
    }

    // ---- CSV ----

    internal static IReadOnlyList<IReadOnlyList<string>> LerCsv(byte[] bytes)
    {
        string texto;
        try
        {
            texto = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            texto = Encoding.Latin1.GetString(bytes);
        }
        var primeira = texto.Split('\n')[0];
        var separador = new[] { ';', ',', '\t' }.OrderByDescending(c => primeira.Count(x => x == c)).First();
        var linhas = new List<IReadOnlyList<string>>();
        var campos = new List<string>();
        var campo = new StringBuilder();
        var entreAspas = false;
        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            if (entreAspas)
            {
                if (c == '"' && i + 1 < texto.Length && texto[i + 1] == '"')
                {
                    campo.Append('"');
                    i++;
                }
                else if (c == '"')
                    entreAspas = false;
                else
                    campo.Append(c);
            }
            else if (c == '"')
                entreAspas = true;
            else if (c == separador)
            {
                campos.Add(campo.ToString());
                campo.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < texto.Length && texto[i + 1] == '\n')
                    i++;
                campos.Add(campo.ToString());
                campo.Clear();
                linhas.Add(campos);
                campos = [];
            }
            else
                campo.Append(c);
        }
        if (campo.Length > 0 || campos.Count > 0)
        {
            campos.Add(campo.ToString());
            linhas.Add(campos);
        }
        return linhas;
    }

    // ---- Excel ----

    /// <summary>
    /// Bloco 40: uma aba de uma planilha do Excel pelo nome (sem diferença de acentos e maiúsculas: "Analítico" ou "analitico").
    /// Lida em fluxo, sem montar a planilha inteira na memória (a de referência da SINAPI tem centenas de milhares de linhas).
    /// </summary>
    /// <exception cref="InvalidDataException">O arquivo não é uma planilha do Excel, ou não tem a aba.</exception>
    /// <exception cref="IOException">Não deu para abrir o arquivo.</exception>
    public static IReadOnlyList<IReadOnlyList<string>> LerAba(string caminho, string aba)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caminho);
        ArgumentException.ThrowIfNullOrWhiteSpace(aba);
        return LerExcel(caminho, aba);
    }

    private static IReadOnlyList<IReadOnlyList<string>> LerExcel(string caminho, string? nomeDaAba = null)
    {
        try
        {
            using var documento = SpreadsheetDocument.Open(caminho, isEditable: false);
            var livro = documento.WorkbookPart ?? throw new InvalidDataException("A planilha está vazia.");
            var abas = livro.Workbook.Sheets?.Elements<Sheet>().ToList() ?? [];
            var aba = (nomeDaAba is null ? abas.FirstOrDefault() : abas.FirstOrDefault(a => MesmoNome(a.Name?.Value, nomeDaAba)))
                      ?? throw new InvalidDataException(nomeDaAba is null ? "A planilha não tem abas." : $"A planilha não tem a aba {nomeDaAba}.");
            var parte = (WorksheetPart)livro.GetPartById(aba.Id!.Value!);
            var textos = livro.SharedStringTablePart?.SharedStringTable.Elements<SharedStringItem>().Select(s => s.InnerText).ToList() ?? [];
            var linhas = new List<IReadOnlyList<string>>();
            List<string>? campos = null;
            using var leitor = OpenXmlReader.Create(parte);
            while (leitor.Read())
            {
                if (leitor.ElementType == typeof(Row))
                {
                    if (leitor.IsStartElement)
                    {
                        if (campos is not null)
                            linhas.Add(campos); // uma linha vazia (<row/>) não tem o fim separado
                        campos = [];
                    }
                    else if (campos is not null)
                    {
                        linhas.Add(campos);
                        campos = null;
                    }
                }
                else if (leitor.ElementType == typeof(Cell) && leitor.IsStartElement && campos is not null)
                {
                    var celula = (Cell)leitor.LoadCurrentElement()!;
                    var coluna = Coluna(celula.CellReference?.Value);
                    while (coluna >= 0 && campos.Count < coluna)
                        campos.Add("");
                    campos.Add(Texto(celula, textos));
                }
            }
            if (campos is not null)
                linhas.Add(campos);
            return linhas;
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or FileFormatException or InvalidOperationException
                                       or System.Xml.XmlException)
        {
            throw new InvalidDataException("O arquivo não é uma planilha do Excel que dê para ler.", ex);
        }
    }

    // O texto da célula: o compartilhado, o "inline", ou o número com vírgula decimal (para o leitor de reais).
    private static string Texto(Cell celula, IReadOnlyList<string> textos)
    {
        var valor = celula.CellValue?.Text ?? celula.InlineString?.InnerText ?? "";
        if (celula.DataType?.Value == CellValues.SharedString && int.TryParse(valor, out var indice) && indice < textos.Count)
            return textos[indice];
        if ((celula.DataType is null || celula.DataType.Value == CellValues.Number)
            && double.TryParse(valor, NumberStyles.Float, CultureInfo.InvariantCulture, out var numero))
            return numero.ToString("0.##########", CultureInfo.GetCultureInfo("pt-BR"));
        return valor;
    }

    private static bool MesmoNome(string? a, string b) =>
        string.Compare(a?.Trim(), b.Trim(), CultureInfo.InvariantCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0;

    // "C7" → 2 (a coluna, a partir de 0); sem referência, -1.
    private static int Coluna(string? referencia)
    {
        if (string.IsNullOrEmpty(referencia))
            return -1;
        var coluna = 0;
        foreach (var c in referencia.TakeWhile(char.IsLetter))
            coluna = coluna * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
        return coluna - 1;
    }
}
