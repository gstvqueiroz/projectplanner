using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using ProjectPlanner.Core;
using ProjectPlanner.Core.Fotos;

namespace ProjectPlanner.Data;

/// <summary>
/// Guarda cada projeto num arquivo SQLite próprio.
/// </summary>
/// <remarks>
/// <para>
/// Salvar monta o arquivo inteiro do zero num arquivo temporário da mesma pasta e só então o põe no
/// lugar do destino. Assim, salvar sempre sobrescreve tudo (vence quem salvar por último, sem
/// controle de concorrência), e uma falha no meio nunca deixa o arquivo original pela metade.
/// </para>
/// <para>
/// Abrir aplica as migrations pendentes (arquivos de versões anteriores são atualizados no lugar) e
/// reconstrói o domínio pelos construtores e métodos normais, revalidando todas as regras. A coluna
/// DataTermino é gravada para quem ler o arquivo por fora; ao abrir, ela é recalculada a partir de
/// DataInicio e Duracao.
/// </para>
/// <para>
/// A estrutura vai em TarefaPaiId, com as tarefas gravadas em pré-ordem (Ordem): o pai sempre vem
/// antes das subtarefas, então ao abrir a árvore se monta numa passada só. A coluna Tipo, como a
/// DataTermino, é para quem ler o arquivo por fora: ao abrir, o tipo vem da estrutura e da duração. Os
/// valores de um resumo (datas, duração, percentual) são os calculados no último cálculo, e são
/// recalculados a partir das subtarefas no próximo.
/// </para>
/// </remarks>
/// <param name="antesDeGravarPorCima">
/// Chamado com o caminho do arquivo logo antes de o novo substituí-lo (só quando ele já existe e o novo ficou pronto):
/// é onde o programa guarda a cópia de segurança da versão anterior (Bloco 25). Uma falha dele não impede de salvar.
/// </param>
public sealed class SqliteProjetoRepository(TimeProvider? relogio = null, Action<string>? antesDeGravarPorCima = null) : IProjetoRepository
{
    private const int SqliteNaoEhBanco = 26; // SQLITE_NOTADB

    private readonly TimeProvider _relogio = relogio ?? TimeProvider.System;

    public void Salvar(Cronograma cronograma, string caminhoArquivo) => Salvar(cronograma, caminhoArquivo, ImagensParaSalvar.Nenhuma);

    private void AvisarAntesDeGravarPorCima(string destino)
    {
        try
        {
            antesDeGravarPorCima!(destino);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A cópia de segurança nunca impede de salvar.
        }
    }

    public void Salvar(Cronograma cronograma, string caminhoArquivo, ImagensParaSalvar imagens)
    {
        ArgumentNullException.ThrowIfNull(cronograma);
        ArgumentNullException.ThrowIfNull(imagens);
        ArgumentException.ThrowIfNullOrWhiteSpace(caminhoArquivo);

        var destino = Path.GetFullPath(caminhoArquivo);
        var pasta = Path.GetDirectoryName(destino)!;
        if (!Directory.Exists(pasta))
            throw new DirectoryNotFoundException($"A pasta \"{pasta}\" não existe.");

        var agora = _relogio.GetUtcNow().UtcDateTime;
        var criacao = cronograma.DataCriacao ?? agora;
        var temporario = Path.Combine(pasta, $".{Path.GetFileName(destino)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var contexto = ProjectPlannerDbContext.Criar(temporario, criarSeNaoExistir: true))
            {
                contexto.Database.Migrate();
                Gravar(contexto, cronograma, criacao, agora, imagens.Novas);
                contexto.SaveChanges();
                CopiarImagensDaOrigem(contexto, imagens.ArquivoDeOrigem);
                VerificarQueTodasAsFotosTemImagem(contexto, imagens.ArquivoDeOrigem);
            }
            if (antesDeGravarPorCima is not null && File.Exists(destino))
                AvisarAntesDeGravarPorCima(destino);
            File.Move(temporario, destino, overwrite: true);
        }
        catch
        {
            ApagarSemFalhar(temporario);
            throw;
        }

        cronograma.DefinirDatasDoArquivo(criacao, agora);
    }

    public Cronograma Abrir(string caminhoArquivo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caminhoArquivo);
        if (!File.Exists(caminhoArquivo))
            throw new FileNotFoundException($"O arquivo de projeto \"{caminhoArquivo}\" não existe.", caminhoArquivo);

        try
        {
            using var contexto = ProjectPlannerDbContext.Criar(caminhoArquivo, criarSeNaoExistir: false);
            AtualizarSchemaSePreciso(contexto, caminhoArquivo);
            return Ler(contexto, caminhoArquivo);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteNaoEhBanco)
        {
            throw NaoEhProjeto(caminhoArquivo, ex);
        }
        catch (DomainValidationException ex)
        {
            throw new InvalidDataException(
                $"O arquivo \"{caminhoArquivo}\" tem dados inválidos: {ex.Message}", ex);
        }
    }

    public IReadOnlyDictionary<Guid, byte[]> LerMiniaturas(string caminhoArquivo, IReadOnlyCollection<Guid> fotos)
    {
        ArgumentNullException.ThrowIfNull(fotos);
        if (fotos.Count == 0)
            return new Dictionary<Guid, byte[]>();
        using var contexto = ContextoDeLeitura(caminhoArquivo);
        var ids = fotos.ToList();
        return contexto.Miniaturas.AsNoTracking().Where(m => ids.Contains(m.FotoId)).ToDictionary(m => m.FotoId, m => m.Dados);
    }

    public byte[]? LerImagem(string caminhoArquivo, Guid foto)
    {
        using var contexto = ContextoDeLeitura(caminhoArquivo);
        return contexto.Imagens.AsNoTracking().Where(i => i.FotoId == foto).Select(i => i.Dados).FirstOrDefault();
    }

    private static ProjectPlannerDbContext ContextoDeLeitura(string caminhoArquivo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caminhoArquivo);
        if (!File.Exists(caminhoArquivo))
            throw new FileNotFoundException($"O arquivo de projeto \"{caminhoArquivo}\" não existe.", caminhoArquivo);
        return ProjectPlannerDbContext.Criar(caminhoArquivo, criarSeNaoExistir: false);
    }

    // As imagens que não vieram da memória (fotos que já estavam no projeto) são copiadas do arquivo de origem
    // direto no SQLite, sem passar pelo .NET: anexa a origem, copia as das fotos que ficaram e solta.
    private static void CopiarImagensDaOrigem(ProjectPlannerDbContext contexto, string? origem)
    {
        if (origem is null || !File.Exists(origem) || !contexto.Fotos.Any())
            return;
        var banco = contexto.Database;
        banco.OpenConnection();
        try
        {
            banco.ExecuteSqlRaw("ATTACH DATABASE {0} AS origem", Path.GetFullPath(origem));
            try
            {
                banco.ExecuteSqlRaw("""
                    INSERT INTO main.FotosImagens (FotoId, Dados)
                    SELECT o.FotoId, o.Dados FROM origem.FotosImagens o
                    WHERE o.FotoId IN (SELECT Id FROM main.FotosTarefa)
                      AND o.FotoId NOT IN (SELECT FotoId FROM main.FotosImagens)
                    """);
                banco.ExecuteSqlRaw("""
                    INSERT INTO main.FotosMiniaturas (FotoId, Dados)
                    SELECT o.FotoId, o.Dados FROM origem.FotosMiniaturas o
                    WHERE o.FotoId IN (SELECT Id FROM main.FotosTarefa)
                      AND o.FotoId NOT IN (SELECT FotoId FROM main.FotosMiniaturas)
                    """);
            }
            catch (SqliteException ex)
            {
                throw new IOException($"Não foi possível copiar as imagens das fotos de \"{origem}\": {ex.Message}", ex);
            }
            finally
            {
                banco.ExecuteSqlRaw("DETACH DATABASE origem");
            }
        }
        finally
        {
            banco.CloseConnection();
        }
    }

    // Nenhuma foto pode ficar sem imagem: se o arquivo de origem sumiu ou mudou, nada é salvo (o temporário é
    // apagado e o destino fica como estava).
    // Bloco 28: um GPS incompleto ou impossível (arquivo mexido por fora) não impede de abrir: a foto fica sem ele.
    private static Coordenadas? Gps(FotoRegistro registro)
    {
        if (registro is not { Latitude: { } latitude, Longitude: { } longitude })
            return null;
        try
        {
            return new Coordenadas(latitude, longitude);
        }
        catch (DomainValidationException)
        {
            return null;
        }
    }

    private static void VerificarQueTodasAsFotosTemImagem(ProjectPlannerDbContext contexto, string? origem)
    {
        var semImagem = contexto.Fotos.Count(f => !contexto.Imagens.Any(i => i.FotoId == f.Id)
                                                  || !contexto.Miniaturas.Any(m => m.FotoId == f.Id));
        if (semImagem > 0)
            throw new IOException($"{semImagem} foto(s) ficariam sem imagem: o arquivo de onde elas vêm " +
                                  $"(\"{origem ?? "nenhum"}\") não foi encontrado ou mudou. Nada foi salvo.");
    }

    private static readonly JsonSerializerOptions OpcoesDasTags = new();

    private static void Gravar(ProjectPlannerDbContext contexto, Cronograma cronograma, DateTime criacao, DateTime agora,
        IReadOnlyDictionary<Guid, ConteudoDeFoto> novas)
    {
        contexto.Projetos.Add(new ProjetoRegistro
        {
            Id = cronograma.Id,
            Nome = cronograma.Nome,
            DataInicio = cronograma.DataInicio,
            DataCriacao = criacao,
            DataUltimaModificacao = agora,
        });

        contexto.Tarefas.AddRange(cronograma.Tarefas.Select((t, ordem) => new TarefaRegistro
        {
            Id = t.Id,
            ProjetoId = cronograma.Id,
            Ordem = ordem,
            Nome = t.Nome,
            Duracao = t.Duracao,
            DataInicio = t.DataInicio,
            DataTermino = t.DataTermino,
            PercentualConcluido = t.PercentualConcluido,
            Notas = t.Notas,
            Tipo = t.Tipo,
            TarefaPaiId = t.TarefaPaiId,
            NaoIniciarAntesDe = t.NaoIniciarAntesDe,
            Prazo = t.Prazo,
            Valor = t.EhResumo ? null : t.Valor,
            Unidade = t.EhResumo ? null : t.Unidade,
            Quantidade = t.EhResumo ? null : t.Quantidade,
        }));

        contexto.Recursos.AddRange(cronograma.Recursos.Lista.Select((r, ordem) => new RecursoRegistro
        {
            Id = r.Id,
            ProjetoId = cronograma.Id,
            Ordem = ordem,
            Nome = r.Nome,
            Tipo = (int)r.Tipo,
            Unidade = r.Unidade,
            CustoUnitario = r.CustoUnitario,
            Disponivel = r.Disponivel,
            Fornecedor = r.Fornecedor,
        }));
        contexto.Atribuicoes.AddRange(cronograma.Tarefas.SelectMany(t => t.Atribuicoes.Select((a, ordem) => new AtribuicaoRegistro
        {
            TarefaId = t.Id,
            RecursoId = a.RecursoId,
            Ordem = ordem,
            Quantidade = a.Quantidade,
        })));

        var fotos = cronograma.Tarefas.SelectMany(t => t.FotosNaOrdemDeCadastro).ToList();
        contexto.Fotos.AddRange(fotos.Select((f, ordem) => new FotoRegistro
        {
            Id = f.Id,
            TarefaId = f.TarefaId,
            Ordem = ordem,
            DataFoto = f.DataFoto,
            DataCadastro = f.DataCadastro,
            Titulo = f.Titulo,
            Descricao = f.Descricao,
            Autor = f.Autor,
            Local = f.Local,
            PercentualConcluidoNaData = f.PercentualConcluidoNaData,
            NomeArquivoOriginal = f.Imagem.NomeArquivoOriginal,
            Largura = f.Imagem.Largura,
            Altura = f.Imagem.Altura,
            TamanhoBytes = f.Imagem.TamanhoBytes,
            HashOriginal = f.Imagem.HashOriginal,
            Tags = f.Tags.Count == 0 ? null : JsonSerializer.Serialize(f.Tags, OpcoesDasTags),
            NoRelatorio = f.NoRelatorio,
            Destaque = f.Destaque,
            Latitude = f.Gps?.Latitude,
            Longitude = f.Gps?.Longitude,
        }));
        foreach (var foto in fotos)
            if (novas.TryGetValue(foto.Id, out var conteudo))
            {
                contexto.Imagens.Add(new FotoImagemRegistro { FotoId = foto.Id, Dados = conteudo.Imagem });
                contexto.Miniaturas.Add(new FotoMiniaturaRegistro { FotoId = foto.Id, Dados = conteudo.Miniatura });
            }

        contexto.Predecessoras.AddRange(cronograma.Predecessoras.Select((p, ordem) => new PredecessoraRegistro
        {
            Ordem = ordem,
            TarefaOrigemId = p.TarefaOrigemId,
            TarefaDestinoId = p.TarefaDestinoId,
            Tipo = p.Tipo,
            Lag = p.Lag,
        }));

        if (cronograma.LinhasDeBase.Count > 0)
            contexto.LinhasDeBase.Add(new LinhasDeBaseRegistro
            {
                ProjetoId = cronograma.Id,
                Versao = FormatoDasLinhasDeBase.Versao,
                Dados = FormatoDasLinhasDeBase.Serializar(cronograma),
            });

        if (!cronograma.Contrato.Vazio)
            contexto.Contrato.Add(new ContratoRegistro
            {
                ProjetoId = cronograma.Id,
                Versao = FormatoDoContrato.Versao,
                Dados = FormatoDoContrato.Serializar(cronograma),
            });

        if (!cronograma.Custos.Vazio)
            contexto.Custos.Add(new CustosRegistro
            {
                ProjetoId = cronograma.Id,
                Versao = FormatoDosCustos.Versao,
                Dados = FormatoDosCustos.Serializar(cronograma),
            });

        if (FormatoDoRelatorio.TemDados(cronograma))
            contexto.Relatorio.Add(new RelatorioRegistro
            {
                ProjetoId = cronograma.Id,
                Versao = FormatoDoRelatorio.Versao,
                Dados = FormatoDoRelatorio.Serializar(cronograma),
            });

        if (!cronograma.Assistente.Vazia)
            contexto.Assistente.Add(new AssistenteRegistro
            {
                ProjetoId = cronograma.Id,
                Versao = FormatoDaConversa.Versao,
                Dados = FormatoDaConversa.Serializar(cronograma.Assistente),
            });
    }

    // Um arquivo de projeto tem a migration inicial aplicada; se tiver alguma que esta versão não
    // conhece, foi salvo por uma versão mais nova e não pode ser aberto (nem atualizado) por esta.
    // Só migra quando falta alguma migration, para que abrir um arquivo atualizado não o altere.
    private static void AtualizarSchemaSePreciso(ProjectPlannerDbContext contexto, string caminhoArquivo)
    {
        var conhecidas = contexto.Database.GetMigrations().ToList();
        var aplicadas = contexto.Database.GetAppliedMigrations().ToList();

        if (!aplicadas.Contains(conhecidas[0]))
            throw NaoEhProjeto(caminhoArquivo);
        if (aplicadas.Except(conhecidas).Any())
            throw new InvalidDataException(
                $"O arquivo \"{caminhoArquivo}\" foi salvo por uma versão mais nova do ProjectPlanner.");
        if (conhecidas.Except(aplicadas).Any())
            contexto.Database.Migrate();
    }

    private static Cronograma Ler(ProjectPlannerDbContext contexto, string caminhoArquivo)
    {
        var projeto = contexto.Projetos.AsNoTracking().ToList() switch
        {
            [var unico] => unico,
            var projetos => throw new InvalidDataException(
                $"O arquivo \"{caminhoArquivo}\" deveria ter um projeto, mas tem {projetos.Count}."),
        };

        var cronograma = new Cronograma(projeto.Id, projeto.Nome, projeto.DataInicio);
        cronograma.DefinirDatasDoArquivo(
            DateTime.SpecifyKind(projeto.DataCriacao, DateTimeKind.Utc),
            DateTime.SpecifyKind(projeto.DataUltimaModificacao, DateTimeKind.Utc));

        foreach (var registro in contexto.Tarefas.AsNoTracking().OrderBy(t => t.Ordem))
        {
            // Um resumo só com marcos tem duração 0 e seria lido como marco, que não pode ter
            // subtarefas; como os valores do resumo são recalculados, ele entra com pelo menos 1 dia.
            var duracao = registro.Tipo == TipoTarefa.Resumo ? Math.Max(registro.Duracao, 1) : registro.Duracao;
            var tarefa = new Tarefa(registro.Id, registro.Nome, registro.DataInicio, duracao);
            tarefa.AtualizarPercentualConcluido(registro.PercentualConcluido);
            tarefa.AlterarNotas(registro.Notas);
            // Blocos 31, 35 e 36: a restrição, o valor, a unidade e a quantidade de um resumo não existem (o arquivo não os
            // grava); o prazo vale nele também.
            if (registro.Tipo != TipoTarefa.Resumo)
            {
                tarefa.DefinirRestricaoDeInicio(registro.NaoIniciarAntesDe);
                tarefa.DefinirValor(registro.Valor);
                tarefa.DefinirUnidade(registro.Unidade);
                tarefa.DefinirQuantidade(registro.Quantidade);
            }
            tarefa.DefinirPrazo(registro.Prazo);
            cronograma.AdicionarTarefa(tarefa, registro.TarefaPaiId);
        }

        // Os recursos e as atribuições (Bloco 32).
        cronograma.Recursos.Restaurar(contexto.Recursos.AsNoTracking().OrderBy(r => r.Ordem).ToList()
            .Select(r => new Core.Recursos.Recurso(r.Id, r.Nome, (Core.Recursos.TipoDeRecurso)r.Tipo, r.Unidade, r.CustoUnitario, r.Disponivel, r.Fornecedor)));
        var porTarefa = cronograma.Tarefas.ToDictionary(t => t.Id);
        foreach (var grupo in contexto.Atribuicoes.AsNoTracking().ToList().GroupBy(a => a.TarefaId))
            cronograma.Recursos.Atribuir(porTarefa[grupo.Key], grupo.OrderBy(a => a.Ordem).Select(a => new Core.Recursos.AtribuicaoDeRecurso(a.RecursoId, a.Quantidade)));

        // Só os ids das imagens, sem os BLOBs. Registros sem imagem são as "fotos de exemplo" das versões 0.14 a
        // 0.21: ficam de fora e somem do arquivo no próximo salvamento (Bloco 22, D6).
        var tarefas = cronograma.Tarefas.ToDictionary(t => t.Id);
        var comImagem = contexto.Imagens.AsNoTracking().Select(i => i.FotoId).ToHashSet();
        var descartadas = 0;
        foreach (var registro in contexto.Fotos.AsNoTracking().OrderBy(f => f.Ordem))
        {
            if (!comImagem.Contains(registro.Id))
            {
                descartadas++;
                continue;
            }
            tarefas[registro.TarefaId].AdicionarFoto(new FotoTarefa(registro.Id, registro.TarefaId,
                new DadosDaImagem(registro.NomeArquivoOriginal, registro.Largura, registro.Altura, registro.TamanhoBytes,
                    registro.HashOriginal),
                new MetadadosFoto(registro.DataFoto, registro.Titulo, registro.Descricao, registro.Autor, registro.Local,
                    registro.PercentualConcluidoNaData,
                    registro.Tags is null ? null : JsonSerializer.Deserialize<List<string>>(registro.Tags, OpcoesDasTags)),
                registro.DataCadastro).ComCuradoria(registro.NoRelatorio, registro.Destaque) with { Gps = Gps(registro) });
        }
        cronograma.RegistrarFotosDescartadas(descartadas);

        foreach (var registro in contexto.Predecessoras.AsNoTracking().OrderBy(p => p.Ordem))
            cronograma.AdicionarPredecessora(new Predecessora(
                registro.TarefaOrigemId, registro.TarefaDestinoId, registro.Tipo, registro.Lag));

        // Linhas de base (Bloco 31); um JSON que não dá para ler é recusado como os outros dados.
        if (contexto.LinhasDeBase.AsNoTracking().FirstOrDefault() is { } linhas)
        {
            if (linhas.Versao != FormatoDasLinhasDeBase.Versao)
                throw new InvalidDataException($"O arquivo \"{caminhoArquivo}\" tem as linhas de base num formato desconhecido.");
            FormatoDasLinhasDeBase.Ler(linhas.Dados, cronograma);
        }

        // O contrato e as medições (Bloco 36), depois das tarefas (os itens são as tarefas); um JSON que não dá para ler é
        // recusado como os outros dados.
        if (contexto.Contrato.AsNoTracking().FirstOrDefault() is { } contrato)
        {
            if (contrato.Versao != FormatoDoContrato.Versao)
                throw new InvalidDataException($"O arquivo \"{caminhoArquivo}\" tem o contrato num formato desconhecido.");
            FormatoDoContrato.Ler(contrato.Dados, cronograma);
        }

        // Os custos realizados (Bloco 37).
        if (contexto.Custos.AsNoTracking().FirstOrDefault() is { } custos)
        {
            if (custos.Versao != FormatoDosCustos.Versao)
                throw new InvalidDataException($"O arquivo \"{caminhoArquivo}\" tem os custos num formato desconhecido.");
            FormatoDosCustos.Ler(custos.Dados, cronograma);
        }

        // Dados da obra e histórico dos relatórios (Bloco 27); um JSON que não dá para ler é recusado como os outros dados.
        if (contexto.Relatorio.AsNoTracking().FirstOrDefault() is { } relatorio)
        {
            if (relatorio.Versao != FormatoDoRelatorio.Versao)
                throw new InvalidDataException($"O arquivo \"{caminhoArquivo}\" tem os dados do relatório num formato desconhecido.");
            FormatoDoRelatorio.Ler(relatorio.Dados, cronograma);
        }

        // A conversa nunca impede de abrir o projeto: uma que não dá para ler fica de fora, com aviso (Bloco 26).
        if (contexto.Assistente.AsNoTracking().FirstOrDefault() is { } assistente)
        {
            try
            {
                if (assistente.Versao != FormatoDaConversa.Versao)
                    throw new InvalidDataException($"Formato {assistente.Versao} da conversa, desconhecido.");
                cronograma.DefinirAssistente(FormatoDaConversa.Ler(assistente.Dados));
            }
            catch (InvalidDataException)
            {
                cronograma.RegistrarConversaDescartada();
            }
        }

        return cronograma;
    }

    private static InvalidDataException NaoEhProjeto(string caminhoArquivo, Exception? causa = null) =>
        new($"O arquivo \"{caminhoArquivo}\" não é um projeto do ProjectPlanner.", causa);

    private static void ApagarSemFalhar(string caminho)
    {
        try
        {
            File.Delete(caminho);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // O erro original é o que importa; um temporário esquecido não atrapalha.
        }
    }
}
