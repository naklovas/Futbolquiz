// ============================================================================
// DeltaFlow · TLS toplu sorgu (kullanıcı: "Excel'den bir kolon IP girip karşılığı olan port – uygulama – TLS versiyonu")
// IP'ler yapıştırılır → POST /api/tls/toplu (arka plan işi) → GET /api/tls/toplu/{id} 3 sn'de bir → tablo + Excel (CSV).
// Yalnız GELEN el sıkışmaları: IP sunucu, port onun portu. Satır = IP × port (ERT'deki portlar + TLS görülen portlar).
// topoloji.html'deki ortak yardımcıları ($, esc, fmt, yetki, yetkiYok, tlsBadge, tlsEski) kullanır.
// ============================================================================

const tt = { id: null, body: null, timer: null, metin: "", saat: 24, filtre: "", yalnizEski: false, yalnizTls: false };

// Excel'den yapıştırılan metinden IPv4'ler (satır, sekme, virgül, noktalı virgül, boşluk ayraçlı; başlık satırı vb. atlanır)
function ttIpler(metin) {
  const out = [], seen = new Set();
  for (const m of String(metin).matchAll(/\b(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})\b/g)) {
    if ([m[1], m[2], m[3], m[4]].some(x => +x > 255)) continue;
    const ip = [m[1], m[2], m[3], m[4]].map(x => +x).join(".");
    if (!seen.has(ip)) { seen.add(ip); out.push(ip); }
  }
  return out;
}

function runTlsToplu() {
  setUiMode("tls");
  if (!$("#ttShell")) ttShell();
}

function ttShell() {
  $("#main").innerHTML = `<section class="panel" id="ttShell">
    <div class="toolbar"><h2 style="margin:0">🔒 TLS toplu sorgu <span class="small muted">AppResponse · IP başına port, uygulama ve kullanılan TLS sürümü</span></h2></div>
    <div class="tt-giris">
      <label style="flex:1">IP listesi <span class="small muted">(Excel'den kolonu kopyalayıp yapıştırın ya da sağdan Excel / CSV dosyası seçin)</span>
        <textarea id="ttIp" rows="7" class="mono" placeholder="10.50.20.15&#10;10.50.20.16&#10;…">${esc(tt.metin)}</textarea></label>
      <div class="tt-yan">
        <div id="ttSay" class="small muted"></div>
        <label>Aralık <select id="ttSaat">${[1, 6, 24].map(h => `<option value="${h}"${tt.saat === h ? " selected" : ""}>son ${h} saat</option>`).join("")}</select></label>
        <label class="small" title="Excel (.xlsx / .xls), CSV ya da TXT dosyasındaki IP'ler (tüm sayfalar)"><input type="file" id="ttDosya" accept=".xlsx,.xlsm,.xls,.csv,.txt" style="max-width:220px"></label>
        <button type="button" class="primary" id="ttGo">Sorgula</button>
        <button type="button" id="ttDur" hidden>Durdur</button>
      </div>
    </div>
    <div class="small muted">Yalnız gelen bağlantılar: IP sunucu, port onun portu. Sürüm, el sıkışmalarında fiilen kullanılan sürümdür (Nessus'un "kabul ettiği" değil). Çok IP'de birkaç dakika sürebilir; IP'ler ${20}'şerli gruplarla sorgulanır.</div>
  </section>
  <section class="panel" id="ttSonuc"${tt.body ? "" : " hidden"}></section>`;
  const say = () => { const n = ttIpler($("#ttIp").value).length; $("#ttSay").textContent = n ? `${fmt(n)} IP bulundu` : "IP yok"; };
  $("#ttIp").addEventListener("input", () => { tt.metin = $("#ttIp").value; say(); });
  $("#ttSaat").addEventListener("change", e => tt.saat = +e.target.value);
  $("#ttDosya").addEventListener("change", async e => {
    const f = e.target.files?.[0]; if (!f) return;
    $("#ttSay").textContent = "dosya okunuyor…";
    try {
      const ips = ttIpler(await ttDosyaMetni(f));
      // kutuya ham dosya değil, bulunan IP'ler yazılır (Excel'in iç yapısı görünmesin)
      $("#ttIp").value = tt.metin = ips.join("\n");
      say();
      if (!ips.length) alert(`"${f.name}" içinde IP adresi bulunamadı.`);
    } catch (err) { $("#ttSay").textContent = ""; alert(`"${f.name}" okunamadı: ${err.message}`); }
    e.target.value = "";
  });
  $("#ttGo").addEventListener("click", ttBasla);
  $("#ttDur").addEventListener("click", () => tt.id && fetch(`api/tls/toplu/${tt.id}/durdur`, { method: "POST" }));
  say();
  if (tt.body) ttCiz();
}

// Dosyadan metin: .xlsx / .xlsm (zip: xl/sharedStrings.xml + sayfalar; DecompressionStream ile açılır), .xls (eski ikili biçim:
// metinler 8 bit ya da UTF-16 durur, ikisi de okunur), diğerleri düz metin. Dış kütüphane yok (iç ağ).
async function ttDosyaMetni(f) {
  const buf = new Uint8Array(await f.arrayBuffer());
  const zip = buf[0] === 0x50 && buf[1] === 0x4b;   // "PK"
  if (zip) return ttXlsxMetni(buf);
  if (buf[0] === 0xd0 && buf[1] === 0xcf) {           // OLE2 (.xls)
    return new TextDecoder("latin1").decode(buf) + "\n" + new TextDecoder("utf-16le").decode(buf.subarray(0, buf.length - (buf.length % 2)));
  }
  let t = new TextDecoder("utf-8").decode(buf);
  if (buf[0] === 0xff && buf[1] === 0xfe) t = new TextDecoder("utf-16le").decode(buf);   // "Unicode metin" olarak kaydedilmiş
  return t;
}
async function ttXlsxMetni(buf) {
  if (typeof DecompressionStream === "undefined") throw new Error("Tarayıcı Excel açmayı desteklemiyor; dosyayı CSV olarak kaydedin ya da kolonu kopyalayıp yapıştırın.");
  const dv = new DataView(buf.buffer, buf.byteOffset, buf.byteLength);
  // merkezi dizin sonu (EOCD) kaydını sondan ara
  let eocd = -1;
  for (let i = buf.length - 22; i >= Math.max(0, buf.length - 66000); i--) if (dv.getUint32(i, true) === 0x06054b50) { eocd = i; break; }
  if (eocd < 0) throw new Error("Excel dosyası çözülemedi (zip dizini yok).");
  const n = dv.getUint16(eocd + 10, true);
  let p = dv.getUint32(eocd + 16, true);
  const parcalar = [];
  for (let k = 0; k < n && dv.getUint32(p, true) === 0x02014b50; k++) {
    const yontem = dv.getUint16(p + 10, true), boy = dv.getUint32(p + 20, true);
    const ad = new TextDecoder().decode(buf.subarray(p + 46, p + 46 + dv.getUint16(p + 28, true)));
    const yerel = dv.getUint32(p + 42, true);
    p += 46 + dv.getUint16(p + 28, true) + dv.getUint16(p + 30, true) + dv.getUint16(p + 32, true);
    if (!/^xl\/(sharedStrings\.xml|worksheets\/[^/]+\.xml)$/i.test(ad)) continue;
    const bas = yerel + 30 + dv.getUint16(yerel + 26, true) + dv.getUint16(yerel + 28, true);
    const veri = buf.subarray(bas, bas + boy);
    if (yontem === 0) parcalar.push(new TextDecoder().decode(veri));
    else if (yontem === 8) parcalar.push(await new Response(new Blob([veri]).stream().pipeThrough(new DecompressionStream("deflate-raw"))).text());
  }
  if (!parcalar.length) throw new Error("Excel dosyasında sayfa bulunamadı.");
  // XML etiketleri arasında IP bölünmesin: etiketleri boşluğa çevir
  return parcalar.join("\n").replace(/<[^>]+>/g, " ");
}

async function ttBasla() {
  const ips = ttIpler($("#ttIp").value);
  if (!ips.length) { alert("Listede IP bulunamadı."); return; }
  if (ips.length > 100 && !confirm(`${ips.length} IP sorgulanacak; AppResponse'ta birkaç dakika sürebilir. Devam edilsin mi?`)) return;
  clearTimeout(tt.timer);
  $("#ttGo").disabled = true;
  try {
    const r = await fetch("api/tls/toplu", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ ips, saat: tt.saat }) });
    const b = await r.json().catch(() => ({ error: `HTTP ${r.status}` }));
    if (r.status === 404 || r.status === 405) throw new Error("Sunucudaki DeltaFlow sürümünde toplu TLS ucu yok (DLL güncel değil).");
    if (!r.ok) throw new Error(b.error || `HTTP ${r.status}`);
    tt.id = b.id; tt.body = { ipSayisi: b.ipSayisi, toplamParca: b.toplamParca, parca: 0, satirlar: [], mesajlar: [], bitti: false };
    $("#ttSonuc").hidden = false; ttCiz(); ttIzle();
  } catch (e) {
    $("#ttGo").disabled = false;
    $("#ttSonuc").hidden = false;
    $("#ttSonuc").innerHTML = `<div class="unk" style="color:var(--err)">${esc(e.message)}</div>`;
  }
}

async function ttIzle() {
  const id = tt.id;
  try {
    const r = await fetch(`api/tls/toplu/${id}`);
    const b = await r.json().catch(() => ({ error: `HTTP ${r.status}` }));
    if (!r.ok) throw new Error(b.error || `HTTP ${r.status}`);
    if (tt.id !== id) return;
    tt.body = b;
  } catch (e) { if (tt.id === id) tt.body = { ...(tt.body ?? {}), bitti: true, hata: e.message }; }
  if (uiMode === "tls" && $("#ttSonuc")) ttCiz();
  if (tt.body?.bitti) { if ($("#ttGo")) $("#ttGo").disabled = false; return; }
  tt.timer = setTimeout(ttIzle, 3000);
}

const ttSurumHtml = r => r.surumler?.length
  ? r.surumler.map(s => `${tlsBadge(s.surum)} <span class="small">${s.yuzde >= 99.95 ? "%100" : "%" + String(s.yuzde).replace(".", ",")}</span>`).join(" ")
  : `<span class="small muted">${esc(r.not ?? "—")}</span>`;

function ttSatirlar() {
  const q = tt.filtre.trim().toLocaleLowerCase("tr");
  return (tt.body?.satirlar ?? []).filter(r =>
    (!tt.yalnizEski || r.surumler?.some(s => tlsEski(s.surum))) &&
    (!tt.yalnizTls || r.surumler?.length) &&
    (!q || [r.ip, r.sunucu, r.port, r.uygulama, ...(r.surumler ?? []).map(s => s.surum)].some(x => String(x ?? "").toLocaleLowerCase("tr").includes(q))));
}

function ttCiz() {
  const el = $("#ttSonuc"), b = tt.body;
  if (!el || !b) return;
  const all = b.satirlar ?? [], list = ttSatirlar();
  const ipTls = new Set(all.filter(r => r.surumler?.length).map(r => r.ip)).size;
  const eski = all.filter(r => r.surumler?.some(s => tlsEski(s.surum)));
  const pct = b.toplamParca ? Math.round((b.parca ?? 0) * 100 / b.toplamParca) : 0;
  const fmtT = x => x ? new Date(x).toLocaleString("tr-TR", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" }) : "";
  $("#ttDur") && ($("#ttDur").hidden = !!b.bitti);
  el.innerHTML = `
    <div class="toolbar">
      <h2 style="margin:0">Sonuç</h2>
      ${!b.bitti ? `<span class="small"><span class="spinner"></span> ${fmt(b.parca ?? 0)} / ${fmt(b.toplamParca ?? 0)} grup · %${pct}</span>` : b.hata ? `<span class="small" style="color:var(--err)">${esc(b.hata)}</span>` : `<span class="small" style="color:var(--ok)">✓ tamamlandı</span>`}
      <span class="small muted">${fmt(b.ipSayisi ?? 0)} IP · ${fmt(ipTls)} IP'de TLS görüldü · ${fmt(eski.length)} satırda eski sürüm${b.bas ? ` · ${fmtT(b.bas)} – ${fmtT(b.bit)}` : ""}${b.yontem ? ` · ${esc(b.yontem)}` : ""}</span>
      <input type="search" id="ttAra" placeholder="ara: IP, sunucu, uygulama, sürüm" value="${esc(tt.filtre)}" style="margin-left:auto;min-width:220px">
      <label class="small" style="flex-direction:row;gap:4px"><input type="checkbox" id="ttEski"${tt.yalnizEski ? " checked" : ""}> Yalnız eski sürüm</label>
      <label class="small" style="flex-direction:row;gap:4px"><input type="checkbox" id="ttTls"${tt.yalnizTls ? " checked" : ""}> Yalnız TLS görülen</label>
      <button type="button" data-csv id="ttCsv"${all.length ? "" : " disabled"}>Excel (CSV)</button>
    </div>
    ${!b.bitti ? `<div class="tt-bar"><i style="width:${pct}%"></i></div>` : ""}
    <div class="tbl-wrap" style="max-height:62vh"><table>
      <thead><tr><th>IP</th><th>Sunucu</th><th>Port</th><th>Uygulama (ERT)</th><th>TLS sürümü</th><th class="num">El sıkışması</th><th class="num">İstemci</th><th>Not</th></tr></thead>
      <tbody>${list.length ? list.map(r => `<tr${r.surumler?.some(s => tlsEski(s.surum)) ? ` class="tls-eski-satir"` : ""}>
        <td class="mono"><a href="${esc(ipTabUrl(r.ip))}" target="_blank" rel="noopener" title="IP sorgusunu yeni sekmede aç">${esc(r.ip)}</a></td>
        <td class="small">${esc(r.sunucu ?? "")}</td><td class="mono">${esc(r.port || "—")}</td>
        <td>${r.uygulama ? esc(r.uygulama) : `<span class="small muted">${r.port ? "kayıt yok" : ""}</span>`}${r.port && !r.ertPortu && r.uygulama ? ` <span class="small muted" title="Bu port ERT'de yok; IP'nin uygulaması gösteriliyor">(IP)</span>` : ""}</td>
        <td>${ttSurumHtml(r)}</td><td class="num">${r.sayi ? fmt(r.sayi) : ""}</td><td class="num">${r.istemci ? fmt(r.istemci) : ""}</td>
        <td class="small muted">${r.surumler?.length ? esc(r.not ?? "") : ""}</td></tr>`).join("")
        : `<tr><td colspan="8" class="unk">${all.length ? "Filtreye uyan satır yok." : b.bitti ? "Sonuç yok." : "Sonuçlar geldikçe burada listelenecek…"}</td></tr>`}</tbody></table></div>
    ${b.mesajlar?.length ? `<div class="msgs">${esc(b.mesajlar.join("\n"))}</div>` : ""}`;
  $("#ttAra").addEventListener("input", e => { tt.filtre = e.target.value; const pos = e.target.selectionStart; ttCiz(); const i = $("#ttAra"); i.focus(); i.setSelectionRange(pos, pos); });
  $("#ttEski").addEventListener("change", e => { tt.yalnizEski = e.target.checked; ttCiz(); });
  $("#ttTls").addEventListener("change", e => { tt.yalnizTls = e.target.checked; ttCiz(); });
  $("#ttCsv").addEventListener("click", ttCsv);
}

function ttCsv() {
  if (!yetki.disari) return yetkiYok("Dışa aktarma");
  const Q = v => { v = String(v ?? ""); return /[;"\n]/.test(v) ? `"${v.replace(/"/g, '""')}"` : v; };
  const rows = [["IP", "SUNUCU", "PORT", "UYGULAMA", "ERT PORTU", "TLS SÜRÜMLERİ", "EN ÇOK KULLANILAN", "ESKİ SÜRÜM", "EL SIKIŞMASI", "İSTEMCİ", "KUTULAR", "NOT"]];
  for (const r of ttSatirlar()) rows.push([r.ip, r.sunucu, r.port, r.uygulama, r.ertPortu ? "evet" : "hayır",
    (r.surumler ?? []).map(s => `${s.surum} %${String(s.yuzde).replace(".", ",")}`).join(" | "), r.surumler?.[0]?.surum ?? "",
    r.surumler?.some(s => tlsEski(s.surum)) ? "VAR" : "", r.sayi || "", r.istemci || "", (r.kutular ?? []).join(", "), r.not]);
  const blob = new Blob(["﻿" + rows.map(r => r.map(Q).join(";")).join("\r\n")], { type: "text/csv;charset=utf-8" });
  const a = document.createElement("a");
  a.href = URL.createObjectURL(blob); a.download = `tls_toplu_${new Date().toISOString().slice(0, 16).replace(/[:T]/g, "")}.csv`; a.click();
  setTimeout(() => URL.revokeObjectURL(a.href), 2000);
}
