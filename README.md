# ProjectPlanner

Planejamento e acompanhamento de obras no computador: cronograma com Gantt e caminho crítico, orçamento (com base
SINAPI), medições, custos, recursos, fotos da obra, relatórios em PDF e Word e indicadores. Inclui um assistente de IA que
monta e atualiza o cronograma pelo chat. Para Windows e macOS, em C# (.NET 10) e Avalonia.

## Baixar

Os pacotes de cada versão estão em Releases: instalador e .zip para Windows (x64 e ARM64) e o .app para Mac (Apple
Silicon). As novidades de cada versão estão em [CHANGELOG.md](CHANGELOG.md).

## O assistente de IA

O assistente funciona pela **conta ProjectPlanner** (menu Conta): sem chave, com créditos por mês e três níveis de modelo
(Padrão: Gemini Flash; Avançado: Gemini Pro; Máximo: Claude Opus). O plano Grátis tem 300 créditos por mês no nível
Padrão. Os pedidos passam pelo servidor deste repositório ([src/ProjectPlanner.Servidor](src/ProjectPlanner.Servidor), ver
[docs/SERVIDOR.md](docs/SERVIDOR.md)), que não guarda o conteúdo das conversas.

**Para quem compila o código:** com a variável de ambiente `GEMINI_API_KEY` (uma chave grátis do Google AI Studio), o
assistente vai direto ao Google, sem conta. Também dá para rodar o próprio servidor (`docs/SERVIDOR.md`) e apontar o
programa para ele com `PROJECTPLANNER_SERVIDOR`.

## Compilar

Precisa do .NET SDK 10.

```bash
dotnet build ProjectPlanner.slnx
dotnet test ProjectPlanner.slnx
dotnet run --project src/ProjectPlanner.App
```

Para gerar os pacotes de Windows e Mac, ver [PUBLISHING.md](PUBLISHING.md).

| Projeto | O quê |
|---|---|
| `src/ProjectPlanner.Core` | O domínio: tarefas, calendário, cálculo do cronograma, orçamento, medições, custos, indicadores |
| `src/ProjectPlanner.Data` | O arquivo `.pplan` e as importações |
| `src/ProjectPlanner.IA` | O assistente e as análises com IA (Gemini) |
| `src/ProjectPlanner.Imagens` | As fotos (leitura, EXIF, miniaturas) |
| `src/ProjectPlanner.Relatorios` | Os relatórios em PDF e Word |
| `src/ProjectPlanner.App` | O programa (Avalonia) |
| `src/ProjectPlanner.Servidor` | O servidor de contas e IA (ASP.NET Core) |

## Licença

[GNU Affero General Public License v3.0](LICENSE) (AGPL-3.0). Você pode usar, estudar, modificar e distribuir o
programa. Quem distribuir uma versão modificada, ou oferecer o servidor modificado como serviço pela internet, precisa
disponibilizar o código-fonte das modificações sob a mesma licença.
