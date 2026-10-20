# Servidor de contas e IA (Blocos 44–46): plano

Decisões do usuário (18/10/2026):

- Login com **e-mail e senha + "Entrar com Google"**.
- O app continua aceitando a **chave própria do Gemini** (grátis); quem assina usa a conta ProjectPlanner, sem chave.
- O servidor fica **neste repositório, aberto**. O que protege o serviço é a chave e o banco, só na nuvem.
- Planos: **grátis com cota pequena + plano pago**. A cobrança automática fica para o Bloco 46.
- Depois: o assistente no **Telegram**, usando as mesmas contas.

## Arquitetura

A chave do Gemini nunca vai no app. O app manda o pedido de IA para o servidor, com o token do usuário.

O servidor:
1. confere o token e a cota;
2. chama o Gemini com a chave dele;
3. registra o uso (`usageMetadata`);
4. devolve a resposta.

O ponto de IA espelha o caminho do Gemini (`/v1/ia/models/{modelo}:generateContent`). Assim, no app, o novo provedor
reaproveita `AssistenteGemini`, `AnalistaGemini`, `ConsultorGemini` e `AnalistaDaObraGemini`, trocando só o endereço e o
cabeçalho (`Authorization: Bearer` no lugar de `x-goog-api-key`).

## Bloco 44 (0.43.0, feito): `src/ProjectPlanner.Servidor` (ASP.NET Core 10, API mínima)

### Onde está cada coisa

| O quê | Onde |
|---|---|
| Início, serviços, JWT, limites e rotas | `Program.cs` |
| Configuração (`OpcoesDoServidor`: Jwt, Gemini, Google, Planos, Email, Limites, Admin) | `Opcoes.cs` |
| Banco (usuários, tokens de renovação, códigos de e-mail, uso de IA) e a escolha SQLite/PostgreSQL | `Dados/` |
| Migrações do PostgreSQL | `Dados/Migracoes/` |
| Hashes, tokens e sessões (`Segredos`, `EmissorDeTokens`, `ChaveDoJwt`) | `Servicos/Segredos.cs` |
| E-mail (log em desenvolvimento, SMTP em produção) e os textos | `Servicos/Email.cs` |
| Validação do "Entrar com Google" | `Servicos/Google.cs` |
| Rotas de conta, sessão, senha e `/v1/eu` | `Rotas/Contas.cs` |
| O ponto de IA | `Rotas/Ia.cs` |
| Trocar o plano à mão | `Rotas/Admin.cs` |
| Limites por minuto | `Rotas/LimitesDeUso.cs` |
| Testes (41, com SQLite; com PostgreSQL pela variável `PP_SERVIDOR_POSTGRES`) | `tests/ProjectPlanner.Servidor.Tests/` |

### Banco (EF Core)

- SQLite em desenvolvimento e nos testes (as tabelas saem direto do modelo); PostgreSQL em produção, com as migrações.
  Qual é qual sai da connection string: com `Host=`, PostgreSQL.
- Tabelas:
  - Usuarios: e-mail normalizado, nome, hash da senha (PasswordHasher, PBKDF2), Google `sub`, e-mail confirmado, plano,
    tentativas erradas e bloqueio;
  - TokensDeRenovacao: só o hash (SHA-256), com rotação e revogação da família se um token já trocado voltar;
  - CodigosDeEmail: confirmação e troca de senha, com hash, validade de 30 min, uso único e 5 tentativas. O de confirmação
    guarda a senha (hash) e o nome do cadastro que o pediu;
  - UsosDeIa: usuário, data, modelo, tokens de entrada, de saída e totais. O conteúdo das conversas não é guardado.
- Mudou o modelo? Gere a migração (as migrações são as do PostgreSQL; não precisa de banco para gerar):

  ```bash
  dotnet ef migrations add NomeDaMudanca --project src/ProjectPlanner.Servidor -o Dados/Migracoes
  ```

### Rotas

Erros sempre como `{"erro": "mensagem para o usuário"}`.

| Rota | O quê |
|---|---|
| `POST /v1/contas` | Cadastro `{email, senha, nome}`: senha de 10 a 200 caracteres, nome de até 120. Manda um código de 6 dígitos por e-mail e responde 202. Num e-mail que já tem conta, responde igual e manda ao dono o aviso "Você já tem uma conta" |
| `POST /v1/contas/confirmar` | `{email, codigo, senha}`: confirma e devolve a sessão. Exige a senha do cadastro daquele código, para que o cadastro de outra pessoa com o mesmo e-mail não vire a senha da conta |
| `POST /v1/contas/reenviar` | `{email}`: um código novo (com a senha do último cadastro); 202 sempre |
| `POST /v1/sessoes` | Login `{email, senha}`: devolve a sessão `{acesso, acessoExpiraEm, renovacao, renovacaoExpiraEm, usuario}`. "E-mail ou senha incorretos." nos dois casos; 403 se falta confirmar o e-mail (só com a senha certa); 5 erros bloqueiam por 15 min (429) |
| `POST /v1/sessoes/google` | `{idToken}`: valida no Google (audiência = os client IDs configurados) e cria a conta ou vincula a que tem o mesmo e-mail (só com o e-mail verificado pelo Google) |
| `GET /v1/google` | Bloco 45: o client ID do Google do programa (404 se o Google não está configurado) |
| `POST /v1/sessoes/google/codigo` | Bloco 45: `{codigo, verificador, redirecionamento}`: o login com Google do programa. O servidor troca o código pelo idToken com o segredo, que fica só nele, e segue como a rota acima. O redirecionamento precisa ser `http://127.0.0.1:porta/` |
| `POST /v1/sessoes/renovar` | `{renovacao}`: um acesso novo e um token de renovação novo (o velho deixa de valer) |
| `POST /v1/sessoes/sair` | `{renovacao}`: encerra aquela sessão (204) |
| `POST /v1/senha/esqueci` | `{email}`: manda o código de troca; 202 sempre |
| `POST /v1/senha/redefinir` | `{email, codigo, novaSenha}`: troca a senha, encerra as outras sessões, tira o bloqueio e devolve uma sessão nova |
| `GET /v1/eu` | Com login: `{perfil, tokensUsadosNoMes, tokensDoPlano}` |
| `POST /v1/ia/models/{modelo}:generateContent` | O pedido de IA (abaixo) |
| `GET /v1/ia/models/{modelo}` | Testar a conexão: confere o login, o modelo, o plano e a chave do servidor, sem ir à IA |
| `GET /v1/modelos` | Bloco 46: os modelos de cada nível, se o plano libera, se o servidor tem a chave e os créditos de um pedido típico |
| `POST /v1/admin/usuarios/{email}/plano` | `{plano}` com o cabeçalho `X-Chave-Admin`: troca o plano (gratis ou profissional) |
| `GET /admin` | Bloco 48: a página do painel financeiro (sem dados; pede a chave de admin) |
| `GET /v1/admin/painel?de=AAAA-MM-DD&ate=AAAA-MM-DD` | Bloco 48: com `X-Chave-Admin`, os números do período (padrão: o mês até hoje; até 366 dias) |

Os códigos por e-mail: no máximo 5 de cada tipo por conta, por hora. Passado isso, a resposta é a mesma e nada é mandado,
o que segura quem tentasse encher a caixa de alguém ou adivinhar o código.

O ponto de IA:
- exige login (`Authorization: Bearer <acesso>`); o token não vai ao Google nem à Anthropic;
- aceita só os modelos do catálogo, e o nível precisa ser do plano (Grátis: só o Padrão), senão 403;
- limita o corpo (15 MB) e os pedidos por minuto de cada usuário (30);
- confere os créditos do mês (UTC) antes de chamar (402 quando acabaram; o pedido que passa do limite ainda é atendido);
- Bloco 46: para os modelos da Anthropic, traduz o pedido do formato do Gemini (instruções, conversa, fotos JPG, PNG, GIF
  e WebP, PDF, e a resposta estruturada como ferramenta obrigatória) e a resposta de volta. Fotos HEIC e outros anexos que
  o Claude não lê dão 400, com a orientação de usar outro nível;
- registra os tokens (a saída inclui os de raciocínio) e o **custo** em milionésimos de dólar, pela tabela de preços do
  modelo, e manda nos cabeçalhos os créditos do pedido (`X-Creditos-Usados`) e o que resta no mês (`X-Creditos-Restantes`);
- traduz as falhas: chave ou modelo do servidor recusados → 502 (sem repassar o que o Google ou a Anthropic disseram),
  429 (e o 529 da Anthropic) → 503, 5xx → 502, sem resposta → 504; um 400 do pedido passa adiante (com a chave, se
  aparecer, trocada por "(chave)").

### Configuração

Tudo por variáveis de ambiente (a seção e a chave separadas por `__`). Nenhum segredo fica no repositório.

| Variável | O quê |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` na nuvem |
| `ConnectionStrings__Banco` | PostgreSQL: `Host=...;Database=...;Username=...;Password=...` ou `postgresql://usuario:senha@servidor/banco?sslmode=require` |
| `Jwt__Chave` | 32 bytes ou mais, aleatória (por exemplo, `openssl rand -base64 48`). Sem ela, em produção, o servidor não sobe |
| `Gemini__Chave` | A chave do Gemini da empresa. Sem ela, a IA responde 503 |
| `Anthropic__Chave` | Bloco 46: a chave da API da Anthropic (o nível Máximo). Sem ela, o Máximo aparece como "indisponível no momento" |
| `Modelos__0__Id`, `Modelos__0__Nivel`, `Modelos__0__Provedor`, `Modelos__0__Nome`, `Modelos__0__DolaresPorMilhaoDeEntrada`, `Modelos__0__DolaresPorMilhaoDeSaida` (e `__1`, `__2`…) | Opcional: o catálogo de modelos. Sem ele, os padrão: Gemini 2.5 Flash (padrao), Gemini 3.1 Pro (avancado) e Claude Opus 5.5 (maximo). Configurado, substitui a lista inteira |
| `Google__ClientIdDoApp`, `Google__SegredoDoApp` | Bloco 45: o client ID OAuth do tipo "App para computador" e o segredo dele. Sem os dois, o "Entrar com Google" do programa fica desligado (o resto funciona) |
| `Google__ClientIds__0`, `__1`… | Opcional: outros client IDs aceitos no idToken (por exemplo, o do Telegram, depois) |
| `Email__Smtp`, `Email__Porta`, `Email__Usuario`, `Email__Senha`, `Email__Remetente` | O SMTP do serviço de e-mail. Sem `Email__Smtp`, os e-mails vão só para o log |
| `Admin__Chave` | A chave da rota de admin e do painel financeiro. Sem ela, os dois ficam fechados |
| `Admin__Email` | Bloco 48: para onde vai o alerta de custo do mês. Sem ele, o alerta só aparece no painel |
| `Financeiro__ReaisPorDolar`, `Financeiro__PercentualSobreODolar` | Bloco 48: o câmbio e o IOF/spread do cartão sobre as contas de IA (padrão 5,50 e 3,5) |
| `Financeiro__PercentualDeImpostosETaxas` | Impostos e taxa do meio de pagamento sobre a receita (padrão 11) |
| `Financeiro__PrecoProfissionalEmReais` | O preço do Profissional, para a receita estimada (padrão 59) |
| `Financeiro__AlertaMensalEmReais` | O custo de IA do mês que dispara o alerta (padrão 100; 0 desliga) |
| `Planos__GratisCreditosPorMes`, `Planos__ProfissionalCreditosPorMes` | Os créditos por mês (padrão 300 e 6.000) |
| `Planos__DolaresPorCredito` | Quanto de custo vale 1 crédito (padrão 0,0005, meio milésimo de dólar) |
| `Planos__NiveisDoGratis` | Os níveis do plano Grátis, separados por vírgula (padrão `padrao`) |
| `Limites__PedidosDeIaPorMinuto`, `Limites__PedidosDeContaPorMinuto`, `Limites__MegabytesPorPedido`, `Limites__TentativasAteBloquear`, `Limites__MinutosDeBloqueio` | Os limites (padrão 30, 10, 15, 5 e 15) |
| `Limites__AtrasDeProxy` | `true` quando a nuvem põe um proxy na frente (quase sempre): os limites por IP usam o `X-Forwarded-For` |

### Rodar e testar

```bash
dotnet run --project src/ProjectPlanner.Servidor
```

Em desenvolvimento: SQLite em `servidor.db`, chave do JWT provisória e os códigos de e-mail no log.

```bash
dotnet test tests/ProjectPlanner.Servidor.Tests
```

Para rodar os mesmos testes num PostgreSQL (cada teste cria um banco temporário e o apaga no fim):

```bash
PP_SERVIDOR_POSTGRES="Host=/tmp;Username=$USER" dotnet test tests/ProjectPlanner.Servidor.Tests
```

### Publicar no Render + Neon + Brevo

O caminho escolhido: o servidor no **Render** (Docker, a partir do GitHub, com HTTPS), o banco no **Neon** (PostgreSQL)
e os e-mails pelo **Brevo** (SMTP). Começa no plano grátis dos três. O arquivo `render.yaml` na raiz descreve o serviço; as
chaves do JWT e de admin o Render gera sozinho.

1. **GitHub:** crie o repositório público (vazio, sem README nem licença) e envie o código.
2. **Neon** (neon.tech): crie o projeto na região **AWS US East (N. Virginia)**, a mesma do Render. Em Connect, copie a
   connection string **sem** "pooled" (o formato `postgresql://…?sslmode=require` serve; o servidor converte).
3. **Brevo** (brevo.com):
   - em Remetentes, cadastre e confirme o e-mail que vai mandar os códigos;
   - em SMTP e API > SMTP, anote o login (algo como `…@smtp-brevo.com`) e gere uma chave SMTP.

   O `render.yaml` já usa `smtp-relay.brevo.com` na porta 2525, que não é bloqueada como a 587 costuma ser em
   hospedagens gratuitas.
4. **Gemini:** no Google AI Studio, crie uma chave nova só para o servidor. No Google Cloud do mesmo projeto, ative o
   faturamento e crie um alerta de orçamento (Faturamento > Orçamentos e alertas).
5. **Render** (render.com): entre com o GitHub, New > Blueprint e escolha o repositório. O Render pede:
   - `ConnectionStrings__Banco`: a do Neon;
   - `Gemini__Chave`: a chave do passo 4;
   - `Email__Usuario`: o login SMTP do Brevo;
   - `Email__Senha`: a chave SMTP do Brevo;
   - `Email__Remetente`: `ProjectPlanner <o-email-confirmado-no-brevo>`;
   - `Google__ClientIdDoApp` e `Google__SegredoDoApp`: podem ficar em branco até criar o cliente do Google (abaixo).
6. **Licença:** no GitHub, Add file > Create new file, nome `LICENSE`, "Choose a license template" > GNU Affero General
   Public License v3.0 > Commit. Depois, aqui: `git pull`.
7. **Conferir:**

   ```bash
   scripts/testar-servidor.sh https://projectplanner-servidor.onrender.com seu-email@exemplo.com
   ```

   O script cria uma conta, pede o código que chegou por e-mail, mostra o plano e faz um pedido pequeno de IA.

No plano grátis, o Render desliga o servidor depois de 15 minutos sem uso: o primeiro pedido depois disso leva cerca de 1
minuto. Para vender, passe para o Starter (US$ 7/mês). A chave de admin fica no Render, em Environment.

Para liberar o plano pago à mão:

```bash
curl -X POST https://projectplanner-servidor.onrender.com/v1/admin/usuarios/ana@exemplo.com/plano -H "X-Chave-Admin: $CHAVE_ADMIN" -H "Content-Type: application/json" -d '{"plano":"profissional"}'
```

Outra nuvem? Qualquer uma que rode o Dockerfile serve:

```bash
docker build -f src/ProjectPlanner.Servidor/Dockerfile -t projectplanner-servidor .
```

A imagem escuta na porta 8080. Configure as variáveis da tabela acima (no mínimo `ASPNETCORE_ENVIRONMENT=Production`,
`ConnectionStrings__Banco`, `Jwt__Chave`, `Gemini__Chave`, `Limites__AtrasDeProxy=true` e o SMTP). Na primeira subida, o
servidor aplica as migrações.

**Cliente do Google** (para o "Entrar com Google" do programa):

1. No Google Cloud Console (console.cloud.google.com), no projeto `projectplanner-servidor`, abra APIs e serviços > Tela de
   permissão OAuth (ou "Google Auth Platform"):
   - tipo **Externo**, nome **ProjectPlanner**, e-mail de suporte e e-mail do desenvolvedor;
   - escopos `openid`, `email` e `profile` (não precisam de verificação do Google);
   - publique o app ("Em produção"). Em "Teste", só os e-mails da lista de testadores conseguem entrar.
2. Em Clientes (ou Credenciais) > Criar cliente OAuth, escolha o tipo **App para computador**, com o nome
   `ProjectPlanner (programa)`.
3. Copie o **ID do cliente** para `Google__ClientIdDoApp` e a **chave secreta do cliente** para `Google__SegredoDoApp`, no
   Render (Environment). O Render reinicia o servidor sozinho.

O programa pede o ID ao servidor (`GET /v1/google`); nada do Google fica no código do programa.

## Bloco 45 (0.44.0, feito): a conta no programa

- **Menu Conta:** "Entrar ou criar conta…" (ou, conectado, "Minha conta…") e "Sair da conta". A tela da conta tem:
  - entrar com e-mail e senha, ou com o Google;
  - criar conta, com o código do e-mail;
  - esqueci a senha;
  - Minha conta, com o plano e o uso do mês numa barra.
- **Rodapé:** "Conta: Nome".
- **Configurações do assistente:** "Usar a IA por" ( ) Minha chave do Gemini / ( ) Conta ProjectPlanner. Sem escolha
  salva: a chave própria, se houver; senão a conta, se conectada. Quem já usa a chave não sente diferença ao atualizar. A
  escolha fica em `assistente.json` (`"provedor": "chave" | "conta"`).
- **Sessão:** o token de renovação e o perfil ficam no cofre do sistema (item `conta-projectplanner`, ao lado do
  `gemini-api-key`). O token de acesso fica só na memória e é renovado quando falta 1 minuto para vencer, ou quando o
  servidor o recusa (o pedido é repetido uma vez). Se a renovação for recusada, a sessão é apagada e o assistente pede para
  entrar de novo.
- **Google:** o programa pede o client ID ao servidor e abre o login no navegador, com PKCE e a volta em
  `http://127.0.0.1:porta/`. O servidor troca o código pelo idToken com o segredo, que fica só nele.
- **Código:**
  - IA: `Conta/ClienteDaConta.cs` (as rotas), `Conta/ProvedorDaConta.cs` (`ProvedorDaConta`,
    `ProvedorConformeCredencial`) e `Gemini/AcessoAoGemini.cs` (`AcessoPorChave` e `AcessoPelaConta`). As classes do Gemini
    recebem o acesso e montam o mesmo corpo.
  - App: `Services/ContaProjectPlanner.cs`, `Services/LoginComGoogle.cs`, `ContaViewModel`/`ContaWindow` e
    `MainWindowViewModel.Conta.cs`.
- **Para testar com um servidor local:** a variável de ambiente `PROJECTPLANNER_SERVIDOR=http://localhost:5199`.

## Modelo de negócio (decidido em 19/10/2026)

- O programa básico (cronograma, Gantt, orçamento, medições, relatórios) continua **grátis e aberto** (AGPL-3.0). Ele é a
  porta de entrada.
- O que se vende é a **assinatura ProjectPlanner Pro**: o que só funciona com o servidor. Isso inclui:
  - a IA, com créditos e três níveis de modelo;
  - depois, o Telegram, o backup e a sincronização na nuvem, o SINAPI atualizado e os relatórios automáticos.
- A chave própria do Gemini sai da tela do programa (Bloco 46) e fica só para quem compila o código (`GEMINI_API_KEY`).

## Bloco 46 (0.45.0, feito): modelos e créditos

- **Três níveis de modelo**, configuráveis no servidor (trocar o modelo de um nível não exige versão nova do programa):

  | Nível | Modelo | US$ por milhão (entrada / saída) | Planos |
  |---|---|---|---|
  | Padrão | Gemini 2.5 Flash | 0,30 / 2,50 | Grátis e Profissional |
  | Avançado | Gemini 3.1 Pro (`gemini-3.1-pro-preview`; até a 0.45.1, o 2.5 Pro, que responde 404 para chaves de projetos novos) | 2,00 / 12,00 | Profissional |
  | Máximo | Claude Opus 5.5 | 4,00 / 20,00 | Profissional |

- **Claude:** o servidor traduz o pedido do formato do Gemini para o da Anthropic e a resposta de volta. A tradução cobre
  texto, fotos, PDF e a resposta estruturada, pela ferramenta "responder". Ela vai como opção (`tool_choice: auto`),
  com a instrução de usá-la: o Opus 5.5 recusa forçar a ferramenta (0.45.1). Se a resposta vier em texto, o JSON é tirado
  dele. O programa continua com um formato só.
- **Créditos no lugar de tokens:** cada pedido custa o valor real (tokens de entrada e de saída, inclusive os de raciocínio,
  pela tabela de preços do modelo), guardado no banco. 1 crédito = US$ 0,0005 de custo. Um pedido típico (10 mil tokens de
  entrada e 1,5 mil de saída) gasta cerca de:
  - 14 créditos no Padrão;
  - 76 no Avançado;
  - 140 no Máximo.
- **Créditos por mês:** Grátis 300 (cerca de 20 pedidos no Padrão); Profissional 6.000 (custo máximo de cerca de US$ 3).
- **Trocar o modelo de um nível** (quando o Google aposentar o 2.5 Flash, por exemplo): no Render, em Environment,
  configure o catálogo inteiro (`Modelos__0__Id`, etc., da tabela acima). O programa não precisa de versão nova.
- **Programa:**
  - as configurações do assistente ficam com a conta e o nível do modelo (a chave própria saiu da tela; a chave antiga, no
    item `gemini-api-key` do cofre, não é mais lida e pode ser apagada no app Acesso às Chaves);
  - um seletor rápido no topo do painel do chat;
  - o saldo de créditos depois de cada resposta (o servidor manda no cabeçalho `X-Creditos-Restantes`);
  - os níveis fora do plano aparecem como "do plano Profissional".

## Bloco 47: assinatura Pro

Mercado Pago (ou Asaas ou Stripe), com Pix e cartão:
- o plano Profissional mensal e os pacotes de créditos extras;
- o aviso do pagamento (webhook), a liberação automática, a renovação e o cancelamento;
- a página do plano no programa (Conta > Minha conta > Assinar).

## Bloco 48 (0.46.0, feito): painel financeiro

- **A página:** `https://projectplanner-servidor.onrender.com/admin` (funciona no celular, no modo claro e no escuro). Ela
  pede a chave de admin, que fica só naquela aba do navegador. A página não tem dado nenhum: os números vêm de
  `/v1/admin/painel` com a chave no cabeçalho. Os dados entram como texto, nunca como HTML, e a página tem
  Content-Security-Policy restrita.
- **Os números do período** (este mês, mês passado, últimos 30 dias ou outro período, até 366 dias):
  - **Cartões:** o custo de IA (em R$, com o IOF, e em US$), a receita estimada, a margem (R$ e %), os pedidos e o custo
    médio, as contas (ativas, Profissional, novas) e os tokens.
  - **Gráfico:** o custo de cada dia.
  - **Tabelas:**
    - por modelo: pedidos, custo, custo médio e créditos médios por pedido (para recalibrar os preços);
    - por plano: contas, ativas, custo, receita e margem;
    - as 10 contas que mais gastam.
- **Receita estimada:** contas no Profissional × preço (`Financeiro__PrecoProfissionalEmReais`) × dias do período / 30.
  Os pagamentos de verdade chegam no Bloco 47.
- **Margem:** receita − impostos e taxas − custo de IA em reais.
- **Alerta:** depois de cada pedido de IA, se o custo do mês passou de `Financeiro__AlertaMensalEmReais`, o servidor
  manda um e-mail a `Admin__Email`, uma vez por mês (tabela `AlertasDeCusto`). O painel mostra a faixa de aviso e para
  quem o e-mail foi.
- **Código:** `Servicos/Painel.cs` (o cálculo), `Rotas/PainelFinanceiro.cs` (as rotas) e `Painel/` (a página e o
  script, embutidos na dll).

## Bloco 49: Telegram

O assistente da obra pelo Telegram, com a mesma conta e os mesmos créditos.

## Depois

- Sincronização e backup dos projetos na nuvem.
- O SINAPI atualizado pelo servidor (o Bloco 40 continua esperando o arquivo ou a permissão para baixar).
- Relatórios automáticos.

## O que depende do usuário

Contas e pagamentos, que o Claude não cria:
- provedor de nuvem (feito: Render + Neon);
- serviço de e-mail (feito: Brevo);
- chave do Gemini da empresa (feito);
- cliente OAuth no Google Cloud (pendente: liga o "Entrar com Google");
- Bloco 46: a conta na Anthropic (console.anthropic.com), com faturamento, e a chave da API no Render (`Anthropic__Chave`);
- Bloco 47: a conta no gateway de pagamento e o CNPJ/contador para os impostos;
- Bloco 49: o bot do Telegram (@BotFather).
