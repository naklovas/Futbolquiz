// ============================================================================
// DeltaFlow · Uygulama görünümü
// Seçilen uygulamanın VIP'leri ve sunucuları; onu kullanan uygulamalar (solda),
// bağımlı olduğu uygulamalar (sağda) ve uygulama içi trafik. topoloji.html'deki
// ortak yardımcıları ($, esc, fmt, segCell, segLabel, trunc, portSort, renderStatus,
// applyZoom, exportImage, state, abort) kullanır.
// ============================================================================

let uiMode = "ip";
const appState = {
  data: null, catalog: null, selected: null, expand: new Set(), topN: 12,
  show: { callers: true, deps: true, internal: true, infra: false }
};

// ---------- Sekmeler ----------
function setUiMode(m) {
  uiMode = m;
  document.querySelectorAll("#modeTabs button").forEach(b => b.classList.toggle("on", b.dataset.ui === m));
  $("#ipLabel").hidden = m !== "ip";
  $("#appLabel").hidden = m !== "app";
  $("#hop2Label").hidden = m !== "ip";
  if (m === "app") loadAppCatalog();
}
document.querySelectorAll("#modeTabs button").forEach(b => b.addEventListener("click", () => {
  setUiMode(b.dataset.ui);
  (b.dataset.ui === "app" ? $("#appName") : $("#ip")).focus();
}));

async function loadAppCatalog() {
  if (appState.catalog) return;
  try {
    const r = await fetch("api/apps");
    if (!r.ok) return;
    appState.catalog = await r.json();
    $("#appList").innerHTML = appState.catalog.map(a =>
      `<option value="${esc(a.name)}">${a.servers} sunucu${a.vips ? ` · ${a.vips} VIP` : ""}${a.owner ? " · " + esc(a.owner) : ""}</option>`).join("");
  } catch { /* liste gelmezse elle yazılabilir */ }
}

// ---------- Sorgu ----------
async function runApp(name) {
  if (name) $("#appName").value = name;
  name = $("#appName").value.trim();
  if (!name) { alert("Bir uygulama seçin."); return; }
  setUiMode("app");
  const srcs = [$("#srcSplunk").checked && "splunk", $("#srcAr").checked && "appresponse"].filter(Boolean);
  if (!srcs.length) { alert("En az bir kaynak seçin."); return; }
  const params = new URLSearchParams({
    name, start: $("#start").value, end: $("#end").value,
    appliance: $("#appliance").value || "0", sources: srcs.join(",")
  });
  history.replaceState(null, "", "?" + new URLSearchParams({ app: name, start: params.get("start"), end: params.get("end") }));

  abort?.abort(); hopAbort?.abort(); aiAbort?.abort();
  abort = new AbortController();
  $("#go").disabled = true; $("#cancel").hidden = false;
  $("#main").innerHTML = `<div class="panel empty"><span class="spinner"></span> "${esc(name)}" uygulamasının sunucuları ve VIP'leri sorgulanıyor…</div>`;
  try {
    const r = await fetch("api/app?" + params, { signal: abort.signal });
    const body = await r.json().catch(() => ({ error: `HTTP ${r.status}` }));
    if (!r.ok) throw new Error(body.error || `HTTP ${r.status}`);
    appState.data = body; appState.selected = null; appState.expand = new Set(); state.zoom = 1;
    renderApp();
  } catch (e) {
    const msg = e.name === "AbortError" ? "Sorgu iptal edildi." : e.message;
    $("#main").innerHTML = `<div class="panel empty" style="color:var(--err)">${esc(msg)}</div>`;
  } finally {
    $("#go").disabled = false; $("#cancel").hidden = true;
  }
}

// ---------- Yardımcılar ----------
const byPort = arr => [...arr].sort(portSort);
const portsTxt = ports => { const p = byPort(ports).map(x => ":" + x); return p.slice(0, 4).join(" ") + (p.length > 4 ? "…" : ""); };
const linkVisible = l => (appState.show.infra || !l.infra) && (!l.env || !!envShow[l.env]);

// Uygulama kutusunun ortada hangi satıra bağlanacağı: VIP satırı, açık segmentte sunucu satırı ya da segment grubu.
function rowOfEndpoint(ip) {
  const d = appState.data;
  if (d.vips.some(v => v.ip === ip)) return "vip:" + ip;
  const s = d.servers.find(x => x.ip === ip);
  if (!s) return null;
  const g = segLabel(s.segment) ?? "Segment envanterinde yok";
  return appState.expand.has(g) ? "srv:" + ip : "seg:" + g;
}

// Kenardaki uygulama grupları: görünür olanlar, ilk N + "Diğer".
function sideLinks(list) {
  const vis = list.filter(linkVisible);
  if (vis.length <= appState.topN) return vis;
  const rest = vis.slice(appState.topN - 1);
  const other = {
    key: "__other", name: `Diğer ${rest.length} grup`, other: rest, unknown: true, infra: false,
    hits: rest.reduce((n, r) => n + r.hits, 0), ports: [...new Set(rest.flatMap(r => r.ports))],
    peers: rest.flatMap(r => r.peers), targets: {}, viaVips: []
  };
  for (const r of rest) for (const [k, v] of Object.entries(r.targets)) other.targets[k] = (other.targets[k] ?? 0) + v;
  return [...vis.slice(0, appState.topN - 1), other];
}

// ODM/TEST gruplarındaki farklı IP sayısı
function appEnvCounts(d) {
  const m = {};
  for (const l of [...d.callers, ...d.deps]) if (l.env) l.peers.forEach(p => (m[l.env] ??= new Set()).add(p.ip));
  return Object.fromEntries(Object.entries(m).map(([k, v]) => [k, v.size]));
}

// ---------- Sayfa ----------
function renderApp() {
  const d = appState.data;
  const callers = d.callers.filter(linkVisible), deps = d.deps.filter(linkVisible);
  const infraCount = d.callers.filter(l => l.infra).length + d.deps.filter(l => l.infra).length;
  $("#main").innerHTML = `
    <section class="panel">${renderStatus({ ...d, splunk: d.splunk, appResponse: d.appResponse, envanter: d.envanter })}</section>
    <section class="panel">
      <div class="target-grid">
        <div>
          <div class="bigip">${esc(d.name)}</div>
          <div class="small muted">${esc(d.owners.join(" · ") || "sahip bilgisi yok")}</div>
          ${d.queriedIps < d.totalIps ? `<div class="msgs">Uygulamanın ${d.totalIps} IP'sinden ilk ${d.queriedIps} tanesi sorgulandı (Uygulama:MaxIp).</div>` : ""}
        </div>
        <div class="kpis">
          <div class="kpi"><b style="color:var(--vip)">${fmt(d.vips.length)}</b><span>VIP</span></div>
          <div class="kpi"><b>${fmt(d.servers.length)}</b><span>sunucu</span></div>
          <div class="kpi"><b style="color:var(--in)">${fmt(callers.filter(l => !l.unknown).length)}</b><span>kullanan uygulama</span></div>
          <div class="kpi"><b style="color:var(--out)">${fmt(deps.filter(l => !l.unknown).length)}</b><span>bağımlı olunan uygulama</span></div>
        </div>
      </div>
    </section>
    <section class="panel">
      <div class="toolbar">
        <h2 style="margin:0">Uygulama topolojisi</h2>
        <div class="checks" style="padding:0">
          <label><input type="checkbox" data-show="callers" ${appState.show.callers ? "checked" : ""}> Kullananlar</label>
          <label><input type="checkbox" data-show="deps" ${appState.show.deps ? "checked" : ""}> Bağımlılıklar</label>
          <label><input type="checkbox" data-show="internal" ${appState.show.internal ? "checked" : ""}> İç trafik</label>
          <label title="DNS, AD, NTP, izleme, RDP/SSH gibi altyapı trafiği"><input type="checkbox" data-show="infra" ${appState.show.infra ? "checked" : ""}> Altyapı (${infraCount})</label>
        </div>
        ${envToggleHtml(appEnvCounts(d))}
        <label style="flex-direction:row;align-items:center;gap:6px">Grup sayısı
          <select id="appTopN">${[8, 12, 20, 40].map(n => `<option value="${n}"${n === appState.topN ? " selected" : ""}>ilk ${n}</option>`).join("")}</select></label>
        <div class="zoom">
          <button type="button" data-zoom="-" title="Uzaklaş">−</button>
          <button type="button" data-zoom="0" title="Ekrana sığdır">Sığdır</button>
          <button type="button" data-zoom="+" title="Yakınlaş">+</button>
          <button type="button" id="dlSvg">SVG</button>
          <button type="button" id="dlPng">PNG</button>
        </div>
      </div>
      <div class="topo-wrap" id="topo"></div>
      <div class="detail" id="detail"></div>
    </section>
    <div class="grid2">
      <section class="panel dir-in" id="appCallers"></section>
      <section class="panel dir-out" id="appDeps"></section>
      <section class="panel" id="appServers"></section>
      <section class="panel" id="appInternal"></section>
    </div>`;

  document.querySelectorAll("[data-show]").forEach(cb => cb.addEventListener("change", () => {
    appState.show[cb.dataset.show] = cb.checked; appState.selected = null; renderApp();
  }));
  wireEnvToggles(() => { appState.selected = null; renderApp(); });
  $("#appTopN").addEventListener("change", ev => { appState.topN = +ev.target.value; renderAppTopology(); });
  document.querySelectorAll("[data-zoom]").forEach(bt => bt.addEventListener("click", () => {
    const z = bt.dataset.zoom;
    state.zoom = z === "0" ? 1 : Math.min(4, Math.max(0.4, state.zoom * (z === "+" ? 1.25 : 0.8)));
    applyZoom();
  }));
  $("#dlSvg").addEventListener("click", () => exportImage("svg"));
  $("#dlPng").addEventListener("click", () => exportImage("png"));
  renderAppTopology();
  renderAppTables();
}

// ---------- Çizim ----------
// Sütunlar: [kullanan uygulamalar] → [UYGULAMA: VIP'ler + sunucular (segment grupları)] → [bağımlı olunan uygulamalar]
function renderAppTopology() {
  const d = appState.data;
  const L = appState.show.callers ? sideLinks(d.callers) : [];
  const R = appState.show.deps ? sideLinks(d.deps) : [];

  // Ortadaki satırlar: VIP'ler, sonra segment grupları (açıksa altında sunucular)
  const segGroups = new Map();
  for (const s of d.servers) {
    const g = segLabel(s.segment) ?? "Segment envanterinde yok";
    if (!segGroups.has(g)) segGroups.set(g, { name: g, seg: s.segment, servers: [] });
    segGroups.get(g).servers.push(s);
  }
  const rows = [];
  if (d.vips.length) rows.push({ kind: "title", text: `VIP'LER (${d.vips.length})` });
  d.vips.forEach(v => rows.push({ kind: "vip", id: "vip:" + v.ip, v }));
  rows.push({ kind: "title", text: `SUNUCULAR (${d.servers.length})` });
  for (const g of segGroups.values()) {
    rows.push({ kind: "seg", id: "seg:" + g.name, g });
    if (appState.expand.has(g.name)) g.servers.forEach(s => rows.push({ kind: "srv", id: "srv:" + s.ip, s }));
  }

  const TOP = 40, NH = 52, NG = 10, RH = { title: 24, vip: 44, seg: 44, srv: 30 }, HEAD = 58;
  const X = { l: 12, c: 440, r: 1030 }, LW = 300, CW = 440, RW = 300, W = X.r + RW + 12, GUT = 26;
  let cy = HEAD;
  rows.forEach(r => { r.y = cy; cy += RH[r.kind] + (r.kind === "title" ? 0 : 6); });
  const CH = cy + 8;
  const colH = n => n ? n * NH + (n - 1) * NG : 0;
  const inner = Math.max(CH, colH(L.length), colH(R.length));
  const H = TOP + inner + 20;
  const cTop = TOP + (inner - CH) / 2;
  const place = list => { let y = TOP + (inner - colH(list.length)) / 2; list.forEach(n => { n.y = y; y += NH + NG; }); };
  place(L); place(R);
  const rowY = new Map(rows.filter(r => r.id).map(r => [r.id, cTop + r.y + RH[r.kind] / 2]));

  const allHits = [...L, ...R].flatMap(l => Object.values(l.targets)).concat(d.internal.map(i => i.hits), [1]);
  const maxHit = Math.max(...allHits);
  const sw = h => (1.2 + 7 * Math.log(1 + h) / Math.log(1 + maxHit)).toFixed(1);
  const curve = (x1, y1, x2, y2) => { const mx = (x1 + x2) / 2; return `M${x1},${y1} C${mx},${y1} ${mx},${y2} ${x2},${y2}`; };
  const sel = appState.selected;
  let edges = "", nodes = "";

  // Kenar kutuları + bağlantılar
  const drawSide = (list, dir) => list.forEach((l, i) => {
    const id = `${dir}${i}`, x = dir === "in" ? X.l : X.r, w = dir === "in" ? LW : RW;
    const agg = new Map();
    for (const [ip, h] of Object.entries(l.targets)) { const rid = rowOfEndpoint(ip); if (rid) agg.set(rid, (agg.get(rid) ?? 0) + h); }
    for (const [rid, h] of agg) {
      const ry = rowY.get(rid); if (ry == null) continue;
      const path = dir === "in" ? curve(X.l + LW, l.y + NH / 2, X.c, ry) : curve(X.c + CW, ry, X.r, l.y + NH / 2);
      edges += `<path class="edge ${dir}" data-rel="${id} ${rid}" marker-end="url(#arr-${dir})" stroke-width="${sw(h)}" d="${path}"><title>${esc(l.name)} ${dir === "in" ? "→" : "←"} ${esc(rid.split(":").slice(1).join(":"))}: ${fmt(h)} hit</title></path>`;
    }
    const isSel = sel && sel.type === "link" && sel.dir === dir && sel.key === l.key;
    const sub = l.unknown && l.key !== "__other"
      ? `envanterde yok · ${fmt(l.peers.length)} IP · ${portsTxt(l.ports)}`
      : `${fmt(l.peers.length)} sunucu · ${portsTxt(l.ports)}${l.viaVips?.length ? " · VIP" : ""}`;
    nodes += `<g class="srv ${dir}${l.unknown ? " more" : ""}${l.infra ? " nohit" : ""}${isSel ? " sel" : ""}" data-id="${id}" data-link="${dir}|${esc(l.key)}" transform="translate(${x},${l.y})">
      <title>${esc(l.name)}\n${esc(l.peers.slice(0, 20).map(p => p.ip).join(", "))}\n${fmt(l.hits)} hit</title>
      <rect width="${w}" height="${NH}" rx="8"></rect>
      <text class="t1" x="10" y="20" style="font-family:inherit">${esc(trunc(l.name, 30))}</text>
      <text class="num" x="${w - 10}" y="20" text-anchor="end">${fmt(l.hits)}</text>
      <text class="t3" x="10" y="40">${esc(trunc(sub, 44))}${l.infra ? " · altyapı" : ""}</text>
    </g>`;
  });
  drawSide(L, "in"); drawSide(R, "out");

  // Uygulama kutusu
  let box = `<g transform="translate(${X.c},${cTop})">
    <rect width="${CW}" height="${CH}" rx="12" style="fill:var(--panel);stroke:var(--text);stroke-width:2"></rect>
    <rect x="1" y="1" width="${CW - 2}" height="${HEAD - 8}" rx="11" style="fill:var(--target)"></rect>
    <text x="${CW / 2}" y="24" text-anchor="middle" style="fill:var(--target-text);font-size:16px;font-weight:800">${esc(trunc(d.name, 40))}</text>
    <text x="${CW / 2}" y="41" text-anchor="middle" style="fill:var(--target-text);font-size:11px;opacity:.85">${esc(trunc(d.owners[0] ?? "", 56))}</text>`;
  for (const r of rows) {
    const h = RH[r.kind];
    if (r.kind === "title") { box += `<text class="colhead" x="${GUT + 4}" y="${r.y + 16}">${r.text}</text>`; continue; }
    const isSel = sel && sel.type === r.kind && sel.id === r.id;
    if (r.kind === "vip") {
      const v = r.v;
      box += `<g class="srv vip${isSel ? " sel" : ""}" data-id="${esc(r.id)}" data-row="${esc(r.id)}" transform="translate(${GUT},${r.y})">
        <title>VIP ${esc(v.ip)}:${esc(v.port ?? "")}\n${esc(v.pool ?? "")}\n${v.members.map(m => `${m.hostname ?? ""} ${m.ip}:${m.port ?? ""}`).join("\n")}</title>
        <rect width="${CW - GUT - 12}" height="${h}" rx="7"></rect>
        <text class="t1" x="10" y="18">VIP ${esc(v.ip)}${v.port ? ":" + esc(v.port) : ""}</text>
        <text class="num" x="${CW - GUT - 22}" y="18" text-anchor="end">${fmt(v.hits)}</text>
        <text class="t3" x="10" y="34">${esc(trunc(`${(v.pool ?? "").split("/").pop() || "havuz ?"} · ${v.members.length} üye`, 52))}</text>
      </g>`;
    } else if (r.kind === "seg") {
      const g = r.g, open = appState.expand.has(g.name);
      const hin = g.servers.reduce((n, s) => n + s.inHits, 0), hout = g.servers.reduce((n, s) => n + s.outHits, 0);
      box += `<g class="srv${isSel ? " sel" : ""}" data-id="${esc(r.id)}" data-row="${esc(r.id)}" transform="translate(${GUT},${r.y})">
        <title>${esc(g.name)}\n${g.servers.map(s => `${s.ip} ${s.hostname ?? ""}`).join("\n")}\nTıkla: sunucuları aç/kapat</title>
        <rect width="${CW - GUT - 12}" height="${h}" rx="7" style="stroke:var(--line)"></rect>
        <text class="t1" x="10" y="18" style="font-family:inherit">${open ? "▾" : "▸"} ${esc(trunc(g.name, 30))}</text>
        <text class="num" x="${CW - GUT - 22}" y="18" text-anchor="end">↓${fmt(hin)} ↑${fmt(hout)}</text>
        <text class="t3" x="10" y="34">${esc(trunc(`${g.servers.length} sunucu${g.seg ? " · " + g.seg.cidr : ""}${g.seg?.vlan ? " · VLAN " + g.seg.vlan : ""}`, 52))}</text>
      </g>`;
    } else {
      const s = r.s;
      box += `<g class="srv${isSel ? " sel" : ""}" data-id="${esc(r.id)}" data-row="${esc(r.id)}" transform="translate(${GUT + 18},${r.y})">
        <title>${esc(s.ip)} ${esc(s.hostname ?? "")}\nPort: ${esc(s.ports.join(", "))}${s.vips.length ? "\nVIP: " + esc(s.vips.join(", ")) : ""}</title>
        <rect width="${CW - GUT - 30}" height="${h}" rx="6" style="stroke:var(--line)"></rect>
        <text class="t2" x="8" y="19" style="font-family:Consolas,ui-monospace,monospace">${esc(s.ip)}</text>
        <text class="t3" x="118" y="19">${esc(trunc(`${s.hostname ?? ""} ${portsTxt(s.ports)}`, 30))}</text>
        <text class="num" x="${CW - GUT - 40}" y="19" text-anchor="end">↓${fmt(s.inHits)} ↑${fmt(s.outHits)}</text>
      </g>`;
    }
  }
  box += `</g>`;

  // İç trafik: kutunun sol iç kenarındaki oluktan yaylar (LB: VIP → üye, doğrudan: sunucu → sunucu)
  let inner2 = "";
  if (appState.show.internal) {
    const agg = new Map();
    for (const i of d.internal) {
      const a = rowOfEndpoint(i.from) ?? null, b = rowOfEndpoint(i.to);
      if (!a || !b || a === b) continue;
      const k = `${a}|${b}|${i.kind}`;
      agg.set(k, (agg.get(k) ?? 0) + i.hits);
    }
    for (const [k, h] of agg) {
      const [a, b, kind] = k.split("|");
      const y1 = rowY.get(a), y2 = rowY.get(b); if (y1 == null || y2 == null) continue;
      const x = X.c + GUT, bend = X.c + 6;
      inner2 += `<path class="edge" data-rel="${esc(a)} ${esc(b)}" style="stroke:var(--vip);${kind === "direct" ? "stroke-dasharray:4 3;" : ""}" marker-end="url(#arr-vip)" stroke-width="${Math.min(4, +sw(h))}" d="M${x},${y1} C${bend},${y1} ${bend},${y2} ${x},${y2}"><title>${kind === "lb" ? "LB" : "Doğrudan"}: ${esc(a.split(":").slice(1).join(":"))} → ${esc(b.split(":").slice(1).join(":"))}: ${fmt(h)} hit</title></path>`;
    }
  }

  const svg = `<svg class="topo" id="topoSvg" viewBox="0 0 ${W} ${H}" xmlns="http://www.w3.org/2000/svg">
    <defs>${["in", "out", "vip"].map(c => `<marker id="arr-${c}" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="9" markerHeight="9" orient="auto" markerUnits="userSpaceOnUse"><path d="M0,1 L9,5 L0,9 z" style="fill:var(--${c})"></path></marker>`).join("")}</defs>
    ${appState.show.callers ? `<text class="colhead" x="${X.l}" y="22">BU UYGULAMAYI KULLANANLAR · ${fmt(d.callers.filter(linkVisible).length)}</text>` : ""}
    <text class="colhead" x="${X.c + CW / 2}" y="22" text-anchor="middle">UYGULAMA</text>
    ${appState.show.deps ? `<text class="colhead" x="${X.r + RW}" y="22" text-anchor="end">BAĞIMLI OLUNANLAR · ${fmt(d.deps.filter(linkVisible).length)}</text>` : ""}
    ${edges}${nodes}${box}${inner2}
  </svg>`;
  $("#topo").innerHTML = svg;
  applyZoom();
  wireAppTopology(L, R);
}

function wireAppTopology(L, R) {
  const svg = $("#topoSvg");
  svg.querySelectorAll("[data-id]").forEach(n => {
    n.addEventListener("mouseenter", () => {
      svg.classList.add("hl");
      const id = n.dataset.id;
      svg.querySelectorAll(".edge").forEach(p => {
        const rel = p.dataset.rel.split(" ");
        if (!rel.includes(id)) return;
        p.classList.add("on");
        rel.forEach(o => svg.querySelector(`[data-id="${CSS.escape(o)}"]`)?.classList.add("on"));
      });
      n.classList.add("on");
    });
    n.addEventListener("mouseleave", () => { svg.classList.remove("hl"); svg.querySelectorAll(".on").forEach(x => x.classList.remove("on")); });
  });
  svg.querySelectorAll("[data-link]").forEach(n => n.addEventListener("click", () => {
    const [dir, key] = n.dataset.link.split("|");
    const same = appState.selected?.type === "link" && appState.selected.dir === dir && appState.selected.key === key;
    appState.selected = same ? null : { type: "link", dir, key, link: (dir === "in" ? L : R).find(l => l.key === key) };
    renderAppTopology(); renderAppDetail();
  }));
  svg.querySelectorAll("[data-row]").forEach(n => n.addEventListener("click", () => {
    const id = n.dataset.row, [kind, ...rest] = id.split(":"), val = rest.join(":");
    if (kind === "seg") {
      appState.expand.has(val) ? appState.expand.delete(val) : appState.expand.add(val);
      renderAppTopology(); return;
    }
    const same = appState.selected?.id === id;
    appState.selected = same ? null : { type: kind, id, val };
    renderAppTopology(); renderAppDetail();
  }));
}

// ---------- Detay kartı ----------
function renderAppDetail() {
  const el = $("#detail"), s = appState.selected, d = appState.data;
  if (!s) { el.innerHTML = ""; return; }
  const close = `<button type="button" id="closeDetail">Kapat</button>`;
  let html = "";
  if (s.type === "link") {
    const l = s.link, dir = s.dir;
    const groups = l.other ?? [l];
    html = `<div class="panel"><div class="toolbar">
        <h2 style="margin:0">${esc(l.name)} <span class="muted" style="font-weight:400">${dir === "in" ? "→ bu uygulamayı kullanıyor" : "← bu uygulama buna bağımlı"}</span></h2>
        <div style="display:flex;gap:8px">${!l.unknown ? `<button type="button" class="primary" data-openapp="${esc(l.name.split(" + ")[0])}">Bu uygulamayı aç</button>` : ""}${close}</div></div>
      ${groups.map(g => `${l.other ? `<h2 style="margin-top:10px">${esc(g.name)}</h2>` : ""}
      <div class="tbl-wrap" style="max-height:300px"><table>
        <thead><tr><th>IP</th><th>Segment</th><th>Port</th><th class="num">Hit</th><th></th></tr></thead>
        <tbody>${g.peers.map(p => `<tr><td class="mono">${esc(p.ip)}</td><td>${segCell(p.segment)}</td><td class="mono">${esc(portsTxt(p.ports))}</td>
          <td class="num">${fmt(p.hits)}</td><td><a href="?ip=${encodeURIComponent(p.ip)}">IP topolojisi</a></td></tr>`).join("")}</tbody>
      </table></div>
      <div class="small muted" style="margin-top:6px">${dir === "in" ? "Geldiği uç noktalar" : "Giden sunucular"}: ${esc(Object.entries(g.targets).sort((a, b) => b[1] - a[1]).map(([k, v]) => `${k} (${fmt(v)})`).join(", "))}
        ${g.viaVips?.length ? ` · VIP üzerinden: ${esc(g.viaVips.join(", "))}` : ""}</div>`).join("")}
    </div>`;
  } else if (s.type === "vip") {
    const v = d.vips.find(x => "vip:" + x.ip === s.id);
    html = `<div class="panel"><div class="toolbar"><h2 style="margin:0">VIP ${esc(v.ip)}:${esc(v.port ?? "")} <span class="muted" style="font-weight:400">${esc(v.pool ?? "")}</span></h2>
        <div style="display:flex;gap:8px"><a class="primary" href="?ip=${encodeURIComponent(v.ip)}" style="padding:6px 12px;border-radius:6px;text-decoration:none">IP topolojisini aç</a>${close}</div></div>
      <div class="tbl-wrap" style="max-height:260px"><table><thead><tr><th>Üye</th><th>IP:port</th><th>Segment</th><th>GW (FW)</th></tr></thead>
      <tbody>${v.members.map(m => `<tr><td><b>${esc(m.hostname ?? "")}</b></td><td class="mono">${esc(m.ip)}:${esc(m.port ?? "")}</td>
        <td>${segCell(m.segment)}</td><td class="mono">${esc(m.gwIps.join(", ") || "—")}<div class="small muted">${esc(m.fw ?? "")}</div></td></tr>`).join("")}</tbody></table></div></div>`;
  } else if (s.type === "srv") {
    const sv = d.servers.find(x => x.ip === s.val);
    const inCallers = d.callers.filter(l => l.targets[sv.ip]).map(l => `${l.name} (${fmt(l.targets[sv.ip])})`);
    const outDeps = d.deps.filter(l => l.targets[sv.ip]).map(l => `${l.name} (${fmt(l.targets[sv.ip])})`);
    html = `<div class="panel"><div class="toolbar"><h2 style="margin:0">${esc(sv.ip)} <span class="muted" style="font-weight:400">${esc(sv.hostname ?? "")}</span></h2>
        <div style="display:flex;gap:8px"><a class="primary" href="?ip=${encodeURIComponent(sv.ip)}" style="padding:6px 12px;border-radius:6px;text-decoration:none">IP topolojisini aç</a>${close}</div></div>
      <div class="target-grid"><div>${segCell(sv.segment)}<div class="small" style="margin-top:6px">Port: ${esc(sv.ports.join(", "))}${sv.vips.length ? `<br>VIP: ${esc(sv.vips.join(", "))}` : ""}</div></div>
      <div class="small"><b>Doğrudan gelenler:</b> ${esc(inCallers.join(", ") || "—")}<br><b>Gittikleri:</b> ${esc(outDeps.join(", ") || "—")}</div></div></div>`;
  }
  el.innerHTML = html;
  $("#closeDetail")?.addEventListener("click", () => { appState.selected = null; renderAppTopology(); renderAppDetail(); });
  el.querySelectorAll("[data-openapp]").forEach(b => b.addEventListener("click", () => { window.scrollTo({ top: 0, behavior: "smooth" }); runApp(b.dataset.openapp); }));
}

// ---------- Tablolar ----------
function renderAppTables() {
  const d = appState.data;
  const linkTable = (list, dir) => {
    const rows = list.filter(linkVisible);
    return `<h2>${dir === "in" ? "Bu uygulamayı kullananlar" : "Bu uygulamanın bağımlı oldukları"} <span class="muted" style="font-weight:400">(${fmt(rows.length)})</span></h2>
      ${rows.length ? `<div class="tbl-wrap"><table><thead><tr><th>Uygulama / segment</th><th class="num">Sunucu</th><th>Port</th><th>${dir === "in" ? "Geldiği uç nokta" : "Giden sunucu"}</th><th class="num">Hit</th></tr></thead>
      <tbody>${rows.map(l => `<tr><td>${l.unknown ? `<span class="unk">envanterde yok</span> · ${esc(l.name)}` : `<a href="#" data-openapp="${esc(l.name.split(" + ")[0])}"><b>${esc(l.name)}</b></a>`}${l.infra ? ' <span class="badge">altyapı</span>' : ""}${l.viaVips.length ? ' <span class="badge vip">VIP</span>' : ""}</td>
        <td class="num">${fmt(l.peers.length)}</td><td class="mono">${esc(byPort(l.ports).join(", "))}</td>
        <td class="mono small">${esc(Object.keys(l.targets).slice(0, 4).join(", "))}${Object.keys(l.targets).length > 4 ? "…" : ""}</td><td class="num">${fmt(l.hits)}</td></tr>`).join("")}</tbody></table></div>`
      : `<div class="empty">Kayıt yok.</div>`}`;
  };
  $("#appCallers").innerHTML = linkTable(d.callers, "in");
  $("#appDeps").innerHTML = linkTable(d.deps, "out");
  $("#appServers").innerHTML = `<h2>Sunucular <span class="muted" style="font-weight:400">(${fmt(d.servers.length)})</span></h2>
    <div class="tbl-wrap"><table><thead><tr><th>IP</th><th>Host</th><th>Segment</th><th>Port</th><th>VIP</th><th class="num">Gelen</th><th class="num">Giden</th></tr></thead>
    <tbody>${d.servers.map(s => `<tr><td class="mono"><a href="?ip=${encodeURIComponent(s.ip)}">${esc(s.ip)}</a></td><td>${esc(s.hostname ?? "")}</td><td>${segCell(s.segment)}</td>
      <td class="mono">${esc(s.ports.join(", "))}</td><td class="mono small">${esc(s.vips.join(", "))}</td><td class="num">${fmt(s.inHits)}</td><td class="num">${fmt(s.outHits)}</td></tr>`).join("")}</tbody></table></div>`;
  $("#appInternal").innerHTML = `<h2>Uygulama içi trafik <span class="muted" style="font-weight:400">(${fmt(d.internal.length)})</span></h2>
    ${d.internal.length ? `<div class="tbl-wrap"><table><thead><tr><th>Kaynak</th><th>Hedef</th><th>Port</th><th>Tür</th><th class="num">Hit</th></tr></thead>
    <tbody>${d.internal.map(i => `<tr><td class="mono">${esc(i.from)}</td><td class="mono">${esc(i.to)}</td><td class="mono">${esc(i.port)}</td>
      <td>${i.kind === "lb" ? '<span class="badge vip">LB (VIP → üye)</span>' : "doğrudan"}</td><td class="num">${fmt(i.hits)}</td></tr>`).join("")}</tbody></table></div>` : `<div class="empty">Kayıt yok.</div>`}`;
  document.querySelectorAll("#appCallers [data-openapp], #appDeps [data-openapp]").forEach(a => a.addEventListener("click", ev => {
    ev.preventDefault(); window.scrollTo({ top: 0, behavior: "smooth" }); runApp(a.dataset.openapp);
  }));
}
