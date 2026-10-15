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
  const srcs = [$("#srcSplunk").checked && "splunk", $("#srcFw").checked && "firewall", $("#srcF5").checked && "f5", $("#srcOp").checked && "opinnate"].filter(Boolean);
  if (!srcs.length) { alert("En az bir kaynak seçin."); return; }
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
  const msgs = [...d.firewallDurum.messages.map(m => "Firewall: " + m), ...d.carbonBlackDurum.messages.map(m => "Carbon Black: " + m),
    ...(d.wafDurum?.messages ?? []).map(m => "F5: " + m), ...(d.opinnateDurum?.messages ?? []).map(m => "Opinnate: " + m)];
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
  // F5 WAF: sonuç bazında özet + son olaylar (engellendi kırmızı, doğrulama / ihlal turuncu)
  const wafCls = r => r === "engellendi" ? "deny" : r.startsWith("geçti") || r.startsWith("izlendi") ? "allow" : "";
  const wf = d.waf ?? [], wo = d.wafOlaylar ?? [];
  const wafTable = wf.length ? `<div class="tbl-wrap"><table>
      <thead><tr><th>Sonuç</th><th class="num">İstek</th><th>İlk</th><th>Son</th><th>VIP (virtual server)</th><th>Saldırı / ihlal</th><th>URL</th><th>Support ID</th><th>Engelleyen</th></tr></thead>
      <tbody>${wf.map(x => `<tr><td><span class="badge ${wafCls(x.sonuc)}">${esc(x.sonuc)}</span></td><td class="num">${fmt(x.count)}</td>
        <td class="mono">${esc(x.first ?? "")}</td><td class="mono"><b>${esc(x.last ?? "")}</b></td><td>${esc(x.vsNames.join(", "))}${x.destPorts.length ? ` <span class="muted">:${esc(x.destPorts.join(", :"))}</span>` : ""}</td>
        <td>${esc([...x.attackTypes, ...x.violations].join(", "))}</td><td class="mono small">${x.uris.map(esc).join("<br>")}</td>
        <td class="mono">${esc(x.supportIds.join(", "))}</td><td>${esc(x.enforcedBy.join(", "))}</td></tr>`).join("")}</tbody></table></div>
      ${wo.length ? `<details style="margin-top:8px"><summary>Son ${wo.length} F5 olayı</summary><div class="tbl-wrap"><table>
        <thead><tr><th>Zaman</th><th>Sonuç</th><th>Metot</th><th>URL</th><th>Port</th><th>VIP</th><th>Saldırı</th><th>İhlal</th><th>HTTP</th><th>Support ID</th></tr></thead>
        <tbody>${wo.map(o => `<tr><td class="mono">${esc(o.time)}</td><td><span class="badge ${wafCls(o.sonuc)}">${esc(o.sonuc)}</span></td><td>${esc(o.method ?? "")}</td>
          <td class="mono small">${esc(o.uri ?? "")}</td><td class="mono">${esc(o.destPort ?? "")}</td><td>${esc(o.vsName ?? "")}</td><td>${esc(o.attackType ?? "")}</td>
          <td>${esc(o.violations ?? "")}</td><td>${esc(o.respCode ?? "")}</td><td class="mono">${esc(o.supportId ?? "")}</td></tr>`).join("")}</tbody></table></div></details>` : ""}`
    : `<div class="muted">${d.wafDurum?.ok ? "F5'te bu kaynaktan bu hedefe güvenlik kaydı yok (hedef WAF arkasındaki bir VIP değilse beklenen durum)." : "F5 sorgulanmadı ya da hata verdi."}</div>`;
  // Opinnate: yol üzerindeki her cihaz ve eşleşen kural (tanımlı kural, trafikten bağımsız)
  const opOn = d.opinnateDurum && d.opinnateDurum.error !== "Sorgulanmadı";
  const isAny = l => !l.length || l.some(x => ["any", "all", "0.0.0.0/0"].includes(String(x).toLowerCase()));
  const izinAct = a => ["accept", "allow", "permit", "pass"].includes(a);
  const opAct = k => !k ? `<span class="badge">kural yok</span>` : !k.aktif ? `<span class="badge">${esc(k.action)} (pasif)</span>`
    : izinAct(k.action) ? `<span class="badge allow">${esc(k.action)}</span>`
    : `<span class="badge deny">${esc(k.action)}</span>${isAny(k.source) && isAny(k.destination) && isAny(k.service)
        ? `<div class="small muted" title="Kural tabanının sonundaki any/any/any engel: bu trafiğe izin veren özel bir kural yok">varsayılan engel</div>` : ""}`;
  const opKararTxt = { "izin": "Kural izin veriyor", "engel": "Kural engelliyor", "kısmi": "Bazı portlar izinli, bazıları engelli",
    "kural yok": "Kural yok", "yol yok": "Yol bulunamadı", "port yok": "Kural kontrolü için port girin" }[d.opinnateKarar] ?? "";
  const opRows = (d.opinnate ?? []).flatMap(y => y.adimlar.map(a => ({ ...a, sorgu: y.sorgu })));
  const opMulti = new Set(opRows.map(a => a.sorgu)).size > 1;
  const opTable = opRows.length ? `<div class="small muted" style="margin:-4px 0 8px">Servis: <span class="mono">${esc(d.opinnateServis ?? "")}</span>${!d.port ? " (port girilmediği için firewall / Carbon Black'te görülen portlar)" : ""} · ${esc(opKararTxt)} · yol üzerindeki cihazlar sırayla</div>
    <div class="tbl-wrap"><table>
      <thead><tr>${opMulti ? "<th>Servis</th>" : ""}<th>#</th><th>Cihaz</th><th>Giriş → çıkış</th><th>Sonuç</th><th>Kural</th><th>Kaynak</th><th>Hedef</th><th>Servis</th><th>Açıklama</th><th>Diğer</th></tr></thead>
      <tbody>${opRows.map((a, i) => { const k = a.kural; const first = i === 0 || opRows[i - 1].sorgu !== a.sorgu;
        const n = opRows.slice(0, i + 1).filter(x => x.sorgu === a.sorgu).length;
        return `<tr${opMulti && first && i ? ` style="border-top:2px solid var(--line)"` : ""}>${opMulti ? `<td class="mono">${first ? esc(a.sorgu) : ""}</td>` : ""}<td class="num">${n}</td><td><b>${esc(a.cihaz)}</b></td>
        <td class="small">${esc(a.giris.join(", "))} → ${esc(a.cikis.join(", "))}</td><td>${opAct(k)}</td>
        <td class="mono">${k ? esc(k.policyId ?? k.id ?? "") : ""}</td>
        <td class="small">${k ? (k.srcNegate ? "<b>DEĞİL</b> " : "") + list(k.source) : ""}</td>
        <td class="small">${k ? (k.dstNegate ? "<b>DEĞİL</b> " : "") + list(k.destination) : ""}</td>
        <td class="small">${k ? list(k.service) : ""}</td><td class="small">${k ? esc(k.comment ?? "") : ""}</td>
        <td class="small muted">${k ? esc([k.schedule && k.schedule !== "always" ? "zaman: " + k.schedule : "", k.nat ? "NAT: " + k.nat : "",
          k.log ? "log: " + k.log : "", k.direction ?? ""].filter(Boolean).join(" · ")) : ""}</td></tr>`; }).join("")}</tbody></table></div>`
    : d.opinnateKarar === "port yok" ? `<div class="muted">Port girilmedi ve firewall / Carbon Black kayıtlarında da port görülmedi. Kural kontrolü için port girin
        (port olmadan Opinnate yalnızca kural tabanının sonundaki varsayılan engel kuralını döndürüyor).</div>`
    : d.opinnateDurum?.ok ? `<div class="muted">Opinnate bu kaynak ile hedef arasında yol / kural döndürmedi.</div>`
    : `<div style="color:var(--err)">Opinnate hatası: ${esc(d.opinnateDurum?.error ?? "bilinmiyor")}</div>`;
  const cbTable = d.carbonBlack.length ? `<div class="tbl-wrap"><table>
      <thead><tr><th>Port</th><th>Süreç</th><th>Gören ajan</th><th class="num">Olay</th><th>Son</th></tr></thead>
      <tbody>${d.carbonBlack.map(c => `<tr><td class="mono">${esc(c.port)}</td><td>${esc(c.process)}</td>
        <td>${c.yon === "outbound" ? "kaynak (giden)" : c.yon === "inbound" ? "hedef (gelen)" : esc(c.yon)}</td><td class="num">${fmt(c.count)}</td><td class="mono"><b>${esc(c.last ?? "")}</b></td></tr>`).join("")}</tbody></table></div>`
    : `<div class="muted">${d.carbonBlackDurum.ok ? "Carbon Black bu bağlantıyı görmemiş (ajan yoksa görünmez)." : "Carbon Black sorgulanmadı ya da hata verdi."}</div>`;

  $("#main").innerHTML = `
    <section class="panel"><div class="status">${pill("Firewall", d.firewallDurum)}${pill("Carbon Black", d.carbonBlackDurum)}${d.wafDurum && d.wafDurum.error !== "Sorgulanmadı" ? pill("F5 WAF", d.wafDurum) : ""}${opOn ? pill("Opinnate", d.opinnateDurum) : ""}
      ${pill("Envanter", d.envanter)}<span class="pill">Aralık ${esc(d.pencere)}</span><span class="pill">Toplam ${(d.elapsedMs / 1000).toFixed(1)} sn</span></div>
      ${msgs.length ? `<div class="msgs">${esc(msgs.join("\n"))}</div>` : ""}</section>
    <section class="panel">
      <div class="verdict ${esc(d.kararTur)}"><div class="big">${esc(d.karar)}</div><div>${esc(d.kararDetay)}</div></div>
      <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(320px,1fr));gap:12px;margin-top:12px">${uc("Kaynak", d.kaynak)}${uc(`Hedef${d.port ? " · port " + esc(d.port) : ""}`, d.hedef)}</div>
    </section>
    ${opOn ? `<section class="panel"><h2>Firewall kuralları (Opinnate)</h2>${opTable}</section>` : ""}
    <section class="panel"><h2>Firewall logu (port ve sonuç bazında)</h2>${fwTable}</section>
    ${olayTable ? `<section class="panel"><h2>Son ${d.sonOlaylar.length} firewall olayı</h2>${olayTable}</section>` : ""}
    <section class="panel"><h2>Carbon Black (uç nokta ajanının gördüğü bağlantılar)</h2>${cbTable}</section>
    ${d.wafDurum && d.wafDurum.error !== "Sorgulanmadı" ? `<section class="panel"><h2>F5 WAF (güvenlik olayları)</h2>${wafTable}</section>` : ""}`;
}
