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
| `POST /v1/sessoes/renovar` | `{renovacao}`: um acesso novo e um token de renovação novo (o velho deixa de valer) |
| `POST /v1/sessoes/sair` | `{renovacao}`: encerra aquela sessão (204) |
| `POST /v1/senha/esqueci` | `{email}`: manda o código de troca; 202 sempre |
| `POST /v1/senha/redefinir` | `{email, codigo, novaSenha}`: troca a senha, encerra as outras sessões, tira o bloqueio e devolve uma sessão nova |
| `GET /v1/eu` | Com login: `{perfil, tokensUsadosNoMes, tokensDoPlano}` |
| `POST /v1/ia/models/{modelo}:generateContent` | O pedido de IA (abaixo) |
| `GET /v1/ia/models/{modelo}` | Testar a conexão: confere o login, o modelo e a chave do servidor, sem ir ao Gemini |
| `POST /v1/admin/usuarios/{email}/plano` | `{plano}` com o cabeçalho `X-Chave-Admin`: troca o plano (gratis ou profissional) |

Os códigos por e-mail: no máximo 5 de cada tipo por conta, por hora. Passado isso, a resposta é a mesma e nada é mandado,
o que segura quem tentasse encher a caixa de alguém ou adivinhar o código.

O ponto de IA:
- exige login (`Authorization: Bearer <acesso>`); o token não vai ao Google;
- aceita só os modelos liberados (`Gemini:Modelos`; padrão gemini-2.5-flash, -flash-lite e -pro);
- limita o corpo (15 MB) e os pedidos por minuto de cada usuário (30);
- confere a cota do mês (UTC) antes de chamar (402 quando acabou; o pedido que passa do limite ainda é atendido);
- registra os tokens do `usageMetadata` (entrada; saída com os de raciocínio; total);
- traduz as falhas: chave ou modelo do servidor recusados → 502 (sem repassar o que o Google disse), 429 do Google → 503,
  5xx → 502, sem resposta → 504; um 400 do pedido passa adiante (com a chave, se aparecer, trocada por "(chave)").

### Configuração

Tudo por variáveis de ambiente (a seção e a chave separadas por `__`). Nenhum segredo fica no repositório.

| Variável | O quê |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` na nuvem |
| `ConnectionStrings__Banco` | PostgreSQL: `Host=...;Database=...;Username=...;Password=...` ou `postgresql://usuario:senha@servidor/banco?sslmode=require` |
| `Jwt__Chave` | 32 bytes ou mais, aleatória (por exemplo, `openssl rand -base64 48`). Sem ela, em produção, o servidor não sobe |
| `Gemini__Chave` | A chave do Gemini da empresa. Sem ela, a IA responde 503 |
| `Gemini__Modelos__0`, `__1`… | Opcional: os modelos liberados |
| `Google__ClientIds__0`, `__1`… | Os client IDs OAuth aceitos no "Entrar com Google". Sem eles, o login com Google fica recusado |
| `Email__Smtp`, `Email__Porta`, `Email__Usuario`, `Email__Senha`, `Email__Remetente` | O SMTP do serviço de e-mail. Sem `Email__Smtp`, os e-mails vão só para o log |
| `Admin__Chave` | A chave da rota de admin. Sem ela, a rota fica fechada |
| `Planos__GratisTokensPorMes`, `Planos__ProfissionalTokensPorMes` | As cotas (padrão 300 mil e 10 milhões de tokens) |
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
   - `Google__ClientIds__0`: pode ficar em branco até o Bloco 45.
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

**Client ID do Google** (para o "Entrar com Google", no Bloco 45): no Google Cloud Console, APIs e serviços > Credenciais,
crie um "ID do cliente OAuth" do tipo "App para computador" e ponha o ID em `Google__ClientIds__0`. A tela de consentimento
pede o nome do app, o e-mail de suporte e os escopos `openid`, `email` e `profile`.

## Bloco 45: login no app

- Telas de Entrar, Criar conta e Esqueci a senha, com o botão do Google: abre o navegador e recebe a volta por um endereço local.
- O token de renovação fica no cofre do sistema (`ICofreDeSegredos`).
- Um provedor "Conta ProjectPlanner" ao lado do Gemini com a chave própria, escolhido nas configurações do assistente.
- Plano e uso do mês aparecem na tela.

## Bloco 46: assinatura

Cobrança (Mercado Pago, Asaas ou Stripe), webhook do pagamento e liberação automática da cota.

## O que depende do usuário

Contas e pagamentos, que o Claude não cria:
- provedor de nuvem;
- serviço de e-mail;
- chave do Gemini da empresa;
- cliente OAuth no Google Cloud;
- mais adiante, o gateway de pagamento.
