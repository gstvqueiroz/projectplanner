#!/usr/bin/env bash
# Bloco 44: confere um servidor publicado, de ponta a ponta: responde, cria uma conta (o código chega por e-mail), entra,
# mostra o plano e manda um pedido pequeno de IA. Uso:
#   scripts/testar-servidor.sh https://projectplanner-servidor.onrender.com seu-email@exemplo.com
# A senha da conta de teste é pedida no terminal (não aparece na tela nem fica no histórico).
set -euo pipefail

URL="${1:?Informe o endereço do servidor, por exemplo https://projectplanner-servidor.onrender.com}"
EMAIL="${2:?Informe o e-mail da conta de teste}"
URL="${URL%/}"

json() { python3 -c 'import json,sys; d=json.load(sys.stdin); print(eval(sys.argv[1], {"d": d}))' "$1"; }
pedir() { curl -sS --max-time 120 -H 'Content-Type: application/json' "$@"; }

echo "1. O servidor responde? (no plano grátis do Render ele pode levar cerca de 1 minuto para acordar)"
pedir "$URL/" ; echo

read -rsp "Senha para a conta de teste (10 caracteres ou mais): " SENHA; echo
CORPO=$(python3 -c 'import json,sys; print(json.dumps({"email": sys.argv[1], "senha": sys.argv[2], "nome": "Teste"}))' "$EMAIL" "$SENHA")

echo "2. Criando a conta (ou, se já existir, tentando entrar)..."
LOGIN=$(python3 -c 'import json,sys; print(json.dumps({"email": sys.argv[1], "senha": sys.argv[2]}))' "$EMAIL" "$SENHA")
SESSAO=$(pedir -X POST "$URL/v1/sessoes" -d "$LOGIN")
if ! echo "$SESSAO" | grep -q '"acesso"'; then
  pedir -X POST "$URL/v1/contas" -d "$CORPO"; echo
  read -rp "Digite o código de 6 dígitos que chegou em $EMAIL: " CODIGO
  CONFIRMAR=$(python3 -c 'import json,sys; print(json.dumps({"email": sys.argv[1], "codigo": sys.argv[2], "senha": sys.argv[3]}))' "$EMAIL" "$CODIGO" "$SENHA")
  SESSAO=$(pedir -X POST "$URL/v1/contas/confirmar" -d "$CONFIRMAR")
fi
ACESSO=$(echo "$SESSAO" | json 'd.get("acesso") or ""')
if [ -z "$ACESSO" ]; then echo "Não deu para entrar: $SESSAO"; exit 1; fi
echo "   Entrou."

echo "3. Plano e uso do mês:"
pedir "$URL/v1/eu" -H "Authorization: Bearer $ACESSO" | json '"%s · plano %s · %s de %s tokens" % (d["perfil"]["email"], d["perfil"]["plano"], d["tokensUsadosNoMes"], d["tokensDoPlano"])'

echo "4. Um pedido pequeno de IA (gasta poucos tokens):"
pedir -X POST "$URL/v1/ia/models/gemini-2.5-flash:generateContent" -H "Authorization: Bearer $ACESSO" \
  -d '{"contents":[{"role":"user","parts":[{"text":"Responda só: servidor ok"}]}]}' \
  | json '(d["candidates"][0]["content"]["parts"][0]["text"].strip() + " · tokens: " + str(d.get("usageMetadata", {}).get("totalTokenCount"))) if "candidates" in d else d'
echo "Pronto."
