// ============================================================================
// DeltaFlow · DB'ye gelenler (Dataskope)
// Seçilen aralıkta veritabanları; birine tıklayınca ona bağlanan sunucular (segment, uygulama, DB / OS kullanıcıları,
// programlar). topoloji.html'deki ortak yardımcıları ($, esc, fmt, segCell, abort, ipTabUrl, expectList) kullanır.
// ============================================================================
const dbState = { body: null, sel: null, sort: "count" };

async function runDbGelen() {
  setUiMode("db");
  const filtre = $("#dbFiltre").value.trim(), tur = $("#dbTur").value.trim();
  const params = new URLSearchParams({ ...(filtre ? { filtre } : {}), ...(tur ? { tur } : {}), start: $("#start").value, end: $("#end").value });
  history.replaceState(null, "", "?" + new URLSearchParams({ db: "", ...Object.fromEntries(params) }));
  abort?.abort();
  abort = new AbortController();
  $("#go").disabled = true; $("#cancel").hidden = false;
  $("#main").innerHTML = `<div class="panel empty"><span class="spinner"></span> Dataskope'tan veritabanı oturumları okunuyor (kayıt çoksa birkaç dakika sürebilir; filtre ve kısa aralık hızlandırır)…</div>`;
  try {
    const r = await fetch("api/dbgelen?" + params, { signal: abort.signal });
    const body = await r.json().catch(() => ({ error: `HTTP ${r.status}: sunucu JSON yerine başka bir yanıt döndü (zaman aşımı, yetki ya da sunucu hata sayfası olabilir)` }));
    if (!r.ok) throw new Error(body.error || `HTTP ${r.status}`);
    expectList(body, "veritabanlari");
    dbState.body = body; dbState.sel = body.veritabanlari.length === 1 ? body.veritabanlari[0].key : null;
    renderDbGelen();
  } catch (e) {
    const msg = e.name === "AbortError" ? "Sorgu iptal edildi." : e.message;
    $("#main").innerHTML = `<div class="panel empty" style="color:var(--err)">${esc(msg)}</div>`;
  } finally {
    $("#go").disabled = false; $("#cancel").hidden = true;
  }
}

const dbTitle = d => [d.instance, d.dbNames.join(", ")].filter(Boolean).join(" · ") || d.serverIp || "?";
const ipA = ip => `<a class="mono" href="${esc(ipTabUrl(ip))}" target="_blank" rel="noopener" title="Topolojisini yeni sekmede aç">${esc(ip)}</a>`;
const segApp = (seg, apps) => `${seg ? segCell(seg) : `<span class="unk">segment yok</span>`}${apps.length ? `<div class="small">${esc(apps.join(", "))}</div>` : ""}`;

function renderDbGelen() {
  const b = dbState.body;
  const pill = (name, s) => `<span class="pill ${s.ok ? "ok" : "err"}" title="${esc(s.error ?? "")}"><b>${name}</b> ${s.ok ? `${fmt(s.rows)} kayıt${s.elapsedMs != null ? ` · ${(s.elapsedMs / 1000).toFixed(1)} sn` : ""}` : esc(s.error)}</span>`;
  const msgs = [...b.dataskope.messages.map(m => "Dataskope: " + m)];
  const sorters = { count: (x, y) => y.count - x.count, clients: (x, y) => y.clientCount - x.clientCount, last: (x, y) => String(y.last).localeCompare(String(x.last)), name: (x, y) => dbTitle(x).localeCompare(dbTitle(y)) };
  const list = [...b.veritabanlari].sort(sorters[dbState.sort]);
  const th = (k, t, cls = "") => `<th class="${cls}" style="cursor:pointer" data-sort="${k}">${t}${dbState.sort === k ? " ▾" : ""}</th>`;
  const dbRows = list.map(d => `<tr data-db="${esc(d.key)}" style="cursor:pointer" class="${dbState.sel === d.key ? "sel-row" : ""}">
      <td>${esc(d.dbType ?? "")}</td><td><b>${esc(dbTitle(d))}</b><div class="small muted">${esc(d.machine ?? "")}</div></td>
      <td class="mono">${esc(d.serverIp ?? "")}${d.port ? ":" + esc(d.port) : ""}</td><td>${segApp(d.segment, d.apps)}</td>
      <td class="num">${fmt(d.clientCount)}</td><td class="num">${fmt(d.count)}</td><td class="mono">${esc(d.last ?? "")}</td></tr>`).join("");
  const sel = b.veritabanlari.find(d => d.key === dbState.sel);
  const detail = sel ? `<section class="panel" id="dbDetail">
      <div class="toolbar"><h2 style="margin:0">${esc(dbTitle(sel))} <span class="muted" style="font-weight:400">· ${esc(sel.dbType ?? "")} · ${sel.serverIp ? ipA(sel.serverIp) : ""}${sel.port ? ":" + esc(sel.port) : ""}</span></h2>
        <button type="button" id="dbClose">Kapat</button></div>
      <div class="small muted" style="margin:-4px 0 8px">Bu veritabanına bağlanan ${fmt(sel.clientCount)} sunucu. IP'ye tıklayınca topolojisi, "Erişim" ile kaynak → DB erişim ayrıntısı yeni sekmede açılır.</div>
      <div class="tbl-wrap"><table>
        <thead><tr><th>Gelen sunucu</th><th>Makine</th><th>Segment / uygulama</th><th>DB kullanıcısı</th><th>OS kullanıcısı</th><th>Program</th><th class="num">Kayıt</th><th>İlk</th><th>Son</th><th></th></tr></thead>
        <tbody>${sel.clients.map(c => `<tr><td>${ipA(c.ip)}</td><td>${esc(c.host ?? "")}</td><td>${segApp(c.segment, c.apps)}</td>
          <td><b>${esc(c.dbUsers.join(", "))}</b></td><td>${esc(c.osUsers.join(", "))}</td><td>${esc(c.programs.join(", "))}</td>
          <td class="num">${fmt(c.count)}</td><td class="mono">${esc(c.first ?? "")}</td><td class="mono"><b>${esc(c.last ?? "")}</b></td>
          <td>${sel.serverIp ? `<a href="?${esc(new URLSearchParams({ src: c.ip, dst: sel.serverIp, ...(sel.port ? { port: sel.port } : {}) }))}" target="_blank" rel="noopener">Erişim</a>` : ""}</td></tr>`).join("")}</tbody>
      </table></div></section>` : "";

  $("#main").innerHTML = `
    <section class="panel"><div class="status">${pill("Dataskope", b.dataskope)}${pill("Envanter", b.envanter)}
      <span class="pill">Aralık ${esc(b.pencere)}</span><span class="pill" title="Dataskope'a gönderilen sorgu">Sorgu: <span class="mono">${esc(b.sorgu || "(filtresiz, tüm veritabanları)")}</span></span>
      <span class="pill">Toplam ${(b.elapsedMs / 1000).toFixed(1)} sn</span></div>
      ${b.eksik ? `<div class="msgs" style="color:var(--warn)">Toplam ${fmt(b.toplam)} kaydın ilk ${fmt(b.okunan)} tanesi okundu; liste eksik olabilir. Filtre yazın ya da aralığı kısaltın.</div>` : ""}
      ${msgs.length ? `<div class="msgs">${esc(msgs.join("\n"))}</div>` : ""}</section>
    <section class="panel"><h2>Veritabanları (${fmt(b.veritabanlari.length)})</h2>
      ${b.veritabanlari.length ? `<div class="small muted" style="margin:-4px 0 8px">Satıra tıklayınca bu veritabanına gelen sunucular açılır.</div>
      <div class="tbl-wrap" style="max-height:45vh"><table><thead><tr><th>Tür</th>${th("name", "Instance · DB")}<th>Sunucu</th><th>Segment / uygulama</th>${th("clients", "Gelen sunucu", "num")}${th("count", "Kayıt", "num")}${th("last", "Son")}</tr></thead>
      <tbody>${dbRows}</tbody></table></div>` : `<div class="muted">Bu aralıkta ve filtrede veritabanı oturumu yok.</div>`}</section>
    ${detail}`;
  document.querySelectorAll("[data-db]").forEach(tr => tr.addEventListener("click", () => {
    dbState.sel = dbState.sel === tr.dataset.db ? null : tr.dataset.db; renderDbGelen();
    $("#dbDetail")?.scrollIntoView({ behavior: "smooth", block: "start" });
  }));
  document.querySelectorAll("[data-sort]").forEach(h => h.addEventListener("click", () => { dbState.sort = h.dataset.sort; renderDbGelen(); }));
  $("#dbClose")?.addEventListener("click", () => { dbState.sel = null; renderDbGelen(); });
}
