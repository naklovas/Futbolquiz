// ============================================================================
// DeltaFlow · TLS toplu sorgu (kullanıcı: "Excel'den bir kolon IP girip karşılığı olan port – uygulama – TLS versiyonu")
// IP'ler yapıştırılır → POST /api/tls/toplu (arka plan işi) → GET /api/tls/toplu/{id} 3 sn'de bir → tablo + Excel (CSV).
// Yalnız GELEN el sıkışmaları: IP sunucu, port onun portu. Satır = IP × port (ERT'deki portlar + TLS görülen portlar).
// topoloji.html'deki ortak yardımcıları ($, esc, fmt, yetki, yetkiYok, tlsBadge, tlsEski) kullanır.
// ============================================================================

const tt = { id: null, body: null, timer: null, metin: "", saat: 24, filtre: "", yalnizEski: false, yalnizTls: false, yalnizSifresiz: false, sifresizKontrol: false,
  fqdn: false, sadecePort: true, giden: false, gidenElle: false, yon: "" };
// "Şifresiz portları da bul" ve "FQDN / endpoint" seçimleri hatırlanır (varsayılan kapalı: ek sorgular sorguyu yavaşlatır)
try { tt.sifresizKontrol = localStorage.getItem("df.ttSifresiz") === "1"; tt.fqdn = localStorage.getItem("df.ttFqdn") === "1"; } catch { }
// Şifreleme durumu (sunucu: TlsTopluSatir.Sifreleme): tls | sifresiz | protokol | trafikyok | bilinmiyor
const TT_SIF = {
  tls: ["🔒 TLS", "tt-sif-tls", "El sıkışmalarında TLS görüldü"],
  sifresiz: ["⚠ olası şifresiz", "tt-sif-yok", "AppResponse bu porta TCP trafiği gördü ama hiç TLS el sıkışması görmedi: iletişim büyük ihtimalle şifresiz (düz HTTP vb.). Sunucuda netsh http show sslcert / uygulama ayarıyla doğrulayın."],
  protokol: ["◐ protokol içinde", "tt-sif-pr", "Trafik var, TLS el sıkışması yok; bu portun protokolü şifrelemeyi kendi içinde yapar (WinRM, SQL, RDP, SMB…) — AppResponse'ta TLS olarak görünmez, ayarı ayrıca kontrol edin."],
  trafikyok: ["— trafik yok", "tt-sif-nt", "AppResponse bu aralıkta bu porta trafik görmedi (trafik yok ya da AppResponse'un gördüğü yoldan geçmiyor)."],
  bilinmiyor: ["?", "tt-sif-nt", "TCP trafiği bilgisi alınamadı"],
};
const ttSifHtml = r => { const x = TT_SIF[r.sifreleme] ?? TT_SIF.bilinmiyor;
  return `<span class="tt-sif ${x[1]}" title="${esc(x[2] + (r.ipucu ? " · " + r.ipucu : ""))}">${x[0]}</span>${r.ipucu ? `<div class="small muted">${esc(r.ipucu.split(":")[0])}</div>` : ""}`; };

// Satır başına IP + port çiftleri (kullanıcı: "elimizde IP ve port var"): "10.1.1.1<sekme>443", "10.1.1.1:443", "10.1.1.1;443" — satırdaki
// IP'den sonraki ilk 1–65535 arası sayı port sayılır; IP'siz / portsuz satırlar atlanır. Sonuç ip → Set(port)
function ttCiftler(metin) {
  const out = new Map();
  for (const satir of String(metin).split(/\r?\n/)) {
    const m = satir.match(/\b(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})\b/);
    if (!m || [m[1], m[2], m[3], m[4]].some(x => +x > 255)) continue;
    const ip = [m[1], m[2], m[3], m[4]].map(x => +x).join(".");
    const kalan = satir.slice(m.index + m[0].length).replace(/\b\d{1,3}(\.\d{1,3}){3}\b/g, " ");
    const p = kalan.match(/(?:^|[^\d.])(\d{1,5})(?![\d.])/);
    if (!p || +p[1] < 1 || +p[1] > 65535) continue;
    if (!out.has(ip)) out.set(ip, new Set());
    out.get(ip).add(String(+p[1]));
  }
  return out;
}

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
  if (yetki && yetki.tls === false) { ttYetkiYok(); return; }
  if (!$("#ttShell")) ttShell();
}
// Yetki:Tls (DokuPanel) yoksa sekme görünür ama içerik çizilmez, sorgu gönderilmez (sunucu da 403 verir)
function ttYetkiYok() {
  if (uiMode !== "tls") return;
  $("#main").innerHTML = `<div class="panel empty" style="color:var(--err)">🔒 TLS toplu sorgu yetkiniz yok — sayfa görüntülenemez. Yetki DokuPanel'den verilir.</div>`;
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
        <label class="small" style="display:flex;flex-direction:row;gap:6px;align-items:center" title="Servise erişilen adlar: istemcilerin bu IP:port'a (ya da sunucunun üyesi olduğu VIP'e) bağlanırken çözdüğü DNS adı (Carbon Black), IIS logundaki Host başlığı ve istek yolları, Nessus sertifika adları. Grup başına iki ek Splunk sorgusu."><input type="checkbox" id="ttFqdnK"${tt.fqdn ? " checked" : ""}> FQDN / endpoint URL bilgisini de al</label>
        <label class="small" style="display:flex;flex-direction:row;gap:6px;align-items:center" title="Sunucunun istemci olduğu (dışarıya yaptığı) TLS bağlantıları da gelir: hedef IP:port, hedefin ERT uygulaması, kullanılan sürüm. Listede tek IP varsa kendiliğinden işaretlenir."><input type="checkbox" id="ttGidenK"${tt.giden ? " checked" : ""}> Giden bağlantıları da al</label>
        <label class="small" id="ttPortL" style="display:flex;flex-direction:row;gap:6px;align-items:center" hidden title="Listede IP'nin yanında port varsa yalnız o portlar listelenir (veri olmasa da satır çıkar); işaretsizse IP'nin tüm portları"><input type="checkbox" id="ttPortK"${tt.sadecePort ? " checked" : ""}> Yalnız verilen portlar</label>
        <label class="small" style="display:flex;flex-direction:row;gap:6px;align-items:center" title="Her 20 IP'lik grupta AppResponse'tan ek bir gruplu flow_tcp raporu çekilir: trafik olan ama TLS el sıkışması görülmeyen portlar '⚠ olası şifresiz' olarak işaretlenir. Kapalıyken yalnız TLS verisi çekilir."><input type="checkbox" id="ttSifK"${tt.sifresizKontrol ? " checked" : ""}> TCP trafiğini de al (olası şifresiz portları bulur, sorgu uzar)</label>
        <button type="button" class="primary" id="ttGo">Sorgula</button>
        <button type="button" id="ttDur" hidden>Durdur</button>
      </div>
    </div>
    <div class="small muted">Varsayılan olarak gelen bağlantılar (▼ Gelen): IP sunucu, port onun portu. "Giden bağlantıları da al" işaretliyse (listede tek IP varsa kendiliğinden) sunucunun dışarıya yaptığı bağlantılar da ▲ Giden olarak gelir: port hedefin portu, uygulama hedefin ERT uygulaması. "TCP trafiğini de al" işaretliyse AppResponse'un TCP trafiği gördüğü ama TLS el sıkışması görmediği portlar "⚠ olası şifresiz" olarak işaretlenir (grup başına bir ek, gruplu trafik raporu). Sürüm, el sıkışmalarında fiilen kullanılan sürümdür (Nessus'un "kabul ettiği" değil). Çok IP'de birkaç dakika sürebilir; IP'ler ${20}'şerli gruplarla sorgulanır. Listede IP'nin yanında port varsa (Excel'de yan yana iki kolon) yalnız o portlar listelenir. "FQDN / endpoint" işaretliyse servise erişilen adlar (istemcilerin çözdüğü DNS adı, IIS Host başlığı, sertifika), önerilen URL ve IIS istek yolları da gelir; IIS dışı servislerde yol ağ verisinde yoktur.</div>
  </section>
  <section class="panel" id="ttSonuc"${tt.body ? "" : " hidden"}></section>`;
  const say = () => {
    const n = ttIpler($("#ttIp").value).length, c = ttCiftler($("#ttIp").value), np = [...c.values()].reduce((a, x) => a + x.size, 0);
    $("#ttSay").textContent = n ? `${fmt(n)} IP bulundu${np ? ` · ${fmt(np)} IP:port` : ""}` : "IP yok";
    $("#ttPortL").hidden = !np;
    // tek IP: gelen + giden hepsi (kullanıcı: "tek IP yazarsam full giden gelen ne varsa baksın"); elle değiştirilmişse dokunulmaz
    if (!tt.gidenElle) { tt.giden = n === 1; $("#ttGidenK").checked = tt.giden; }
  };
  $("#ttIp").addEventListener("input", () => { tt.metin = $("#ttIp").value; say(); });
  $("#ttSaat").addEventListener("change", e => tt.saat = +e.target.value);
  $("#ttDosya").addEventListener("change", async e => {
    const f = e.target.files?.[0]; if (!f) return;
    $("#ttSay").textContent = "dosya okunuyor…";
    try {
      const metin = await ttDosyaMetni(f), ips = ttIpler(metin);
      // kutuya ham dosya değil, bulunan IP'ler yazılır (Excel'in iç yapısı görünmesin)
      // IP + port çiftleri varsa satırlar "ip<sekme>port" olarak, yoksa yalnız IP'ler
      const cf = ttCiftler(metin);
      $("#ttIp").value = tt.metin = cf.size ? [...cf].flatMap(([ip, ps]) => [...ps].map(p => `${ip}\t${p}`)).concat(ips.filter(ip => !cf.has(ip))).join("\n") : ips.join("\n");
      say();
      if (!ips.length) alert(`"${f.name}" içinde IP adresi bulunamadı.`);
    } catch (err) { $("#ttSay").textContent = ""; alert(`"${f.name}" okunamadı: ${err.message}`); }
    e.target.value = "";
  });
  $("#ttGo").addEventListener("click", ttBasla);
  $("#ttFqdnK").addEventListener("change", e => { tt.fqdn = e.target.checked; try { localStorage.setItem("df.ttFqdn", e.target.checked ? "1" : "0"); } catch { } });
  $("#ttPortK").addEventListener("change", e => tt.sadecePort = e.target.checked);
  $("#ttGidenK").addEventListener("change", e => { tt.giden = e.target.checked; tt.gidenElle = true; });
  $("#ttSifK").addEventListener("change", e => {
    tt.sifresizKontrol = e.target.checked;
    try { localStorage.setItem("df.ttSifresiz", e.target.checked ? "1" : "0"); } catch { }
  });
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
  // Satır yapısı korunur (IP ve port aynı satırda kalsın): paylaşılan metinler (sharedStrings) dizinle çözülür, her <row> bir satır, hücreler sekmeyle
  const xmlMetin = x => x.replace(/<[^>]+>/g, "").replace(/&lt;/g, "<").replace(/&gt;/g, ">").replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&amp;/g, "&");
  const ssXml = parcalar.find(x => x.includes("<sst")) ?? "";
  const ss = [...ssXml.matchAll(/<si>([\s\S]*?)<\/si>/g)].map(m => xmlMetin(m[1]));
  const satirlar = [];
  for (const sayfa of parcalar.filter(x => x.includes("<sheetData")))
    for (const row of sayfa.matchAll(/<row\b[^>]*>([\s\S]*?)<\/row>/g))
      satirlar.push([...row[1].matchAll(/<c\b([^>]*?)(?:\/>|>([\s\S]*?)<\/c>)/g)].map(c => {
        const ic = c[2] ?? "", v = ic.match(/<v>([\s\S]*?)<\/v>/)?.[1];
        if (/\bt="s"/.test(c[1]) && v != null) return ss[+v] ?? "";
        if (/\bt="inlineStr"/.test(c[1])) return xmlMetin(ic);
        return v != null ? xmlMetin(v) : "";
      }).join("\t"));
  return satirlar.length ? satirlar.join("\n") : parcalar.join("\n").replace(/<[^>]+>/g, " ");
}

async function ttBasla() {
  const ips = ttIpler($("#ttIp").value);
  if (!ips.length) { alert("Listede IP bulunamadı."); return; }
  if (ips.length > 100 && !confirm(`${ips.length} IP sorgulanacak; AppResponse'ta birkaç dakika sürebilir. Devam edilsin mi?`)) return;
  clearTimeout(tt.timer);
  $("#ttGo").disabled = true;
  try {
    const cf = ttCiftler($("#ttIp").value), portlar = {};
    if (tt.sadecePort) for (const [ip, ps] of cf) portlar[ip] = [...ps];
    const r = await fetch("api/tls/toplu", { method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ ips, saat: tt.saat, sifresiz: tt.sifresizKontrol, fqdn: tt.fqdn, portlar, giden: tt.giden }) });
    const b = await r.json().catch(() => ({ error: `HTTP ${r.status}` }));
    if (r.status === 403) throw new Error(b.error || "TLS toplu sorgu yetkiniz yok. Yetki DokuPanel'den verilir.");
    if (r.status === 404 || r.status === 405) throw new Error("Sunucudaki DeltaFlow sürümünde toplu TLS ucu yok (DLL güncel değil).");
    if (!r.ok) throw new Error(b.error || `HTTP ${r.status}`);
    tt.id = b.id; tt.body = { ipSayisi: b.ipSayisi, toplamParca: b.toplamParca, parca: 0, satirlar: [], mesajlar: [], bitti: false, sifresiz: tt.sifresizKontrol, fqdn: tt.fqdn, giden: tt.giden };
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

// FQDN adayları: kaynak rozetiyle, ilk 3 + "N daha" (açılır); önerilen URL; IIS endpoint yolları (en çok istek alan 3 + açılır)
const TT_KAYNAK = k => k.startsWith("DNS · VIP") ? "VIP DNS" : k.startsWith("DNS") ? "DNS" : k === "IIS Host" ? "IIS" : k.startsWith("Sertifika") ? "sertifika" : "sunucu DNS";
function ttFqdnHtml(r) {
  const l = r.fqdn ?? [];
  if (!l.length) return r.port ? `<span class="small muted">bulunamadı</span>` : "";
  const sat = f => `<div class="mono small" title="${esc(f.kaynaklar.join(" · ") + (f.sayi ? ` · ${f.sayi} bağlantı, ${f.istemci} istemci` : ""))}">${esc(f.ad)} ${f.kaynaklar.map(k => `<span class="tt-fk">${esc(TT_KAYNAK(k))}</span>`).join("")}</div>`;
  return l.slice(0, 3).map(sat).join("") + (l.length > 3 ? `<details><summary class="small muted">${l.length - 3} ad daha</summary>${l.slice(3).map(sat).join("")}</details>` : "");
}
function ttUcHtml(r) {
  const l = r.uclar ?? [];
  if (!l.length) return "";
  const sat = u => `<div class="mono small" title="${esc(`${u.metot ?? ""} ${u.yol} · ${u.istek} istek, ${u.istemci} istemci${u.site ? " · IIS site W3SVC" + u.site : ""}`)}">${esc(u.yol)} <span class="muted">${fmt(u.istek)}</span></div>`;
  return l.slice(0, 3).map(sat).join("") + (l.length > 3 ? `<details><summary class="small muted">${l.length - 3} yol daha</summary>${l.slice(3).map(sat).join("")}</details>` : "");
}

function ttSatirlar() {
  const q = tt.filtre.trim().toLocaleLowerCase("tr");
  return (tt.body?.satirlar ?? []).filter(r =>
    (!tt.yon || (r.yon ?? "gelen") === tt.yon) &&
    (!tt.yalnizEski || r.surumler?.some(s => tlsEski(s.surum))) &&
    (!tt.yalnizTls || r.surumler?.length) &&
    (!tt.yalnizSifresiz || tt.body?.sifresiz === false || r.sifreleme === "sifresiz") &&
    (!q || [r.ip, r.sunucu, r.port, r.uygulama, r.url, r.website, r.vip, r.hedef, r.hedefSunucu, ...(r.surumler ?? []).map(s => s.surum), ...(r.fqdn ?? []).map(f => f.ad), ...(r.uclar ?? []).map(u => u.yol)]
      .some(x => String(x ?? "").toLocaleLowerCase("tr").includes(q))));
}

function ttCiz() {
  const el = $("#ttSonuc"), b = tt.body;
  if (!el || !b) return;
  const all = b.satirlar ?? [], list = ttSatirlar();
  const ipTls = new Set(all.filter(r => r.surumler?.length).map(r => r.ip)).size;
  const eski = all.filter(r => r.surumler?.some(s => tlsEski(s.surum)));
  const sifresiz = all.filter(r => r.sifreleme === "sifresiz");
  const sk = b.sifresiz !== false;   // ek trafik sorgusu yapıldıysa şifreleme / trafik kolonları
  const fk = !!b.fqdn;               // FQDN / endpoint araması yapıldıysa ad / URL / yol kolonları
  const fqBulunan = all.filter(r => r.fqdn?.length).length;
  const gk = !!b.giden;              // giden el sıkışmaları da alındıysa Yön ve Hedef kolonları
  const nGelen = all.filter(r => (r.yon ?? "gelen") === "gelen" && r.port).length, nGiden = all.filter(r => r.yon === "giden").length;
  const pct = b.toplamParca ? Math.round((b.parca ?? 0) * 100 / b.toplamParca) : 0;
  const fmtT = x => x ? new Date(x).toLocaleString("tr-TR", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" }) : "";
  $("#ttDur") && ($("#ttDur").hidden = !!b.bitti);
  el.innerHTML = `
    <div class="toolbar">
      <h2 style="margin:0">Sonuç</h2>
      ${!b.bitti ? `<span class="small"><span class="spinner"></span> ${fmt(b.parca ?? 0)} / ${fmt(b.toplamParca ?? 0)} grup · %${pct}</span>` : b.hata ? `<span class="small" style="color:var(--err)">${esc(b.hata)}</span>` : `<span class="small" style="color:var(--ok)">✓ tamamlandı</span>`}
      <span class="small muted">${fmt(b.ipSayisi ?? 0)} IP · ${fmt(ipTls)} IP'de TLS görüldü · ${fmt(eski.length)} satırda eski sürüm ${sk ? ` · <b style="color:${sifresiz.length ? "var(--err)" : "inherit"}">${fmt(sifresiz.length)} portta olası şifresiz</b>` : ""}${b.bas ? ` · ${fmtT(b.bas)} – ${fmtT(b.bit)}` : ""}${b.yontem ? ` · ${esc(b.yontem)}` : ""}${fk ? ` · ${fmt(fqBulunan)} / ${fmt(all.filter(r => r.port).length)} portta FQDN bulundu` : ""}${b.istenenPort ? ` · yalnız verilen ${fmt(b.istenenPort)} IP:port` : ""}${gk ? ` · ▼ ${fmt(nGelen)} gelen, ▲ ${fmt(nGiden)} giden` : ""}</span>
      ${gk ? `<select id="ttYon" title="Gelen: sunucu hizmet veriyor (port sunucunun portu) · Giden: sunucu başka servisi çağırıyor (port hedefin portu)"><option value="">Yön: Tümü</option><option value="gelen"${tt.yon === "gelen" ? " selected" : ""}>▼ Gelen</option><option value="giden"${tt.yon === "giden" ? " selected" : ""}>▲ Giden</option></select>` : ""}
      <input type="search" id="ttAra" placeholder="ara: IP, sunucu, uygulama, sürüm" value="${esc(tt.filtre)}" style="margin-left:auto;min-width:220px">
      <label class="small" style="display:flex;flex-direction:row;gap:4px;align-items:center"><input type="checkbox" id="ttEski"${tt.yalnizEski ? " checked" : ""}> Yalnız eski sürüm</label>
      <label class="small" style="display:flex;flex-direction:row;gap:4px;align-items:center"><input type="checkbox" id="ttTls"${tt.yalnizTls ? " checked" : ""}> Yalnız TLS görülen</label>
      ${sk ? `<label class="small" style="display:flex;flex-direction:row;gap:4px;align-items:center" title="Trafik var ama TLS el sıkışması yok"><input type="checkbox" id="ttSif"${tt.yalnizSifresiz ? " checked" : ""}> Yalnız olası şifresiz</label>` : ""}
      <button type="button" data-csv id="ttCsv"${all.length ? "" : " disabled"}>Excel (CSV)</button>
    </div>
    ${!b.bitti ? `<div class="tt-bar"><i style="width:${pct}%"></i></div>` : ""}
    <div class="tbl-wrap" style="max-height:62vh"><table>
      <thead><tr><th>IP</th><th>Sunucu</th>${gk ? `<th title="▼ Gelen: bu sunucuya gelen bağlantı (port bu sunucunun portu) · ▲ Giden: bu sunucunun başka sunucuya yaptığı bağlantı (port hedefin portu)">Yön</th><th title="Giden bağlantıda çağrılan sunucu">Hedef (giden)</th>` : ""}<th title="Gelen: bu sunucunun dinlediği port · Giden: hedefin portu">Port</th><th title="Gelen: bu portun ERT uygulaması · Giden: hedefin ERT uygulaması">Uygulama (ERT)</th>${sk ? `<th>Şifreleme</th>` : ""}<th>TLS sürümü</th><th class="num">El sıkışması</th>${sk ? `<th class="num" title="AppResponse flow_tcp: bu porta gelen TCP trafiği (bağlantı / satır sayısı)">TCP trafiği</th>` : ""}<th class="num">İstemci</th><th>Not</th>${fk ? `<th title="Servise erişilen adlar; DNS = istemcilerin bağlanırken çözdüğü ad (Carbon Black), VIP DNS = sunucunun üyesi olduğu VIP'e bağlanırken, IIS = IIS logundaki Host başlığı, sertifika = Nessus'un gördüğü sertifika adları, sunucu DNS = Nessus'un sunucu adı">FQDN</th><th title="En çok kullanılan ad + port + http/https (TLS görüldüyse ya da sertifika varsa https)">Önerilen URL</th><th title="IIS logunda görülen istek yolları (sayı / GUID parçaları {n} / {guid}); IIS dışı servislerde yol ağ verisinde yok">Endpoint (IIS)</th><th>VIP</th><th>IIS website (ERT)</th>` : ""}</tr></thead>
      <tbody>${list.length ? list.map(r => `<tr${r.surumler?.some(s => tlsEski(s.surum)) || r.sifreleme === "sifresiz" ? ` class="tls-eski-satir"` : ""}>
        <td class="mono"><a href="${esc(ipTabUrl(r.ip))}" target="_blank" rel="noopener" title="IP sorgusunu yeni sekmede aç">${esc(r.ip)}</a></td>
        <td class="small">${esc(r.sunucu ?? "")}</td>
        ${gk ? `<td class="small" style="white-space:nowrap">${r.yon === "giden" ? `<b style="color:var(--out, #b26b00)">▲ Giden</b>` : r.port ? `<b style="color:var(--in)">▼ Gelen</b>` : ""}</td>
          <td class="small">${r.yon === "giden" ? `<span class="mono"><a href="${esc(ipTabUrl(r.hedef))}" target="_blank" rel="noopener">${esc(r.hedef ?? "")}</a></span>${r.hedefSunucu ? `<div class="muted">${esc(r.hedefSunucu)}</div>` : ""}` : ""}</td>` : ""}
        <td class="mono" title="${r.yon === "giden" ? "hedefin portu" : "bu sunucunun portu"}">${esc(r.port || "—")}${gk && r.port ? `<div class="small muted">${r.yon === "giden" ? "hedef portu" : "kendi portu"}</div>` : ""}</td>
        <td>${r.uygulama ? esc(r.uygulama) : `<span class="small muted">${r.port ? "kayıt yok" : ""}</span>`}${r.port && !r.ertPortu && r.uygulama ? ` <span class="small muted" title="Bu port ERT'de yok; IP'nin uygulaması gösteriliyor">(IP)</span>` : ""}</td>
        ${sk ? `<td>${r.port ? ttSifHtml(r) : ""}</td>` : ""}<td>${ttSurumHtml(r)}</td><td class="num">${r.sayi ? fmt(r.sayi) : ""}</td>${sk ? `<td class="num">${r.trafik ? fmt(r.trafik) : ""}</td>` : ""}<td class="num">${r.istemci ? fmt(r.istemci) : ""}</td>
        <td class="small muted">${r.surumler?.length ? esc(r.not ?? "") : ""}</td>
        ${fk ? `<td>${ttFqdnHtml(r)}</td><td class="mono small">${r.url ? esc(r.url) : ""}</td><td>${ttUcHtml(r)}</td><td class="mono small">${esc(r.vip ?? "")}</td><td class="small">${esc(r.website ?? "")}</td>` : ""}</tr>`).join("")
        : `<tr><td colspan="${(sk ? 10 : 8) + (fk ? 5 : 0) + (gk ? 2 : 0)}" class="unk">${all.length ? "Filtreye uyan satır yok." : b.bitti ? "Sonuç yok." : "Sonuçlar geldikçe burada listelenecek…"}</td></tr>`}</tbody></table></div>
    ${b.mesajlar?.length ? `<div class="msgs">${esc(b.mesajlar.join("\n"))}</div>` : ""}`;
  $("#ttAra").addEventListener("input", e => { tt.filtre = e.target.value; const pos = e.target.selectionStart; ttCiz(); const i = $("#ttAra"); i.focus(); i.setSelectionRange(pos, pos); });
  $("#ttEski").addEventListener("change", e => { tt.yalnizEski = e.target.checked; ttCiz(); });
  $("#ttTls").addEventListener("change", e => { tt.yalnizTls = e.target.checked; ttCiz(); });
  $("#ttSif")?.addEventListener("change", e => { tt.yalnizSifresiz = e.target.checked; ttCiz(); });
  $("#ttYon")?.addEventListener("change", e => { tt.yon = e.target.value; ttCiz(); });
  $("#ttCsv").addEventListener("click", ttCsv);
}

function ttCsv() {
  if (!yetki.disari) return yetkiYok("Dışa aktarma");
  const Q = v => { v = String(v ?? ""); return /[;"\n]/.test(v) ? `"${v.replace(/"/g, '""')}"` : v; };
  const fk = !!tt.body?.fqdn;
  const gk = !!tt.body?.giden;
  const rows = [["IP", "SUNUCU", ...(gk ? ["YÖN", "HEDEF IP", "HEDEF SUNUCU"] : []), "PORT", "UYGULAMA", "ERT PORTU", ...(fk ? ["IIS WEBSITE (ERT)", "VIP", "ÖNERİLEN URL", "FQDN", "FQDN KAYNAKLARI", "ENDPOINT URL'LERİ (IIS)"] : []), "ŞİFRELEME", "ŞİFRELEME NOTU", "TLS SÜRÜMLERİ", "EN ÇOK KULLANILAN", "ESKİ SÜRÜM", "EL SIKIŞMASI", "TCP TRAFİĞİ", "İSTEMCİ", "KUTULAR", "NOT"]];
  const SIF_CSV = { tls: "TLS", sifresiz: "OLASI ŞİFRESİZ", protokol: "PROTOKOL İÇİNDE", trafikyok: "TRAFİK YOK", bilinmiyor: "BİLİNMİYOR" };
  // endpoint URL'leri: önerilen temel adres + IIS yolu (adres yoksa yalnız yol)
  const ucUrl = r => (r.uclar ?? []).map(u => `${r.url ?? ""}${u.yol} (${u.istek})`).join(" | ");
  for (const r of ttSatirlar()) rows.push([r.ip, r.sunucu,
    ...(gk ? [r.yon === "giden" ? "GİDEN (hedefin portu)" : r.port ? "GELEN (kendi portu)" : "", r.hedef ?? "", r.hedefSunucu ?? ""] : []),
    r.port, r.uygulama, r.ertPortu ? "evet" : "hayır",
    ...(fk ? [r.website ?? "", r.vip ?? "", r.url ?? "", (r.fqdn ?? []).map(f => f.ad).join(" | "), (r.fqdn ?? []).map(f => `${f.ad}: ${f.kaynaklar.join(", ")}`).join(" | "), ucUrl(r)] : []),
    r.port ? SIF_CSV[r.sifreleme] ?? "" : "", r.ipucu ?? "",
    (r.surumler ?? []).map(s => `${s.surum} %${String(s.yuzde).replace(".", ",")}`).join(" | "), r.surumler?.[0]?.surum ?? "",
    r.surumler?.some(s => tlsEski(s.surum)) ? "VAR" : "", r.sayi || "", r.trafik || "", r.istemci || "", (r.kutular ?? []).join(", "), r.not]);
  const blob = new Blob(["﻿" + rows.map(r => r.map(Q).join(";")).join("\r\n")], { type: "text/csv;charset=utf-8" });
  const a = document.createElement("a");
  a.href = URL.createObjectURL(blob); a.download = `tls_toplu_${new Date().toISOString().slice(0, 16).replace(/[:T]/g, "")}.csv`; a.click();
  setTimeout(() => URL.revokeObjectURL(a.href), 2000);
}
