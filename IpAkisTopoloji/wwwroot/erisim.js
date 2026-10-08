// ============================================================================
// DeltaFlow · Erişim sorgula
// Kaynak IP'den hedef IP'ye (isteğe bağlı port) erişim olmuş mu: Firewall izin / engel, son ne zaman,
// kural, NAT; Carbon Black bağlantıyı görmüş mü; iki ucun segmenti ve uygulamaları.
// topoloji.html'deki ortak yardımcıları ($, esc, fmt, segCell, toLocalInput, abort, ipTabUrl) kullanır.
// ============================================================================

// Erişim sorusu genelde geçmişe bakar: aralık 1 günden kısaysa son 7 güne genişletilir.
function accDefaultRange() {
  const s = new Date($("#start").value), e = new Date($("#end").value);
  if (!(e - s > 24 * 3600e3)) $("#start").value = toLocalInput(new Date((isNaN(e) ? Date.now() : +e) - 7 * 24 * 3600e3));
}

async function runAccess() {
  setUiMode("erisim");
  const src = $("#accSrc").value.trim(), dst = $("#accDst").value.trim(), port = $("#accPort").value.trim();
  if (!src || !dst) { alert("Kaynak ve hedef IP girin."); return; }
  const srcs = [$("#srcSplunk").checked && "splunk", $("#srcFw").checked && "firewall"].filter(Boolean);
  if (!srcs.length) { alert("Splunk (Carbon Black) ya da Firewall kaynaklarından en az birini seçin."); return; }
  const params = new URLSearchParams({ src, dst, ...(port ? { port } : {}), start: $("#start").value, end: $("#end").value, sources: srcs.join(",") });
  history.replaceState(null, "", "?" + new URLSearchParams({ src, dst, ...(port ? { port } : {}), start: params.get("start"), end: params.get("end") }));

  abort?.abort();
  abort = new AbortController();
  $("#go").disabled = true; $("#cancel").hidden = false;
  $("#main").innerHTML = `<div class="panel empty"><span class="spinner"></span> ${esc(src)} → ${esc(dst)}${port ? ":" + esc(port) : ""} erişimi Firewall ve Carbon Black kayıtlarında aranıyor…</div>`;
  try {
    const r = await fetch("api/erisim?" + params, { signal: abort.signal });
    const body = await r.json().catch(() => ({ error: `HTTP ${r.status}: sunucu JSON yerine başka bir yanıt döndü (zaman aşımı, yetki ya da sunucu hata sayfası olabilir)` }));
    if (!r.ok) throw new Error(body.error || `HTTP ${r.status}`);
    expectList(body, "firewall");
    renderAccess(body);
  } catch (e) {
    const msg = e.name === "AbortError" ? "Sorgu iptal edildi." : e.message;
    $("#main").innerHTML = `<div class="panel empty" style="color:var(--err)">${esc(msg)}</div>`;
  } finally {
    $("#go").disabled = false; $("#cancel").hidden = true;
  }
}

function renderAccess(d) {
  const pill = (name, s) => {
    const cls = s.ok ? "ok" : s.error === "Sorgulanmadı" ? "skip" : "err";
    const info = s.ok ? `${fmt(s.rows)} satır${s.elapsedMs != null ? ` · ${(s.elapsedMs / 1000).toFixed(1)} sn` : ""}` : esc(s.error);
    return `<span class="pill ${cls}" title="${esc(s.error ?? "")}"><b>${name}</b> ${info}</span>`;
  };
  const msgs = [...d.firewallDurum.messages.map(m => "Firewall: " + m), ...d.carbonBlackDurum.messages.map(m => "Carbon Black: " + m)];
  const ipLink = ip => `<a class="mono" href="${esc(ipTabUrl(ip))}" target="_blank" rel="noopener" title="Topolojisini yeni sekmede aç">${esc(ip)}</a>`;
  const uc = (title, u) => `<div class="panel" style="margin:0">
      <div class="small muted">${title}</div>
      <div style="font-size:20px;font-weight:800;margin:2px 0 6px">${ipLink(u.ip)}</div>
      <div>${u.segment ? segCell(u.segment) : `<span class="unk">Segment envanterinde yok</span>`}</div>
      <div class="small" style="margin-top:4px">${u.apps.length ? esc(u.apps.join(", ")) : `<span class="muted">uygulama kaydı yok</span>`}</div>
      ${u.vip.length ? `<div class="small" style="margin-top:4px;color:var(--vip)">${esc(u.vip.slice(0, 6).join(" · "))}${u.vip.length > 6 ? ` +${u.vip.length - 6}` : ""}</div>` : ""}
    </div>`;
  const act = (a, denied) => `<span class="badge ${denied ? "deny" : "allow"}">${esc(a)}</span>`;
  const list = l => esc((l ?? []).join(", "));

  const fwTable = d.firewall.length ? `<div class="tbl-wrap"><table>
      <thead><tr><th>Port</th><th>Sonuç</th><th class="num">Oturum</th><th>İlk</th><th>Son</th><th>Kural</th><th>Kapanma nedeni</th><th>Uygulama (App-ID)</th><th>NAT</th><th>Cihaz</th></tr></thead>
      <tbody>${d.firewall.map(x => `<tr><td class="mono">${esc(x.port)}</td><td>${act(x.action, x.denied)}</td><td class="num">${fmt(x.count)}</td>
        <td class="mono">${esc(x.first ?? "")}</td><td class="mono"><b>${esc(x.last ?? "")}</b></td><td>${list(x.rules)}</td><td>${list(x.reasons)}</td>
        <td>${list(x.apps)}</td><td class="mono">${list(x.nat)}</td><td>${list(x.devices)}</td></tr>`).join("")}</tbody></table></div>`
    : `<div class="muted">${d.firewallDurum.ok ? "Bu aralıkta firewall kaydı yok." : "Firewall sorgulanmadı ya da hata verdi."}</div>`;
  const olayTable = d.sonOlaylar.length ? `<div class="tbl-wrap" style="max-height:360px"><table>
      <thead><tr><th>Zaman</th><th>Kaynak port</th><th>Hedef</th><th>Port</th><th>NAT</th><th>Sonuç</th><th>Kural</th><th>Kapanma nedeni</th><th>Cihaz</th></tr></thead>
      <tbody>${d.sonOlaylar.map(o => `<tr><td class="mono">${esc(o.time)}</td><td class="mono">${esc(o.srcPort)}</td><td class="mono">${esc(o.destIp)}</td>
        <td class="mono">${esc(o.destPort)}</td><td class="mono">${esc(o.natIp ?? "")}</td><td>${act(o.action, o.denied)}</td><td>${esc(o.rule ?? "")}</td>
        <td>${esc(o.reason ?? "")}</td><td>${esc(o.device ?? "")}</td></tr>`).join("")}</tbody></table></div>` : "";
  const cbTable = d.carbonBlack.length ? `<div class="tbl-wrap"><table>
      <thead><tr><th>Port</th><th>Süreç</th><th>Gören ajan</th><th class="num">Olay</th><th>Son</th></tr></thead>
      <tbody>${d.carbonBlack.map(c => `<tr><td class="mono">${esc(c.port)}</td><td>${esc(c.process)}</td>
        <td>${c.yon === "outbound" ? "kaynak (giden)" : c.yon === "inbound" ? "hedef (gelen)" : esc(c.yon)}</td><td class="num">${fmt(c.count)}</td><td class="mono"><b>${esc(c.last ?? "")}</b></td></tr>`).join("")}</tbody></table></div>`
    : `<div class="muted">${d.carbonBlackDurum.ok ? "Carbon Black bu bağlantıyı görmemiş (ajan yoksa görünmez)." : "Carbon Black sorgulanmadı ya da hata verdi."}</div>`;

  $("#main").innerHTML = `
    <section class="panel"><div class="status">${pill("Firewall", d.firewallDurum)}${pill("Carbon Black", d.carbonBlackDurum)}
      ${pill("Envanter", d.envanter)}<span class="pill">Aralık ${esc(d.pencere)}</span><span class="pill">Toplam ${(d.elapsedMs / 1000).toFixed(1)} sn</span></div>
      ${msgs.length ? `<div class="msgs">${esc(msgs.join("\n"))}</div>` : ""}</section>
    <section class="panel">
      <div class="verdict ${esc(d.kararTur)}"><div class="big">${esc(d.karar)}</div><div>${esc(d.kararDetay)}</div></div>
      <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(320px,1fr));gap:12px;margin-top:12px">${uc("Kaynak", d.kaynak)}${uc(`Hedef${d.port ? " · port " + esc(d.port) : ""}`, d.hedef)}</div>
    </section>
    <section class="panel"><h2>Firewall (port ve sonuç bazında)</h2>${fwTable}</section>
    ${olayTable ? `<section class="panel"><h2>Son ${d.sonOlaylar.length} firewall olayı</h2>${olayTable}</section>` : ""}
    <section class="panel"><h2>Carbon Black (uç nokta ajanının gördüğü bağlantılar)</h2>${cbTable}</section>`;
}
