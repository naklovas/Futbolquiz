// ============================================================================
// DeltaFlow · Sunucu etiketleme
// ERT_HOSTIPADDRESS tablosunun TAG kolonu: kolon bazında filtre + sıralama + sayfalama ile sunucu listesi,
// seçilenlere toplu etiket ekle / kaldır, satırda + Etiket, etiketteki × ile kaldır. IP kartındaki "+ Etiket"
// de buradan yazar. topoloji.html'deki ortak yardımcıları ($, esc, fmt, tagMap, ipTabUrl) kullanır.
// Alt etiket "ANA/ALT" (ör. MOBILBANKACILIK/MOBIL APP): ana etiket filtresi altlarını da getirir. "Etiket açıklamaları"
// paneli ana ve alt etiketlere açıklama yazar (/api/etiket/aciklama; AI sınıflandırma prompt'una da girer).
// ============================================================================

const etq = {
  f: { ip: "", sunucu: "", uygulama: "", muhafiz: "", segment: "", etiket: "" },
  sort: "ip", desc: false, page: 1, size: 100,
  rows: [], total: 0, tum: 0, etiketli: 0, tags: [], canWrite: false,
  selected: new Set(), shell: false, timer: null, seq: 0, descOpen: false, descWarn: null
};

const ETQ_COLS = [
  { key: "ip", label: "IP adresi", ph: "10.1." },
  { key: "sunucu", label: "Sunucu adı", ph: "ara…" },
  { key: "uygulama", label: "Uygulamalar", ph: "ara…" },
  { key: "muhafiz", label: "Varlık muhafızı", ph: "ara…" },
  { key: "segment", label: "Segment", ph: "ara…" },
  { key: "etiket", label: "Etiketler" }
];

(function etqStyle() {
  const css = `
  .eg-head { display:flex; justify-content:space-between; align-items:flex-end; gap:16px; flex-wrap:wrap; margin-bottom:14px; }
  .eg-head h2 { margin:0; font-size:20px; }
  .eg-sub { color:var(--muted); font-size:12.5px; margin-top:4px; }
  .eg-stats { display:flex; gap:10px; flex-wrap:wrap; }
  .eg-stat { border:1px solid var(--line); border-radius:10px; padding:8px 14px; min-width:110px; background:var(--bg); }
  .eg-stat b { display:block; font-size:20px; font-variant-numeric:tabular-nums; }
  .eg-stat span { color:var(--muted); font-size:11.5px; text-transform:uppercase; letter-spacing:.04em; }
  .eg-pills { display:flex; gap:8px; flex-wrap:wrap; align-items:center; margin:4px 0 12px; }
  .eg-pills .lbl { color:var(--muted); font-size:12px; margin-right:2px; }
  .eg-pill { border:1px solid var(--line); background:var(--panel); color:var(--text); border-radius:999px; padding:4px 12px; font-size:12.5px; cursor:pointer; }
  .eg-pill.on { background:var(--text); color:var(--panel); border-color:var(--text); }
  .eg-pill i { font-style:normal; opacity:.65; margin-left:4px; font-variant-numeric:tabular-nums; }
  .eg-grid { border:1px solid var(--line); border-radius:10px; overflow:auto; max-height:calc(100vh - 330px); }
  .eg-grid table { font-size:13px; }
  .eg-grid thead th { background:var(--bg); color:var(--muted); font-size:11.5px; text-transform:uppercase; letter-spacing:.04em; padding:10px 10px 6px; border-bottom:none; user-select:none; }
  .eg-grid thead th .arr { opacity:.35; margin-left:4px; }
  .eg-grid thead th.sorted { color:var(--text); }
  .eg-grid thead th.sorted .arr { opacity:1; color:var(--in); }
  .eg-grid thead tr.flt th { top:34px; padding:0 8px 8px; cursor:default; border-bottom:1px solid var(--line); text-transform:none; letter-spacing:0; }
  .eg-grid thead tr.flt input, .eg-grid thead tr.flt select { width:100%; box-sizing:border-box; font:inherit; font-size:12.5px; padding:5px 8px;
    border:1px solid var(--line); border-radius:6px; background:var(--panel); color:var(--text); }
  .eg-grid thead tr.flt input.has, .eg-grid thead tr.flt select.has { border-color:var(--in); box-shadow:0 0 0 2px var(--in-soft); }
  .eg-grid tbody td { padding:9px 10px; vertical-align:middle; }
  .eg-grid tbody tr:nth-child(even) td { background:color-mix(in srgb, var(--bg) 55%, var(--panel)); }
  .eg-grid tbody tr:hover td { background:var(--in-soft); }
  .eg-grid tbody tr.sel td { background:color-mix(in srgb, var(--in-soft) 75%, var(--panel)); }
  .eg-grid td.ip a { font-family:Consolas, ui-monospace, monospace; font-weight:600; color:var(--text); text-decoration:none; }
  .eg-grid td.ip a:hover { color:var(--in); text-decoration:underline; }
  .eg-grid td.app { max-width:280px; }
  .eg-grid td.app span { display:inline-block; background:var(--bg); border:1px solid var(--line); border-radius:4px; padding:0 6px; margin:1px 3px 1px 0; font-size:12px; }
  .eg-grid td.dim { color:var(--muted); }
  .eg-grid .cb { width:34px; text-align:center; }
  .eg-tag { --h:210; display:inline-flex; align-items:center; gap:4px; border-radius:999px; padding:2px 9px; margin:1px 4px 1px 0; font-size:11.5px; font-weight:600;
    letter-spacing:.02em; background:hsl(var(--h) 65% 50% / .13); color:hsl(var(--h) 60% 34%); border:1px solid hsl(var(--h) 60% 50% / .35); }
  .eg-tag a { color:inherit; text-decoration:none; opacity:.55; font-weight:700; }
  .eg-tag a:hover { opacity:1; }
  :root[data-theme="dark"] .eg-tag { color:hsl(var(--h) 75% 74%); }
  @media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) .eg-tag { color:hsl(var(--h) 75% 74%); } }
  .eg-add { border:1px dashed var(--line); background:transparent; color:var(--muted); border-radius:999px; padding:1px 9px; font-size:11.5px; cursor:pointer; }
  .eg-add:hover { color:var(--in); border-color:var(--in); }
  .eg-foot { display:flex; justify-content:space-between; align-items:center; gap:12px; margin-top:10px; color:var(--muted); font-size:12.5px; flex-wrap:wrap; }
  .eg-pager { display:flex; gap:4px; align-items:center; }
  .eg-pager button { border:1px solid var(--line); background:var(--panel); color:var(--text); border-radius:6px; padding:3px 10px; cursor:pointer; }
  .eg-pager button:disabled { opacity:.4; cursor:default; }
  .eg-bar { margin:0 0 12px; display:flex; align-items:center; gap:10px; flex-wrap:wrap; background:var(--text); color:var(--panel);
    border-radius:12px; padding:10px 14px; box-shadow:0 8px 24px rgba(0,0,0,.18); }
  .eg-bar b { font-variant-numeric:tabular-nums; }
  .eg-bar input { font:inherit; padding:6px 10px; border-radius:6px; border:none; width:220px; }
  .eg-bar button { font:inherit; border:none; border-radius:6px; padding:6px 12px; cursor:pointer; }
  .eg-bar .add { background:var(--in); color:#fff; }
  .eg-bar .del { background:transparent; color:var(--panel); border:1px solid color-mix(in srgb, var(--panel) 40%, transparent); }
  .eg-bar .clr { background:transparent; color:var(--panel); opacity:.7; margin-left:auto; }
  .eg-empty { padding:40px; text-align:center; color:var(--muted); }
  .eg-tag .sub { font-weight:500; margin-left:3px; opacity:.85; }
  .eg-pills.subs { margin-top:-6px; padding-left:14px; border-left:2px solid var(--line); }
  .eg-pills.subs .lbl { font-size:11.5px; }
  .eg-bar input.subin { width:170px; }
  .eg-desc { border:1px solid var(--line); border-radius:10px; background:var(--bg); padding:12px 14px; margin-bottom:12px; display:grid; gap:8px; }
  .eg-desc h3 { margin:0; font-size:14px; display:flex; justify-content:space-between; gap:10px; align-items:baseline; }
  .eg-desc .row { display:grid; grid-template-columns:minmax(180px, 260px) 70px 1fr auto; gap:10px; align-items:start; }
  .eg-desc .row.sub { padding-left:22px; }
  .eg-desc .row.sub > :first-child::before { content:"└ "; color:var(--muted); }
  .eg-desc .row span.n { color:var(--muted); font-size:12px; padding-top:5px; font-variant-numeric:tabular-nums; }
  .eg-desc textarea { width:100%; box-sizing:border-box; font:inherit; font-size:12.5px; padding:5px 8px; border:1px solid var(--line); border-radius:6px;
    background:var(--panel); color:var(--text); resize:vertical; min-height:30px; }
  .eg-desc .txt { font-size:12.5px; padding-top:4px; color:var(--text); white-space:pre-wrap; }
  .eg-desc .ok { color:#2e9e6a; font-size:12px; }
  @media (max-width:800px) { .eg-desc .row { grid-template-columns:1fr; } }`;
  const st = document.createElement("style"); st.textContent = css; document.head.appendChild(st);
})();

// Etiket adına göre sabit renk tonu (alt etiket ana etiketin rengini alır)
function tagHue(t) { let h = 0; for (const c of String(t).split("/")[0]) h = (h * 31 + c.charCodeAt(0)) % 360; return h; }
// "ANA/ALT" → ANA › ALT
function tagLabel(t) { const [a, b] = String(t).split("/"); return b ? `${esc(a)}<span class="sub">› ${esc(b)}</span>` : esc(a); }
function tagDesc(t) { return (etq.tags.find(x => x.ad === t) || (globalThis.topoTagList || []).find(x => x.ad === t))?.aciklama || ""; }
function tagChip(t, ip, removable) {
  const d = tagDesc(t);
  return `<span class="eg-tag" style="--h:${tagHue(t)}"${d ? ` title="${esc(d)}"` : ""}>${tagLabel(t)}${removable ? `<a href="#" data-untag="${esc(t)}" data-ip="${esc(ip)}" title="Etiketi kaldır">×</a>` : ""}</span>`;
}
const tagParent = t => String(t).split("/")[0];

function runEtiket() {
  setUiMode("etiket");
  history.replaceState(null, "", "?etiket=");
  etq.shell = false;
  etqLoad();
}

async function etqLoad() {
  const my = ++etq.seq;
  const p = new URLSearchParams({ sirala: etq.sort, yon: etq.desc ? "desc" : "asc", sayfa: etq.page, boyut: etq.size });
  for (const [k, v] of Object.entries(etq.f)) if (v) p.set(k, v);
  if (!etq.shell) $("#main").innerHTML = `<div class="panel empty"><span class="spinner"></span> Sunucular yükleniyor…</div>`;
  try {
    const r = await fetch("api/etiket/sunucular?" + p);
    const b = await r.json().catch(() => ({ error: `HTTP ${r.status}` }));
    if (!r.ok) throw new Error(b.error || `HTTP ${r.status}`);
    if (my !== etq.seq) return;   // daha yeni bir istek var
    Object.assign(etq, { rows: b.sunucular, total: b.toplam, tum: b.tum, etiketli: b.etiketli, tags: b.etiketler, canWrite: b.yazabilir, descWarn: b.aciklamaUyari });
    if (!etq.shell) etqShell(); else etqUpdate();
  } catch (e) {
    if (my === etq.seq) $("#main").innerHTML = `<div class="panel empty" style="color:var(--err)">${esc(e.message)}</div>`;
  }
}

// Sayfa iskeleti bir kez çizilir (filtre kutuları odağını kaybetmesin); sonra yalnız gövde, sayaçlar ve haplar güncellenir.
function etqShell() {
  const w = etq.canWrite;
  $("#main").innerHTML = `
  <section class="panel">
    <div class="eg-head">
      <div><h2>Sunucu etiketleme</h2>
        <div class="eg-sub">Envanter (ERT_HOSTIPADDRESS) sunucuları · etiketler TAG kolonuna yazılır${w ? "" : " · <b>yalnız görüntüleme yetkiniz var</b>"}</div></div>
      <div style="display:flex;gap:10px;align-items:flex-end;flex-wrap:wrap"><button type="button" id="egDescBtn" title="Ana ve alt etiketlerin ne olduğunu yazın; AI sınıflandırması da bu açıklamaları kullanır">Etiket açıklamaları</button>
        <div class="eg-stats" id="egStats"></div></div>
    </div>
    <div id="egDesc"></div>
    <div class="eg-pills" id="egPills"></div>
    <div class="eg-pills subs" id="egSubs" hidden></div>
    ${w ? `<div class="eg-bar" id="egBar" hidden>
      <b id="egSelN"></b>
      <input type="text" id="egNew" list="egTagList" placeholder="Etiket (ör. MOBILBANKACILIK)">
      <datalist id="egTagList"></datalist>
      <input type="text" id="egSub" class="subin" list="egSubList" placeholder="Alt etiket (isteğe bağlı)">
      <datalist id="egSubList"></datalist>
      <button type="button" class="add" id="egAdd">Etiket ekle</button>
      <button type="button" class="del" id="egDel">Etiketi kaldır</button>
      <span id="egMsg"></span>
      <button type="button" class="clr" id="egClr">Seçimi temizle</button>
    </div>` : ""}
    <div class="eg-grid"><table>
      <thead>
        <tr>${w ? `<th class="cb"><input type="checkbox" id="egAll" title="Bu sayfadakilerin hepsini seç"></th>` : ""}
          ${ETQ_COLS.map(c => `<th data-sort="${c.key}">${c.label}<span class="arr"></span></th>`).join("")}
          ${w ? "<th></th>" : ""}</tr>
        <tr class="flt">${w ? "<th></th>" : ""}
          ${ETQ_COLS.map(c => c.key === "etiket"
            ? `<th><select data-f="etiket"></select></th>`
            : `<th><input type="search" data-f="${c.key}" placeholder="${c.ph}" value="${esc(etq.f[c.key])}"></th>`).join("")}
          ${w ? "<th></th>" : ""}</tr>
      </thead>
      <tbody id="egBody"></tbody>
    </table></div>
    <div class="eg-foot"><span id="egCount"></span>
      <div class="eg-pager"><label>Sayfa başına <select id="egSize">${[50, 100, 200, 500].map(n => `<option${n === etq.size ? " selected" : ""}>${n}</option>`).join("")}</select></label>
        <button type="button" id="egPrev">‹ Önceki</button><span id="egPage"></span><button type="button" id="egNext">Sonraki ›</button></div></div>
  </section>`;
  etq.shell = true;

  document.querySelectorAll(".eg-grid [data-sort]").forEach(th => th.addEventListener("click", () => {
    if (etq.sort === th.dataset.sort) etq.desc = !etq.desc; else { etq.sort = th.dataset.sort; etq.desc = false; }
    etq.page = 1; etqLoad();
  }));
  document.querySelectorAll(".eg-grid [data-f]").forEach(el => el.addEventListener(el.tagName === "SELECT" ? "change" : "input", () => {
    etq.f[el.dataset.f] = el.value.trim();
    el.classList.toggle("has", !!el.value);
    clearTimeout(etq.timer);
    etq.timer = setTimeout(() => { etq.page = 1; etqLoad(); }, el.tagName === "SELECT" ? 0 : 350);
  }));
  $("#egSize").addEventListener("change", ev => { etq.size = +ev.target.value; etq.page = 1; etqLoad(); });
  $("#egPrev").addEventListener("click", () => { if (etq.page > 1) { etq.page--; etqLoad(); } });
  $("#egNext").addEventListener("click", () => { if (etq.page * etq.size < etq.total) { etq.page++; etqLoad(); } });
  if (w) {
    $("#egAll").addEventListener("change", ev => { etq.rows.forEach(r => ev.target.checked ? etq.selected.add(r.ip) : etq.selected.delete(r.ip)); etqUpdate(); });
    // Etiket + alt etiket → "ANA/ALT"
    const full = () => { const a = $("#egNew").value.trim(), b = $("#egSub").value.trim(); return a && b ? `${a}/${b}` : a; };
    $("#egAdd").addEventListener("click", () => applyTag([...etq.selected], full(), null));
    $("#egDel").addEventListener("click", () => applyTag([...etq.selected], null, full()));
    $("#egNew").addEventListener("input", egSubList);
    $("#egSub").addEventListener("keydown", ev => { if (ev.key === "Enter") { ev.preventDefault(); $("#egAdd").click(); } });
    $("#egClr").addEventListener("click", () => { etq.selected.clear(); etqUpdate(); });
    $("#egNew").addEventListener("keydown", ev => { if (ev.key === "Enter") { ev.preventDefault(); $("#egAdd").click(); } });
  }
  $("#egDescBtn").addEventListener("click", () => { etq.descOpen = !etq.descOpen; egDescRender(); });
  $("#egBody").addEventListener("change", ev => {
    const cb = ev.target.closest("[data-sel]");
    if (!cb) return;
    cb.checked ? etq.selected.add(cb.dataset.sel) : etq.selected.delete(cb.dataset.sel);
    etqUpdate();
  });
  etqUpdate();
}

function etqUpdate() {
  const w = etq.canWrite, f = etq.f;
  $("#egStats").innerHTML = [["Envanterde", etq.tum], ["Etiketli", etq.etiketli], ["Etiketsiz", etq.tum - etq.etiketli], ["Filtre sonucu", etq.total]]
    .map(([l, n]) => `<div class="eg-stat"><b>${fmt(n)}</b><span>${l}</span></div>`).join("");
  // Haplar: ana etiketler; bir ana etiket (ya da altı) seçiliyse altında alt etiket hapları
  const pill = (t, label) => `<button type="button" class="eg-pill${f.etiket === t.ad ? " on" : ""}" data-pill="${esc(t.ad)}"${t.aciklama ? ` title="${esc(t.aciklama)}"` : ""}><span class="eg-tag" style="--h:${tagHue(t.ad)};margin:0">${label}</span><i>${fmt(t.sunucu)}</i></button>`;
  $("#egPills").innerHTML = `<span class="lbl">Hızlı filtre:</span>`
    + [["", "Tümü", etq.tum], ["*", "Etiketli", etq.etiketli], ["-", "Etiketsiz", etq.tum - etq.etiketli]]
      .map(([v, l, n]) => `<button type="button" class="eg-pill${f.etiket === v ? " on" : ""}" data-pill="${v}">${l}<i>${fmt(n)}</i></button>`).join("")
    + etq.tags.filter(t => !t.ad.includes("/")).map(t => pill(t, esc(t.ad) + (etq.tags.some(x => tagParent(x.ad) === t.ad && x.ad !== t.ad) ? ` <span class="sub">▾</span>` : ""))).join("");
  const root = f.etiket && !["*", "-"].includes(f.etiket) ? tagParent(f.etiket) : null;
  const subs = root ? etq.tags.filter(t => t.ad.startsWith(root + "/")) : [];
  $("#egSubs").hidden = !subs.length;
  $("#egSubs").innerHTML = subs.length ? `<span class="lbl">${esc(root)} alt etiketleri:</span>` + subs.map(t => pill(t, esc(t.ad.slice(root.length + 1)))).join("") : "";
  document.querySelectorAll("#egPills [data-pill], #egSubs [data-pill]").forEach(b => b.addEventListener("click", () => {
    f.etiket = b.dataset.pill; etq.page = 1; etqLoad();
  }));
  const sel = document.querySelector('.eg-grid select[data-f="etiket"]');
  sel.innerHTML = `<option value="">tümü</option><option value="*">etiketli</option><option value="-">etiketsiz</option>`
    + etq.tags.map(t => `<option value="${esc(t.ad)}">${t.ad.includes("/") ? "&nbsp;&nbsp;└ " + esc(t.ad.split("/")[1]) : esc(t.ad)}</option>`).join("");
  sel.value = f.etiket; sel.classList.toggle("has", !!f.etiket);
  document.querySelectorAll(".eg-grid [data-sort]").forEach(th => {
    const on = th.dataset.sort === etq.sort;
    th.classList.toggle("sorted", on);
    th.querySelector(".arr").textContent = on ? (etq.desc ? "▼" : "▲") : "↕";
  });

  $("#egBody").innerHTML = etq.rows.length ? etq.rows.map(r => `<tr class="${etq.selected.has(r.ip) ? "sel" : ""}">
      ${w ? `<td class="cb"><input type="checkbox" data-sel="${esc(r.ip)}"${etq.selected.has(r.ip) ? " checked" : ""}></td>` : ""}
      <td class="ip"><a href="${esc(ipTabUrl(r.ip))}" target="_blank" rel="noopener" title="Topolojisini yeni sekmede aç">${esc(r.ip)}</a></td>
      <td>${esc(r.hostname ?? "")}</td>
      <td class="app">${r.apps.map(a => `<span>${esc(a)}</span>`).join("") || `<span class="dim">—</span>`}</td>
      <td class="${r.muhafiz ? "" : "dim"}">${esc(r.muhafiz ?? "—")}</td>
      <td class="${r.segment ? "" : "dim"}">${esc(r.segment ?? "—")}</td>
      <td>${r.tags.map(t => tagChip(t, r.ip, w)).join("") || `<span class="dim">—</span>`}</td>
      ${w ? `<td style="text-align:right"><button type="button" class="eg-add" data-tagip="${esc(r.ip)}">+ Etiket</button></td>` : ""}
    </tr>`).join("")
    : `<tr><td colspan="${ETQ_COLS.length + (w ? 2 : 0)}" class="eg-empty">Filtreye uyan sunucu yok.</td></tr>`;

  const from = etq.total ? (etq.page - 1) * etq.size + 1 : 0, to = Math.min(etq.page * etq.size, etq.total);
  $("#egCount").textContent = `${fmt(from)}–${fmt(to)} / ${fmt(etq.total)} sunucu`;
  $("#egPage").textContent = ` ${etq.page} / ${Math.max(1, Math.ceil(etq.total / etq.size))} `;
  $("#egPrev").disabled = etq.page <= 1; $("#egNext").disabled = etq.page * etq.size >= etq.total;
  if (w) {
    const allOn = etq.rows.length > 0 && etq.rows.every(r => etq.selected.has(r.ip));
    $("#egAll").checked = allOn;
    $("#egAll").indeterminate = !allOn && etq.rows.some(r => etq.selected.has(r.ip));
    $("#egBar").hidden = etq.selected.size === 0;
    $("#egSelN").textContent = `${fmt(etq.selected.size)} sunucu seçili`;
    $("#egTagList").innerHTML = etq.tags.filter(t => !t.ad.includes("/")).map(t => `<option value="${esc(t.ad)}">`).join("");
    egSubList();
  }
  if (etq.descOpen) egDescRender();
}

// Alt etiket önerileri: yazılan ana etiketin mevcut altları
function egSubList() {
  const a = ($("#egNew")?.value || "").trim().toLocaleUpperCase("tr");
  if (!$("#egSubList")) return;
  $("#egSubList").innerHTML = etq.tags.filter(t => a && t.ad.startsWith(a + "/")).map(t => `<option value="${esc(t.ad.slice(a.length + 1))}">`).join("");
}

// Etiket açıklamaları paneli: ana etiketler ve altları; yazma yetkisi varsa düzenlenir
function egDescRender() {
  const box = $("#egDesc");
  if (!box) return;
  $("#egDescBtn")?.classList.toggle("on", etq.descOpen);
  if (!etq.descOpen) { box.innerHTML = ""; return; }
  const w = etq.canWrite && !etq.descWarn;
  const tags = etq.tags;
  box.innerHTML = `<div class="eg-desc">
    <h3>Etiket açıklamaları <span class="eg-sub">Alt etiket eklemek için seçim çubuğunda "Alt etiket" kutusunu doldurun (ANA/ALT olarak yazılır)</span></h3>
    ${etq.descWarn ? `<div class="iv-warn" style="margin:0">${esc(etq.descWarn)}</div>` : ""}
    ${tags.length ? tags.map(t => `<div class="row${t.ad.includes("/") ? " sub" : ""}">
      <div><span class="eg-tag" style="--h:${tagHue(t.ad)}">${t.ad.includes("/") ? esc(t.ad.split("/")[1]) : esc(t.ad)}</span></div>
      <span class="n">${fmt(t.sunucu)} sunucu</span>
      ${w ? `<textarea rows="1" maxlength="2000" data-desc="${esc(t.ad)}" placeholder="${t.ad.includes("/") ? "Bu alt etiketteki sunucular ne iş yapar?" : "Bu etiket neyi kapsar?"}">${esc(t.aciklama || "")}</textarea>
        <span><button type="button" data-dsave="${esc(t.ad)}">Kaydet</button> <span class="ok" data-dok="${esc(t.ad)}"></span></span>`
        : `<div class="txt">${esc(t.aciklama || "—")}</div><span></span>`}
    </div>`).join("") : `<div class="eg-sub">Henüz etiket yok.</div>`}
  </div>`;
  box.querySelectorAll("[data-dsave]").forEach(b => b.addEventListener("click", async () => {
    const tag = b.dataset.dsave, ta = box.querySelector(`textarea[data-desc="${CSS.escape(tag)}"]`), ok = box.querySelector(`[data-dok="${CSS.escape(tag)}"]`);
    b.disabled = true;
    try {
      const r = await fetch("api/etiket/aciklama", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ etiket: tag, aciklama: ta.value }) });
      const res = await r.json().catch(() => ({ error: `HTTP ${r.status}` }));
      if (!r.ok) throw new Error(res.error || `HTTP ${r.status}`);
      const t = etq.tags.find(x => x.ad === tag); if (t) t.aciklama = res.aciklama;
      ok.textContent = "✓ kaydedildi";
      loadTopoTags?.(true);
    } catch (e) { alert("Açıklama kaydedilemedi: " + e.message); }
    b.disabled = false;
  }));
}

// Etiket ekle / kaldır → ERT TAG kolonu. Dönen sunucularla tablo, IP kartı ve Topoloji listesi güncellenir.
async function applyTag(ips, add, remove) {
  if (!ips.length) return;
  const tag = (add ?? remove ?? "").trim();
  if (!tag) { alert("Etiket yazın (ör. VERIAMBARI)."); return; }
  if (!add && remove && !remove.includes("/") && etq.tags.some(t => t.ad.startsWith(remove.toLocaleUpperCase("tr") + "/")))
    if (!confirm(`"${remove}" yalnız ana etiket olarak yazılmış sunuculardan kaldırılır; "${remove}/…" alt etiketleri durur. Alt etiketi kaldırmak için "Alt etiket" kutusunu da doldurun. Devam?`)) return;
  const msg = $("#egMsg");
  if (msg) msg.textContent = "Kaydediliyor…";
  try {
    const r = await fetch("api/etiket", { method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ ips, ...(add ? { ekle: tag } : { kaldir: tag }) }) });
    const b = await r.json().catch(() => ({ error: `HTTP ${r.status}` }));
    if (!r.ok) throw new Error(b.error || `HTTP ${r.status}`);
    tagMap ??= {};
    for (const srv of b.sunucular) tagMap[srv.ip] = srv.tags;
    loadTopoTags?.(true);
    if (uiMode === "etiket") {
      await etqLoad();   // sayaçlar, haplar ve filtre sonucu yeniden
      const m = $("#egMsg"); if (m) m.textContent = `✓ ${fmt(ips.length)} sunucu güncellendi`;
    } else if (uiMode === "ip" && state.data) render();
  } catch (e) {
    if (msg) msg.textContent = ""; alert("Etiket kaydedilemedi: " + e.message);
  }
}

// Tek sunucu: "+ Etiket" düğmesi ve rozetteki × (etiketleme tablosu ve IP kartı)
document.addEventListener("click", ev => {
  const add = ev.target.closest("[data-tagip]");
  if (add) {
    const t = prompt(`${add.dataset.tagip} için etiket (ör. VERIAMBARI; alt etiket için ANA/ALT, ör. MOBILBANKACILIK/MOBIL APP):`, "");
    if (t?.trim()) applyTag([add.dataset.tagip], t.trim(), null);
    return;
  }
  const del = ev.target.closest("[data-untag]");
  if (del) {
    ev.preventDefault();
    if (confirm(`${del.dataset.ip} sunucusundan "${del.dataset.untag}" etiketi kaldırılsın mı?`)) applyTag([del.dataset.ip], null, del.dataset.untag);
  }
});
