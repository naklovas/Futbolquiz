// ============================================================================
// DeltaFlow · Sunucu etiketleme
// ERT_HOSTIPADDRESS tablosunun TAG kolonu: sunucuları ara, etiketleri gör, seçilenlere etiket ekle / kaldır.
// Ayrıca IP kartındaki "+ Etiket" düğmesi ve rozetlerdeki × (tek sunucu). topoloji.html'deki ortak
// yardımcıları ($, esc, fmt, tagMap) kullanır.
// ============================================================================

const etqState = { rows: [], total: 0, tags: [], canWrite: false, selected: new Set() };

async function runEtiket() {
  setUiMode("etiket");
  const q = $("#etqQ").value.trim(), tag = $("#etqTag").value, none = $("#etqNone").checked;
  history.replaceState(null, "", "?" + new URLSearchParams({ etiket: q }));
  $("#main").innerHTML = `<div class="panel empty"><span class="spinner"></span> Sunucular yükleniyor…</div>`;
  try {
    const p = new URLSearchParams({ ...(q ? { q } : {}), ...(tag ? { etiket: tag } : {}), ...(none ? { etiketsiz: "true" } : {}) });
    const r = await fetch("api/etiket/sunucular?" + p);
    const b = await r.json().catch(() => ({ error: `HTTP ${r.status}` }));
    if (!r.ok) throw new Error(b.error || `HTTP ${r.status}`);
    etqState.rows = b.sunucular; etqState.total = b.toplam; etqState.tags = b.etiketler; etqState.canWrite = b.yazabilir;
    etqState.selected = new Set([...etqState.selected].filter(ip => b.sunucular.some(s => s.ip === ip)));
    fillTagSelects();
    renderEtiket();
  } catch (e) {
    $("#main").innerHTML = `<div class="panel empty" style="color:var(--err)">${esc(e.message)}</div>`;
  }
}

function fillTagSelects() {
  const cur = $("#etqTag").value;
  $("#etqTag").innerHTML = `<option value="">tümü</option>` + etqState.tags.map(t => `<option value="${esc(t)}"${t === cur ? " selected" : ""}>${esc(t)}</option>`).join("");
}

function renderEtiket() {
  const s = etqState, w = s.canWrite;
  const chip = (ip, t) => `<span class="badge tagchip">${esc(t)}${w ? ` <a href="#" data-untag="${esc(t)}" data-ip="${esc(ip)}" title="Etiketi kaldır">×</a>` : ""}</span>`;
  const allOn = s.rows.length > 0 && s.rows.every(r => s.selected.has(r.ip));
  $("#main").innerHTML = `
    <section class="panel">
      <div class="toolbar">
        <h2 style="margin:0">Sunucu etiketleme</h2>
        <span class="small muted">${fmt(s.total)} sunucu${s.total > s.rows.length ? ` · ilk ${fmt(s.rows.length)} gösteriliyor, aramayı daraltın` : ""} · etiketler ERT_HOSTIPADDRESS tablosunun TAG kolonuna yazılır</span>
      </div>
      ${w ? `<div class="toolbar" style="gap:8px">
        <b class="small">${fmt(s.selected.size)} sunucu seçili</b>
        <input type="text" id="etqNew" list="etqTagList" placeholder="Etiket (ör. VERIAMBARI)" style="width:220px">
        <datalist id="etqTagList">${s.tags.map(t => `<option value="${esc(t)}">`).join("")}</datalist>
        <button type="button" class="primary" id="etqAdd"${s.selected.size ? "" : " disabled"}>Seçilenlere ekle</button>
        <button type="button" id="etqDel"${s.selected.size ? "" : " disabled"}>Seçilenlerden kaldır</button>
        <span class="small" id="etqMsg"></span>
      </div>` : `<div class="small muted">Etiketleri görebilirsiniz; yazma yetkiniz yok.</div>`}
      <div class="tbl-wrap" style="max-height:calc(100vh - 320px)"><table>
        <thead><tr>${w ? `<th style="width:32px"><input type="checkbox" id="etqAll"${allOn ? " checked" : ""} title="Hepsini seç"></th>` : ""}
          <th>IP</th><th>Sunucu</th><th>Uygulamalar</th><th>Segment</th><th>Etiketler</th>${w ? "<th></th>" : ""}</tr></thead>
        <tbody>${s.rows.map(r => `<tr>
          ${w ? `<td><input type="checkbox" data-sel="${esc(r.ip)}"${s.selected.has(r.ip) ? " checked" : ""}></td>` : ""}
          <td class="mono"><a href="${esc(ipTabUrl(r.ip))}" target="_blank" rel="noopener" title="Topolojisini yeni sekmede aç">${esc(r.ip)}</a></td>
          <td>${esc(r.hostname ?? "")}</td><td class="small">${esc(r.apps.join(", "))}</td><td class="small">${esc(r.segment ?? "")}</td>
          <td>${r.tags.map(t => chip(r.ip, t)).join(" ") || `<span class="muted small">—</span>`}</td>
          ${w ? `<td><button type="button" class="ghost small" data-tagip="${esc(r.ip)}">+ Etiket</button></td>` : ""}
        </tr>`).join("")}</tbody>
      </table></div>
    </section>`;
  if (!w) return;
  $("#etqAll")?.addEventListener("change", ev => { s.rows.forEach(r => ev.target.checked ? s.selected.add(r.ip) : s.selected.delete(r.ip)); renderEtiket(); });
  document.querySelectorAll("[data-sel]").forEach(cb => cb.addEventListener("change", () => {
    cb.checked ? s.selected.add(cb.dataset.sel) : s.selected.delete(cb.dataset.sel);
    $("#etqAdd").disabled = $("#etqDel").disabled = !s.selected.size;
    document.querySelector("#main .toolbar + .toolbar b").textContent = `${fmt(s.selected.size)} sunucu seçili`;
  }));
  $("#etqAdd").addEventListener("click", () => applyTag([...s.selected], $("#etqNew").value, null));
  $("#etqDel").addEventListener("click", () => applyTag([...s.selected], null, $("#etqNew").value));
}

// Etiket ekle / kaldır → ERT TAG kolonu. Dönen sunucularla tablo, IP kartı ve Topoloji listesi güncellenir.
async function applyTag(ips, add, remove) {
  if (!ips.length) return;
  const tag = (add ?? remove ?? "").trim();
  if (!tag) { alert("Etiket yazın (ör. VERIAMBARI)."); return; }
  const msg = $("#etqMsg");
  if (msg) msg.textContent = "Kaydediliyor…";
  try {
    const r = await fetch("api/etiket", { method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ ips, ...(add ? { ekle: tag } : { kaldir: tag }) }) });
    const b = await r.json().catch(() => ({ error: `HTTP ${r.status}` }));
    if (!r.ok) throw new Error(b.error || `HTTP ${r.status}`);
    tagMap ??= {};
    for (const srv of b.sunucular) {
      tagMap[srv.ip] = srv.tags;
      const row = etqState.rows.find(x => x.ip === srv.ip);
      if (row) row.tags = srv.tags;
    }
    if (add && !etqState.tags.includes(tag.toLocaleUpperCase("tr"))) etqState.tags.push(tag.toLocaleUpperCase("tr"));
    loadTopoTags?.(true);
    if (uiMode === "etiket") { renderEtiket(); fillTagSelects(); const m = $("#etqMsg"); if (m) m.textContent = `${fmt(b.guncellenen)} satır güncellendi.`; }
    else if (uiMode === "ip" && state.data) render();
  } catch (e) {
    if (msg) msg.textContent = ""; alert("Etiket kaydedilemedi: " + e.message);
  }
}

// Tek sunucu: "+ Etiket" düğmesi ve rozetteki × (etiketleme tablosu ve IP kartı)
document.addEventListener("click", ev => {
  const add = ev.target.closest("[data-tagip]");
  if (add) {
    const t = prompt(`${add.dataset.tagip} için etiket (ör. VERIAMBARI):`, "");
    if (t?.trim()) applyTag([add.dataset.tagip], t.trim(), null);
    return;
  }
  const del = ev.target.closest("[data-untag]");
  if (del) {
    ev.preventDefault();
    if (confirm(`${del.dataset.ip} sunucusundan "${del.dataset.untag}" etiketi kaldırılsın mı?`)) applyTag([del.dataset.ip], null, del.dataset.untag);
  }
});
