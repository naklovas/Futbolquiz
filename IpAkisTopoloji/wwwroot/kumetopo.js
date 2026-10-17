// ============================================================================
// DeltaFlow · Küme topolojisi (İşlev haritası → "Topoloji çiz")
// Kümeler kutu kutu; içlerinde VIP'ler (yük dengeleyici simgesi) ve VIP'e bağlı olmayan sunucular. Elemanlar arası trafik
// (Carbon Black / AppResponse / Firewall, seçili tarih aralığı) eğri oklarla çizilir; kalınlık trafik, kırmızı kesik = engellenen.
// Elemana tıklayınca ekranın ortasında ayrıntı penceresi: VIP üyeleri, bağlar, etiket dışı trafik; üyeye tıklayınca sunucu
// ayrıntısı (AI rolü / gerekçesi, IP düzeyinde akışlar). Veri: /api/islev/topoloji (KumeTopoloji.cs).
// ============================================================================

const kt = { tag: "", data: null, scale: 1, mode: "eleman", infra: false, abort: null, hover: null, pos: null, stack: [] };

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
  .kt-svg .el { cursor:pointer; }
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
  .kt-svg.dim .lk { stroke-opacity:.06; } .kt-svg.dim .el { opacity:.35; }
  .kt-svg.dim .lk.hl { stroke-opacity:.9; } .kt-svg.dim .el.hl { opacity:1; }
  .kt-svg .lkh { fill:none; stroke:transparent; stroke-width:12; cursor:pointer; }
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
  const p = new URLSearchParams({ etiket: tag, start: $("#start").value, end: $("#end").value,
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
      <div class="seg"><button data-ktm="eleman" class="${kt.mode === "eleman" ? "on" : ""}">Eleman bağları</button><button data-ktm="kume" class="${kt.mode === "kume" ? "on" : ""}">Küme bağları</button></div>
      <label><input type="checkbox" id="ktInfra" ${kt.infra ? "checked" : ""}> Altyapı portları (DNS, AD, RDP…)</label>
      <span class="sp"></span>
      <button id="ktZo" title="Uzaklaştır">−</button><button id="ktFit">Sığdır</button><button id="ktZi" title="Yakınlaştır">+</button>
      <button id="ktAgain" class="primary">Yeniden sorgula</button>
    </div>
    <div class="kt-src">${pill("Carbon Black", d.splunk)}${pill("AppResponse", d.appResponse)}${pill("Firewall", d.firewall)}
      <span>${d.sorgulanan} IP sorgulandı${d.sorgulanan < d.toplam ? ` (etikette ${d.toplam} sunucu; üst sınır Islev:TopolojiMaxIp)` : ""} · ${(d.elapsedMs / 1000).toFixed(1)} sn</span></div>
    <div class="kt-wrap" id="ktWrap"></div>
    <div class="legend" style="margin-top:8px"><span>◯ VIP</span><span>▭ sunucu</span><span>— trafik (kalınlık = hacim)</span><span style="color:var(--err)">- - engellenen</span><span>Elemana tıklayın: ayrıntı</span></div>
  </section>`;
  $("#ktBack").addEventListener("click", () => runIslev(kt.tag));
  $("#ktAgain").addEventListener("click", () => ktRun(kt.tag));
  document.querySelectorAll("[data-ktm]").forEach(b => b.addEventListener("click", () => { kt.mode = b.dataset.ktm; ktShell(); }));
  $("#ktInfra").addEventListener("change", e => { kt.infra = e.target.checked; ktDraw(); });
  $("#ktZi").addEventListener("click", () => { kt.scale = Math.min(2.5, kt.scale * 1.2); ktDraw(); });
  $("#ktZo").addEventListener("click", () => { kt.scale = Math.max(0.3, kt.scale / 1.2); ktDraw(); });
  $("#ktFit").addEventListener("click", () => { kt.scale = Math.min(1, ($("#ktWrap").clientWidth - 4) / kt.pos.W); ktDraw(); });
  ktLayout();
  kt.scale = Math.min(1, ($("#ktWrap").clientWidth - 4) / kt.pos.W);
  ktDraw();
}

// Yerleşim: kümeler sütunlara dizilir (katmanlı): bağlantıyı başlatanlar solda, karşılayanlar sağda (TCP yönü).
// Kutunun içinde elemanlar alt alta satırdır; oklar kutu kenarından, elemanın satır hizasından çıkar / girer.
function ktLayout() {
  const d = kt.data, RH = 56, HEAD = 50, PAD = 10, BW = 250, GX = 230, GY = 36;
  const elemKume = {};
  d.kumeler.forEach(k => k.elemanlar.forEach(e => elemKume[e.id] = k.ad));
  // küme düzeyinde yönlü ağırlıklar
  const w = {};
  d.baglar.forEach(b => { const f = elemKume[b.from], t = elemKume[b.to]; if (f && t && f !== t) w[f + "\u0000" + t] = (w[f + "\u0000" + t] || 0) + b.hits; });
  const names = d.kumeler.map(k => k.ad);
  // Döngüleri kırmak için yalnız baskın yön alınır (A→B > B→A ise A→B), sonra en uzun yol katmanı
  const out = Object.fromEntries(names.map(n => [n, []]));
  for (const key in w) { const [f, t] = key.split("\u0000"); if (w[key] > (w[t + "\u0000" + f] || 0) || (w[key] === w[t + "\u0000" + f] && f < t)) out[f].push(t); }
  const layer = Object.fromEntries(names.map(n => [n, 0]));
  const state = {};
  const visit = n => { if (state[n] === 2) return; if (state[n] === 1) return; state[n] = 1; for (const t of out[n]) { visit(t); } state[n] = 2; };
  names.forEach(visit);
  // Kahn benzeri gevşetme (döngü kalırsa sınırlı tur)
  for (let it = 0; it < names.length; it++) {
    let changed = false;
    for (const f of names) for (const t of out[f]) if (layer[t] < layer[f] + 1 && layer[f] + 1 < names.length) { layer[t] = layer[f] + 1; changed = true; }
    if (!changed) break;
  }
  const maxL = Math.max(0, ...Object.values(layer));
  const cols = Array.from({ length: maxL + 1 }, () => []);
  d.kumeler.forEach(k => cols[layer[k.ad]].push(k));
  // Sütun içinde: önceki sütundaki bağlı kümelerin ortalama sırasına göre (kesişmeleri azaltır)
  const rank = {};
  cols.forEach((col, ci) => {
    if (ci > 0) col.sort((a, b) => bary(a) - bary(b));
    col.forEach((k, i) => rank[k.ad] = i);
    function bary(k) {
      const ns = names.filter(n => layer[n] < ci && (w[n + "\u0000" + k.ad] || w[k.ad + "\u0000" + n]));
      return ns.length ? ns.reduce((s, n) => s + rank[n], 0) / ns.length : 99;
    }
  });
  // Birden çok sütun atlayan oklar kutuların üstündeki / altındaki kanaldan geçer: kanal yüksekliği ayrılır
  const nLong = d.baglar.filter(b => elemKume[b.from] && elemKume[b.to] && Math.abs(layer[elemKume[b.from]] - layer[elemKume[b.to]]) > 1).length;
  const CH = nLong ? 14 + Math.min(nLong, 24) * 5 : 0;
  const boxes = {}, elems = {};
  let H = 0;
  cols.forEach((col, ci) => {
    let y = 24 + CH;
    const x = 24 + ci * (BW + GX);
    for (const k of col) {
      const h = HEAD + k.elemanlar.length * RH + PAD;
      boxes[k.ad] = { x, y, w: BW, h, k, col: ci };
      k.elemanlar.forEach((e, j) => elems[e.id] = { x, y: y + HEAD + j * RH, cx: x + 30, cy: y + HEAD + j * RH + RH / 2, e, k: k.ad, col: ci });
      y += h + GY;
    }
    H = Math.max(H, y - GY + 24);
  });
  H += CH;
  kt.pos = { boxes, elems, W: Math.max(24 + cols.length * (BW + GX) - GX + 24, 600), H, elemKume, BW, CH };
}

function ktLinks() {
  const { elems, elemKume } = kt.pos;
  let links = kt.data.baglar.filter(b => elems[b.from] && elems[b.to] && (kt.infra || !b.altyapi));
  if (kt.mode === "kume") {
    const agg = {};
    for (const b of links) {
      const f = elemKume[b.from], t = elemKume[b.to];
      if (f === t) continue;
      const a = agg[f + "→" + t] ??= { from: "k:" + f, to: "k:" + t, hits: 0, engel: 0, portlar: new Set(), n: 0 };
      a.hits += b.hits; a.engel += b.engel; a.n++; b.portlar.forEach(p => a.portlar.add(p));
    }
    links = Object.values(agg).map(a => ({ ...a, portlar: [...a.portlar] }));
  }
  return links;
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

function ktDraw() {
  const { boxes, elems, W, H } = kt.pos;
  const links = ktLinks();
  const max = Math.max(1, ...links.map(l => l.hits));
  const BW = kt.pos.BW;
  // Uç noktası: elemanın satır hizasında kutunun sağ / sol kenarı; küme bağında kutunun ortası
  const pt = (id, side) => {
    if (id.startsWith("k:")) { const bx = boxes[id.slice(2)]; return { x: side === "r" ? bx.x + bx.w : bx.x, y: bx.y + Math.min(bx.h / 2, 60), col: bx.col }; }
    const p = elems[id]; return { x: side === "r" ? p.x + BW : p.x, y: p.cy, col: p.col };
  };
  const colOf = id => id.startsWith("k:") ? boxes[id.slice(2)].col : elems[id].col;
  // Aynı kenar noktasından çıkan birden çok ok hafifçe aralansın
  const used = {};
  const spread = (key) => { used[key] = (used[key] || 0) + 1; return (used[key] - 1) % 5 * 3 - 6; };
  const lanes = { top: 0, bot: 0 };
  const paths = links.map((l, i) => {
    const cf = colOf(l.from), ct = colOf(l.to);
    let x1, y1, x2, y2, dAttr;
    if (Math.abs(ct - cf) > 1) {   // uzun ok: sütun boşluğundan kanala çık, kanalda ilerle, hedef sütun boşluğundan in
      const fwd = ct > cf, a = pt(l.from, fwd ? "r" : "l"), b = pt(l.to, fwd ? "l" : "r");
      x1 = a.x; y1 = a.y + spread(l.from + (fwd ? "r" : "l")); x2 = b.x + (fwd ? -2 : 2); y2 = b.y + spread(l.to + (fwd ? "l" : "r"));
      const top = (y1 + y2) / 2 < kt.pos.H / 2, lane = top ? lanes.top++ : lanes.bot++;
      const cy = top ? 10 + (lane % 24) * 5 : kt.pos.H - 10 - (lane % 24) * 5;
      const s1 = fwd ? 1 : -1, g = 50 + (lane % 6) * 8;
      dAttr = `M${x1},${y1} C${x1 + s1 * g},${y1} ${x1 + s1 * g},${cy} ${x1 + s1 * (g + 50)},${cy} L${x2 - s1 * (g + 50)},${cy} C${x2 - s1 * g},${cy} ${x2 - s1 * g},${y2} ${x2},${y2}`;
    } else if (ct > cf) {          // sağa: kaynağın sağ kenarı → hedefin sol kenarı
      const a = pt(l.from, "r"), b = pt(l.to, "l");
      x1 = a.x; y1 = a.y + spread(l.from + "r"); x2 = b.x - 2; y2 = b.y + spread(l.to + "l");
      const m = (x2 - x1) * 0.5;
      dAttr = `M${x1},${y1} C${x1 + m},${y1} ${x2 - m},${y2} ${x2},${y2}`;
    } else if (ct < cf) {   // sola (geri yön): kaynağın sol kenarı → hedefin sağ kenarı
      const a = pt(l.from, "l"), b = pt(l.to, "r");
      x1 = a.x; y1 = a.y + spread(l.from + "l"); x2 = b.x + 2; y2 = b.y + spread(l.to + "r");
      const m = (x1 - x2) * 0.5;
      dAttr = `M${x1},${y1} C${x1 - m},${y1} ${x2 + m},${y2} ${x2},${y2}`;
    } else {                // aynı sütun: sağ kenardan çıkıp sağ kenara döner
      const a = pt(l.from, "r"), b = pt(l.to, "r");
      x1 = a.x; y1 = a.y + spread(l.from + "r"); x2 = b.x + 2; y2 = b.y + spread(l.to + "r");
      const bulge = 40 + Math.min(120, Math.abs(y2 - y1) * 0.35);
      dAttr = `M${x1},${y1} C${x1 + bulge},${y1} ${x2 + bulge},${y2} ${x2},${y2}`;
    }
    const w = (1.2 + 4 * Math.log10(1 + l.hits) / Math.log10(1 + max)).toFixed(1);
    const cls = ["lk", l.engel > 0 && l.engel >= l.hits ? "deny" : "", l.altyapi ? "infra" : "",
      !l.from.startsWith("k:") && kt.pos.elemKume[l.from] === kt.pos.elemKume[l.to] ? "intra" : ""].join(" ");
    return `<path class="${cls}" d="${dAttr}" stroke-width="${w}" marker-end="url(#ktArr${l.engel > 0 && l.engel >= l.hits ? "D" : ""})" data-f="${esc(l.from)}" data-t="${esc(l.to)}"/>
      <path class="lkh" d="${dAttr}" data-li="${i}"/>`;
  }).join("");
  const boxSvg = Object.values(boxes).map(b => {
    const h = tagHue(b.k.ad);
    return `<g class="box" style="--h:${h}"><rect class="bg" x="${b.x}" y="${b.y}" width="${b.w}" height="${b.h}" rx="10"/>
      <path d="M${b.x} ${b.y + 10} a10 10 0 0 1 10 -10 h${b.w - 20} a10 10 0 0 1 10 10 v30 h${-b.w} z" style="fill:hsl(${h} 60% 50% / .14)"/>
      <text class="bt" x="${b.x + 12}" y="${b.y + 19}">${esc(ktCut(b.k.ad, 27))}</text>
      <text class="bs" x="${b.x + 12}" y="${b.y + 34}">${b.k.sunucu} sunucu · ${b.k.elemanlar.filter(e => e.tur === "vip").length} VIP</text>
      ${b.k.elemanlar.map(e => {
        const p = elems[e.id];
        return `<g class="el" data-el="${esc(e.id)}"><rect class="hit" x="${p.x + 4}" y="${p.y + 3}" width="${b.w - 8}" height="50" rx="7"/>
          ${ktIcon(e, p.cx, p.cy, h)}
          <text class="nm" x="${p.x + 56}" y="${p.cy - 3}">${esc(ktCut(e.ad, 24))}</text>
          <text class="ip" x="${p.x + 56}" y="${p.cy + 12}">${esc(e.ip + (e.port ? ":" + e.port : ""))}${e.tur === "vip" ? ` · ${e.uyeler.length} üye` : ""}</text>
          <title>${esc(e.tur === "vip" ? `VIP ${e.ip}:${e.port || ""} · ${e.ad} · ${e.uyeler.length} üye` : `${e.ad} · ${e.alt || ""}`)}</title></g>`;
      }).join("")}</g>`;
  }).join("");
  const s = kt.scale;
  $("#ktWrap").innerHTML = `<svg class="kt-svg" id="ktSvg" viewBox="0 0 ${W} ${kt.pos.H}" width="${(W * s).toFixed(0)}" height="${(kt.pos.H * s).toFixed(0)}" xmlns="http://www.w3.org/2000/svg">
    <defs>
      <marker id="ktArr" viewBox="0 0 10 10" refX="9" refY="5" markerUnits="userSpaceOnUse" markerWidth="10" markerHeight="10" orient="auto"><path d="M0,0 L10,5 L0,10 z" fill="var(--in)" fill-opacity=".8"/></marker>
      <marker id="ktArrD" viewBox="0 0 10 10" refX="9" refY="5" markerUnits="userSpaceOnUse" markerWidth="10" markerHeight="10" orient="auto"><path d="M0,0 L10,5 L0,10 z" fill="var(--err)"/></marker>
    </defs>
    ${boxSvg}<g>${paths}</g></svg>`;
  ktWire(links);
}

function ktWire(links) {
  const svg = $("#ktSvg");
  const tip = document.querySelector(".kt-tip") || document.body.appendChild(Object.assign(document.createElement("div"), { className: "kt-tip", hidden: true }));
  const name = id => id.startsWith("k:") ? id.slice(2) : kt.pos.elems[id]?.e.ad || id;
  const light = id => {
    svg.classList.toggle("dim", !!id);
    svg.querySelectorAll(".hl").forEach(x => x.classList.remove("hl"));
    if (!id) return;
    svg.querySelectorAll(`.lk`).forEach(p => {
      if (p.dataset.f === id || p.dataset.t === id) {
        p.classList.add("hl");
        svg.querySelector(`.el[data-el="${CSS.escape(p.dataset.f === id ? p.dataset.t : p.dataset.f)}"]`)?.classList.add("hl");
      }
    });
    svg.querySelector(`.el[data-el="${CSS.escape(id)}"]`)?.classList.add("hl");
  };
  svg.querySelectorAll(".el").forEach(g => {
    g.addEventListener("mouseenter", () => light(g.dataset.el));
    g.addEventListener("mouseleave", () => light(null));
    g.addEventListener("click", () => ktOpen(g.dataset.el));
  });
  svg.querySelectorAll(".lkh").forEach(p => {
    const l = links[+p.dataset.li];
    p.addEventListener("mousemove", ev => {
      tip.hidden = false; tip.style.left = ev.clientX + 14 + "px"; tip.style.top = ev.clientY + 12 + "px";
      tip.innerHTML = `<b>${esc(name(l.from))}</b> → <b>${esc(name(l.to))}</b><br>${fmt(l.hits)} bağlantı${l.engel ? ` · <span style="color:#ff9a9a">${fmt(l.engel)} engellendi</span>` : ""}<br>Port: ${esc(l.portlar.slice(0, 12).join(", ") || "—")}${l.n ? `<br>${l.n} eleman bağı` : ""}`;
    });
    p.addEventListener("mouseleave", () => tip.hidden = true);
  });
}

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

function ktElemName(id) { const p = kt.pos.elems[id]; return p ? `${p.e.tur === "vip" ? "VIP " : ""}${p.e.ad}` : id; }
function ktHead(title, sub, back) {
  return `<div class="mh"><div>${back ? `<a href="#" data-ktback style="font-size:12px">← geri</a>` : ""}<h3>${title}</h3><div class="eg-sub">${sub}</div></div>
    <button data-ktclose>Kapat</button></div>`;
}

function ktOpen(id) {
  const p = kt.pos.elems[id];
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
