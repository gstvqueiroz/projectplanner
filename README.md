# ProjectPlanner

Planejamento e acompanhamento de obras no computador: cronograma com Gantt e caminho crítico, orçamento (com base
SINAPI), medições, custos, recursos, fotos da obra, relatórios em PDF e Word e indicadores. Inclui um assistente de IA que
monta e atualiza o cronograma pelo chat. Para Windows e macOS, em C# (.NET 10) e Avalonia.

## Baixar

Os pacotes de cada versão estão em Releases: instalador e .zip para Windows (x64 e ARM64) e o .app para Mac (Apple
Silicon). As novidades de cada versão estão em [CHANGELOG.md](CHANGELOG.md).

## O assistente de IA

Duas formas de usar:

- **Com a sua chave do Gemini** (grátis no Google AI Studio): ela fica no cofre do seu computador e os pedidos vão direto
  ao Google. Ver [docs/ASSISTENTE-IA.md](docs/ASSISTENTE-IA.md).
- **Com uma conta ProjectPlanner** (em breve): sem chave, com um plano grátis e um plano pago. Os pedidos passam pelo
  servidor deste repositório ([src/ProjectPlanner.Servidor](src/ProjectPlanner.Servidor), ver
  [docs/SERVIDOR.md](docs/SERVIDOR.md)).

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
