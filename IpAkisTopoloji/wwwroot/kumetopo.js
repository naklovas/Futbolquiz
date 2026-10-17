// ============================================================================
// DeltaFlow · Küme topolojisi (İşlev haritası → "Topoloji çiz")
// Kümeler kutu kutu; içlerinde VIP'ler (yük dengeleyici simgesi) ve VIP'e bağlı olmayan sunucular. Elemanlar arası trafik
// (Carbon Black / AppResponse / Firewall, seçili tarih aralığı) eğri oklarla çizilir; kalınlık trafik, kırmızı kesik = engellenen.
// Elemana tıklayınca ekranın ortasında ayrıntı penceresi: VIP üyeleri, bağlar, etiket dışı trafik; üyeye tıklayınca sunucu
// ayrıntısı (AI rolü / gerekçesi, IP düzeyinde akışlar). Veri: /api/islev/topoloji (KumeTopoloji.cs).
// ============================================================================

const kt = { tag: "", data: null, scale: 1, mode: "kume", expanded: new Set(), cols: 0, per: 5, infra: false, abort: null, hover: null, pos: null, stack: [] };

(function ktStyle() {
  const css = `
  .kt-bar { display:flex; gap:8px; flex-wrap:wrap; align-items:center; margin:2px 0 12px; }
  .kt-bar .sp { flex:1; }
  .kt-bar label { display:flex; gap:6px; align-items:center; font-size:12.5px; color:var(--text); }
  .kt-src { display:flex; gap:8px; flex-wrap:wrap; font-size:12px; color:var(--muted); margin-bottom:10px; }
  .kt-wrap { border:1px solid var(--line); border-radius:10px; background:var(--bg); overflow:auto; max-height:calc(100vh - 250px); position:relative; }
  .kt-svg { display:block; }
  .kt-svg .box rect.bg { fill:var(--panel); stroke:hsl(var(--h) 55% 50% / .55); stroke-width:1.5; }
  .kt-svg .box rect.hd { fill:hsl(var(--h) 60% 50% / .12); }
  .kt-svg .box .bt { font-size:14px; font-weight:700; fill:var(--text); }
  .kt-svg .box .bs { font-size:11px; fill:var(--muted); }
  .kt-svg .frame rect { fill:none; stroke:var(--muted); stroke-opacity:.45; stroke-width:1.5; stroke-dasharray:8 5; }
  .kt-svg .frame text { font-size:14px; font-weight:800; fill:var(--text); letter-spacing:.04em; text-transform:uppercase; }
  .kt-svg .frame text.fs { font-size:11.5px; font-weight:500; fill:var(--muted); text-transform:none; letter-spacing:0; }
  .kt-svg .el { cursor:pointer; }
  .kt-svg .el .tile { fill:var(--panel); stroke:var(--line); }
  .kt-svg .el.vip .tile { fill:hsl(var(--h, 210) 60% 50% / .07); }
  .kt-svg .el:hover .tile, .kt-svg .el.hl .tile { stroke:var(--in); fill:var(--in-soft); }
  .kt-svg .el.idle .tile { fill:transparent; stroke-dasharray:4 3; }
  .kt-svg .el.idle .nm { fill:var(--muted); font-weight:500; }
  .kt-svg .lk.over { stroke:var(--out); stroke-opacity:.95; }
  .kt-svg .el .hit { fill:transparent; }
  .kt-svg .el:hover .hit, .kt-svg .el.on .hit { fill:var(--in-soft); }
  .kt-svg .el .nm { font-size:11.5px; font-weight:600; fill:var(--text); }
  .kt-svg .el .ip { font-size:10.5px; fill:var(--muted); font-family:Consolas, ui-monospace, monospace; }
  .kt-svg .ico-vip circle { fill:hsl(var(--h) 60% 50% / .16); stroke:hsl(var(--h) 55% 45%); stroke-width:1.6; }
  .kt-svg .ico-vip path { stroke:hsl(var(--h) 55% 40%); stroke-width:1.8; fill:none; stroke-linecap:round; }
  .kt-svg .ico-srv rect { fill:var(--panel); stroke:var(--muted); stroke-width:1.4; }
  .kt-svg .ico-srv line { stroke:var(--muted); stroke-width:1.4; }
  .kt-svg .ico-srv circle { fill:#2e9e6a; }
  .kt-svg .badge rect { fill:hsl(var(--h) 55% 45%); } .kt-svg .badge text { fill:#fff; font-size:10px; font-weight:700; }
  .kt-svg .lk { fill:none; stroke:var(--in); stroke-opacity:.45; }
  .kt-svg .lk.deny { stroke:var(--err); stroke-dasharray:6 4; stroke-opacity:.7; }
  .kt-svg .lk.infra { stroke:var(--unknown); stroke-dasharray:2 4; }
  .kt-svg .lk.intra { stroke-opacity:.3; }
  .kt-svg.dim .lk { stroke-opacity:.07; } .kt-svg.dim .el { opacity:.4; }
  .kt-svg.dim .lk.hl { stroke-opacity:.95; } .kt-svg.dim .el.hl { opacity:1; }
  .kt-svg .lkh { fill:none; stroke:transparent; stroke-width:10; cursor:pointer; pointer-events:stroke; }
  .kt-svg .lk { pointer-events:none; }
  .kt-tip { position:fixed; pointer-events:none; background:var(--text); color:var(--panel); font-size:12px; padding:6px 9px; border-radius:6px; z-index:60; max-width:360px; }
  .kt-modal-bg { position:fixed; inset:0; background:rgba(10,14,22,.45); z-index:70; display:flex; align-items:center; justify-content:center; padding:16px; }
  .kt-modal { background:var(--panel); border:1px solid var(--line); border-radius:12px; box-shadow:0 20px 60px rgba(0,0,0,.3);
    width:min(860px, 100%); max-height:86vh; display:flex; flex-direction:column; }
  .kt-modal .mh { padding:14px 18px; border-bottom:1px solid var(--line); display:flex; gap:12px; align-items:flex-start; justify-content:space-between; }
  .kt-modal .mh h3 { margin:0; font-size:17px; }
  .kt-modal .mb { overflow:auto; padding:14px 18px 20px; display:grid; gap:16px; font-size:13px; }
  .kt-modal h4 { margin:0 0 6px; font-size:12px; text-transform:uppercase; letter-spacing:.05em; color:var(--muted); }
  .kt-modal table { font-size:12.5px; width:100%; }
  .kt-modal tr.cl { cursor:pointer; } .kt-modal tr.cl:hover td { background:var(--in-soft); }
  .kt-modal .ip { font-family:Consolas, ui-monospace, monospace; }
  .kt-modal .two { display:grid; grid-template-columns:1fr 1fr; gap:16px; }
  @media (max-width:700px) { .kt-modal .two { grid-template-columns:1fr; } }
  .kt-modal .kv { display:grid; grid-template-columns:120px 1fr; gap:5px 10px; }
  .kt-modal .kv dt { color:var(--muted); }
  .kt-modal .why { background:var(--bg); border:1px solid var(--line); border-radius:8px; padding:10px 12px; line-height:1.45; }
  .kt-modal .stat { display:flex; gap:10px; flex-wrap:wrap; }`;
  const st = document.createElement("style"); st.textContent = css; document.head.appendChild(st);
})();

async function ktRun(tag) {
  setUiMode("topo");
  kt.tag = tag; kt.stack = [];
  ivStopPoll();
  const srcs = selectedSources();
  if (!srcs.length) { alert("En az bir kaynak seçin."); return; }
  await tnLoad(tag);
  const p = new URLSearchParams({ etiket: tag, tenant: tenantParam(), start: $("#start").value, end: $("#end").value,
    appliance: $("#appliance").value || "0", sources: srcs.join(",") });
  history.replaceState(null, "", "?" + new URLSearchParams({ topo: tag, ciz: "1", start: p.get("start"), end: p.get("end") }));
  kt.abort?.abort(); kt.abort = new AbortController();
  $("#main").innerHTML = `<div class="panel empty"><span class="spinner"></span> "${esc(tag)}" kümelerinin trafiği sorgulanıyor (VIP'ler ve sunucular)…
    <button id="ktCancel" style="margin-left:10px">Vazgeç</button></div>`;
  $("#ktCancel").addEventListener("click", () => { kt.abort.abort(); runIslev(tag); });
  try {
    const r = await fetch("api/islev/topoloji?" + p, { signal: kt.abort.signal });
    const b = await r.json().catch(() => ({ error: `HTTP ${r.status}` }));
    if (!r.ok) throw new Error(b.error || `HTTP ${r.status}`);
    if (uiMode !== "topo") return;
    // Birden çok kurum: kutu = kurum × küme; anahtar "Kurum · Küme", kutu başlığında yalnız küme adı (kurum çerçevede)
    if (b.coklu) b.kumeler.forEach(k => { k.kume = k.ad; k.ad = `${k.tenant} · ${k.ad}`; });
    kt.data = b;
    ktShell();
  } catch (e) {
    if (e.name === "AbortError") return;
    $("#main").innerHTML = `<div class="panel empty" style="color:var(--err)">${esc(e.message)} <button onclick="runIslev(kt.tag)" style="margin-left:10px">İşlev haritasına dön</button></div>`;
  }
}

function ktShell() {
  const d = kt.data;
  const pill = (n, s) => s ? `<span class="pill ${s.ok ? "ok" : s.error === "Sorgulanmadı" ? "skip" : "err"}" title="${esc(s.error || (s.messages || []).join(" · "))}">${n}${s.ok ? ` · ${fmt(s.rows)} satır` : ""}</span>` : "";
  const nVip = d.kumeler.reduce((a, k) => a + k.elemanlar.filter(e => e.tur === "vip").length, 0);
  const nSrv = d.kumeler.reduce((a, k) => a + k.elemanlar.filter(e => e.tur !== "vip").length, 0);
  $("#main").innerHTML = `
  <section class="panel">
    <div class="eg-head">
      <div><h2>Küme topolojisi · ${esc(d.etiket)}</h2>
        <div class="eg-sub">${esc($("#start").value.replace("T", " "))} – ${esc($("#end").value.replace("T", " "))} · kutular işlev kümeleri, içlerinde VIP'ler ve VIP'e bağlı olmayan sunucular · oklar aralarındaki trafik</div></div>
      <div class="eg-stats">
        <div class="eg-stat"><b>${d.kumeler.length}</b><span>Küme</span></div>
        <div class="eg-stat"><b>${nVip}</b><span>VIP</span></div>
        <div class="eg-stat"><b>${nSrv}</b><span>Tekil sunucu</span></div>
        <div class="eg-stat"><b>${fmt(d.baglar.length)}</b><span>Bağ</span></div>
      </div>
    </div>
    <div class="kt-bar">
      <button id="ktBack">← İşlev haritası</button>
      <div class="seg"><button data-ktm="kume" class="${kt.mode === "kume" ? "on" : ""}" title="Kümeler arası toplam trafik; elemanın üstüne gelince kendi bağları çizilir">Küme bağları</button><button data-ktm="eleman" class="${kt.mode === "eleman" ? "on" : ""}" title="Bütün VIP / sunucu bağları kanallardan">Tüm eleman bağları</button></div>
      <label>Kutu içi <select id="ktPer">${[2, 3, 4, 5, 6].map(v => `<option${v === kt.per ? " selected" : ""}>${v}</option>`).join("")}</select></label>
      <label>Sütun <select id="ktCols">${[["0", "Otomatik"], ["2", "2"], ["3", "3"], ["4", "4"], ["5", "5"]].map(([v, t]) => `<option value="${v}"${+v === kt.cols ? " selected" : ""}>${t}</option>`).join("")}</select></label>
      <label><input type="checkbox" id="ktInfra" ${kt.infra ? "checked" : ""}> Altyapı portları (DNS, AD, RDP…)</label>
      <span class="sp"></span>
      <button id="ktZo" title="Uzaklaştır">−</button><button id="ktFit">Sığdır</button><button id="ktZi" title="Yakınlaştır">+</button>
      <button id="ktAgain" class="primary">Yeniden sorgula</button>
    </div>
    <div class="kt-src">${pill("Carbon Black", d.splunk)}${pill("AppResponse", d.appResponse)}${pill("Firewall", d.firewall)}
      <span>${d.sorgulanan} IP sorgulandı${d.sorgulanan < d.toplam ? ` (etikette ${d.toplam} sunucu; üst sınır Islev:TopolojiMaxIp)` : ""} · ${(d.elapsedMs / 1000).toFixed(1)} sn</span></div>
    <div class="kt-wrap" id="ktWrap"></div>
    <div class="legend" style="margin-top:8px"><span>◯ VIP</span><span>▭ sunucu</span><span>— trafik (kalınlık = hacim)</span><span style="color:var(--err)">- - engellenen</span><span>Elemanın üstüne gelin: kendi bağları · tıklayın: ayrıntı</span><span>Küme adının üstüne gelin: kümenin bağları</span></div>
  </section>`;
  $("#ktBack").addEventListener("click", () => runIslev(kt.tag));
  $("#ktAgain").addEventListener("click", () => ktRun(kt.tag));
  document.querySelectorAll("[data-ktm]").forEach(b => b.addEventListener("click", () => { kt.mode = b.dataset.ktm; ktShell(); }));
  $("#ktPer").addEventListener("change", e => { kt.per = +e.target.value; ktDraw(); kt.scale = Math.min(1, ($("#ktWrap").clientWidth - 4) / kt.pos.W); ktDraw(); });
  $("#ktCols").addEventListener("change", e => { kt.cols = +e.target.value; ktDraw(); kt.scale = Math.min(1, ($("#ktWrap").clientWidth - 4) / kt.pos.W); ktDraw(); });
  $("#ktInfra").addEventListener("change", e => { kt.infra = e.target.checked; ktDraw(); });
  $("#ktZi").addEventListener("click", () => { kt.scale = Math.min(2.5, kt.scale * 1.2); ktDraw(); });
  $("#ktZo").addEventListener("click", () => { kt.scale = Math.max(0.3, kt.scale / 1.2); ktDraw(); });
  $("#ktFit").addEventListener("click", () => { kt.scale = Math.min(1, ($("#ktWrap").clientWidth - 4) / kt.pos.W); ktDraw(); });
  ktLayout();
  kt.scale = Math.min(1, ($("#ktWrap").clientWidth - 4) / kt.pos.W);
  ktDraw();
}

// Yerleşim: kümeler 3–4 sütunluk ızgarada (veri akış sırasına göre okuma düzeninde); kutu içinde elemanlar 2 sütunlu karo.
// Trafiği olmayan sunucular "+N trafiksiz" karosunda toplanır (tıklayınca açılır). Oklar yalnız kutular arasındaki yatay /
// dikey boşluklardan (kanal) gider; her kanalda her oka ayrı şerit verilir, kanal genişliği şerit sayısına göre büyür.
const KT = { TW: 158, TH: 44, TG: 6, PAD: 10, HEAD: 46 };
KT.BW = KT.PAD * 2 + KT.TW * 2 + KT.TG;

function ktVisible(k) {
  const busy = new Set();
  kt.data.baglar.forEach(b => { busy.add(b.from); busy.add(b.to); });
  const show = [], idle = [];
  for (const e of k.elemanlar) {
    const active = e.tur === "vip" || busy.has(e.id) || e.gelen + e.giden > 0 || e.disGelen.length || e.disGiden.length;
    (active || kt.expanded.has(k.ad) ? show : idle).push(e);
  }
  return { show, idle };
}

// Hangi bağlar çizilecek: küme modunda kümeler arası toplam, eleman modunda eleman → eleman (aynı kutu içi hariç)
function ktLinks() {
  const ek = kt.pos?.elemKume || {};
  const base = kt.data.baglar.filter(b => (kt.infra || !b.altyapi) && ek[b.from] && ek[b.to] && ek[b.from] !== ek[b.to]);
  if (kt.mode !== "kume") return base;
  const agg = {};
  for (const b of base) {
    const f = ek[b.from], t = ek[b.to];
    const a = agg[f + "\u0000" + t] ??= { from: "k:" + f, to: "k:" + t, hits: 0, engel: 0, portlar: new Set(), n: 0 };
    a.hits += b.hits; a.engel += b.engel; a.n++; b.portlar.forEach(p => a.portlar.add(p));
  }
  return Object.values(agg).map(a => ({ ...a, portlar: [...a.portlar] }));
}

function ktLayout() {
  const d = kt.data, { TW, TH, TG, PAD, HEAD, BW } = KT;
  const elemKume = {};
  d.kumeler.forEach(k => k.elemanlar.forEach(e => elemKume[e.id] = k.ad));
  // akış sırası: kümeler arası net giden trafiği fazla olan önce
  const net = {};
  d.baglar.forEach(b => { const f = elemKume[b.from], t = elemKume[b.to]; if (f && t && f !== t) { net[f] = (net[f] || 0) + b.hits; net[t] = (net[t] || 0) - b.hits; } });
  const flow = (a, b) => (net[b.ad] || 0) - (net[a.ad] || 0) || b.elemanlar.length - a.elemanlar.length;
  // Birden çok kurum: her kurum kendi satırlarından başlar (kurum çerçevesi), kurum içinde akış sırası
  const sections = d.coklu
    ? [...new Set(d.kumeler.map(k => k.tenant))].sort((a, b) => a.localeCompare(b, "tr")).map(t => ({ t, ks: d.kumeler.filter(k => k.tenant === t).sort(flow) }))
    : [{ t: null, ks: [...d.kumeler].sort(flow) }];
  const maxSec = Math.max(...sections.map(s => s.ks.length));
  // kutu ızgarası: kutu içi karo sütunu 4+ ise kutular geniş, otomatikte 3 sütun
  // Otomatik: ekran genişliğine sığan en çok kutu sütunu (kutu genişliği = karo sayısı × kutu içi sütun); seçiciyle sabitlenebilir
  const fitCols = () => {
    // %15'e kadar küçültmeye izin verilir (yazılar hâlâ okunur)
    const avail = Math.max(600, ($("#ktWrap")?.clientWidth || 1600) - 8) * 1.15;
    const bw = k => { const v = ktVisible(k), n = Math.max(1, v.show.length + (v.idle.length ? 1 : 0)), tc = Math.min(kt.per, n);
      return Math.max(KT.PAD * 2 + tc * TW + (tc - 1) * TG, 250); };
    for (let c = maxSec; c > 1; c--) {
      const ok = sections.every(sec => {
        const w = Array(c).fill(0);
        sec.ks.forEach((k, i) => w[i % c] = Math.max(w[i % c], bw(k)));
        return w.reduce((a, b) => a + b, 0) + (c + 1) * 50 <= avail;
      });
      if (ok) return c;
    }
    return 1;
  };
  const C = Math.max(1, Math.min(maxSec, kt.cols || fitCols()));
  const cell = {}, order = [], secRows = [];
  let R = 0;
  for (const sec of sections) {
    const r0 = R;
    sec.ks.forEach((k, i) => { cell[k.ad] = { r: R + Math.floor(i / C), c: i % C }; order.push(k); });
    R += Math.ceil(sec.ks.length / C);
    secRows.push({ t: sec.t, r0, r1: R - 1 });
  }
  kt.pos = { elemKume, cell, C, R };

  // 1) güzergâhlar ızgara düzeyinde (hangi kanallar) → şerit sayıları → kanal genişlikleri
  const links = ktLinks();
  const hUse = Array.from({ length: R + 1 }, () => []), vUse = Array.from({ length: C + 1 }, () => []);
  const routes = links.map((l, i) => {
    const A = cell[elemKume[l.from] || l.from.slice(2)], B = cell[elemKume[l.to] || l.to.slice(2)];
    const rt = { A, B, i };
    if (B.r > A.r) { rt.h1 = A.r + 1; rt.exit = "bot"; rt.enter = "top"; if (B.r > A.r + 1) { rt.v = B.c; rt.h2 = B.r; } }
    else if (B.r < A.r) { rt.h1 = A.r; rt.exit = "top"; rt.enter = "bot"; if (B.r < A.r - 1) { rt.v = B.c; rt.h2 = B.r + 1; } }
    else { rt.h1 = A.r + 1; rt.exit = "bot"; rt.enter = "bot"; }
    rt.l1 = hUse[rt.h1].push(i) - 1;
    if (rt.v != null) { rt.lv = vUse[rt.v].push(i) - 1; rt.l2 = hUse[rt.h2].push(i) - 1; }
    return rt;
  });
  const LANE = 4;
  const cap = kt.mode === "kume" ? 170 : 260;
  const gH = hUse.map(u => Math.min(cap, Math.max(44, 26 + u.length * LANE)));
  // kurum başlığı için kurumun ilk satırının üstündeki kanala yer
  // (başlık bandı hPad kadar; şeritler bandın altındaki kısımda kalır)
  const hPad = Array(R + 1).fill(0);
  if (d.coklu) secRows.forEach(sr => { hPad[sr.r0] = 34; gH[sr.r0] += 34; });
  const gV = vUse.map(u => Math.min(cap, Math.max(48, 26 + u.length * LANE)));

  // 2) kutu ve karo konumları
  const boxes = {}, elems = {};
  const rowH = Array(R).fill(0);
  const vis = {}, nTiles = {};
  // kutu içi karo sütunu: sütundaki en kalabalık kutuya göre (en çok kt.per); sütundaki kutular aynı genişlikte
  const colTc = Array(C).fill(1);
  order.forEach(k => {
    vis[k.ad] = ktVisible(k);
    nTiles[k.ad] = Math.max(1, vis[k.ad].show.length + (vis[k.ad].idle.length ? 1 : 0));
    const c = cell[k.ad].c;
    colTc[c] = Math.max(colTc[c], Math.min(kt.per, nTiles[k.ad]));
  });
  const colW = colTc.map(tc => Math.max(KT.PAD * 2 + tc * TW + (tc - 1) * TG, 250));
  order.forEach(k => {
    const tc = colTc[cell[k.ad].c];
    rowH[cell[k.ad].r] = Math.max(rowH[cell[k.ad].r], HEAD + Math.ceil(nTiles[k.ad] / tc) * (TH + TG) + PAD);
  });
  const colX = [], rowY = [];
  let x = gV[0];
  for (let c = 0; c < C; c++) { colX[c] = x; x += colW[c] + gV[c + 1]; }
  let y = gH[0];
  for (let r = 0; r < R; r++) { rowY[r] = y; y += rowH[r] + gH[r + 1]; }
  const W = x, H = y;
  order.forEach(k => {
    const { r, c } = cell[k.ad], v = vis[k.ad];
    const tiles = [...v.show.map(e => ({ e })), ...(v.idle.length ? [{ idle: v.idle, id: "idle:" + k.ad }] : [])];
    const tc = colTc[c];
    const h = HEAD + Math.ceil(Math.max(1, tiles.length) / tc) * (TH + TG) + PAD;
    const b = boxes[k.ad] = { x: colX[c], y: rowY[r], w: colW[c], h, k, idle: v.idle, tiles: [] };
    tiles.forEach((t, j) => {
      const tx = b.x + PAD + (j % tc) * (TW + TG), ty = b.y + HEAD + Math.floor(j / tc) * (TH + TG);
      const p = { x: tx, y: ty, cx: tx + TW / 2, e: t.e, idle: t.idle, id: t.e?.id || t.id, k: k.ad };
      b.tiles.push(p);
      if (t.e) elems[t.e.id] = p;
    });
  });
  // gizli (trafiksiz) elemanlar kutunun "+N" karosuna bağlanır
  order.forEach(k => vis[k.ad].idle.forEach(e => elems[e.id] = boxes[k.ad].tiles.find(t => t.idle)));

  // kanal merkezleri
  const hY = i => (i === 0 ? gH[0] / 2 : rowY[i - 1] + rowH[i - 1] + gH[i] / 2) + hPad[i] / 2;
  const vX = j => j === 0 ? gV[0] / 2 : colX[j - 1] + colW[j - 1] + gV[j] / 2;
  // kurum çerçeveleri: kurumun satırlarını çevreler, başlık kanalın üst kısmında
  const frames = d.coklu ? secRows.map(sr => ({ t: sr.t, x: 6, y: rowY[sr.r0] - gH[sr.r0] + 6, w: W - 12,
    h: rowY[sr.r1] + rowH[sr.r1] + 10 - (rowY[sr.r0] - gH[sr.r0] + 6),
    n: d.kumeler.filter(k => k.tenant === sr.t).reduce((a, k) => a + k.sunucu, 0) })) : [];
  Object.assign(kt.pos, { boxes, elems, W, H, links, routes, hUse, vUse, gH, gV, hY, vX, rowY, rowH, colX, LANE, frames, hPad });
}

// Rota noktaları → köşeleri yuvarlatılmış yol
function ktRound(pts, r = 8) {
  let d = `M${pts[0][0]},${pts[0][1]}`;
  for (let i = 1; i < pts.length - 1; i++) {
    const [px, py] = pts[i - 1], [x, y] = pts[i], [nx, ny] = pts[i + 1];
    const d1 = Math.hypot(x - px, y - py), d2 = Math.hypot(nx - x, ny - y), rr = Math.min(r, d1 / 2, d2 / 2);
    const ax = x - (x - px) / (d1 || 1) * rr, ay = y - (y - py) / (d1 || 1) * rr;
    const bx = x + (nx - x) / (d2 || 1) * rr, by = y + (ny - y) / (d2 || 1) * rr;
    d += ` L${ax.toFixed(1)},${ay.toFixed(1)} Q${x},${y} ${bx.toFixed(1)},${by.toFixed(1)}`;
  }
  const [lx, ly] = pts[pts.length - 1];
  return d + ` L${lx},${ly}`;
}

// Bir bağın rota noktaları. ax/bx: kutu kenarında çıkış / giriş x'i; lane: kanal içi şerit kaydırması
function ktPoints(rt, ax, bx, off1, offV, off2, fromTile, toTile) {
  const P = kt.pos, A = P.boxes[P.cellName[rt.A.r + "," + rt.A.c]], B = P.boxes[P.cellName[rt.B.r + "," + rt.B.c]];
  const ay = rt.exit === "bot" ? A.y + A.h : A.y, by = rt.enter === "bot" ? B.y + B.h : B.y;
  const sy = fromTile ? (rt.exit === "bot" ? fromTile.y + KT.TH : fromTile.y) : ay;
  const ey = toTile ? (rt.enter === "bot" ? toTile.y + KT.TH : toTile.y) : by;
  const g1 = P.hY(rt.h1) + off1;
  const pts = [[ax, sy], [ax, g1]];
  if (rt.v != null) {
    const vx = P.vX(rt.v) + offV, g2 = P.hY(rt.h2) + off2;
    pts.push([vx, g1], [vx, g2], [bx, g2]);
  } else pts.push([bx, g1]);
  pts.push([bx, ey + (rt.enter === "bot" ? 3 : -3)]);
  return pts;
}

function ktDraw() {
  ktLayout();
  const P = kt.pos, { boxes, elems, W, H, links, routes, hUse, vUse, LANE } = P;
  P.cellName = {};
  Object.entries(P.cell).forEach(([n, c]) => P.cellName[c.r + "," + c.c] = n);
  const max = Math.max(1, ...links.map(l => l.hits));
  // şerit kaydırması kanalın içinde kalır: şerit aralığı = min(4px, kanal / şerit sayısı)
  const laneOff = (arr, idx, size) => (idx - (arr.length - 1) / 2) * Math.min(LANE, (size - 16) / Math.max(1, arr.length));
  const hOff = (i, idx) => laneOff(hUse[i], idx, P.gH[i] - P.hPad[i]), vOff = (j, idx) => laneOff(vUse[j], idx, P.gV[j]);
  // kutu kenarında çıkış / giriş noktaları: aynı kenardaki bağlar karşı ucun x'ine göre sıralanıp yayılır
  const side = {};
  const pushSide = (box, s, i, otherX) => (side[box + "|" + s] ??= []).push({ i, otherX });
  const cx = id => id.startsWith("k:") ? boxes[id.slice(2)].x + boxes[id.slice(2)].w / 2 : elems[id].cx;
  routes.forEach(rt => {
    const l = links[rt.i];
    pushSide(P.cellName[rt.A.r + "," + rt.A.c], rt.exit, rt.i, cx(l.to));
    pushSide(P.cellName[rt.B.r + "," + rt.B.c], rt.enter, rt.i, cx(l.from));
  });
  const anchor = {};
  for (const [key, arr] of Object.entries(side)) {
    const box = boxes[key.split("|")[0]];
    arr.sort((a, b) => a.otherX - b.otherX);
    arr.forEach((a, j) => (anchor[a.i] ??= {})[key] = box.x + 18 + (box.w - 36) * (arr.length === 1 ? 0.5 : j / (arr.length - 1)));
  }
  const paths = routes.map(rt => {
    const l = links[rt.i];
    const elemMode = !l.from.startsWith("k:");
    const ft = elemMode ? elems[l.from] : null, tt = elemMode ? elems[l.to] : null;
    let ax, bx;
    if (elemMode) { ax = ft.cx; bx = tt.cx; }
    else {
      const an = anchor[rt.i];
      ax = an[P.cellName[rt.A.r + "," + rt.A.c] + "|" + rt.exit]; bx = an[P.cellName[rt.B.r + "," + rt.B.c] + "|" + rt.enter];
    }
    // eleman modunda da ok kutu kenarında başlar / biter (karonun x hizasında); kutunun içinden çizgi geçmez
    const pts = ktPoints(rt, ax, bx, hOff(rt.h1, rt.l1), rt.v != null ? vOff(rt.v, rt.lv) : 0,
      rt.v != null ? hOff(rt.h2, rt.l2) : 0, null, null);
    const dAttr = ktRound(pts);
    const w = (1.4 + 5 * Math.log10(1 + l.hits) / Math.log10(1 + max)).toFixed(1);
    const deny = l.engel > 0 && l.engel >= l.hits;
    return `<path class="lk${deny ? " deny" : ""}${l.altyapi ? " infra" : ""}" d="${dAttr}" stroke-width="${w}" marker-end="url(#ktArr${deny ? "D" : ""})" data-f="${esc(l.from)}" data-t="${esc(l.to)}" data-fk="${esc(P.elemKume[l.from] || l.from.slice(2))}" data-tk="${esc(P.elemKume[l.to] || l.to.slice(2))}"/>
      <path class="lkh" d="${dAttr}" data-li="${rt.i}"/>`;
  }).join("");

  const boxSvg = Object.values(boxes).map(b => {
    const h = tagHue(b.k.ad), nVip = b.k.elemanlar.filter(e => e.tur === "vip").length;
    return `<g class="box" data-box="${esc(b.k.ad)}" style="--h:${h}"><rect class="bg" x="${b.x}" y="${b.y}" width="${b.w}" height="${b.h}" rx="10"/>
      <path d="M${b.x} ${b.y + 10} a10 10 0 0 1 10 -10 h${b.w - 20} a10 10 0 0 1 10 10 v26 h${-b.w} z" style="fill:hsl(${h} 60% 50% / .15)"/>
      <text class="bt" x="${b.x + 12}" y="${b.y + 17}">${esc(ktCut(b.k.kume || b.k.ad, 38))}</text>
      <text class="bs" x="${b.x + 12}" y="${b.y + 31}">${b.k.sunucu} sunucu · ${nVip} VIP${b.idle.length ? ` · ${b.idle.length} trafiksiz` : ""}</text>
      ${b.tiles.map(t => t.idle
        ? `<g class="el idle" data-idle="${esc(b.k.ad)}"><rect class="tile" x="${t.x}" y="${t.y}" width="${KT.TW}" height="${KT.TH}" rx="7"/>
            <text class="nm" x="${t.cx}" y="${t.y + 19}" text-anchor="middle">+${t.idle.length} trafiksiz sunucu</text>
            <text class="ip" x="${t.cx}" y="${t.y + 33}" text-anchor="middle">göstermek için tıklayın</text></g>`
        : `<g class="el${t.e.tur === "vip" ? " vip" : ""}" data-el="${esc(t.e.id)}"><rect class="tile" x="${t.x}" y="${t.y}" width="${KT.TW}" height="${KT.TH}" rx="7"/>
            ${ktIcon(t.e, t.x + 20, t.y + KT.TH / 2, h)}
            <text class="nm" x="${t.x + 38}" y="${t.y + 19}">${esc(ktCut(t.e.ad, 17))}</text>
            <text class="ip" x="${t.x + 38}" y="${t.y + 33}">${esc(t.e.ip)}${t.e.tur === "vip" ? ` · ${t.e.uyeler.length} üye` : ""}</text>
            <title>${esc(t.e.tur === "vip" ? `VIP ${t.e.ip}:${t.e.port || ""} · ${t.e.ad} · ${t.e.uyeler.length} üye` : `${t.e.ad} · ${t.e.ip} · ${t.e.alt || ""}`)}</title></g>`).join("")}</g>`;
  }).join("");
  const s = kt.scale;
  $("#ktWrap").innerHTML = `<svg class="kt-svg" id="ktSvg" viewBox="0 0 ${W} ${H}" width="${(W * s).toFixed(0)}" height="${(H * s).toFixed(0)}" xmlns="http://www.w3.org/2000/svg">
    <defs>
      <marker id="ktArr" viewBox="0 0 10 10" refX="9" refY="5" markerUnits="userSpaceOnUse" markerWidth="9" markerHeight="9" orient="auto"><path d="M0,0 L10,5 L0,10 z" fill="var(--in)"/></marker>
      <marker id="ktArrD" viewBox="0 0 10 10" refX="9" refY="5" markerUnits="userSpaceOnUse" markerWidth="9" markerHeight="9" orient="auto"><path d="M0,0 L10,5 L0,10 z" fill="var(--err)"/></marker>
      <marker id="ktArrH" viewBox="0 0 10 10" refX="9" refY="5" markerUnits="userSpaceOnUse" markerWidth="10" markerHeight="10" orient="auto"><path d="M0,0 L10,5 L0,10 z" fill="var(--out)"/></marker>
    </defs>
    ${P.frames.map(f => `<g class="frame"><rect x="${f.x}" y="${f.y}" width="${f.w}" height="${f.h}" rx="14"/>
      <text x="${f.x + 14}" y="${f.y + 20}">${esc(f.t)}</text><text class="fs" x="${f.x + 26 + f.t.length * 11}" y="${f.y + 20}">${f.n} sunucu</text></g>`).join("")}
    ${boxSvg}<g id="ktLinks">${paths}</g><g id="ktOver"></g></svg>`;
  ktWire(links);
}

function ktWire(links) {
  const svg = $("#ktSvg"), P = kt.pos;
  const tip = document.querySelector(".kt-tip") || document.body.appendChild(Object.assign(document.createElement("div"), { className: "kt-tip", hidden: true }));
  const name = id => id.startsWith("k:") ? id.slice(2) : P.elems[id]?.e?.ad || id;
  const clear = () => { svg.classList.remove("dim"); svg.querySelectorAll(".hl").forEach(x => x.classList.remove("hl")); $("#ktOver").innerHTML = ""; };
  // Elemanın üstüne gelince: kendi eleman bağları (küme modunda üstte ayrı katmanda, kanallardan) çizilir, gerisi soluklaşır
  const lightElem = id => {
    clear();
    const mine = kt.data.baglar.filter(b => (b.from === id || b.to === id) && (kt.infra || !b.altyapi) && P.elems[b.from]?.e && P.elems[b.to]?.e);
    svg.classList.add("dim");
    svg.querySelector(`.el[data-el="${CSS.escape(id)}"]`)?.classList.add("hl");
    const max = Math.max(1, ...mine.map(b => b.hits));
    $("#ktOver").innerHTML = mine.map(b => {
      const fk = P.elemKume[b.from], tk = P.elemKume[b.to];
      svg.querySelector(`.el[data-el="${CSS.escape(b.from === id ? b.to : b.from)}"]`)?.classList.add("hl");
      if (fk === tk) {   // aynı kutu içi: kısa yay
        const a = P.elems[b.from], c = P.elems[b.to];
        return `<path class="lk hl over" d="M${a.cx},${a.y + KT.TH / 2} Q${(a.cx + c.cx) / 2 + 30},${(a.y + c.y) / 2 + KT.TH / 2} ${c.cx},${c.y + KT.TH / 2}" stroke-width="2.5" marker-end="url(#ktArrH)"/>`;
      }
      const A = P.cell[fk], B = P.cell[tk];
      const rt = { A, B };
      if (B.r > A.r) { rt.h1 = A.r + 1; rt.exit = "bot"; rt.enter = "top"; if (B.r > A.r + 1) { rt.v = B.c; rt.h2 = B.r; } }
      else if (B.r < A.r) { rt.h1 = A.r; rt.exit = "top"; rt.enter = "bot"; if (B.r < A.r - 1) { rt.v = B.c; rt.h2 = B.r + 1; } }
      else { rt.h1 = A.r + 1; rt.exit = "bot"; rt.enter = "bot"; }
      const pts = ktPoints(rt, P.elems[b.from].cx, P.elems[b.to].cx, 0, 0, 0, null, null);
      const w = (2 + 4 * Math.log10(1 + b.hits) / Math.log10(1 + max)).toFixed(1);
      return `<path class="lk hl over" d="${ktRound(pts)}" stroke-width="${w}" marker-end="url(#ktArrH)"/>`;
    }).join("");
  };
  const lightBox = name => {
    clear(); svg.classList.add("dim");
    svg.querySelectorAll(".lk").forEach(p => { if (p.dataset.fk === name || p.dataset.tk === name) p.classList.add("hl"); });
    svg.querySelectorAll(`.box[data-box="${CSS.escape(name)}"] .el`).forEach(e => e.classList.add("hl"));
  };
  svg.querySelectorAll(".el[data-el]").forEach(g => {
    g.addEventListener("mouseenter", () => lightElem(g.dataset.el));
    g.addEventListener("mouseleave", clear);
    g.addEventListener("click", () => ktOpen(g.dataset.el));
  });
  svg.querySelectorAll(".el[data-idle]").forEach(g => g.addEventListener("click", () => { kt.expanded.add(g.dataset.idle); ktDraw(); }));
  svg.querySelectorAll(".box text.bt").forEach(t => {
    const n = t.closest(".box").dataset.box;
    t.style.cursor = "pointer";
    t.addEventListener("mouseenter", () => lightBox(n));
    t.addEventListener("mouseleave", clear);
    t.addEventListener("click", () => { if (kt.expanded.has(n)) { kt.expanded.delete(n); ktDraw(); } });
  });
  svg.querySelectorAll(".lkh").forEach(p => {
    const l = links[+p.dataset.li];
    p.addEventListener("mousemove", ev => {
      tip.hidden = false; tip.style.left = ev.clientX + 14 + "px"; tip.style.top = ev.clientY + 12 + "px";
      tip.innerHTML = `<b>${esc(name(l.from))}</b> → <b>${esc(name(l.to))}</b><br>${fmt(l.hits)} bağlantı${l.engel ? ` · <span style="color:#ff9a9a">${fmt(l.engel)} engellendi</span>` : ""}<br>Port: ${esc(l.portlar.slice(0, 12).join(", ") || "—")}${l.n ? `<br>${l.n} eleman bağı (elemanların üstüne gelin)` : ""}`;
      p.previousElementSibling?.classList.add("hl"); svg.classList.add("dim");
    });
    p.addEventListener("mouseleave", () => { tip.hidden = true; clear(); });
  });
}

function ktIcon(e, cx, cy, h) {
  if (e.tur === "vip") {
    return `<g class="ico-vip" style="--h:${h}"><circle cx="${cx}" cy="${cy}" r="15"/>
      <path d="M${cx - 8} ${cy} H${cx - 2} M${cx - 2} ${cy} L${cx + 6} ${cy - 6} M${cx - 2} ${cy} H${cx + 7} M${cx - 2} ${cy} L${cx + 6} ${cy + 6}"/></g>`;
  }
  return `<g class="ico-srv"><rect x="${cx - 11}" y="${cy - 14}" width="22" height="28" rx="3"/>
    <line x1="${cx - 6}" y1="${cy - 7}" x2="${cx + 6}" y2="${cy - 7}"/><line x1="${cx - 6}" y1="${cy - 1}" x2="${cx + 6}" y2="${cy - 1}"/>
    <circle cx="${cx + 5}" cy="${cy + 8}" r="2"/></g>`;
}

const ktCut = (s, n) => (s = s || "").length > n ? s.slice(0, n - 1) + "…" : s;

// ---------- Ayrıntı penceresi ----------
function ktModal(html) {
  let bg = document.querySelector(".kt-modal-bg");
  if (!bg) {
    bg = document.body.appendChild(Object.assign(document.createElement("div"), { className: "kt-modal-bg" }));
    bg.addEventListener("click", e => { if (e.target === bg) ktClose(); });
  }
  bg.innerHTML = `<div class="kt-modal" role="dialog">${html}</div>`;
  bg.querySelector("[data-ktclose]")?.addEventListener("click", ktClose);
  bg.querySelector("[data-ktback]")?.addEventListener("click", () => { kt.stack.pop(); const prev = kt.stack.pop(); prev ? prev() : ktClose(); });
  return bg;
}
function ktClose() { kt.stack = []; document.querySelector(".kt-modal-bg")?.remove(); }
document.addEventListener("keydown", e => { if (e.key === "Escape" && document.querySelector(".kt-modal-bg")) ktClose(); });

// Eleman ve kümesi (gizli / trafiksiz olanlar dahil)
function ktFind(id) { for (const k of kt.data.kumeler) for (const e of k.elemanlar) if (e.id === id) return { e, k: k.ad }; return null; }
function ktElemName(id) { const p = ktFind(id); return p ? `${p.e.tur === "vip" ? "VIP " : ""}${p.e.ad}` : id; }
function ktHead(title, sub, back) {
  return `<div class="mh"><div>${back ? `<a href="#" data-ktback style="font-size:12px">← geri</a>` : ""}<h3>${title}</h3><div class="eg-sub">${sub}</div></div>
    <button data-ktclose>Kapat</button></div>`;
}

function ktOpen(id) {
  const p = ktFind(id);
  if (!p) return;
  if (p.e.tur !== "vip") return ktServer(p.e.uyeler[0].ip);
  kt.stack.push(() => ktOpen(id));
  const e = p.e, h = tagHue(p.k);
  const ins = kt.data.baglar.filter(b => b.to === id), outs = kt.data.baglar.filter(b => b.from === id);
  const bagTable = (rows, side) => rows.length ? `<table><thead><tr><th>${side === "in" ? "Gelen" : "Giden"}</th><th>Port</th><th class="num">Bağlantı</th></tr></thead><tbody>
    ${rows.map(b => { const o = side === "in" ? b.from : b.to; return `<tr class="cl" data-open="${esc(o)}"><td>${esc(ktElemName(o))}<div class="eg-sub">${esc(kt.pos.elemKume[o] || "")}</div></td><td>${esc(b.portlar.join(", "))}</td><td class="num">${fmt(b.hits)}${b.engel ? ` <span style="color:var(--err)">⛔ ${fmt(b.engel)}</span>` : ""}</td></tr>`; }).join("")}</tbody></table>`
    : `<div class="eg-sub">Etiketli kümelerle ${side === "in" ? "gelen" : "giden"} trafik yok.</div>`;
  const disTable = (rows, side) => rows.length ? `<table><tbody>${rows.map(r => `<tr><td>${esc(r.ad)}</td><td>${esc(r.portlar.join(", "))}</td><td class="num">${fmt(r.hits)}</td></tr>`).join("")}</tbody></table>`
    : `<div class="eg-sub">Yok.</div>`;
  const bg = ktModal(`${ktHead(`<span style="color:hsl(${h} 55% 45%)">◉</span> VIP <span class="ip">${esc(e.ip)}${e.port ? ":" + esc(e.port) : ""}</span>`,
      `${esc(e.ad)}${e.alt && e.alt !== e.ad ? " · havuz " + esc(e.alt) : ""} · ${esc(p.k)} · ${esc(e.segment || "")}`, kt.stack.length > 1)}
    <div class="mb">
      <div class="stat"><div class="eg-stat"><b>${e.uyeler.length}</b><span>Üye</span></div><div class="eg-stat"><b>${fmt(e.gelen)}</b><span>Gelen</span></div><div class="eg-stat"><b>${fmt(e.giden)}</b><span>Giden (üyelerden)</span></div></div>
      <div><h4>Havuz üyeleri — tıklayın</h4><table><thead><tr><th>Sunucu</th><th>IP</th><th>Port</th><th>AI rolü</th><th class="num">Gelen</th><th class="num">Giden</th></tr></thead><tbody>
        ${e.uyeler.map(u => `<tr class="cl" data-srv="${esc(u.ip)}"><td><b>${esc(u.hostname || "—")}</b></td><td class="ip">${esc(u.ip)}</td><td>${esc(u.port || "")}</td><td>${esc(u.rol || "—")}</td><td class="num">${fmt(u.gelen)}</td><td class="num">${fmt(u.giden)}</td></tr>`).join("")}</tbody></table></div>
      <div class="two"><div><h4>Kümelerden gelen</h4>${bagTable(ins, "in")}</div><div><h4>Kümelere giden</h4>${bagTable(outs, "out")}</div></div>
      <div class="two"><div><h4>Etiket dışından gelen</h4>${disTable(e.disGelen)}</div><div><h4>Etiket dışına giden</h4>${disTable(e.disGiden)}</div></div>
    </div>`);
  bg.querySelectorAll("[data-srv]").forEach(r => r.addEventListener("click", () => ktServer(r.dataset.srv)));
  bg.querySelectorAll("[data-open]").forEach(r => r.addEventListener("click", () => ktOpen(r.dataset.open)));
}

function ktServer(ip) {
  kt.stack.push(() => ktServer(ip));
  let u, el, kume;
  for (const k of kt.data.kumeler) for (const e of k.elemanlar) { const x = e.uyeler.find(m => m.ip === ip); if (x && !u) { u = x; el = e; kume = k.ad; } }
  if (!u) return;
  const elemOf = x => { for (const k of kt.data.kumeler) for (const e of k.elemanlar) if ((e.tur === "vip" && e.ip === x) || e.uyeler.some(m => m.ip === x)) return e; return null; };
  const flows = kt.data.akislar.filter(a => a.src === ip || a.dst === ip);
  const peerName = x => { const e = elemOf(x); const m = e?.uyeler.find(z => z.ip === x); return m?.hostname || (e?.tur === "vip" && e.ip === x ? "VIP " + e.ad : x); };
  const ft = (rows, side) => rows.length ? `<table><thead><tr><th>${side === "in" ? "Kaynak" : "Hedef"}</th><th>IP</th><th>Port</th><th class="num">Bağlantı</th></tr></thead><tbody>
    ${rows.map(a => { const o = side === "in" ? a.src : a.dst; return `<tr class="cl" data-srv2="${esc(o)}"><td>${esc(peerName(o))}<div class="eg-sub">${esc(kt.pos.elemKume[elemOf(o)?.id] || "")}</div></td><td class="ip">${esc(o)}</td><td>${esc(a.port)}</td><td class="num">${fmt(a.hits)}${a.engel ? ` <span style="color:var(--err)">⛔</span>` : ""}</td></tr>`; }).join("")}</tbody></table>`
    : `<div class="eg-sub">Etiketli sunucularla ${side === "in" ? "gelen" : "giden"} trafik yok.</div>`;
  const g = u.guven == null ? "" : `%${u.guven}`;
  const bg = ktModal(`${ktHead(`▭ ${esc(u.hostname || ip)}`, `<span class="ip">${esc(ip)}</span> · ${esc(kume)}${el.tur === "vip" ? ` · VIP ${esc(el.ip)}:${esc(el.port || "")} üyesi` : ""} · ${esc(el.segment || "")}`, kt.stack.length > 1)}
    <div class="mb">
      <dl class="kv"><dt>Küme</dt><dd><b>${esc(kume)}</b></dd><dt>AI rolü</dt><dd>${esc(u.rol || "—")} ${g ? `<span class="eg-sub">(güven ${g}${u.elle ? ", elle atandı" : ""})</span>` : ""}</dd>
        <dt>Trafik</dt><dd>${fmt(u.gelen)} gelen · ${fmt(u.giden)} giden (etiketli sunucularla)</dd></dl>
      <div id="ktWhy" class="eg-sub"><span class="spinner"></span> AI gerekçesi ve kanıtlar yükleniyor…</div>
      <div class="two"><div><h4>Gelen</h4>${ft(flows.filter(a => a.dst === ip), "in")}</div><div><h4>Giden</h4>${ft(flows.filter(a => a.src === ip), "out")}</div></div>
      <div style="display:flex;gap:8px"><a href="?ip=${encodeURIComponent(ip)}" target="_blank" rel="noopener" style="padding:6px 12px;border-radius:6px;text-decoration:none;background:var(--in);color:#fff">IP topolojisini aç ↗</a></div>
    </div>`);
  bg.querySelectorAll("[data-srv2]").forEach(r => r.addEventListener("click", () => { const e = elemOf(r.dataset.srv2); if (e?.tur === "vip" && e.ip === r.dataset.srv2) ktOpen(e.id); else ktServer(r.dataset.srv2); }));
  fetch("api/islev/profil?" + new URLSearchParams({ ip })).then(r => r.ok ? r.json() : null).then(p => {
    const box = document.getElementById("ktWhy");
    if (!box || !p) { if (box) box.textContent = "Kanıtlar alınamadı."; return; }
    const list = (t, a) => a?.length ? `<div><b>${t}:</b> ${a.map(esc).join(" · ")}</div>` : "";
    box.className = "";
    box.innerHTML = `${p.detay?.gerekce ? `<div class="why"><b>AI gerekçesi:</b> ${esc(p.detay.gerekce)}</div>` : ""}
      <div style="display:grid;gap:4px;margin-top:8px;font-size:12.5px">${list("MSY", p.msy)}${list("ÇSY", p.csy)}${list("Kahin", p.kahin)}
      ${p.kurulum?.length ? `<div><b>Kurulum:</b> ${p.kurulum.map(k => `${esc(k.tarih || "")} ${esc(k.baslik || "")}${k.aciklama ? " — " + esc(k.aciklama) : ""}`).join(" · ")}</div>` : ""}
      ${p.talepler?.length ? `<div><b>İş istekleri (${p.talepler.length}):</b> ${p.talepler.slice(0, 5).map(t => `${t.link ? `<a href="${esc(t.link)}" target="_blank" rel="noopener">${esc(t.no)}</a>` : esc(t.no)} ${esc(t.baslik || "")}`).join(" · ")}</div>` : ""}</div>`;
  }).catch(() => { const box = document.getElementById("ktWhy"); if (box) box.textContent = "Kanıtlar alınamadı."; });
}
