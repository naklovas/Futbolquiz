// ============================================================================
// DeltaFlow · Kurum seçimi — KonsolideSegmentler4.Domain (değişken adları "tenant" kaldı)
// Topoloji sekmesinde çoklu seçimli açılır kutu: arama, "Tümü", kurum başına sunucu sayısı. Seçim tarayıcıda hatırlanır.
// Boş seçim = tümü. İşlev haritası ve küme topolojisi tenant parametresiyle süzülür; birden çok kurum gösterilirse
// ekranlar kurum bazında gruplar, tek kurum seçiliyse gruplama yapılmaz.
// ============================================================================
const tn = { list: [], sel: new Set(), tag: null, open: false };
try { (JSON.parse(localStorage.getItem("df.tenant") || "[]")).forEach(t => tn.sel.add(t)); } catch { /* yok */ }

(function tnStyle() {
  const css = `
  .tn-wrap { position:relative; }
  .tn-btn { min-width:200px; text-align:left; display:flex; justify-content:space-between; gap:8px; align-items:center; }
  .tn-btn::after { content:"▾"; color:var(--muted); }
  .tn-pop { position:absolute; top:calc(100% + 4px); left:0; z-index:40; width:300px; background:var(--panel); border:1px solid var(--line);
    border-radius:10px; box-shadow:0 12px 32px rgba(0,0,0,.18); padding:8px; display:grid; gap:6px; color:var(--text); }
  .tn-pop input[type=search] { width:100%; box-sizing:border-box; }
  .tn-list { max-height:300px; overflow:auto; display:grid; gap:1px; }
  .tn-list label { display:flex; gap:8px; align-items:center; padding:5px 6px; border-radius:6px; font-size:13px; color:var(--text); cursor:pointer; }
  .tn-list label:hover { background:var(--in-soft); }
  .tn-list label span { flex:1; } .tn-list label i { font-style:normal; color:var(--muted); font-size:12px; }
  .tn-list label.all { border-bottom:1px solid var(--line); border-radius:6px 6px 0 0; font-weight:600; }
  .tn-foot { display:flex; justify-content:flex-end; gap:6px; }`;
  const st = document.createElement("style"); st.textContent = css; document.head.appendChild(st);
})();

const tenantParam = () => [...tn.sel].join(",");
function tnSave() { try { localStorage.setItem("df.tenant", JSON.stringify([...tn.sel])); } catch { /* yok */ } }
function tnLabel() {
  if (!tn.sel.size) return "Tümü";
  if (tn.sel.size === 1) return [...tn.sel][0];
  return `${tn.sel.size} kurum`;
}

// Etiketin kurumlarını yükler; listede olmayan eski seçimler bırakılır
async function tnLoad(tag) {
  if (tn.tag === tag && tn.list.length) return;
  tn.tag = tag;
  try {
    const r = await fetch("api/tenantlar?" + new URLSearchParams(tag ? { etiket: tag } : {}));
    tn.list = r.ok ? await r.json() : [];
  } catch { tn.list = []; }
  const names = new Set(tn.list.map(t => t.ad));
  [...tn.sel].forEach(t => { if (!names.has(t)) tn.sel.delete(t); });
  tnRender();
}

function tnRender() {
  const host = $("#tenantBox");
  if (!host) return;
  host.innerHTML = `<button type="button" class="tn-btn" id="tnBtn" title="KonsolideSegmentler4 · Domain">${esc(tnLabel())}</button>
    ${tn.open ? `<div class="tn-pop" id="tnPop">
      <input type="search" id="tnQ" placeholder="Kurum ara…">
      <div class="tn-list" id="tnList"></div>
      <div class="tn-foot"><button type="button" id="tnCancel">Vazgeç</button><button type="button" class="primary" id="tnOk">Uygula</button></div>
    </div>` : ""}`;
  $("#tnBtn").addEventListener("click", e => { e.stopPropagation(); tn.open = !tn.open; tn.draft = new Set(tn.sel); tnRender(); });
  if (!tn.open) return;
  const list = () => {
    const q = ($("#tnQ").value || "").toLocaleLowerCase("tr");
    const all = !tn.draft.size;
    $("#tnList").innerHTML = `<label class="all"><input type="checkbox" data-tn="*" ${all ? "checked" : ""}><span>Tümü</span><i>${fmt(tn.list.reduce((a, t) => a + t.sunucu, 0))}</i></label>` +
      tn.list.filter(t => !q || t.ad.toLocaleLowerCase("tr").includes(q)).map(t =>
        `<label><input type="checkbox" data-tn="${esc(t.ad)}" ${tn.draft.has(t.ad) ? "checked" : ""}><span>${esc(t.ad)}</span><i>${fmt(t.sunucu)}</i></label>`).join("");
  };
  list();
  $("#tnQ").addEventListener("input", list);
  $("#tnQ").focus();
  $("#tnPop").addEventListener("click", e => e.stopPropagation());
  $("#tnList").addEventListener("change", e => {
    const v = e.target.dataset.tn;
    if (v === "*") tn.draft.clear();
    else if (e.target.checked) tn.draft.add(v); else tn.draft.delete(v);
    // hepsi tek tek seçildiyse "Tümü" sayılır
    if (tn.draft.size === tn.list.length) tn.draft.clear();
    list();
  });
  $("#tnCancel").addEventListener("click", () => { tn.open = false; tnRender(); });
  $("#tnOk").addEventListener("click", () => {
    const changed = [...tn.draft].sort().join() !== [...tn.sel].sort().join();
    tn.sel = new Set(tn.draft); tn.open = false; tnSave(); tnRender();
    if (changed) tnApply();
  });
}
document.addEventListener("click", () => { if (tn.open) { tn.open = false; tnRender(); } });

// Seçim değişince açık olan görünüm yeniden yüklenir
function tnApply() {
  if (uiMode !== "topo") return;
  if (iv.ver?.id) iv.ver.id = null;   // kayıtlı versiyon başka kurum seçimiyle gösterilemez → canlıya geç
  if (kt.data && document.getElementById("ktSvg")) ktRun(kt.tag);
  else if (iv.tag) ivLoad();
}
