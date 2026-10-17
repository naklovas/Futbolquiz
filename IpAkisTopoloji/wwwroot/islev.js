// ============================================================================
// DeltaFlow · İşlev haritası (Topoloji sekmesi)
// Bir etiketin sunucuları AI ile işlevlerine göre kümelenir: küme → segment → VIP → sunucu. Bağımlılık çizilmez.
// AI'a giden bilgi: ERT, segment, VIP, MSY, ÇSY, Envanter Kahini, SM kurulum talepleri ve iş istekleri (Islev.cs).
// Sınıflandırma sunucuda arka planda çalışır; ilerleme /api/islev/durum ile izlenir. Sunucuya tıklayınca ayrıntı
// çekmecesi: AI gerekçesi, küme düzeltme, AI'a giden kanıtlar. topoloji.html / etiket.js yardımcılarını kullanır.
// ============================================================================

const iv = { tag: "", data: null, q: "", poll: null, sel: null, seq: 0, pick: null };

(function ivStyle() {
  const css = `
  .iv-tools { display:flex; gap:8px; flex-wrap:wrap; align-items:center; margin:2px 0 14px; }
  .iv-tools input[type=search] { min-width:260px; }
  .iv-tools button.ai { background:var(--in); color:#fff; border-color:var(--in); }
  .iv-tools .sp { flex:1; }
  .iv-run { border:1px solid var(--line); border-radius:10px; padding:10px 14px; margin-bottom:14px; background:var(--bg); display:grid; gap:6px; font-size:13px; }
  .iv-run .bar { height:8px; border-radius:99px; background:var(--line); overflow:hidden; }
  .iv-run .bar i { display:block; height:100%; background:var(--in); transition:width .4s; }
  .iv-run details { color:var(--muted); font-size:12px; }
  .iv-warn { border:1px solid #d9a33c; background:#d9a33c22; border-radius:10px; padding:10px 14px; margin-bottom:14px; font-size:13px; }
  .iv-warn code { display:block; margin-top:6px; font-size:12px; user-select:all; }
  .iv-grid { display:grid; grid-template-columns:repeat(auto-fill, minmax(340px, 1fr)); gap:14px; align-items:start; }
  .iv-card { --h:210; border:1px solid var(--line); border-top:4px solid hsl(var(--h) 60% 50%); border-radius:10px; background:var(--panel); display:flex; flex-direction:column; max-height:620px; }
  .iv-card.none { --h:0; border-top-color:var(--muted); }
  .iv-card header { all:unset; display:block; padding:10px 14px 8px; border-bottom:1px solid var(--line); }
  .iv-card h3 { margin:0; font-size:15px; display:flex; justify-content:space-between; gap:8px; align-items:baseline; }
  .iv-card h3 span { font-size:12px; color:var(--muted); font-weight:500; white-space:nowrap; }
  .iv-acts { display:flex; gap:12px; margin-top:3px; font-size:11.5px; }
  .iv-merge { color:var(--muted); text-decoration:none; }
  .iv-merge:hover { color:var(--in); }
  .iv-mergebox { display:flex; gap:6px; margin-top:8px; }
  .iv-mergebox select, .iv-mergebox input { flex:1; min-width:0; }
  .iv-mergebox button { padding:5px 10px; }
  .iv-card p { margin:4px 0 0; color:var(--muted); font-size:12.5px; line-height:1.4; }
  .iv-body { overflow:auto; padding:6px 10px 10px; }
  .iv-seg { margin-top:8px; }
  .iv-seg > div:first-child { font-size:11px; text-transform:uppercase; letter-spacing:.05em; color:var(--muted); padding:2px 4px; display:flex; justify-content:space-between; gap:6px; }
  .iv-seg > div:first-child b { color:var(--text); font-weight:600; text-transform:none; letter-spacing:0; font-size:12.5px; }
  .iv-vip { border-left:3px solid hsl(var(--h) 60% 50% / .55); margin:4px 0 6px 4px; padding-left:8px; }
  .iv-vip .vh { font-size:11.5px; color:var(--muted); padding:2px 0; }
  .iv-vip .vh b { font-family:Consolas, ui-monospace, monospace; color:var(--text); font-weight:600; }
  .iv-srv { display:grid; grid-template-columns:10px 1fr auto; gap:2px 8px; align-items:center; padding:5px 6px; border-radius:6px; cursor:pointer; }
  .iv-srv:hover, .iv-srv.on { background:var(--in-soft); }
  .iv-srv.pk { grid-template-columns:16px 10px 1fr auto; }
  .iv-srv.pk::before { content:""; width:12px; height:12px; border:1.5px solid var(--muted); border-radius:3px; grid-row:1; }
  .iv-srv.pk .rl { grid-column:3 / 5; }
  .iv-srv.picked { background:var(--in-soft); }
  .iv-srv.picked::before { background:var(--in); border-color:var(--in); box-shadow:inset 0 0 0 2px var(--panel); }
  .iv-tools button.on { background:var(--text); color:var(--panel); }
  .iv-srv .dot { width:8px; height:8px; border-radius:50%; background:var(--line); }
  .iv-srv .dot.g3 { background:#2e9e6a; } .iv-srv .dot.g2 { background:#d9a33c; } .iv-srv .dot.g1 { background:#d4553f; }
  .iv-srv .nm { font-size:13px; font-weight:600; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
  .iv-srv .ip { font-family:Consolas, ui-monospace, monospace; font-size:11.5px; color:var(--muted); }
  .iv-srv .rl { grid-column:2 / 4; font-size:11.5px; color:var(--muted); overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
  .iv-srv .el { font-size:10px; border:1px solid var(--line); border-radius:4px; padding:0 4px; margin-left:4px; color:var(--muted); font-weight:500; }
  .iv-drawer { position:fixed; top:0; right:0; bottom:0; width:min(520px, 100vw); background:var(--panel); border-left:1px solid var(--line);
    box-shadow:-12px 0 32px rgba(0,0,0,.18); z-index:50; display:flex; flex-direction:column; }
  .iv-drawer .dh { padding:14px 18px; border-bottom:1px solid var(--line); display:flex; justify-content:space-between; gap:10px; align-items:start; }
  .iv-drawer .dh h3 { margin:0; font-size:17px; }
  .iv-drawer .db { overflow:auto; padding:14px 18px 24px; display:grid; gap:14px; font-size:13px; }
  .iv-drawer dl { display:grid; grid-template-columns:110px 1fr; gap:6px 10px; margin:0; }
  .iv-drawer h4 { margin:0 0 6px; font-size:12px; text-transform:uppercase; letter-spacing:.05em; color:var(--muted); }
  .iv-drawer ul { margin:0; padding-left:18px; display:grid; gap:4px; }
  .iv-drawer .why { background:var(--bg); border:1px solid var(--line); border-radius:8px; padding:10px 12px; line-height:1.45; }
  .iv-drawer .fix { display:flex; gap:6px; flex-wrap:wrap; }
  .iv-drawer .fix input { flex:1; min-width:180px; }
  .iv-drawer table { font-size:12px; }
  .iv-drawer pre { white-space:pre-wrap; font-size:11.5px; background:var(--bg); border:1px solid var(--line); border-radius:8px; padding:10px; max-height:320px; overflow:auto; }`;
  const st = document.createElement("style"); st.textContent = css; document.head.appendChild(st);
})();

function ivStopPoll() { clearTimeout(iv.poll); iv.poll = null; }

async function runIslev(tag) {
  setUiMode("topo");
  iv.tag = tag; iv.sel = null; iv.q = "";
  history.replaceState(null, "", "?" + new URLSearchParams({ topo: tag }));
  $("#main").innerHTML = `<div class="panel empty"><span class="spinner"></span> "${esc(tag)}" işlev haritası yükleniyor…</div>`;
  await ivLoad();
}

async function ivLoad() {
  const my = ++iv.seq;
  try {
    const r = await fetch("api/islev?" + new URLSearchParams({ etiket: iv.tag }));
    const b = await r.json().catch(() => ({ error: `HTTP ${r.status}` }));
    if (!r.ok) throw new Error(b.error || `HTTP ${r.status}`);
    if (my !== iv.seq || uiMode !== "topo") return;
    iv.data = b;
    ivRender();
    if (b.durum?.calisiyor) ivWatch();
  } catch (e) {
    if (my === iv.seq) $("#main").innerHTML = `<div class="panel empty" style="color:var(--err)">${esc(e.message)}</div>`;
  }
}

function ivRender() {
  const d = iv.data, w = d.yazabilir, run = d.durum?.calisiyor && d.durum.etiket === iv.tag;
  const kumeSay = d.kumeler.filter(k => k.ad !== "Sınıflanmamış").length;
  $("#main").innerHTML = `
  <section class="panel">
    <div class="eg-head">
      <div><h2>İşlev haritası · ${esc(d.etiket)}</h2>
        <div class="eg-sub">AI, etiketli sunucuları envanter, sanal sunucu notları ve SM iş isteklerine bakarak işlevlerine göre kümeledi · Küme → Segment → VIP → Sunucu</div></div>
      <div class="eg-stats">
        <div class="eg-stat"><b>${fmt(d.toplam)}</b><span>Sunucu</span></div>
        <div class="eg-stat"><b>${fmt(d.siniflanan)}</b><span>Sınıflanan</span></div>
        <div class="eg-stat"><b>${fmt(kumeSay)}</b><span>Küme</span></div>
      </div>
    </div>
    ${d.uyari ? `<div class="iv-warn"><b>AI kolonları eksik.</b> Sonuçlar ERT tablosuna yazılır; önce kolonları ekleyin:<code>${esc(d.uyari.replace(/^.*Ekleyin: /, ""))}</code></div>` : ""}
    <div class="iv-tools">
      <input type="search" id="ivQ" placeholder="Sunucu, IP, rol, VIP ara…" value="${esc(iv.q)}">
      <span class="sp"></span>
      ${w ? `<button class="ai" id="ivRun" ${run || d.uyari ? "disabled" : ""} title="Henüz sınıflanmamış sunucuları AI'a gönderir, sonra kümeler">AI ile sınıflandır${d.toplam - d.siniflanan ? ` (${fmt(d.toplam - d.siniflanan)} yeni)` : ""}</button>
      <button id="ivAll" ${run || d.uyari ? "disabled" : ""} title="Elle düzeltilenler hariç tüm sunucuları yeniden sınıflar">Hepsini yeniden</button>
      <button id="ivClu" ${run || d.uyari || !d.siniflanan ? "disabled" : ""} title="Sunucuları yeniden sormadan yalnız kümeleri yeniden oluşturur">Yeniden kümele</button>` : ""}
      ${w ? `<button id="ivPick" class="${iv.pick ? "on" : ""}" title="Sunucuları seçip yeni bir kümeye ya da var olan bir kümeye taşıyın">${iv.pick ? "Seçimi bitir" : "+ Yeni küme / taşı"}</button>` : ""}
      <button id="ivTopo" class="primary" title="Kümeleri kutu kutu, içlerinde VIP'ler ve sunucularla; aralarındaki trafikle çizer (seçili tarih aralığı)">Topoloji çiz</button>
      <button id="ivDep" title="Etiketli sunucuların uygulama görünümündeki eski bağımlılık çizimi">Eski çizim</button>
    </div>
    <div id="ivRunBox"></div>
    <div id="ivPickBar"></div>
    <div class="iv-grid" id="ivGrid"></div>
  </section>
  <div id="ivDrawerHost"></div>`;
  ivGrid(); ivRunBox(); ivPickBar();
  $("#ivQ").addEventListener("input", e => { iv.q = e.target.value; ivGrid(); });
  $("#ivRun")?.addEventListener("click", () => ivStart({ hepsi: false }));
  $("#ivAll")?.addEventListener("click", () => {
    if (confirm(`"${iv.tag}" etiketli ${d.toplam} sunucunun hepsi (elle düzeltilenler hariç) AI'a yeniden gönderilsin mi? Uzun sürebilir.`)) ivStart({ hepsi: true });
  });
  $("#ivClu")?.addEventListener("click", () => ivStart({ sadeceKumele: true }));
  $("#ivPick")?.addEventListener("click", () => { iv.pick = iv.pick ? null : new Set(); ivCloseDrawer(); ivRender(); });
  $("#ivTopo").addEventListener("click", () => ktRun(iv.tag));
  $("#ivDep").addEventListener("click", () => { ivStopPoll(); runApp("etiket:" + iv.tag, "topo"); });
  if (iv.sel) ivDrawer(iv.sel);
}

function ivGrid() {
  const q = iv.q.trim().toLocaleLowerCase("tr");
  const hit = s => !q || [s.ip, s.hostname, s.rol, s.muhafiz, ...(s.apps || [])].some(v => v && v.toLocaleLowerCase("tr").includes(q));
  const srv = s => {
    const g = s.guven == null ? "" : s.guven >= 75 ? "g3" : s.guven >= 50 ? "g2" : "g1";
    return `<div class="iv-srv${iv.sel === s.ip ? " on" : ""}${iv.pick ? " pk" : ""}${iv.pick?.has(s.ip) ? " picked" : ""}" data-ivip="${esc(s.ip)}" title="${s.guven != null ? `AI güveni %${s.guven}` : "Sınıflanmamış"}">
      <span class="dot ${g}"></span><span class="nm">${esc(s.hostname || s.ip)}${s.elle ? `<span class="el">elle</span>` : ""}</span><span class="ip">${esc(s.ip)}</span>
      <span class="rl">${esc(s.rol || (s.apps || []).join(", ") || "—")}</span></div>`;
  };
  const cards = iv.data.kumeler.map(k => {
    const segs = k.segmentler.map(sg => {
      const vips = sg.vipler.map(v => {
        const list = v.sunucular.filter(hit);
        const vipHit = q && [v.vip, v.app, v.pool].some(x => x && x.toLocaleLowerCase("tr").includes(q));
        const shown = vipHit ? v.sunucular : list;
        return shown.length ? `<div class="iv-vip"><div class="vh">VIP <b>${esc(v.vip)}</b>${v.app ? ` · ${esc(v.app)}` : ""}${v.pool ? ` · ${esc(v.pool)}` : ""}</div>${shown.map(srv).join("")}</div>` : "";
      }).join("");
      const free = sg.sunucular.filter(hit).map(srv).join("");
      if (!vips && !free) return "";
      const n = new Set([...sg.vipler.flatMap(v => v.sunucular.map(s => s.ip)), ...sg.sunucular.map(s => s.ip)]).size;
      return `<div class="iv-seg"><div><b>${esc(sg.ad)}</b><span>${sg.cidr ? esc(sg.cidr) + " · " : ""}${n} sunucu</span></div>${vips}${free}</div>`;
    }).join("");
    if (!segs) return "";
    const none = k.ad === "Sınıflanmamış";
    return `<article class="iv-card${none ? " none" : ""}" style="--h:${tagHue(k.ad)}">
      <header><h3>${esc(k.ad)}<span>${k.sunucu} sunucu</span></h3>${!none && iv.data.yazabilir ? `<div class="iv-acts"><a href="#" class="iv-merge" data-rename="${esc(k.ad)}" title="Kümenin adını değiştirir; yeniden kümelemede de bu ad korunur">✎ yeniden adlandır</a>${iv.data.kumeler.length > 2 ? `<a href="#" class="iv-merge" data-merge="${esc(k.ad)}" title="Bu kümedeki tüm sunucuları başka bir kümeye taşır (elle atanmış sayılır)">⇢ birleştir</a>` : ""}</div>` : ""}${k.aciklama ? `<p>${esc(k.aciklama)}</p>` : none ? `<p>AI henüz sınıflandırmadı. "AI ile sınıflandır" ile başlatın.</p>` : ""}</header>
      <div class="iv-body">${segs}</div></article>`;
  }).join("");
  $("#ivGrid").innerHTML = cards || `<div class="eg-empty">${iv.data.toplam ? "Aramaya uyan sunucu yok." : `"${esc(iv.tag)}" etiketli sunucu yok.`}</div>`;
}

// ---------- Arka plan işi ----------
async function ivStart(body) {
  try {
    const r = await fetch("api/islev/calistir", { method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ etiket: iv.tag, hepsi: false, sadeceKumele: false, ...body }) });
    const b = await r.json().catch(() => ({ error: `HTTP ${r.status}` }));
    if (!r.ok) throw new Error(b.error || `HTTP ${r.status}`);
    iv.data.durum = b; ivRender(); ivWatch();
  } catch (e) { alert(e.message); }
}

function ivWatch() {
  ivStopPoll();
  iv.poll = setTimeout(async () => {
    if (uiMode !== "topo" || !iv.data) return;
    try {
      const r = await fetch("api/islev/durum");
      const d = await r.json();
      const was = iv.data.durum?.calisiyor;
      iv.data.durum = d;
      if (!d.calisiyor) { if (was) await ivLoad(); return; }
      ivRunBox();
      // Ara sonuçlar da görünsün: her 30 sn'de bir haritayı tazele
      if ((iv.ticks = (iv.ticks || 0) + 1) % 10 === 0) { await ivLoad(); return; }
    } catch { /* bir sonraki turda yeniden */ }
    ivWatch();
  }, 3000);
}

function ivRunBox() {
  const d = iv.data?.durum, box = $("#ivRunBox");
  if (!box || !d || !d.asama) { if (box) box.innerHTML = ""; return; }
  const pct = d.toplam ? Math.round(100 * d.biten / d.toplam) : d.calisiyor ? 5 : 100;
  const t = s => s ? new Date(s).toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" }) : "";
  box.innerHTML = `<div class="iv-run">
    <div style="display:flex;justify-content:space-between;gap:10px;flex-wrap:wrap">
      <span>${d.calisiyor ? `<span class="spinner"></span> ` : ""}<b>${esc(d.asama)}</b>${d.etiket && d.etiket !== iv.tag ? ` · ${esc(d.etiket)}` : ""}
        ${d.toplam ? ` · ${fmt(d.biten)} / ${fmt(d.toplam)} sunucu` : ""}${d.hata ? ` · <span style="color:var(--err)">${d.hata} hata</span>` : ""}</span>
      <span style="color:var(--muted)">${esc(d.baslatan || "")} · ${t(d.baslangic)}${d.bitis ? "–" + t(d.bitis) : ""}
        ${d.calisiyor && iv.data.yazabilir ? ` <button id="ivStop" style="margin-left:8px">Durdur</button>` : ""}</span>
    </div>
    ${d.calisiyor || d.toplam ? `<div class="bar"><i style="width:${pct}%"></i></div>` : ""}
    ${d.mesaj ? `<div style="color:var(--err)">${esc(d.mesaj)}</div>` : ""}
    ${d.hatalar?.length ? `<details><summary>Hatalar (${d.hatalar.length})</summary><ul>${d.hatalar.map(h => `<li>${esc(h)}</li>`).join("")}</ul></details>` : ""}
  </div>`;
  $("#ivStop")?.addEventListener("click", async () => { await fetch("api/islev/durdur", { method: "POST" }); });
}

// ---------- Ayrıntı çekmecesi ----------
function ivFind(ip) {
  for (const k of iv.data.kumeler) for (const sg of k.segmentler) {
    const s = [...sg.sunucular, ...sg.vipler.flatMap(v => v.sunucular)].find(x => x.ip === ip);
    if (s) return { k, sg, s };
  }
  return null;
}

async function ivDrawer(ip) {
  iv.sel = ip;
  document.querySelectorAll(".iv-srv").forEach(e => e.classList.toggle("on", e.dataset.ivip === ip));
  const f = ivFind(ip), host = $("#ivDrawerHost");
  if (!f || !host) return;
  const kumeler = iv.data.kumeler.map(k => k.ad).filter(a => a !== "Sınıflanmamış");
  host.innerHTML = `<aside class="iv-drawer">
    <div class="dh"><div><h3>${esc(f.s.hostname || ip)}</h3><div class="eg-sub"><span style="font-family:Consolas,monospace">${esc(ip)}</span> · ${esc(f.sg.ad)}</div></div>
      <div style="display:flex;gap:6px"><a href="?ip=${encodeURIComponent(ip)}" style="padding:6px 12px;border-radius:6px;text-decoration:none;background:var(--in);color:#fff">IP topolojisi</a>
      <button id="ivClose">Kapat</button></div></div>
    <div class="db" id="ivDb"><div class="empty"><span class="spinner"></span> AI'a giden bilgiler toplanıyor…</div></div></aside>`;
  $("#ivClose").addEventListener("click", ivCloseDrawer);
  let p;
  try {
    const r = await fetch("api/islev/profil?" + new URLSearchParams({ ip }));
    p = await r.json().catch(() => ({ error: `HTTP ${r.status}` }));
    if (!r.ok) throw new Error(p.error || `HTTP ${r.status}`);
  } catch (e) { if (iv.sel === ip) $("#ivDb").innerHTML = `<div style="color:var(--err)">${esc(e.message)}</div>`; return; }
  if (iv.sel !== ip || !$("#ivDb")) return;
  const dt = p.detay, link = (no, url) => url ? `<a href="${esc(url)}" target="_blank" rel="noopener">${esc(no)}</a>` : esc(no);
  const list = (title, arr) => arr?.length ? `<div><h4>${title}</h4><ul>${arr.map(x => `<li>${esc(x)}</li>`).join("")}</ul></div>` : "";
  $("#ivDb").innerHTML = `
    <div><h4>AI kararı</h4>
      <dl><dt>Küme</dt><dd><b>${esc(p.kume || "Sınıflanmamış")}</b></dd>
        ${dt ? `<dt>İşlev</dt><dd>${esc(dt.islev || "—")}</dd><dt>Rol</dt><dd>${esc(dt.rol || "—")}</dd>
        <dt>Güven</dt><dd>%${dt.guven}</dd><dt>Kaynak</dt><dd>${esc(dt.kaynak || "—")} · ${esc(dt.tarih || "")}</dd>` : ""}</dl>
      ${dt?.gerekce ? `<div class="why" style="margin-top:8px">${esc(dt.gerekce)}</div>` : ""}
    </div>
    ${iv.data.yazabilir ? `<div><h4>Kümeyi düzelt</h4><div class="fix">
      <input id="ivFix" list="ivKumeList" placeholder="Küme adı" value="${esc(p.kume || "")}"><datalist id="ivKumeList">${kumeler.map(k => `<option value="${esc(k)}">`).join("")}</datalist>
      <button class="primary" id="ivFixSave">Kaydet</button>${dt?.kaynak?.startsWith("Elle") ? `<button id="ivFixClear" title="Elle atamayı kaldırır; sunucu bir sonraki çalıştırmada yeniden sınıflanır">Elle atamayı kaldır</button>` : ""}</div>
      <div class="eg-sub">Elle atanan küme AI'ca değiştirilmez.</div></div>` : ""}
    ${list("MSY sanal sunucu", p.msy)}${list("ÇSY sanal sunucu", p.csy)}${list("Envanter Kahini", p.kahin)}
    ${p.kurulum?.length ? `<div><h4>Sunucu kurulum talepleri</h4><ul>${p.kurulum.map(k => `<li>${link(k.no, k.link)} · ${esc(k.tarih || "")} · ${esc(k.baslik || "")}${k.aciklama ? ` — <b>${esc(k.aciklama)}</b>` : ""}</li>`).join("")}</ul></div>` : ""}
    ${p.talepler?.length ? `<div><h4>İş istekleri (${p.talepler.length})</h4><table><thead><tr><th>No</th><th>Tarih</th><th>Bu sunucu</th><th>Konu</th></tr></thead><tbody>
      ${p.talepler.map(t => `<tr title="${esc([t.kaynak && "Kaynak: " + t.kaynak, t.hedef && "Hedef: " + t.hedef, t.aciklama].filter(Boolean).join("\n"))}"><td>${link(t.no, t.link)}</td><td>${esc(t.tarih || "")}</td><td>${esc(t.yon || "")}</td><td>${esc(t.tur ? t.tur + " · " : "")}${esc(t.baslik || "")}</td></tr>`).join("")}</tbody></table></div>` : `<div class="eg-sub">SM'de bu sunucuyla eşleşen iş isteği bulunamadı.</div>`}
    ${p.hatalar?.length ? `<div style="color:var(--err);font-size:12px">${p.hatalar.map(esc).join("<br>")}</div>` : ""}
    <details><summary>AI'a giden metin</summary><pre>${esc(p.metin)}</pre></details>`;
  const save = async kume => {
    try {
      const r = await fetch("api/islev/duzelt", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ ips: [ip], kume }) });
      const b = await r.json().catch(() => ({ error: `HTTP ${r.status}` }));
      if (!r.ok) throw new Error(b.error || `HTTP ${r.status}`);
      await ivLoad();
    } catch (e) { alert(e.message); }
  };
  $("#ivFixSave")?.addEventListener("click", () => { const v = $("#ivFix").value.trim(); if (v) save(v); });
  $("#ivFixClear")?.addEventListener("click", () => save(null));
}

function ivCloseDrawer() {
  iv.sel = null;
  if ($("#ivDrawerHost")) $("#ivDrawerHost").innerHTML = "";
  document.querySelectorAll(".iv-srv.on").forEach(e => e.classList.remove("on"));
}

document.addEventListener("click", e => {
  const el = e.target.closest?.("[data-ivip]");
  if (!el || uiMode !== "topo") return;
  if (iv.pick) {   // seçim modunda tıklama seçer / bırakır (aynı sunucu birden çok VIP altında olabilir)
    const ip = el.dataset.ivip;
    iv.pick.has(ip) ? iv.pick.delete(ip) : iv.pick.add(ip);
    document.querySelectorAll(`.iv-srv[data-ivip="${CSS.escape(ip)}"]`).forEach(x => x.classList.toggle("picked", iv.pick.has(ip)));
    ivPickBar();
    return;
  }
  ivDrawer(el.dataset.ivip);
});
// Seçim çubuğu: seçilen sunucuları yeni bir kümeye (adı + isteğe bağlı açıklama) ya da var olan kümeye taşır.
function ivPickBar() {
  const box = $("#ivPickBar");
  if (!box) return;
  if (!iv.pick) { box.innerHTML = ""; return; }
  const n = iv.pick.size, names = iv.data.kumeler.map(k => k.ad).filter(a => a !== "Sınıflanmamış");
  if (!box.firstChild) {
    box.innerHTML = `<div class="eg-bar">
      <span><b id="ivPickN">0</b> sunucu seçili</span>
      <input id="ivPickName" list="ivPickList" placeholder="Yeni küme adı ya da var olan küme" maxlength="100">
      <datalist id="ivPickList">${names.map(k => `<option value="${esc(k)}">`).join("")}</datalist>
      <input id="ivPickDesc" placeholder="Açıklama (yeni küme için, isteğe bağlı)" maxlength="300" style="width:300px">
      <button class="add" id="ivPickGo">Kümeyi oluştur</button>
      <button class="clr" id="ivPickClr">Seçimi temizle</button>
    </div>`;
    const upd = () => {
      const v = $("#ivPickName").value.trim(), ex = names.some(k => k.toLocaleLowerCase("tr") === v.toLocaleLowerCase("tr"));
      $("#ivPickGo").textContent = ex ? "Bu kümeye taşı" : "Kümeyi oluştur";
      $("#ivPickDesc").hidden = ex;
    };
    $("#ivPickName").addEventListener("input", upd);
    $("#ivPickClr").addEventListener("click", () => { iv.pick.clear(); document.querySelectorAll(".iv-srv.picked").forEach(x => x.classList.remove("picked")); ivPickBar(); });
    $("#ivPickGo").addEventListener("click", async () => {
      const kume = $("#ivPickName").value.trim(), aciklama = $("#ivPickDesc").hidden ? "" : $("#ivPickDesc").value.trim();
      if (!iv.pick.size) { alert("Önce sunuculara tıklayarak seçin."); return; }
      if (!kume) { alert("Küme adı yazın."); $("#ivPickName").focus(); return; }
      if (kume === "Sınıflanmamış") { alert("Bu ad kullanılamaz."); return; }
      $("#ivPickGo").disabled = true;
      try {
        const r = await fetch("api/islev/duzelt", { method: "POST", headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ ips: [...iv.pick], kume, aciklama: aciklama || null }) });
        const b = await r.json().catch(() => ({ error: `HTTP ${r.status}` }));
        if (!r.ok) throw new Error(b.error || `HTTP ${r.status}`);
        iv.pick = null;
        await ivLoad();
      } catch (err) { alert(err.message); $("#ivPickGo").disabled = false; }
    });
    upd();
  }
  $("#ivPickN").textContent = n;
}

// Kümeyi başka bir kümeyle birleştir: kartın tüm sunucuları hedef kümeye elle atanır.
document.addEventListener("click", e => {
  const a = e.target.closest?.(".iv-merge[data-merge]");
  if (!a || uiMode !== "topo") return;
  e.preventDefault();
  const from = a.dataset.merge, head = a.closest("header");
  if (head.querySelector(".iv-mergebox")) return;
  const others = iv.data.kumeler.map(k => k.ad).filter(x => x !== from && x !== "Sınıflanmamış");
  head.insertAdjacentHTML("beforeend", `<div class="iv-mergebox"><select>${others.map(o => `<option>${esc(o)}</option>`).join("")}</select>
    <button class="primary">Taşı</button><button>Vazgeç</button></div>`);
  const box = head.querySelector(".iv-mergebox"), [sel] = box.getElementsByTagName("select"), [go, no] = box.getElementsByTagName("button");
  no.addEventListener("click", () => box.remove());
  go.addEventListener("click", async () => {
    const k = iv.data.kumeler.find(x => x.ad === from);
    const ips = [...new Set(k.segmentler.flatMap(sg => [...sg.sunucular, ...sg.vipler.flatMap(v => v.sunucular)]).map(s => s.ip))];
    if (!confirm(`"${from}" kümesindeki ${ips.length} sunucu "${sel.value}" kümesine taşınsın mı?`)) return;
    go.disabled = true;
    try {
      const r = await fetch("api/islev/duzelt", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ ips, kume: sel.value }) });
      const b = await r.json().catch(() => ({ error: `HTTP ${r.status}` }));
      if (!r.ok) throw new Error(b.error || `HTTP ${r.status}`);
      await ivLoad();
    } catch (err) { alert(err.message); go.disabled = false; }
  });
});
// Kümeyi yeniden adlandır
document.addEventListener("click", e => {
  const a = e.target.closest?.(".iv-merge[data-rename]");
  if (!a || uiMode !== "topo") return;
  e.preventDefault();
  const from = a.dataset.rename, head = a.closest("header");
  if (head.querySelector(".iv-mergebox")) return;
  head.insertAdjacentHTML("beforeend", `<div class="iv-mergebox"><input maxlength="100" value="${esc(from)}">
    <button class="primary">Kaydet</button><button>Vazgeç</button></div>`);
  const box = head.querySelector(".iv-mergebox"), [inp] = box.getElementsByTagName("input"), [go, no] = box.getElementsByTagName("button");
  inp.focus(); inp.select();
  no.addEventListener("click", () => box.remove());
  const save = async () => {
    const yeni = inp.value.trim();
    if (!yeni || yeni === from) { box.remove(); return; }
    if (iv.data.kumeler.some(k => k.ad.toLocaleLowerCase("tr") === yeni.toLocaleLowerCase("tr")) &&
        !confirm(`"${yeni}" adlı bir küme zaten var; iki küme birleşecek. Devam edilsin mi?`)) return;
    go.disabled = true;
    try {
      const r = await fetch("api/islev/yeniden-adlandir", { method: "POST", headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ etiket: iv.tag, eski: from, yeni }) });
      const b = await r.json().catch(() => ({ error: `HTTP ${r.status}` }));
      if (!r.ok) throw new Error(b.error || `HTTP ${r.status}`);
      await ivLoad();
    } catch (err) { alert(err.message); go.disabled = false; }
  };
  go.addEventListener("click", save);
  inp.addEventListener("keydown", ev => { if (ev.key === "Enter") save(); else if (ev.key === "Escape") { ev.stopPropagation(); box.remove(); } });
});
document.addEventListener("keydown", e => { if (e.key === "Escape" && iv.sel) ivCloseDrawer(); });
