// ============================================================================
// DeltaFlow · Küme topolojisi (İşlev haritası → "Topoloji çiz")
// Kümeler kutu kutu; içlerinde VIP'ler (yük dengeleyici simgesi) ve VIP'e bağlı olmayan sunucular. Elemanlar arası trafik
// (Carbon Black / AppResponse / Firewall, seçili tarih aralığı) eğri oklarla çizilir; kalınlık trafik, kırmızı kesik = engellenen.
// Elemana tıklayınca ekranın ortasında ayrıntı penceresi: VIP üyeleri, bağlar, etiket dışı trafik; üyeye tıklayınca sunucu
// ayrıntısı (AI rolü / gerekçesi, IP düzeyinde akışlar). Veri: /api/islev/topoloji (KumeTopoloji.cs).
// ============================================================================

const kt = { tag: "", data: null, scale: 1, mode: "kume", expanded: new Set(), cols: 0, per: 5, alt: "uygulama", infra: false, abort: null, hover: null, pos: null, stack: [] };

(function ktStyle() {
  const css = `
  .kt-bar { display:flex; gap:8px; flex-wrap:wrap; align-items:center; margin:2px 0 12px; }
  .kt-bar .sp { flex:1; }
  .kt-bar label { display:flex; gap:6px; align-items:center; font-size:12.5px; color:var(--text); }
  .kt-src { display:flex; gap:8px; flex-wrap:wrap; font-size:12px; color:var(--muted); margin-bottom:10px; }
  .kt-wrap { border:1px solid var(--line); border-radius:10px; background:var(--bg); overflow:auto; position:relative; }
  .kt-svg { display:block; }
  .kt-svg .box rect.bg { fill:var(--panel); stroke:hsl(var(--h) 55% 50% / .55); stroke-width:1.5; }
  .kt-svg .box rect.hd { fill:hsl(var(--h) 60% 50% / .12); }
  .kt-svg .box .bt { font-size:14px; font-weight:700; fill:var(--text); }
  .kt-svg .box .bs { font-size:11px; fill:var(--muted); }
  .kt-svg .frame rect { fill:none; stroke:var(--muted); stroke-opacity:.45; stroke-width:1.5; stroke-dasharray:8 5; }
  .kt-svg .frame text { font-size:14px; font-weight:800; fill:var(--text); letter-spacing:.04em; text-transform:uppercase; }
  .kt-svg .frame text.fs { font-size:11.5px; font-weight:500; fill:var(--muted); text-transform:none; letter-spacing:0; }
  .kt-svg .ah text { font-size:11px; font-weight:700; fill:hsl(var(--h) 45% 38%); letter-spacing:.02em; }
  .kt-svg .ah tspan { font-weight:500; fill:var(--muted); }
  .kt-svg .ah line { stroke:hsl(var(--h) 50% 50% / .25); }
  :root[data-theme="dark"] .kt-svg .ah text { fill:hsl(var(--h) 60% 72%); }
  @media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) .kt-svg .ah text { fill:hsl(var(--h) 60% 72%); } }
  .kt-svg .el { cursor:pointer; }
  .kt-svg .el .tile { fill:var(--panel); stroke:var(--line); }
  .kt-svg .el.vip .tile { fill:hsl(var(--h, 210) 60% 50% / .07); }
  .kt-svg .el:hover .tile, .kt-svg .el.hl .tile { stroke:var(--in); fill:var(--in-soft); }
  .kt-svg .el.idle .tile { fill:transparent; stroke-dasharray:4 3; }
  .kt-svg .el.idle .nm { fill:var(--muted); font-weight:500; }
  .kt-svg .lk.over { stroke:var(--out); stroke-opacity:.95; }
  .kt-svg .el .hit { fill:transparent; }
  .kt-svg .el:hover .hit, .kt-svg .el.on .hit { fill:var(--in-soft); }
  .kt-svg .el .nm { font-size:10.5px; font-weight:600; fill:var(--text); }
  .kt-svg .el .ip { font-size:9px; fill:var(--muted); font-family:Consolas, ui-monospace, monospace; }
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

// Ctrl + tekerlek: imlecin altındaki nokta sabit kalacak şekilde yakınlaştır; boş alanda sürükle: kaydır
function ktSetScale(s1, cx, cy) {
  const w = $("#ktWrap"), svg = $("#ktSvg");
  if (!w || !svg) return;
  const s0 = kt.scale; s1 = Math.max(0.15, Math.min(3, s1));
  const r = w.getBoundingClientRect(), px = (cx ?? r.left + w.clientWidth / 2) - r.left, py = (cy ?? r.top + w.clientHeight / 2) - r.top;
  const ux = (w.scrollLeft + px) / s0, uy = (w.scrollTop + py) / s0;
  kt.scale = s1;
  svg.setAttribute("width", (kt.pos.W * s1).toFixed(0)); svg.setAttribute("height", (kt.pos.H * s1).toFixed(0));
  w.scrollLeft = ux * s1 - px; w.scrollTop = uy * s1 - py;
}
function ktZoomPan() {
  const w = $("#ktWrap");
  if (!w || w.dataset.zp) return;
  w.dataset.zp = "1";
  w.addEventListener("wheel", e => { if (!e.ctrlKey) return; e.preventDefault(); ktSetScale(kt.scale * (e.deltaY < 0 ? 1.15 : 1 / 1.15), e.clientX, e.clientY); }, { passive: false });
  let drag = null;
  w.addEventListener("mousedown", e => {
    if (e.button !== 0 || e.target.closest(".el, .lkh, .box text.bt")) return;
    drag = { x: e.clientX, y: e.clientY, l: w.scrollLeft, t: w.scrollTop }; w.style.cursor = "grabbing"; e.preventDefault();
  });
  window.addEventListener("mousemove", e => { if (!drag) return; w.scrollLeft = drag.l - (e.clientX - drag.x); w.scrollTop = drag.t - (e.clientY - drag.y); });
  window.addEventListener("mouseup", () => { if (drag) { drag = null; w.style.cursor = ""; } });
}

// Ekrana sığdır: çizim alanı pencerenin altına kadar uzar; ölçek genişliğe VE yüksekliğe sığacak şekilde
function ktWrapH() { const w = $("#ktWrap"); return w ? Math.max(320, window.innerHeight - w.getBoundingClientRect().top - window.scrollY * 0 - 46) : 600; }
function ktFitScale() {
  const w = $("#ktWrap");
  if (!w || !kt.pos) return 1;
  w.style.height = ktWrapH() + "px";
  return Math.max(0.2, Math.min(1, (w.clientWidth - 4) / kt.pos.W, (w.clientHeight - 4) / kt.pos.H));
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
      <label>Kutu içi grup <select id="ktAlt"><option value="uygulama"${kt.alt === "uygulama" ? " selected" : ""}>Uygulama (ERT)</option><option value="yok"${kt.alt !== "uygulama" ? " selected" : ""}>Yok</option></select></label>
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
    <div class="legend" style="margin-top:8px"><span>◯ VIP</span><span>▭ sunucu</span><span>— trafik (kalınlık = hacim)</span><span style="color:var(--err)">- - engellenen</span><span>Elemanın üstüne gelin: kendi bağları · tıklayın: ayrıntı</span><span>Ctrl + tekerlek: yakınlaştır · boş yerde sürükle: kaydır</span><span>Küme adının üstüne gelin: kümenin bağları</span></div>
  </section>`;
  $("#ktBack").addEventListener("click", () => runIslev(kt.tag));
  $("#ktAgain").addEventListener("click", () => ktRun(kt.tag));
  document.querySelectorAll("[data-ktm]").forEach(b => b.addEventListener("click", () => { kt.mode = b.dataset.ktm; ktShell(); }));
  $("#ktAlt").addEventListener("change", e => { kt.alt = e.target.value; ktDraw(); });
  $("#ktPer").addEventListener("change", e => { kt.per = +e.target.value; ktDraw(); kt.scale = ktFitScale(); ktDraw(); });
  $("#ktCols").addEventListener("change", e => { kt.cols = +e.target.value; ktDraw(); kt.scale = ktFitScale(); ktDraw(); });
  $("#ktInfra").addEventListener("change", e => { kt.infra = e.target.checked; ktDraw(); });
  $("#ktZi").addEventListener("click", () => ktSetScale(kt.scale * 1.25));
  $("#ktZo").addEventListener("click", () => ktSetScale(kt.scale / 1.25));
  $("#ktFit").addEventListener("click", () => { kt.scale = ktFitScale(); ktDraw(); });
  ktLayout();
  kt.scale = ktFitScale();
  ktDraw();
  ktZoomPan();
}

// Yerleşim: kümeler 3–4 sütunluk ızgarada (veri akış sırasına göre okuma düzeninde); kutu içinde elemanlar 2 sütunlu karo.
// Trafiği olmayan sunucular "+N trafiksiz" karosunda toplanır (tıklayınca açılır). Oklar yalnız kutular arasındaki yatay /
// dikey boşluklardan (kanal) gider; her kanalda her oka ayrı şerit verilir, kanal genişliği şerit sayısına göre büyür.
// Kompakt karo (kullanıcı: "biraz daha küçük, sayfada yakınlaştırarak bakarız")
const KT = { TW: 128, TH: 32, TG: 4, PAD: 8, HEAD: 40 };
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

// Kutunun içi: kt.alt === "uygulama" ise karolar ERT APPNAME'e göre alt başlıklar altında, değilse düz ızgara.
// Döner: { h, items: [{ hdr, n, y } | { e | idle, col, y }] } (y kutu üstüne göre)
const KT_AH = 16;
function ktPlan(k, tc) {
  const { TH, TG, HEAD } = KT, v = ktVisible(k), items = [];
  let y = HEAD;
  const put = list => { list.forEach((t, j) => items.push({ ...t, col: j % tc, y: y + Math.floor(j / tc) * (TH + TG) })); y += Math.ceil(list.length / tc) * (TH + TG); };
  if (kt.alt === "uygulama") {
    const g = new Map();
    v.show.forEach(e => { const a = e.uygulama || "Uygulama kaydı yok"; (g.get(a) || g.set(a, []).get(a)).push(e); });
    const groups = [...g.entries()].sort((a, b) => (a[0] === "Uygulama kaydı yok") - (b[0] === "Uygulama kaydı yok") || b[1].length - a[1].length || a[0].localeCompare(b[0], "tr"));
    for (const [ad, es] of groups) { items.push({ hdr: ad, n: es.length, y }); y += KT_AH; put(es.map(e => ({ e }))); }
    if (v.idle.length) { items.push({ hdr: "Trafiksiz", n: v.idle.length, y }); y += KT_AH; put([{ idle: v.idle }]); }
  } else put([...v.show.map(e => ({ e })), ...(v.idle.length ? [{ idle: v.idle }] : [])]);
  if (!items.length) y += TH + TG;
  return { h: y + KT.PAD, items, idle: v.idle };
}

// Yerleşim (canlıda satır hizalı ızgara uzun kutular yüzünden boşluklu / şekilsiz kaldı → sütun yığma):
// kümeler C sütuna "en kısa sütuna koy" kuralıyla üst üste dizilir (akış sırası korunur). Oklar kutunun YAN kenarından çıkar,
// sütunlar arası dikey kanaldan gider; komşu sütun → aradaki kanal, aynı sütun → sağdaki kanal, uzak sütun → en üst ya da
// en alt yatay kanal üzerinden. Her kanalda her oka ayrı şerit.
function ktLayout() {
  const d = kt.data, { TW, TG, PAD } = KT;
  const elemKume = {};
  d.kumeler.forEach(k => k.elemanlar.forEach(e => elemKume[e.id] = k.ad));
  const net = {};
  d.baglar.forEach(b => { const f = elemKume[b.from], t = elemKume[b.to]; if (f && t && f !== t) { net[f] = (net[f] || 0) + b.hits; net[t] = (net[t] || 0) - b.hits; } });
  const flow = (a, b) => (net[b.ad] || 0) - (net[a.ad] || 0) || b.elemanlar.length - a.elemanlar.length;
  const sections = d.coklu
    ? [...new Set(d.kumeler.map(k => k.tenant))].sort((a, b) => a.localeCompare(b, "tr")).map(t => ({ t, ks: d.kumeler.filter(k => k.tenant === t).sort(flow) }))
    : [{ t: null, ks: [...d.kumeler].sort(flow) }];
  const maxSec = Math.max(...sections.map(s => s.ks.length));
  const tiles = k => { const v = ktVisible(k); return Math.max(1, v.show.length + (v.idle.length ? 1 : 0)); };
  const bw = k => { const tc = Math.min(kt.per, tiles(k)); return Math.max(PAD * 2 + tc * TW + (tc - 1) * TG, 250); };
  const GAPY = 26;
  const plan = {};
  d.kumeler.forEach(k => plan[k.ad] = ktPlan(k, Math.min(kt.per, tiles(k))));
  // Yerleşim kuralı (kullanıcı): en kalabalık (en yüksek) küme en üstte SOL sütunda; diğerleri büyükten küçüğe sağdaki
  // sütunlara "en kısa sütuna" dizilir; sağdakiler soldakini aşarsa sol sütunun altına da konabilir.
  const stack = C => {
    const cell = {}, secH = [], colW = Array(C).fill(200);
    sections.forEach((sec, si) => {
      const ks = [...sec.ks].sort((a, b) => plan[b.ad].h - plan[a.ad].h || flow(a, b));
      const colH = Array(C).fill(0);
      ks.forEach((k, i) => {
        let c = 0;
        if (i > 0 && C > 1) {
          const rest = colH.slice(1), m = Math.min(...rest);
          c = m < colH[0] ? rest.indexOf(m) + 1 : colH.indexOf(Math.min(...colH));
        }
        cell[k.ad] = { c, s: si, yRel: colH[c] };
        colH[c] += plan[k.ad].h + GAPY;
        colW[c] = Math.max(colW[c], bw(k));
      });
      secH[si] = Math.max(...colH) - GAPY;
    });
    return { cell, secH, colW };
  };
  // Otomatik sütun: ekrana (genişlik ve yükseklik) en büyük ölçekle sığan sütun sayısı
  const fitCols = () => {
    const w = $("#ktWrap"), aw = Math.max(600, (w?.clientWidth || 1600) - 8), ah = Math.max(320, (w ? ktWrapH() : 900) - 8);
    let best = 1, bestS = 0;
    for (let c = 1; c <= Math.min(maxSec, 7); c++) {
      const t = stack(c), W = t.colW.reduce((a, b) => a + b, 0) + (c + 1) * 70, H = t.secH.reduce((a, b) => a + b + 60, 0) + 60;
      const sc = Math.min(aw / W, ah / H);
      if (sc > bestS + 0.01) { best = c; bestS = sc; }
    }
    return best;
  };
  const C = Math.max(1, Math.min(maxSec, kt.cols || fitCols()));

  // 1) sütun yığma (sütun içi y, bölüme göre)
  const { cell, secH, colW } = stack(C);
  kt.pos = { elemKume, cell, C };

  // 2) güzergâh tipleri ve kanal kullanımı (uzak sütunlar için üst / alt kanal, bölüm içi göreli y'ye göre)
  const links = ktLinks();
  const kOf = id => elemKume[id] || id.slice(2);
  const vUse = Array.from({ length: C + 1 }, () => []), chUse = { top: [], bot: [] };
  const routes = links.map((l, i) => {
    const A = cell[kOf(l.from)], B = cell[kOf(l.to)], rt = { i, A, B, ka: kOf(l.from), kb: kOf(l.to) };
    if (A.c === B.c) { rt.out = "R"; rt.in = "R"; rt.g1 = A.c + 1; }
    else if (B.c === A.c + 1) { rt.out = "R"; rt.in = "L"; rt.g1 = A.c + 1; }
    else if (B.c === A.c - 1) { rt.out = "L"; rt.in = "R"; rt.g1 = A.c; }
    else if (B.c > A.c) { rt.out = "R"; rt.in = "L"; rt.g1 = A.c + 1; rt.g2 = B.c; }
    else { rt.out = "L"; rt.in = "R"; rt.g1 = A.c; rt.g2 = B.c + 1; }
    rt.l1 = vUse[rt.g1].push(i) - 1;
    if (rt.g2 != null) {
      const mid = (A.yRel + plan[rt.ka].h / 2 + B.yRel + plan[rt.kb].h / 2) / 2, tot = secH[A.s] || 1;
      rt.ch = A.s !== B.s ? (A.s < B.s ? "bot" : "top") : mid < tot / 2 ? "top" : "bot";
      rt.lc = chUse[rt.ch].push(i) - 1;
      rt.l2 = vUse[rt.g2].push(i) - 1;
    }
    return rt;
  });
  const LANE = 4, cap = kt.mode === "kume" ? 170 : 260;
  const gV = vUse.map(u => Math.min(cap, Math.max(44, 24 + u.length * LANE)));
  const chH = { top: chUse.top.length ? Math.min(140, 22 + chUse.top.length * 3.5) : 16, bot: chUse.bot.length ? Math.min(140, 22 + chUse.bot.length * 3.5) : 16 };

  // 3) konumlar
  const colX = [];
  let x = gV[0];
  for (let c = 0; c < C; c++) { colX[c] = x; x += colW[c] + gV[c + 1]; }
  const W = x;
  const TITLE = d.coklu ? 34 : 0, SECGAP = 44;
  const secY = [];
  let y = chH.top + 8;
  sections.forEach((sec, si) => { secY[si] = y + TITLE; y += TITLE + secH[si] + SECGAP; });
  const H = y - SECGAP + 8 + chH.bot;
  const boxes = {}, elems = {};
  sections.forEach((sec, si) => sec.ks.forEach(k => {
    const { c, yRel } = cell[k.ad], pl = plan[k.ad];
    const b = boxes[k.ad] = { x: colX[c], y: secY[si] + yRel, w: bw(k), h: pl.h, k, idle: pl.idle, tiles: [], hdrs: [], c };
    for (const it of pl.items) {
      if (it.hdr != null) { b.hdrs.push({ t: it.hdr, n: it.n, y: b.y + it.y }); continue; }
      const tx = b.x + PAD + it.col * (TW + TG), ty = b.y + it.y;
      const p = { x: tx, y: ty, cx: tx + TW / 2, cy: ty + KT.TH / 2, e: it.e, idle: it.idle, id: it.e?.id || "idle:" + k.ad, k: k.ad };
      b.tiles.push(p);
      if (it.e) elems[it.e.id] = p;
    }
    pl.idle.forEach(e => elems[e.id] = b.tiles.find(t => t.idle));
  }));
  const vX = j => j === 0 ? gV[0] / 2 : colX[j - 1] + colW[j - 1] + gV[j] / 2;
  const chY = ch => ch === "top" ? chH.top / 2 + 2 : H - chH.bot / 2 - 2;
  const frames = d.coklu ? sections.map((sec, si) => ({ t: sec.t, x: 6, y: secY[si] - TITLE - 4, w: W - 12, h: secH[si] + TITLE + 14,
    n: sec.ks.reduce((a, k) => a + k.sunucu, 0) })) : [];
  Object.assign(kt.pos, { boxes, elems, W, H, links, routes, vUse, chUse, gV, chH, vX, chY, colX, colW, LANE, frames });
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

// Bir bağın noktaları: ay/by kutu kenarındaki çıkış / giriş y'si; o1/o2/oc şerit kaydırmaları
function ktPoints(rt, ay, by, o1 = 0, o2 = 0, oc = 0) {
  const P = kt.pos, A = P.boxes[rt.ka], B = P.boxes[rt.kb];
  const ax = rt.out === "R" ? A.x + A.w : A.x;
  const bx = rt.in === "R" ? B.x + B.w + 2 : B.x - 2;
  const g1 = P.vX(rt.g1) + o1;
  if (rt.g2 == null) return [[ax, ay], [g1, ay], [g1, by], [bx, by]];
  const g2 = P.vX(rt.g2) + o2, cy = P.chY(rt.ch) + oc;
  return [[ax, ay], [g1, ay], [g1, cy], [g2, cy], [g2, by], [bx, by]];
}

function ktDraw() {
  ktLayout();
  const P = kt.pos, { boxes, elems, W, H, links, routes, vUse, chUse, LANE } = P;
  const max = Math.max(1, ...links.map(l => l.hits));
  const laneOff = (n, idx, size) => (idx - (n - 1) / 2) * Math.min(LANE, (size - 14) / Math.max(1, n));
  // küme modunda kenar noktaları: aynı kenara düşen bağlar karşı ucun y'sine göre sıralanıp başlığın altından itibaren dizilir
  const side = {};
  const otherY = id => id.startsWith("k:") ? boxes[id.slice(2)].y : elems[id].cy;
  routes.forEach(rt => {
    const l = links[rt.i];
    (side[rt.ka + "|" + rt.out] ??= []).push({ i: rt.i, oy: otherY(l.to), end: "a" });
    (side[rt.kb + "|" + rt.in] ??= []).push({ i: rt.i, oy: otherY(l.from), end: "b" });
  });
  const anchor = {};
  for (const [key, arr] of Object.entries(side)) {
    const b = boxes[key.split("|")[0]];
    arr.sort((p, q) => p.oy - q.oy);
    const top = b.y + 22, room = Math.max(10, b.h - 32), step = Math.min(9, room / Math.max(1, arr.length));
    arr.forEach((a, j) => (anchor[a.i] ??= {})[a.end] = top + j * step);
  }
  const paths = routes.map(rt => {
    const l = links[rt.i], elemMode = !l.from.startsWith("k:");
    const ay = elemMode ? elems[l.from].cy : anchor[rt.i].a, by = elemMode ? elems[l.to].cy : anchor[rt.i].b;
    const pts = ktPoints(rt, ay, by, laneOff(vUse[rt.g1].length, rt.l1, P.gV[rt.g1]),
      rt.g2 != null ? laneOff(vUse[rt.g2].length, rt.l2, P.gV[rt.g2]) : 0,
      rt.g2 != null ? laneOff(chUse[rt.ch].length, rt.lc, P.chH[rt.ch]) : 0);
    const dAttr = ktRound(pts);
    const w = (1.4 + 4.5 * Math.log10(1 + l.hits) / Math.log10(1 + max)).toFixed(1);
    const deny = l.engel > 0 && l.engel >= l.hits;
    return `<path class="lk${deny ? " deny" : ""}${l.altyapi ? " infra" : ""}" d="${dAttr}" stroke-width="${w}" marker-end="url(#ktArr${deny ? "D" : ""})" data-f="${esc(l.from)}" data-t="${esc(l.to)}" data-fk="${esc(rt.ka)}" data-tk="${esc(rt.kb)}"/>
      <path class="lkh" d="${dAttr}" data-li="${rt.i}"/>`;
  }).join("");

  const boxSvg = Object.values(boxes).map(b => {
    const h = tagHue(b.k.ad), nVip = b.k.elemanlar.filter(e => e.tur === "vip").length;
    return `<g class="box" data-box="${esc(b.k.ad)}" style="--h:${h}"><rect class="bg" x="${b.x}" y="${b.y}" width="${b.w}" height="${b.h}" rx="10"/>
      <path d="M${b.x} ${b.y + 10} a10 10 0 0 1 10 -10 h${b.w - 20} a10 10 0 0 1 10 10 v22 h${-b.w} z" style="fill:hsl(${h} 60% 50% / .15)"/>
      <text class="bt" x="${b.x + 10}" y="${b.y + 15}">${esc(ktCut(b.k.kume || b.k.ad, 38))}</text>
      <text class="bs" x="${b.x + 10}" y="${b.y + 27}">${b.k.sunucu} sunucu · ${nVip} VIP${b.idle.length ? ` · ${b.idle.length} trafiksiz` : ""}</text>
      ${b.hdrs.map(hd => `<g class="ah"><text x="${b.x + KT.PAD + 2}" y="${hd.y + 11}">${esc(ktCut(hd.t, Math.floor((b.w - 70) / 6.5)))}<tspan dx="5">${hd.n}</tspan></text>
        <line x1="${b.x + KT.PAD + Math.min(b.w - 60, 12 + Math.min(hd.t.length, Math.floor((b.w - 70) / 6.5)) * 6.5 + 20)}" y1="${hd.y + 8}" x2="${b.x + b.w - KT.PAD}" y2="${hd.y + 8}"/></g>`).join("")}
      ${b.tiles.map(t => t.idle
        ? `<g class="el idle" data-idle="${esc(b.k.ad)}"><rect class="tile" x="${t.x}" y="${t.y}" width="${KT.TW}" height="${KT.TH}" rx="7"/>
            <text class="nm" x="${t.cx}" y="${t.y + 14}" text-anchor="middle">+${t.idle.length} trafiksiz</text>
            <text class="ip" x="${t.cx}" y="${t.y + 26}" text-anchor="middle">tıkla: göster</text></g>`
        : `<g class="el${t.e.tur === "vip" ? " vip" : ""}" data-el="${esc(t.e.id)}"><rect class="tile" x="${t.x}" y="${t.y}" width="${KT.TW}" height="${KT.TH}" rx="7"/>
            ${ktIcon(t.e, t.x + 14, t.y + KT.TH / 2, h)}
            <text class="nm" x="${t.x + 27}" y="${t.y + 14}">${esc(ktCut(t.e.ad, 16))}</text>
            <text class="ip" x="${t.x + 27}" y="${t.y + 26}">${esc(t.e.ip)}${t.e.tur === "vip" ? ` · ${t.e.uyeler.length}ü` : ""}</text>
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
      const A = P.cell[fk], B = P.cell[tk], rt = { A, B, ka: fk, kb: tk };
      if (A.c === B.c) { rt.out = "R"; rt.in = "R"; rt.g1 = A.c + 1; }
      else if (B.c === A.c + 1) { rt.out = "R"; rt.in = "L"; rt.g1 = A.c + 1; }
      else if (B.c === A.c - 1) { rt.out = "L"; rt.in = "R"; rt.g1 = A.c; }
      else if (B.c > A.c) { rt.out = "R"; rt.in = "L"; rt.g1 = A.c + 1; rt.g2 = B.c; }
      else { rt.out = "L"; rt.in = "R"; rt.g1 = A.c; rt.g2 = B.c + 1; }
      if (rt.g2 != null) rt.ch = (P.elems[b.from].cy + P.elems[b.to].cy) / 2 < P.H / 2 ? "top" : "bot";
      const pts = ktPoints(rt, P.elems[b.from].cy, P.elems[b.to].cy);
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
    // başlığa tıklama: özet görünümde kutuyu açar / kapatır; ayrıntılıda açılmış trafiksizleri toplar
    t.addEventListener("click", () => {
      if (kt.expanded.has(n)) { kt.expanded.delete(n); ktDraw(); }
    });
  });
  svg.querySelectorAll(".lkh").forEach(p => {
    const l = links[+p.dataset.li];
    p.addEventListener("mousemove", ev => {
      tip.hidden = false; tip.style.left = ev.clientX + 14 + "px"; tip.style.top = ev.clientY + 12 + "px";
      tip.innerHTML = `<b>${esc(l.ad ? l.ad[0] : name(l.from))}</b> → <b>${esc(l.ad ? l.ad[1] : name(l.to))}</b><br>${fmt(l.hits)} bağlantı${l.engel ? ` · <span style="color:#ff9a9a">${fmt(l.engel)} engellendi</span>` : ""}<br>Port: ${esc(l.portlar.slice(0, 12).join(", ") || "—")}${l.n ? `<br>${l.n} eleman bağı (elemanların üstüne gelin)` : ""}`;
      p.previousElementSibling?.classList.add("hl"); svg.classList.add("dim");
    });
    p.addEventListener("mouseleave", () => { tip.hidden = true; clear(); });
  });
}

function ktIcon(e, cx, cy, h) {
  if (e.tur === "vip") {
    return `<g class="ico-vip" style="--h:${h}"><circle cx="${cx}" cy="${cy}" r="10"/>
      <path d="M${cx - 5} ${cy} H${cx - 1} M${cx - 1} ${cy} L${cx + 4} ${cy - 4} M${cx - 1} ${cy} H${cx + 5} M${cx - 1} ${cy} L${cx + 4} ${cy + 4}"/></g>`;
  }
  return `<g class="ico-srv"><rect x="${cx - 7}" y="${cy - 10}" width="14" height="20" rx="2"/>
    <line x1="${cx - 4}" y1="${cy - 5}" x2="${cx + 4}" y2="${cy - 5}"/><line x1="${cx - 4}" y1="${cy - 1}" x2="${cx + 4}" y2="${cy - 1}"/>
    <circle cx="${cx + 3}" cy="${cy + 6}" r="1.5"/></g>`;
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
      <dl class="kv"><dt>Küme</dt><dd><b>${esc(kume)}</b></dd>${el.uygulama ? `<dt>Uygulama (ERT)</dt><dd>${esc(el.uygulama)}</dd>` : ""}<dt>AI rolü</dt><dd>${esc(u.rol || "—")} ${g ? `<span class="eg-sub">(güven ${g}${u.elle ? ", elle atandı" : ""})</span>` : ""}</dd>
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
