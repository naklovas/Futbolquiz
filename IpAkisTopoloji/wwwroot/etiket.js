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
  { key: "segment", label: "Segment", ph: "ad, CIDR (10.1.2.0/24) ya da IP öneki…" },
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
  .eg-tag .sub { font-weight:500; margin-left:3px; opacity:.85; white-space:nowrap; }
  .eg-pills.subs { margin-top:-6px; padding-left:14px; border-left:2px solid var(--line); }
  .eg-pills.subs .lbl { font-size:11.5px; font-weight:600; }
  .eg-pill.on .eg-tag { background:var(--panel); color:var(--text); border-color:var(--panel); }
  .eg-grp { display:inline-flex; gap:3px; align-items:center; flex-wrap:wrap; padding:2px 6px 2px 4px; border:1px dashed var(--line); border-radius:999px; }
  .eg-pills.subs .eg-pill { padding:2px 8px; font-size:11.5px; }
  .eg-pills.subs .eg-grp .eg-pill + .eg-pill { opacity:.9; }
  .eg-tg { --h:210; display:inline-flex; flex-wrap:wrap; align-items:center; gap:1px 2px; border-radius:10px; padding:1px 7px; margin:1px 4px 1px 0; font-size:11.5px;
    font-weight:600; background:hsl(var(--h) 65% 50% / .13); color:hsl(var(--h) 60% 34%); border:1px solid hsl(var(--h) 60% 50% / .35); line-height:1.5; }
  :root[data-theme="dark"] .eg-tg { color:hsl(var(--h) 75% 74%); }
  @media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) .eg-tg { color:hsl(var(--h) 75% 74%); } }
  .eg-tg [data-etag] { cursor:pointer; border-radius:4px; padding:0 2px; }
  .eg-tg [data-etag]:hover { background:hsl(var(--h) 60% 50% / .22); }
  .eg-tg .s { font-weight:500; }
  .eg-tg .sep { opacity:.4; margin:0 2px; }
  .eg-tg a { color:inherit; text-decoration:none; opacity:.5; font-weight:700; }
  .eg-tg a:hover { opacity:1; }
  .eg-pop { position:fixed; z-index:50; background:var(--panel); border:1px solid var(--line); border-radius:10px; box-shadow:0 10px 30px rgba(0,0,0,.2);
    padding:10px; display:grid; gap:8px; width:380px; max-width:calc(100vw - 32px); font-size:12.5px; }
  .eg-pop input { font:inherit; padding:6px 8px; border:1px solid var(--line); border-radius:6px; background:var(--bg); color:var(--text); }
  .eg-pop .b { display:flex; gap:6px; flex-wrap:wrap; }
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
// "A/B/C" → A › B › C (en çok 4 seviye)
function tagLabel(t) { const [a, ...r] = String(t).split("/"); return esc(a) + r.map(x => `<span class="sub">› ${esc(x)}</span>`).join(""); }
const tagDepth = t => String(t).split("/").length;
const tagLast = t => String(t).split("/").pop();
// t, p'nin kendisi ya da altında mı
const tagUnder = (t, p) => t === p || String(t).startsWith(p + "/");
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
  if (egs.aktif) { egSegYerel(); if (!etq.shell) etqShell(); else etqUpdate(); return; }
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
      <div style="display:flex;gap:10px;align-items:flex-end;flex-wrap:wrap"><button type="button" id="egDescBtn" title="Ana ve alt başlıkların ne olduğunu yazın; AI sınıflandırması da bu açıklamaları kullanır">Etiket açıklamaları</button>
        <div class="eg-stats" id="egStats"></div></div>
    </div>
    <div id="egDesc"></div>
    <div id="egZincir"></div>
    <div class="eg-pills" id="egPills"></div>
    <div class="eg-pills subs" id="egSubs" hidden></div>
    <div id="egSeg"></div>
    ${w ? `<div class="eg-bar" id="egBar" hidden>
      <b id="egSelN"></b>
      <input type="text" id="egNew" list="egTagList" placeholder="Etiket (ör. MOBILBANKACILIK)">
      <datalist id="egTagList"></datalist>
      <input type="text" id="egSub" class="subin" list="egSubList" placeholder="Alt başlık (isteğe bağlı; seviye için /, ör. ACH RTGS/DB/TEST)" title="En çok 4 seviye: Etiket IRAK + Alt başlık ACH RTGS/DB/TEST → IRAK › ACH RTGS › DB › TEST">
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
  egSegInit();
  egZincirYukle();
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
  const etiketsiz = etq.tum - etq.etiketli;
  $("#egStats").innerHTML = (egs.aktif ? [["Segmentte", etq.tum], ["ERT'de yok", egs.yok]] : [["Envanterde", etq.tum]])
    .concat([["Etiketli", etq.etiketli], ["Etiketsiz", etiketsiz], ["Filtre sonucu", etq.total]])
    .map(([l, n]) => `<div class="eg-stat"><b>${fmt(n)}</b><span>${l}</span></div>`).join("");
  // Haplar: ana etiketler; bir ana etiket (ya da altı) seçiliyse altında alt etiket hapları
  const pill = (t, label) => `<button type="button" class="eg-pill${f.etiket === t.ad ? " on" : ""}" data-pill="${esc(t.ad)}"${t.aciklama ? ` title="${esc(t.aciklama)}"` : ""}><span class="eg-tag" style="--h:${tagHue(t.ad)};margin:0">${label}</span><i>${fmt(t.sunucu)}</i></button>`;
  $("#egPills").innerHTML = `<span class="lbl">Hızlı filtre:</span>`
    + [["", "Tümü", etq.tum], ["*", "Etiketli", etq.etiketli], ["-", "Etiketsiz", etiketsiz]]
      .concat(egs.aktif ? [["!ert", "ERT'de yok", egs.yok]] : [])
      .map(([v, l, n]) => `<button type="button" class="eg-pill${f.etiket === v ? " on" : ""}" data-pill="${v}">${l}<i>${fmt(n)}</i></button>`).join("")
    + etq.tags.filter(t => !t.ad.includes("/")).map(t => pill(t, esc(t.ad) + (etq.tags.some(x => tagParent(x.ad) === t.ad && x.ad !== t.ad) ? ` <span class="sub">▾</span>` : ""))).join("");
  // Seçilen ana etiketin bütün alt başlıkları TEK satırda (kullanıcı: "alt alta listeliyorsun, her şey aşağı kayıyor"): 2. seviye başına bir grup,
  // altındakiler grubun içinde "DB › TEST" gibi; satır taşarsa sarar
  const root = f.etiket && !["*", "-", "!ert"].includes(f.etiket) ? tagParent(f.etiket) : null;
  const subs = root ? etq.tags.filter(t => t.ad.startsWith(root + "/")) : [];
  const ikinci = subs.filter(t => tagDepth(t.ad) === 2);
  $("#egSubs").hidden = !subs.length;
  $("#egSubs").innerHTML = subs.length ? `<span class="lbl">${esc(root)} ›</span>` + ikinci.map(c => {
    const alt = subs.filter(t => t.ad.startsWith(c.ad + "/"));
    return `<span class="eg-grp">${pill(c, esc(tagLast(c.ad)))}${alt.map(t => pill(t, t.ad.slice(c.ad.length + 1).split("/").map(esc).join(" › "))).join("")}</span>`;
  }).join("") : "";
  document.querySelectorAll("#egPills [data-pill], #egSubs [data-pill]").forEach(b => b.addEventListener("click", () => {
    f.etiket = b.dataset.pill; etq.page = 1;
    // ağaçtan seç: gerçek bir etiket başlığıysa seçim çubuğundaki kutular o yolla dolar (altına yeni seviye yazmak için)
    if (etq.canWrite && $("#egNew") && !["", "*", "-", "!ert"].includes(f.etiket)) {
      const [a, ...r] = f.etiket.split("/");
      $("#egNew").value = a; $("#egSub").value = r.length ? r.join("/") + "/" : "";
    }
    etqLoad();
  }));
  const sel = document.querySelector('.eg-grid select[data-f="etiket"]');
  sel.innerHTML = `<option value="">tümü</option><option value="*">etiketli</option><option value="-">etiketsiz</option>`
    + etq.tags.map(t => `<option value="${esc(t.ad)}">${tagDepth(t.ad) > 1 ? "&nbsp;&nbsp;&nbsp;".repeat(tagDepth(t.ad) - 1) + "└ " + esc(tagLast(t.ad)) : esc(t.ad)}</option>`).join("");
  if (egs.aktif) sel.insertAdjacentHTML("beforeend", `<option value="!ert">ERT'de yok</option>`);
  sel.value = f.etiket; sel.classList.toggle("has", !!f.etiket);
  document.querySelectorAll(".eg-grid [data-sort]").forEach(th => {
    const on = th.dataset.sort === etq.sort;
    th.classList.toggle("sorted", on);
    th.querySelector(".arr").textContent = on ? (etq.desc ? "▼" : "▲") : "↕";
  });

  $("#egBody").innerHTML = etq.rows.length ? etq.rows.map(r => `<tr class="${etq.selected.has(r.ip) ? "sel" : ""}">
      ${w ? `<td class="cb"><input type="checkbox" data-sel="${esc(r.ip)}"${etq.selected.has(r.ip) ? " checked" : ""}></td>` : ""}
      <td class="ip"><a href="${esc(ipTabUrl(r.ip))}" target="_blank" rel="noopener" title="Topolojisini yeni sekmede aç">${esc(r.ip)}</a></td>
      <td>${esc(r.hostname ?? "")}${r.kaynak ? ` <span class="dim" style="font-size:11px">· ${esc(r.kaynak)}</span>` : ""}</td>
      <td class="app">${r.apps.map(a => `<span>${esc(a)}</span>`).join("") || (r.not ? `<span class="dim" style="border:none;background:none;white-space:normal" title="${esc(r.not)}">${esc(r.not.length > 90 ? r.not.slice(0, 90) + "…" : r.not)}</span>` : `<span class="dim">—</span>`)}</td>
      <td class="${r.muhafiz ? "" : "dim"}">${esc(r.muhafiz ?? "—")}</td>
      <td class="${r.segment ? "" : "dim"}">${esc(r.segment ?? "—")}${r.cidr ? `<div class="dim" style="font-size:11px">${esc(r.cidr)}</div>` : ""}</td>
      <td>${r.ertYok ? `<span class="eg-yok" title="ERT_HOSTIPADDRESS tablosunda yok (ağda görüldü); etiketi DeltaFlow'un kendi tablosunda (DF_ETIKET_EK) tutulur">ERT'de yok</span> ` : ""}${tagGrup(r.tags, r.ip, w) || (r.ertYok ? "" : `<span class="dim">—</span>`)}</td>
      ${w ? `<td style="text-align:right"><button type="button" class="eg-add" data-tagip="${esc(r.ip)}">+ Etiket</button></td>` : ""}
    </tr>`).join("")
    : `<tr><td colspan="${ETQ_COLS.length + (w ? 2 : 0)}" class="eg-empty">Filtreye uyan sunucu yok.</td></tr>`;

  const from = etq.total ? (etq.page - 1) * etq.size + 1 : 0, to = Math.min(etq.page * etq.size, etq.total);
  $("#egCount").textContent = `${fmt(from)}–${fmt(to)} / ${fmt(etq.total)} sunucu`;
  $("#egPage").textContent = ` ${etq.page} / ${Math.max(1, Math.ceil(etq.total / etq.size))} `;
  $("#egPrev").disabled = etq.page <= 1; $("#egNext").disabled = etq.page * etq.size >= etq.total;
  if (w) {
    const sec = etq.rows, allOn = sec.length > 0 && sec.every(r => etq.selected.has(r.ip));
    $("#egAll").checked = allOn;
    $("#egAll").indeterminate = !allOn && sec.some(r => etq.selected.has(r.ip));
    $("#egBar").hidden = etq.selected.size === 0;
    $("#egSelN").textContent = `${fmt(etq.selected.size)} sunucu seçili`;
    $("#egTagList").innerHTML = etq.tags.filter(t => !t.ad.includes("/")).map(t => `<option value="${esc(t.ad)}">`).join("");
    egSubList();
  }
  if (etq.descOpen) egDescRender();
  egSegRender();
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
    <h3>Etiket açıklamaları <span class="eg-sub">Etiketi değiştirmek için tablodaki etiketin üzerine tıklayın</span></h3>
    ${etq.descWarn ? `<div class="iv-warn" style="margin:0">${esc(etq.descWarn)}</div>` : ""}
    ${tags.length ? tags.map(t => `<div class="row${t.ad.includes("/") ? " sub" : ""}"${tagDepth(t.ad) > 2 ? ` style="padding-left:${22 * (tagDepth(t.ad) - 1)}px"` : ""}>
      <div><span class="eg-tag" style="--h:${tagHue(t.ad)}" title="${esc(t.ad.split("/").join(" › "))}">${esc(tagLast(t.ad))}</span></div>
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
  if (!add && remove && etq.tags.some(t => t.ad.startsWith(remove.toLocaleUpperCase("tr") + "/")))
    if (!confirm(`"${remove}" yalnız tam olarak bu etiketle yazılmış sunuculardan kaldırılır; "${remove}/…" alt başlıkları durur. Alt başlığı kaldırmak için tam yolunu yazın. Devam?`)) return;
  const msg = $("#egMsg");
  if (msg) msg.textContent = "Kaydediliyor…";
  try {
    const r = await fetch("api/etiket", { method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ ips, ...(add ? { ekle: tag } : { kaldir: tag }), adlar: egSegAdlar(ips) }) });
    const b = await r.json().catch(() => ({ error: `HTTP ${r.status}` }));
    if (!r.ok) throw new Error(b.error || `HTTP ${r.status}`);
    tagMap ??= {};
    for (const srv of b.sunucular) tagMap[srv.ip] = srv.tags;
    for (const ip of ips) if (!b.sunucular.some(x => x.ip === ip)) tagMap[ip] = [];   // ERT dışı IP'nin son etiketi kaldırıldı
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


// ---------------------------------------------------------------------------
// Segment getir (kullanıcı: "segment getir kısmı olsun oradan segment bulayım ama tüm ipleri gelsin", "Carbon Black'ten tara demeyelim"):
// segment tarama API'si (kayıtlı tarama varsa ondan açılır; "Yenile" baştan getirir) segmentteki IP'leri verir; ERT'dekiler
// /api/etiket/sunucular?segment=<CIDR'ler> ile gelir. Ana tablo bu listeye geçer (filtre / sıralama / sayfa tarayıcıda);
// ERT'de olmayan IP'ler "ERT'de yok" rozetiyle listelenir; etiketleri DF_ETIKET_EK'e yazılır (sunucu adıyla). Port listesi gösterilmez.
// ---------------------------------------------------------------------------
const egs = { segs: null, aktif: null, liste: [], yok: 0, yukleniyor: false, ilerleme: "", hata: null, seq: 0 };

(function egsStyle() {
  const st = document.createElement("style");
  st.textContent = `
  .eg-seg { display:flex; gap:8px; align-items:center; flex-wrap:wrap; margin:0 0 12px; }
  .eg-seg b { margin-right:2px; }
  .eg-seg input { font:inherit; font-size:12.5px; padding:5px 8px; border:1px solid var(--line); border-radius:6px; background:var(--panel); color:var(--text); width:280px; max-width:100%; }
  .eg-seg .info { color:var(--muted); font-size:12px; }
  .eg-seg.on input { border-color:var(--in); box-shadow:0 0 0 2px var(--in-soft); }
  .eg-yok { display:inline-block; border-radius:999px; padding:1px 8px; font-size:11px; font-weight:600; background:color-mix(in srgb, var(--err) 14%, transparent); color:var(--err); border:1px solid color-mix(in srgb, var(--err) 40%, transparent); }`;
  document.head.appendChild(st);
})();

function egSegInit() {
  const box = $("#egSeg");
  if (!box) return;
  box.innerHTML = `<div class="eg-seg" id="egSegBox"><b>Segment getir</b>
      <input type="search" id="egSegIn" list="egSegList" placeholder="Segment adı ya da CIDR (ör. 10.1.2.0/24)" value="${esc(egs.aktif?.ad ?? "")}"><datalist id="egSegList"></datalist>
      <button type="button" id="egSegGo">Getir</button>
      <button type="button" id="egSegYen" title="Kayıtlı sonucu değil, segmenti baştan getirir">Yenile</button>
      <button type="button" id="egSegKapat" title="Bütün envantere dön">Kapat</button>
      <span class="info" id="egSegInfo"></span></div>`;
  const go = mod => egSegGetir($("#egSegIn").value.trim(), mod);
  $("#egSegGo").addEventListener("click", () => go(""));
  $("#egSegYen").addEventListener("click", () => go("yeniden"));
  $("#egSegIn").addEventListener("keydown", ev => { if (ev.key === "Enter") { ev.preventDefault(); go(""); } });
  $("#egSegKapat").addEventListener("click", () => {
    egs.seq++; Object.assign(egs, { aktif: null, liste: [], yok: 0, hata: null, yukleniyor: false });
    if (etq.f.etiket === "!ert") etq.f.etiket = "";
    $("#egSegIn").value = ""; etq.page = 1; etqLoad();
  });
  const fill = () => { $("#egSegList").innerHTML = (egs.segs || []).map(s => `<option value="${esc(s.ad)}">${esc(s.cidrler.join(", "))}${s.kurum ? " · " + esc(s.kurum) : ""} · ${fmt(s.ertSunucu)} sunucu</option>`).join(""); };
  if (egs.segs) fill();
  else fetch("api/segment/liste").then(r => r.ok ? r.json() : []).then(l => { egs.segs = l; fill(); }).catch(() => {});
  egSegRender();
}

function egSegRender() {
  if (!$("#egSegBox")) return;
  $("#egSegBox").classList.toggle("on", !!egs.aktif);
  $("#egSegGo").disabled = egs.yukleniyor;
  $("#egSegYen").hidden = !egs.aktif;
  $("#egSegKapat").hidden = !egs.aktif && !egs.hata;
  const a = egs.aktif;
  $("#egSegInfo").innerHTML = egs.yukleniyor ? `<span class="spinner"></span> ${esc(egs.ilerleme)}`
    : egs.hata ? `<span style="color:var(--err)">${esc(egs.hata)}</span>`
    : a ? esc(`${a.ad}${a.cidrler.length ? " · " + a.cidrler.join(", ") : ""} · ${fmt(egs.liste.length)} IP (${fmt(egs.yok)} tanesi ERT'de yok${a.csy || a.msy ? `; ÇSY ${fmt(a.csy)}, MSY ${fmt(a.msy)}` : ""})${a.tarih ? ` · ${a.tarih} tarihli kayıt` : ""}`)
      + (a.uyari ? ` <span style="color:var(--err)">${esc(a.uyari)}</span>` : "")
    : "Segmentteki bütün IP'ler tabloya gelir: ERT, ağda görülenler ve MSY / ÇSY sanal sunucu envanteri";
}

async function egSegGetir(seg, mod) {
  if (!seg) { alert("Segment adı ya da CIDR yazın."); return; }
  const my = ++egs.seq;
  Object.assign(egs, { hata: null, yukleniyor: true, ilerleme: "Getiriliyor…" });
  egSegRender();
  const json = async r => { const b = await r.json().catch(() => ({ error: `HTTP ${r.status}` })); if (!r.ok) throw new Error(b.error || `HTTP ${r.status}`); return b; };
  try {
    // 1) ağ taraması (kayıtlı varsa ondan); alınamazsa ERT + MSY / ÇSY ile devam edilir
    let s = null, kayit = null, taramaHata = null;
    try {
      let d = await json(await fetch("api/segment/tarama", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ segment: seg, saat: 24, mod }) }));
      while (d.calisiyor) {
        if (my !== egs.seq) return;
        egs.ilerleme = `Getiriliyor…${d.toplam ? ` ${fmt(d.biten)} / ${fmt(d.toplam)}` : ""}`;
        egSegRender();
        await new Promise(res => setTimeout(res, 2000));
        d = await json(await fetch("api/segment/tarama/" + encodeURIComponent(d.id)));
      }
      if (d.hata) throw new Error(d.hata);
      if (!d.sonuc) throw new Error(d.asama || "Sonuç gelmedi.");
      s = d.sonuc; kayit = d.kayit;
    } catch (e) { taramaHata = e.message; }
    if (my !== egs.seq) return;
    const cidrler = s?.cidrler?.length ? s.cidrler : egSegCidr(seg);
    if (!s && !cidrler.length) throw new Error(taramaHata || "Segment bulunamadı.");
    // 2) ERT'deki sunucular
    const ert = new Map(), q = cidrler.length ? cidrler.join(",") : seg;
    for (let page = 1; page <= 40; page++) {
      const b = await json(await fetch("api/etiket/sunucular?" + new URLSearchParams({ segment: q, sayfa: page, boyut: 500, sirala: "ip" })));
      for (const r of b.sunucular) ert.set(r.ip, r);
      if (page * 500 >= b.toplam) break;
    }
    // 3) MSY / ÇSY sanal sunucu envanterleri (ERT'de olmayan VM'ler; kullanıcı: "ERT'de hiç yok ama ÇSY envanterinde var")
    let sanal = [], sanalUyari = null;
    if (cidrler.length) {
      try { const b = await json(await fetch("api/etiket/sanal?" + new URLSearchParams({ cidr: cidrler.join(",") }))); sanal = b.sunucular || []; sanalUyari = b.uyari; }
      catch (e) { sanalUyari = "MSY / ÇSY okunamadı: " + e.message; }
    }
    if (my !== egs.seq) return;
    const liste = [...ert.values()], var_ = new Map(liste.map(r => [r.ip, r]));
    const segAd = s?.segment ?? seg;
    for (const x of s?.sunucular || []) if (!var_.has(x.ip)) {
      const r = { ip: x.ip, hostname: x.sunucu, apps: [], muhafiz: null, segment: x.segment ?? segAd, cidr: null, tags: tagMap?.[x.ip] ?? [], ertYok: true, kaynak: "ağda görüldü" };
      liste.push(r); var_.set(x.ip, r);
    }
    for (const v of sanal) {
      const r = var_.get(v.ip);
      if (r) { if (r.ertYok) { r.hostname ||= v.ad; r.kaynak = r.kaynak === "ağda görüldü" ? v.kaynak + " · ağda görüldü" : r.kaynak || v.kaynak; r.not ||= v.not; } continue; }
      const y = { ip: v.ip, hostname: v.ad, apps: [], muhafiz: null, segment: segAd, cidr: null, tags: tagMap?.[v.ip] ?? [], ertYok: true, kaynak: v.kaynak, not: v.not };
      liste.push(y); var_.set(v.ip, y);
    }
    const say = k => liste.filter(r => r.ertYok && (r.kaynak || "").includes(k)).length;
    Object.assign(egs, { aktif: { ad: segAd, cidrler, tarih: kayit?.tarih ? new Date(kayit.tarih).toLocaleString("tr-TR") : null,
        msy: say("MSY"), csy: say("ÇSY"), uyari: [taramaHata ? "Ağ taraması alınamadı: " + taramaHata : null, sanalUyari].filter(Boolean).join(" · ") || null },
      liste, yok: liste.filter(r => r.ertYok).length, yukleniyor: false });
    etq.page = 1;
  } catch (e) {
    if (my === egs.seq) Object.assign(egs, { hata: e.message, yukleniyor: false });
  }
  if (my === egs.seq) { egSegRender(); etqLoad(); }
}

// Segment listesinde filtre / sıralama / sayfa (sunucu tarafındaki listeyle aynı kolonlar)
function egSegYerel() {
  const f = etq.f, low = v => String(v ?? "").toLocaleLowerCase("tr");
  const has = (v, q) => !q || low(v).includes(low(q));
  const tagsOf = r => tagMap?.[r.ip] ?? r.tags;
  const u = ip => ip.split(".").reduce((a, b) => a * 256 + (+b || 0), 0);
  let rows = egs.liste.map(r => ({ ...r, tags: tagsOf(r) }));
  const tum = rows.length, etiketli = rows.filter(r => r.tags.length).length;
  rows = rows.filter(r => has(r.ip, f.ip) && has(r.hostname, f.sunucu) && has(r.apps.join(" ") + " " + (r.not || ""), f.uygulama) && has(r.muhafiz, f.muhafiz)
    && (has(r.segment, f.segment) || has(r.cidr, f.segment))
    && (!f.etiket || (f.etiket === "!ert" ? r.ertYok : f.etiket === "*" ? r.tags.length > 0 : f.etiket === "-" ? !r.tags.length
      : r.tags.some(t => tagUnder(t, f.etiket)))));
  const key = { ip: r => u(r.ip), sunucu: r => low(r.hostname), uygulama: r => low(r.apps.join(",")), muhafiz: r => low(r.muhafiz),
    segment: r => low(r.segment), etiket: r => low(r.tags.join(",")) }[etq.sort] ?? (r => u(r.ip));
  rows.sort((a, b) => { const x = key(a), y = key(b); const c = x < y ? -1 : x > y ? 1 : 0; return etq.desc ? -c : c; });
  Object.assign(etq, { rows: rows.slice((etq.page - 1) * etq.size, etq.page * etq.size), total: rows.length, tum, etiketli });
}

// ERT dışı seçili IP'lerin sunucu adları (DF_ETIKET_EK.HOSTNAME)
function egSegAdlar(ips) {
  const m = {};
  for (const ip of ips) { const r = egs.liste.find(x => x.ip === ip); if (r?.ertYok && r.hostname) m[ip] = r.hostname; }
  return m;
}

// Zincirleme girilmiş etiketler (IRAK/BAGDAT + BAGDAT/ACTIVE DIRECTORY → IRAK/BAGDAT/ACTIVE DIRECTORY): önizleme + birleştir (/api/etiket/zincir)
const egz = { satirlar: [], acik: false };
async function egZincirYukle() {
  try {
    const r = await fetch("api/etiket/zincir");
    egz.satirlar = r.ok ? (await r.json()).satirlar || [] : [];
  } catch { egz.satirlar = []; }
  egZincirCiz();
}
// birleşince ana etiket olmaktan çıkacak kökler (ör. BAGDAT, DB, ALTYAPI)
function egZincirKokler(z) {
  const yeni = new Set(z.flatMap(x => x.yeni).map(tagParent));
  return [...new Set(z.flatMap(x => x.eski).map(tagParent))].filter(r => !yeni.has(r));
}
function egZincirCiz() {
  const box = $("#egZincir");
  if (!box) return;
  const z = egz.satirlar;
  if (!z.length) { box.innerHTML = ""; return; }
  const ornek = z[0];
  box.innerHTML = `<div class="iv-warn" style="margin:0 0 12px">
    <b>${fmt(z.length)} sunucuda zincirleme girilmiş etiket var</b> (ör. ${ornek.eski.map(tagLabel).join(" + ")}): bunlar ayrı ana etiket sayılıyor,
    bu yüzden Topoloji listesinde ${esc(egZincirKokler(z).slice(0, 6).join(", ")) || "alt başlıklar"} ayrı ana etiket olarak görünüyor.
    <button type="button" id="egZAc">${egz.acik ? "Gizle" : "Önizle"}</button>
    ${etq.canWrite ? `<button type="button" id="egZUy" class="primary">Hepsini tek etikete birleştir</button>` : ""}
    ${egz.acik ? `<div class="eg-grid" style="margin-top:10px;max-height:320px"><table><thead><tr><th>IP</th><th>Sunucu</th><th>Şu an → birleşince</th></tr></thead><tbody>
      ${z.map(x => `<tr><td class="ip">${esc(x.ip)}</td><td>${esc(x.hostname ?? "")}${x.ertYok ? ` <span class="eg-yok">ERT'de yok</span>` : ""}</td>
        <td><div>${x.eski.map(t => `<span class="eg-tag" style="--h:${tagHue(t)};opacity:.6">${tagLabel(t)}</span>`).join("")}</div>
          <div style="margin-top:4px"><b>→</b> ${x.yeni.map(t => `<span class="eg-tag" style="--h:${tagHue(t)}">${tagLabel(t)}</span>`).join("")}</div></td></tr>`).join("")}
    </tbody></table></div>` : ""}
  </div>`;
  $("#egZAc").addEventListener("click", () => { egz.acik = !egz.acik; egZincirCiz(); });
  $("#egZUy")?.addEventListener("click", async ev => {
    if (!confirm(`${z.length} sunucunun etiketleri birleştirilecek (ERT TAG kolonu; ERT'de olmayanlar DF_ETIKET_EK). Önce "Önizle" ile kontrol edin. Devam?`)) return;
    ev.target.disabled = true;
    try {
      const r = await fetch("api/etiket/zincir", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({}) });
      const b = await r.json().catch(() => ({ error: `HTTP ${r.status}` }));
      if (!r.ok) throw new Error(b.error || `HTTP ${r.status}`);
      alert(`${fmt(b.guncellenen)} sunucunun etiketleri birleştirildi.`);
      fetch("api/etiketler").then(x => x.ok ? x.json() : null).then(x => { if (x?.ipler) tagMap = x.ipler; }).catch(() => {});
      loadTopoTags?.(true);
      await etqLoad();
      await egZincirYukle();
    } catch (e) { alert("Birleştirilemedi: " + e.message); ev.target.disabled = false; }
  });
}

// Etiket düzenleme: yeni yol sor → önizleme (kaç sunucu, hangi etiketler) → onay → uygula (/api/etiket/duzenle)
async function egDuzenle(eski, hazir) {
  const yeni = hazir ?? prompt(`"${eski.split("/").join(" › ")}" için yeni yol (seviyeler / ile, en çok 4).\nAltındaki başlıklar da birlikte taşınır.\nÖrn: IRAK/ALTYAPI/MICROSOFT ATA`, eski);
  if (!yeni?.trim() || yeni.trim().toLocaleUpperCase("tr") === eski) return;
  const post = async uygula => {
    const r = await fetch("api/etiket/duzenle", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ eski, yeni: yeni.trim(), uygula }) });
    const b = await r.json().catch(() => ({ error: `HTTP ${r.status}` }));
    if (!r.ok) throw new Error(b.error || `HTTP ${r.status}`);
    return b;
  };
  try {
    const o = await post(false);
    const liste = o.etiketler.slice(0, 15).map(x => `  ${x.eski.split("/").join(" › ")}  →  ${x.yeni.split("/").join(" › ")}`).join("\n");
    if (!confirm(`${fmt(o.sunucu)} sunucuda ${o.etiketler.length} etiket değişecek:\n\n${liste}${o.etiketler.length > 15 ? `\n  … ve ${o.etiketler.length - 15} etiket daha` : ""}\n\nAçıklamalar ve kayıtlı topoloji versiyonları da taşınır. Devam?`)) return;
    const u = await post(true);
    if (etq.f.etiket && (etq.f.etiket === eski || etq.f.etiket.startsWith(eski + "/"))) etq.f.etiket = u.yeni + etq.f.etiket.slice(eski.length);
    fetch("api/etiketler").then(x => x.ok ? x.json() : null).then(x => { if (x?.ipler) tagMap = x.ipler; }).catch(() => {});
    loadTopoTags?.(true);
    await etqLoad();
    await egZincirYukle();
    alert(`${fmt(u.guncellenen)} sunucunun etiketi güncellendi.`);
  } catch (e) { alert("Düzenlenemedi: " + e.message); }
}

// Tablodaki etiket hücresi (kullanıcı: "fazla etiket olduğu için biçimsiz"): aynı kök etiketin başlıkları TEK rozette — IRAK › ALTYAPI · BAGDAT › ACTIVE DIRECTORY;
// her başlık tıklanınca düzenlenir (data-etag), × ile kaldırılır
function tagGrup(tags, ip, w) {
  const g = new Map();
  for (const t of [...tags].sort()) { const r = tagParent(t); (g.get(r) || g.set(r, []).get(r)).push(t); }
  const x = t => w ? `<a href="#" data-untag="${esc(t)}" data-ip="${esc(ip)}" title="Bu etiketi kaldır">×</a>` : "";
  const ed = (t, html) => `<span data-etag="${esc(t)}" data-ip="${esc(ip)}" title="${esc(t.split("/").join(" › "))}${w ? " — değiştirmek için tıklayın" : ""}">${html}</span>`;
  return [...g].map(([r, ts]) => {
    const kok = ts.includes(r), alt = ts.filter(t => t !== r);
    const d = tagDesc(r);
    return `<span class="eg-tg" style="--h:${tagHue(r)}"${d ? ` title="${esc(d)}"` : ""}>${kok ? ed(r, esc(r)) + x(r) : esc(r)}${alt.map((t, i) =>
      `${i || kok ? `<span class="sep">·</span>` : " "}${ed(t, `<span class="s">› ${t.slice(r.length + 1).split("/").map(esc).join(" › ")}</span>`)}${x(t)}`).join("")}</span>`;
  }).join("");
}

// Etiketi yerinde değiştir: bu sunucuda / seçili sunucularda / bu etiketi taşıyan tüm sunucularda (alt seviyeleriyle, önizlemeli)
document.addEventListener("click", ev => {
  const el = ev.target.closest(".eg-grid [data-etag]");
  if (!el || !etq.canWrite) return;
  ev.preventDefault();
  egPop(el, el.dataset.etag, el.dataset.ip);
});
function egPopKapat() { document.querySelector(".eg-pop")?.remove(); }
function egPop(el, eski, ip) {
  egPopKapat();
  const sec = etq.selected.has(ip) && etq.selected.size > 1 ? [...etq.selected] : null;
  const n = etq.tags.find(t => t.ad === eski)?.sunucu;
  const box = document.createElement("div");
  box.className = "eg-pop";
  box.innerHTML = `<div><b>Etiketi değiştir</b> <span class="eg-sub">· seviyeler / ile, en çok 4</span></div>
    <input type="text" value="${esc(eski)}" list="egPopList"><datalist id="egPopList">${etq.tags.map(t => `<option value="${esc(t.ad)}">`).join("")}</datalist>
    <div class="b"><button type="button" class="primary" data-k="bu">Bu sunucuda</button>
      ${sec ? `<button type="button" data-k="sec">Seçili ${fmt(sec.length)} sunucuda</button>` : ""}
      <button type="button" data-k="tum" title="Bu etiketi (ve altındaki başlıkları) taşıyan tüm sunucularda; önce önizleme">Tüm sunucularda${n ? ` (${fmt(n)})` : ""}</button>
      <button type="button" data-k="iptal">İptal</button></div>`;
  document.body.appendChild(box);
  const r = el.getBoundingClientRect();
  box.style.left = Math.max(8, Math.min(r.left, innerWidth - box.offsetWidth - 8)) + "px";
  box.style.top = (r.bottom + box.offsetHeight + 12 > innerHeight ? Math.max(8, r.top - box.offsetHeight - 6) : r.bottom + 6) + "px";
  const inp = box.querySelector("input");
  inp.focus(); inp.select();
  const yap = async k => {
    const yeni = inp.value.trim().toLocaleUpperCase("tr");
    if (k === "iptal" || !yeni || yeni === eski) { egPopKapat(); return; }
    egPopKapat();
    if (k === "tum") { egDuzenle(eski, yeni); return; }
    const ips = k === "sec" ? sec : [ip];
    try {
      const res = await fetch("api/etiket", { method: "POST", headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ ips, ekle: yeni, kaldir: eski, adlar: egSegAdlar(ips) }) });
      const b = await res.json().catch(() => ({ error: `HTTP ${res.status}` }));
      if (!res.ok) throw new Error(b.error || `HTTP ${res.status}`);
      tagMap ??= {};
      for (const srv of b.sunucular) tagMap[srv.ip] = srv.tags;
      loadTopoTags?.(true);
      await etqLoad();
    } catch (e) { alert("Etiket değiştirilemedi: " + e.message); }
  };
  box.querySelectorAll("[data-k]").forEach(b => b.addEventListener("click", () => yap(b.dataset.k)));
  inp.addEventListener("keydown", e => { if (e.key === "Enter") yap("bu"); if (e.key === "Escape") egPopKapat(); });
  setTimeout(() => document.addEventListener("mousedown", function dis(e) { if (!box.contains(e.target)) { egPopKapat(); document.removeEventListener("mousedown", dis); } }), 0);
}

// Segment getir: tarama alınamazsa CIDR'lar segment listesinden ya da yazılan CIDR'dan
function egSegCidr(seg) {
  const p = seg.split(/[,;\s]+/).filter(Boolean);
  if (p.length && p.every(x => /^\d{1,3}(\.\d{1,3}){3}\/\d{1,2}$/.test(x))) return p;
  return (egs.segs || []).find(x => x.ad.toLocaleUpperCase("tr") === seg.toLocaleUpperCase("tr"))?.cidrler || [];
}
