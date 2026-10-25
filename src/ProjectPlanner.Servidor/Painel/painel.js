// Bloco 48: o painel financeiro. Os dados vêm de /v1/admin/painel com a chave de admin; nada do servidor entra como HTML.
(function () {
  "use strict";
  var CHAVE = "pp-chave-admin";
  var reais = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });
  var dolares = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "USD", maximumFractionDigits: 4 });
  var numero = new Intl.NumberFormat("pt-BR");
  var umaCasa = new Intl.NumberFormat("pt-BR", { maximumFractionDigits: 1 });
  var $ = function (id) { return document.getElementById(id); };

  function guardada() { try { return sessionStorage.getItem(CHAVE); } catch (e) { return null; } }
  function guardar(v) { try { if (v) sessionStorage.setItem(CHAVE, v); else sessionStorage.removeItem(CHAVE); } catch (e) { /* aba sem armazenamento */ } }
  var chave = guardada();

  function el(tag, texto, classe) {
    var e = document.createElement(tag);
    if (texto !== undefined && texto !== null) e.textContent = String(texto);
    if (classe) e.className = classe;
    return e;
  }
  function iso(d) { return d.toISOString().slice(0, 10); }

  function periodo() {
    var hoje = new Date(); var utc = new Date(Date.UTC(hoje.getUTCFullYear(), hoje.getUTCMonth(), hoje.getUTCDate()));
    switch ($("periodo").value) {
      case "anterior":
        var fim = new Date(Date.UTC(utc.getUTCFullYear(), utc.getUTCMonth(), 0));
        return { de: iso(new Date(Date.UTC(fim.getUTCFullYear(), fim.getUTCMonth(), 1))), ate: iso(fim) };
      case "30":
        return { de: iso(new Date(utc.getTime() - 29 * 86400000)), ate: iso(utc) };
      case "outro":
        return { de: $("de").value, ate: $("ate").value };
      default:
        return { de: iso(new Date(Date.UTC(utc.getUTCFullYear(), utc.getUTCMonth(), 1))), ate: iso(utc) };
    }
  }

  function mostrarErro(id, texto) { var e = $(id); e.textContent = texto || ""; e.hidden = !texto; }

  async function carregar() {
    var p = periodo();
    mostrarErro("erro", "");
    var url = "/v1/admin/painel?de=" + encodeURIComponent(p.de || "") + "&ate=" + encodeURIComponent(p.ate || "");
    var resposta;
    try {
      resposta = await fetch(url, { headers: { "X-Chave-Admin": chave || "" }, cache: "no-store" });
    } catch (e) {
      mostrarErro($("painel").hidden ? "erroEntrar" : "erro", "Sem conexão com o servidor. Ele pode estar acordando: tente de novo em 1 minuto.");
      return;
    }
    var corpo = await resposta.json().catch(function () { return {}; });
    if (resposta.status === 401) {
      guardar(null); chave = null;
      $("painel").hidden = true; $("entrar").hidden = false;
      mostrarErro("erroEntrar", corpo.erro || "Chave de admin errada.");
      return;
    }
    if (!resposta.ok) { mostrarErro("erro", corpo.erro || ("Erro " + resposta.status)); return; }
    $("entrar").hidden = true; $("painel").hidden = false;
    desenhar(corpo);
  }

  function kpi(rotulo, valor, nota, classe) {
    var c = el("div", null, "kpi");
    c.appendChild(el("div", rotulo, "rotulo"));
    c.appendChild(el("div", valor, "valor" + (classe ? " " + classe : "")));
    if (nota) c.appendChild(el("div", nota, "nota"));
    return c;
  }

  function tabela(destino, cabecalho, linhas, vazio) {
    var alvo = $(destino); alvo.textContent = "";
    if (!linhas.length) { alvo.appendChild(el("p", vazio || "Nenhum pedido no período.", "premissas")); return; }
    var t = el("table"); var cab = el("tr");
    cabecalho.forEach(function (h) { cab.appendChild(el("th", h)); });
    var thead = el("thead"); thead.appendChild(cab); t.appendChild(thead);
    var tbody = el("tbody");
    linhas.forEach(function (l) { var tr = el("tr"); l.forEach(function (v) { tr.appendChild(el("td", v)); }); tbody.appendChild(tr); });
    t.appendChild(tbody); alvo.appendChild(t);
  }

  function grafico(dias) {
    var alvo = $("grafico"); alvo.textContent = "";
    var ns = "http://www.w3.org/2000/svg"; var largura = 1000, altura = 200, base = 168;
    var maximo = Math.max.apply(null, dias.map(function (d) { return d.custoEmReais; }).concat([0.01]));
    var svg = document.createElementNS(ns, "svg"); svg.setAttribute("viewBox", "0 0 " + largura + " " + altura);
    svg.setAttribute("role", "img"); svg.setAttribute("aria-label", "Custo de IA por dia");
    var passo = largura / Math.max(dias.length, 1);
    dias.forEach(function (d, i) {
      var h = Math.round((d.custoEmReais / maximo) * (base - 10));
      var r = document.createElementNS(ns, "rect");
      r.setAttribute("x", (i * passo + passo * 0.15).toFixed(1)); r.setAttribute("width", (passo * 0.7).toFixed(1));
      r.setAttribute("y", String(base - h)); r.setAttribute("height", String(Math.max(h, d.pedidos ? 1 : 0)));
      var dica = document.createElementNS(ns, "title");
      dica.textContent = d.dia.split("-").reverse().join("/") + ": " + reais.format(d.custoEmReais) + " · " + numero.format(d.pedidos) + " pedidos";
      r.appendChild(dica); svg.appendChild(r);
      if (i % Math.max(1, Math.ceil(dias.length / 16)) === 0) {
        var t = document.createElementNS(ns, "text");
        t.setAttribute("x", (i * passo + passo / 2).toFixed(1)); t.setAttribute("y", String(altura - 4)); t.setAttribute("text-anchor", "middle");
        t.textContent = d.dia.slice(8, 10); svg.appendChild(t);
      }
    });
    var topo = document.createElementNS(ns, "text"); topo.setAttribute("x", "0"); topo.setAttribute("y", "22");
    topo.textContent = "máx. " + reais.format(maximo); svg.appendChild(topo);
    alvo.appendChild(svg);
  }

  function nivel(n) { return { padrao: "Padrão", intermediario: "Intermediário", avancado: "Avançado", maximo: "Máximo" }[n] || n; }
  function plano(p) { return { gratis: "Grátis", profissional: "Profissional" }[p] || p; }

  function desenhar(d) {
    var r = d.resumo, a = d.alerta, f = d.premissas;
    var alerta = $("alerta");
    alerta.hidden = !a.passou;
    if (a.passou) alerta.textContent = "O custo de IA deste mês (" + reais.format(a.custoDoMesEmReais) + ") passou do limite de " +
      reais.format(a.limiteEmReais) + "." + (a.emailEnviadoPara ? " O aviso foi mandado para " + a.emailEnviadoPara + "."
      : " Configure Admin__Email no Render para receber o aviso por e-mail.");

    var k = $("kpis"); k.textContent = "";
    k.appendChild(kpi("Custo de IA", reais.format(r.custoEmReais), dolares.format(r.custoEmDolares) + " · com IOF"));
    k.appendChild(kpi("Receita estimada", reais.format(r.receitaEstimadaEmReais), numero.format(r.profissionais) + " no Profissional a " + reais.format(f.precoProfissionalEmReais)));
    k.appendChild(kpi("Margem", reais.format(r.margemEmReais), r.margemPercentual === null ? "sem receita no período" : umaCasa.format(r.margemPercentual) + "% da receita, depois de impostos e taxas",
      r.margemEmReais >= 0 ? "bom" : "ruim"));
    k.appendChild(kpi("Pedidos de IA", numero.format(r.pedidos), "custo médio " + reais.format(r.custoMedioPorPedidoEmReais)));
    k.appendChild(kpi("Contas", numero.format(r.usuarios), numero.format(r.usuariosAtivos) + " usaram a IA · " + numero.format(r.novosCadastros) + " novas no período"));
    k.appendChild(kpi("Tokens", numero.format(r.tokensDeEntrada + r.tokensDeSaida), numero.format(r.tokensDeEntrada) + " de entrada · " + numero.format(r.tokensDeSaida) + " de saída"));

    grafico(d.porDia);
    tabela("porModelo", ["Modelo", "Nível", "Pedidos", "Custo", "Médio/pedido", "Créditos/pedido"], d.porModelo.map(function (m) {
      return [m.nome, nivel(m.nivel), numero.format(m.pedidos), reais.format(m.custoEmReais), reais.format(m.custoMedioEmReais), umaCasa.format(m.creditosMedios)];
    }));
    tabela("porRotina", ["Rotina", "Pedidos", "Custo", "Médio/pedido", "Créditos/pedido", "Do cache"], d.porRotina.map(function (l) {
      return [l.nome, numero.format(l.pedidos), reais.format(l.custoEmReais), reais.format(l.custoMedioEmReais), umaCasa.format(l.creditosMedios),
        umaCasa.format(l.percentualDoCache) + "%"];
    }));
    tabela("porPlano", ["Plano", "Contas", "Ativas", "Pedidos", "Custo", "Receita", "Margem"], d.porPlano.map(function (p) {
      return [plano(p.plano), numero.format(p.usuarios), numero.format(p.usuariosAtivos), numero.format(p.pedidos), reais.format(p.custoEmReais),
        reais.format(p.receitaEmReais), reais.format(p.margemEmReais)];
    }));
    tabela("quemMaisGasta", ["Conta", "Plano", "Pedidos", "Custo", "Créditos usados"], d.quemMaisGasta.map(function (u) {
      return [u.email, plano(u.plano), numero.format(u.pedidos), reais.format(u.custoEmReais), umaCasa.format(u.creditosUsados) + " de " + numero.format(u.creditosDoPlano)];
    }));
    tabela("falhas", ["Código", "Quando (UTC)", "Rota", "Erro", "Onde"], d.falhas.map(function (f) {
      return [f.codigo, f.quando.replace("T", " ").slice(0, 19), f.rota, f.tipo + ": " + f.mensagem, f.pilha || ""];
    }), "Nenhum erro no período.");
    $("premissas").textContent = "Período de " + d.de.split("-").reverse().join("/") + " a " + d.ate.split("-").reverse().join("/") + " (UTC). " +
      "Premissas: US$ 1 = " + reais.format(f.reaisPorDolar) + " + " + umaCasa.format(f.percentualSobreODolar) + "% (IOF e spread); impostos e taxas de " +
      umaCasa.format(f.percentualDeImpostosETaxas) + "% sobre a receita; alerta a partir de " + reais.format(f.alertaMensalEmReais) + " por mês. " +
      "A receita é estimada pelo preço do plano Profissional até os pagamentos de verdade (Bloco 47). Ajuste as premissas no Render (variáveis Financeiro__…).";
  }

  $("formEntrar").addEventListener("submit", function (e) {
    e.preventDefault(); chave = $("chave").value.trim(); $("chave").value = ""; guardar(chave); mostrarErro("erroEntrar", ""); carregar();
  });
  $("periodo").addEventListener("change", function () {
    var outro = $("periodo").value === "outro"; $("de").hidden = !outro; $("ate").hidden = !outro;
    if (!outro) carregar();
  });
  $("atualizar").addEventListener("click", carregar);
  $("sair").addEventListener("click", function () { guardar(null); chave = null; $("painel").hidden = true; $("entrar").hidden = false; });
  if (chave) carregar();
})();
