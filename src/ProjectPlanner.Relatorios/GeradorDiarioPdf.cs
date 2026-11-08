using System.Globalization;
using ProjectPlanner.Core.Diario;
using ProjectPlanner.Core.Relatorios;
using SkiaSharp;

namespace ProjectPlanner.Relatorios;

/// <summary>Bloco 81: uma foto do dia no RDO: a imagem (miniatura ou a própria) e a legenda.</summary>
public sealed record FotoDoDiaNoPdf(byte[] Imagem, string Legenda);

/// <summary>Bloco 81: um dia no PDF: o registro, o número do RDO e as fotos do dia (no máximo 8 entram, quatro por linha).</summary>
public sealed record DiaNoPdf(DiaDaObra Dia, int Numero, IReadOnlyList<FotoDoDiaNoPdf> Fotos);

/// <summary>Bloco 81: o que vai no alto de cada RDO além do dia: o início da obra e o término previsto (para o "dia N da obra").</summary>
public sealed record PrazoNoDiario(DateOnly Inicio, DateOnly? Termino);

/// <summary>
/// Bloco 81: o Diário de Obra (RDO) em PDF, A4 retrato, um ou vários dias (cada um começa numa página): o número e a data, a obra,
/// o clima de manhã e de tarde, o efetivo, os equipamentos, os serviços executados, as ocorrências, até oito fotos do dia e as
/// assinaturas (o responsável técnico e a fiscalização). O que não cabe numa página continua na seguinte.
/// </summary>
public sealed class GeradorDiarioPdf
{
    private const float Largura = 595.28f;
    private const float Altura = 841.89f;
    private const float Margem = 40f;
    private const float Limite = Largura - Margem;
    private const float Fundo = Altura - Margem - 20;
    private const float AlturaDaLinha = 17f;
    public const int FotosPorDia = 8;

    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly SKColor Texto = new(0x21, 0x21, 0x21);
    private static readonly SKColor Cinza = new(0x6B, 0x6B, 0x6B);
    private static readonly SKColor Linha = new(0xCF, 0xD8, 0xDC);
    private static readonly SKColor Faixa = new(0xF4, 0xF6, 0xF8);
    private static readonly SKColor Destaque = new(0x15, 0x65, 0xC0);
    private static readonly SKColor Alerta = new(0xC6, 0x28, 0x28);

    public void Gerar(IReadOnlyList<DiaNoPdf> dias, CabecalhoDoRelatorio cabecalho, PrazoNoDiario prazo, string geradoEm, Stream destino)
    {
        ArgumentNullException.ThrowIfNull(dias);
        ArgumentNullException.ThrowIfNull(cabecalho);
        ArgumentNullException.ThrowIfNull(prazo);
        ArgumentNullException.ThrowIfNull(destino);
        if (dias.Count == 0)
            throw new ArgumentException("Escolha pelo menos um dia do diário.", nameof(dias));
        using var f = new Fontes();
        using var documento = SKDocument.CreatePdf(destino, new SKDocumentPdfMetadata
        {
            Title = dias.Count == 1 ? $"RDO nº {dias[0].Numero} · {cabecalho.Obra}" : $"Diário de Obra · {cabecalho.Obra}",
            Creator = "ProjectPlanner",
            Producer = "ProjectPlanner",
        });
        var pagina = 0;
        foreach (var dia in dias)
            new Escrita(documento, f, cabecalho, prazo, geradoEm, dia, () => ++pagina).Escrever();
        documento.Close();
    }

    // A escrita de um dia, com o cursor vertical e a troca de página.
    private sealed class Escrita(SKDocument documento, Fontes f, CabecalhoDoRelatorio cab, PrazoNoDiario prazo, string geradoEm, DiaNoPdf dia,
        Func<int> proximaPagina)
    {
        private SKCanvas _c = null!;
        private float _y;

        private DiaDaObra D => dia.Dia;

        public void Escrever()
        {
            Primeira();
            Clima();
            Tabela("EFETIVO", ["Função", "Empresa", "Quantidade"], [0.5f, 0.32f, 0.18f],
                [.. D.Efetivo.Select(e => new[] { e.Funcao, e.Empresa ?? "Própria", e.Quantidade.ToString(Brasil) })],
                D.Efetivo.Count == 0 ? "Sem efetivo registrado." : $"Total: {D.TotalDoEfetivo} {(D.TotalDoEfetivo == 1 ? "pessoa" : "pessoas")}");
            Tabela("EQUIPAMENTOS", ["Equipamento", "Quantidade"], [0.82f, 0.18f],
                [.. D.Equipamentos.Select(e => new[] { e.Nome, e.Quantidade.ToString(Brasil) })], D.Equipamentos.Count == 0 ? "Sem equipamentos registrados." : null);
            // Bloco 84: a coluna da quantidade executada no dia só aparece quando algum serviço tem.
            if (D.Atividades.Any(a => a.Quantidade is not null))
                Tabela("SERVIÇOS EXECUTADOS", ["Serviço", "Executado no dia", "% ao fim do dia"], [0.6f, 0.22f, 0.18f],
                    [.. D.Atividades.Select(a => new[] { a.Descricao, a.Quantidade is { } q ? $"{Core.Planejamento.Moeda.FormatarQuantidade(q)} {a.Unidade}".TrimEnd() : "—",
                        a.Percentual is { } p ? $"{p}%" : "—" })], null);
            else
                Tabela("SERVIÇOS EXECUTADOS", ["Serviço", "% ao fim do dia"], [0.82f, 0.18f],
                    [.. D.Atividades.Select(a => new[] { a.Descricao, a.Percentual is { } p ? $"{p}%" : "—" })], D.Atividades.Count == 0 ? "Nenhum serviço registrado." : null);
            Ocorrencias();
            Fotos();
            Assinaturas();
            Rodape();
            documento.EndPage();
        }

        private void NovaPagina(bool continuacao)
        {
            if (continuacao)
            {
                Rodape();
                documento.EndPage();
            }
            _c = documento.BeginPage(Largura, Altura);
            _y = Margem;
            if (continuacao)
            {
                Escrever($"RDO nº {dia.Numero:000} · {D.Data.ToString("dd/MM/yyyy", Brasil)} (continuação)", Margem, _y + 10, f.Negrito(10), Cinza);
                _y += 30;
            }
        }

        // Garante a altura; senão, continua na próxima página.
        private void Caber(float altura)
        {
            if (_y + altura > Fundo)
                NovaPagina(continuacao: true);
        }

        private void Primeira()
        {
            NovaPagina(continuacao: false);
            Escrever("DIÁRIO DE OBRA (RDO)", Margem, Margem + 6, f.Negrito(10), Cinza);
            var codigo = $"RDO nº {dia.Numero:000}";
            Direita(codigo, Limite, Margem + 22, f.Negrito(22), Destaque);
            var reservado = f.Negrito(22).MeasureText(codigo) + 20;
            if (cab.Logotipo is { Length: > 0 } logo)
            {
                var direitaDoLogo = Limite - f.Negrito(22).MeasureText(codigo) - 14;
                GeradorBoletimPdf.DesenharLogotipo(_c, logo, new SKRect(direitaDoLogo - 110, Margem - 6, direitaDoLogo, Margem + 30));
                reservado = Limite - direitaDoLogo + 120;
            }
            Escrever(Cortar(cab.Obra, f.Negrito(16), Limite - Margem - reservado), Margem, Margem + 28, f.Negrito(16), Texto);
            _y = Margem + 44;
            foreach (var linha in new[]
                     {
                         cab.DadosDaObra.Contratante is { } ct ? $"Contratante: {ct}" : null, cab.DadosDaObra.Contrato is { } ctr ? $"Contrato: {ctr}" : null,
                         cab.DadosDaObra.Endereco is { } end ? $"Endereço: {end}" : null,
                     }.OfType<string>())
            {
                Escrever(Cortar(linha, f.Normal(8.5f), Limite - Margem), Margem, _y, f.Normal(8.5f), Cinza);
                _y += 12;
            }
            _y += 6;
            using (var fundo = new SKPaint { Color = Faixa })
                _c.DrawRect(new SKRect(Margem, _y, Limite, _y + 34), fundo);
            var diaDaObra = D.Data.DayNumber - prazo.Inicio.DayNumber + 1;
            var semana = D.Data.ToString("dddd", Brasil);
            Escrever($"{D.Data.ToString("dd/MM/yyyy", Brasil)} · {char.ToUpper(semana[0], Brasil)}{semana[1..]}", Margem + 8, _y + 14,
                f.Negrito(11), Texto);
            var prazos = (diaDaObra >= 1 ? $"Dia {diaDaObra} da obra" : "Antes do início da obra")
                         + (prazo.Termino is { } termino ? $" · término previsto {termino.ToString("dd/MM/yyyy", Brasil)}" : "");
            Escrever(prazos, Margem + 8, _y + 27, f.Normal(8.5f), Cinza);
            var situacao = D.Fechamento is { } fechado
                ? $"Fechado em {fechado.Quando.ToString("dd/MM/yyyy HH:mm", Brasil)}{(fechado.Responsavel is { } r ? $" por {r}" : "")}"
                : "Aberto (rascunho)";
            Direita(situacao, Limite - 8, _y + 14, f.Normal(8.5f), D.Fechado ? Texto : Alerta);
            _y += 50;
        }

        private void Clima()
        {
            Titulo("CLIMA");
            Tabela(null, ["Período", "Tempo", "Condição"], [0.34f, 0.33f, 0.33f],
            [
                ["Manhã", ClimaDoPeriodo.NomeDoTempo(D.Manha.Tempo), D.Manha.Praticavel ? "Praticável" : "Impraticável"],
                ["Tarde", ClimaDoPeriodo.NomeDoTempo(D.Tarde.Tempo), D.Tarde.Praticavel ? "Praticável" : "Impraticável"],
            ], null);
        }

        private void Titulo(string titulo)
        {
            Caber(22 + AlturaDaLinha * 2);
            Escrever(titulo, Margem, _y + 8, f.Negrito(7.5f), Cinza);
            _y += 14;
        }

        // Uma tabela com o cabeçalho cinza, as linhas e uma nota no fim; o cabeçalho se repete se a tabela continua noutra página.
        private void Tabela(string? titulo, string[] colunas, float[] larguras, IReadOnlyList<string[]> linhas, string? nota)
        {
            if (titulo is not null)
                Titulo(titulo);
            var x = new float[colunas.Length];
            var total = Limite - Margem;
            for (var i = 1; i < x.Length; i++)
                x[i] = x[i - 1] + larguras[i - 1] * total;
            using var fundo = new SKPaint { Color = Faixa };
            using var traco = new SKPaint { Color = Linha, StrokeWidth = 0.6f };
            void Cabecalho()
            {
                _c.DrawRect(new SKRect(Margem, _y, Limite, _y + AlturaDaLinha), fundo);
                for (var i = 0; i < colunas.Length; i++)
                    Escrever(colunas[i], Margem + x[i] + 4, _y + 12, f.Negrito(8), Texto);
                _y += AlturaDaLinha;
            }
            Cabecalho();
            foreach (var linha in linhas)
            {
                if (_y + AlturaDaLinha > Fundo)
                {
                    NovaPagina(continuacao: true);
                    Cabecalho();
                }
                for (var i = 0; i < colunas.Length; i++)
                {
                    var largura = (i + 1 < x.Length ? x[i + 1] : total) - x[i] - 8;
                    Escrever(Cortar(linha[i], f.Normal(8.5f), largura), Margem + x[i] + 4, _y + 12, f.Normal(8.5f), Texto);
                }
                _y += AlturaDaLinha;
                _c.DrawLine(Margem, _y, Limite, _y, traco);
            }
            if (nota is not null)
            {
                Caber(AlturaDaLinha);
                Escrever(nota, Margem + 4, _y + 12, linhas.Count == 0 ? f.Normal(8.5f) : f.Negrito(8.5f), linhas.Count == 0 ? Cinza : Texto);
                _y += AlturaDaLinha;
            }
            _y += 10;
        }

        private void Ocorrencias()
        {
            Titulo("OCORRÊNCIAS E OBSERVAÇÕES");
            var linhas = D.Ocorrencias is { } texto ? Quebrar(texto, f.Normal(9), Limite - Margem) : ["Nenhuma ocorrência."];
            foreach (var linha in linhas)
            {
                Caber(13);
                Escrever(linha, Margem, _y + 10, f.Normal(9), D.Ocorrencias is null ? Cinza : Texto);
                _y += 13;
            }
            _y += 10;
        }

        // Até oito fotos, quatro por linha, com a legenda embaixo.
        private void Fotos()
        {
            var fotos = dia.Fotos.Take(FotosPorDia).ToList();
            if (fotos.Count == 0)
                return;
            Titulo(dia.Fotos.Count > FotosPorDia ? $"FOTOS DO DIA ({FotosPorDia} DE {dia.Fotos.Count})" : "FOTOS DO DIA");
            const float espaco = 8f;
            const int porLinha = 4;
            var largura = (Limite - Margem - (porLinha - 1) * espaco) / porLinha;
            var altura = largura * 0.75f;
            for (var i = 0; i < fotos.Count; i += porLinha)
            {
                Caber(altura + 26);
                for (var k = 0; k < porLinha && i + k < fotos.Count; k++)
                {
                    var caixa = new SKRect(Margem + k * (largura + espaco), _y, Margem + k * (largura + espaco) + largura, _y + altura);
                    using (var fundo = new SKPaint { Color = Faixa })
                        _c.DrawRect(caixa, fundo);
                    using (var imagem = SKImage.FromEncodedData(fotos[i + k].Imagem))
                        if (imagem is not null)
                        {
                            var escala = Math.Min(caixa.Width / imagem.Width, caixa.Height / imagem.Height);
                            var (l, a) = (imagem.Width * escala, imagem.Height * escala);
                            var destino = new SKRect(caixa.MidX - l / 2, caixa.MidY - a / 2, caixa.MidX + l / 2, caixa.MidY + a / 2);
                            _c.DrawImage(imagem, destino, new SKSamplingOptions(SKFilterMode.Linear));
                        }
                    Escrever(Cortar(fotos[i + k].Legenda, f.Normal(7), largura), caixa.Left, caixa.Bottom + 10, f.Normal(7), Cinza);
                }
                _y += altura + 18;
            }
            _y += 4;
        }

        private void Assinaturas()
        {
            Caber(56);
            _y = Math.Max(_y + 30, Fundo - 32); // embaixo da página, com o espaço da assinatura acima da linha
            using var traco = new SKPaint { Color = Texto, StrokeWidth = 0.7f };
            var largura = (Limite - Margem - 30) / 2;
            void Assinatura(float x, string rotulo, string? nome)
            {
                _c.DrawLine(x, _y, x + largura, _y, traco);
                Escrever(rotulo, x, _y + 12, f.Negrito(8), Texto);
                if (nome is not null)
                    Escrever(Cortar(nome, f.Normal(8), largura), x, _y + 23, f.Normal(8), Cinza);
            }
            Assinatura(Margem, "Responsável técnico (contratada)", D.Fechamento?.Responsavel ?? cab.Responsavel);
            Assinatura(Margem + largura + 30, "Fiscalização (contratante)", cab.DadosDaObra.Contratante);
            _y += 40;
        }

        private void Rodape()
        {
            var y = Altura - Margem + 16;
            Escrever($"Gerado pelo ProjectPlanner em {geradoEm}", Margem, y, f.Normal(7.5f), Cinza);
            Direita($"RDO nº {dia.Numero:000} · página {proximaPagina()}", Limite, y, f.Normal(7.5f), Cinza);
        }

        private void Escrever(string texto, float x, float y, SKFont fonte, SKColor cor)
        {
            using var tinta = new SKPaint { Color = cor, IsAntialias = true };
            _c.DrawText(texto, x, y, SKTextAlign.Left, fonte, tinta);
        }

        private void Direita(string texto, float x, float y, SKFont fonte, SKColor cor)
        {
            using var tinta = new SKPaint { Color = cor, IsAntialias = true };
            _c.DrawText(texto, x, y, SKTextAlign.Right, fonte, tinta);
        }
    }

    private static List<string> Quebrar(string texto, SKFont fonte, float largura)
    {
        var linhas = new List<string>();
        foreach (var paragrafo in texto.Replace("\r", "").Split('\n'))
        {
            var atual = "";
            foreach (var palavra in paragrafo.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var tentativa = atual.Length == 0 ? palavra : $"{atual} {palavra}";
                if (fonte.MeasureText(tentativa) <= largura || atual.Length == 0)
                    atual = tentativa;
                else
                {
                    linhas.Add(atual);
                    atual = palavra;
                }
            }
            linhas.Add(atual);
        }
        return linhas;
    }

    private static string Cortar(string texto, SKFont fonte, float largura)
    {
        if (fonte.MeasureText(texto) <= largura)
            return texto;
        while (texto.Length > 1 && fonte.MeasureText(texto + "…") > largura)
            texto = texto[..^1];
        return texto.TrimEnd() + "…";
    }
}
