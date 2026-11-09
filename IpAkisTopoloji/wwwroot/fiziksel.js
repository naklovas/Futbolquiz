// ============================================================================
// DeltaFlow · Fiziksel envanter (oda planı → kabin → host → VM → IP → uygulama)
// "Bu kabin kapanırsa hangi uygulamalar etkilenir?" — veri doku DB'deki nlyte tablolarından (FizikselEnvanter.cs), yalnız okunur.
// topoloji.html'deki ortak yardımcıları ($, esc, fmt, ipTabUrl, yetki, yetkiYok, setUiMode) kullanır.
// ============================================================================

const fz = { ozet: null, oda: null, odaVeri: null, kabin: null, kabinVeri: null, yuz: "Front", host: null, mod: "host", ara: null, vurgu: new Set(), eksik: null };
const FZ_UH = 13;   // 1U yüksekliği (px)
// nlyte CihazTuru → arayüz adı ve renk (dbo.NlyteCihaz; eski NlyteSunucu'da hepsi Server)
const FZ_TUR = {
  Server: ["sunucu", "#3b7dd8"], Network: ["ağ cihazı", "#2e9d5b"], Firewall: ["firewall", "#c0392b"], LoadBalancer: ["yük dengeleyici", "#d35400"],
  PatchPanel: ["patch panel", "#7f8c8d"], Storage: ["storage", "#8e44ad"], PDU: ["PDU", "#e67e22"], Chassis: ["chassis", "#b7950b"],
  KVM: ["KVM", "#16a085"], UPS: ["UPS", "#a04000"], Peripheral: ["çevre birimi", "#5d6d7e"], Cooling: ["soğutma", "#2980b9"],
  StaticSwitch: ["statik switch", "#117a65"], Auxiliary: ["yardımcı", "#839192"], Other: ["diğer", "#95a5a6"],
};
// Uygulama değerleri (ERT VARLIKDEGERI…): VM = uygulamalarının en yükseği, host / kabin = içindekilerin en yükseği
const FZ_DEGER = [["v", "V", "Varlık"], ["i", "İ", "İş"], ["e", "E", "Erişilebilirlik"], ["g", "G", "Gizlilik"], ["b", "B", "Bütünlük"]];
// değerler 0–5 (canlıda 5'e kadar görüldü): 5 koyu kırmızı, 4 kırmızı, 3 turuncu-kırmızı, 2 turuncu, 1 yeşil, 0 gri
const fzDegerRenk = n => n == null ? "var(--line)" : n >= 5 ? "#8e1b10" : n === 4 ? "#c0392b" : n === 3 ? "#d35400" : n === 2 ? "#e69a22" : n === 1 ? "#2e9d5b" : "#95a5a6";
function fzDeger(d, uzun) {
  if (!d) return "";
  return `<span class="fz-dg">${FZ_DEGER.filter(([k]) => d[k] != null).map(([k, kisa, ad]) =>
    `<span style="--dc:${fzDegerRenk(d[k])}" title="${esc(ad)}: ${d[k]}${d.kaynak?.[k] ? " (en yüksek: " + esc(d.kaynak[k]) + ")" : ""}">${uzun ? esc(ad) + " " : kisa}${d[k]}</span>`).join("")}</span>`;
}
const fzDegerMetin = d => d ? FZ_DEGER.filter(([k]) => d[k] != null).map(([k, kisa]) => kisa + d[k]).join(" ") : "";
try { fz.renk = localStorage.getItem("df.fzRenk") || "vm"; } catch { fz.renk = "vm"; }
const fzTurAd = t => FZ_TUR[t]?.[0] ?? (t || "diğer");
const fzTurRenk = t => FZ_TUR[t]?.[1] ?? FZ_TUR.Other[1];
// "12 cihaz: 7 sunucu (2'si VM'li), 2 ağ cihazı, 2 PDU"
function fzTurOzet(turlar, toplam) {
  if (!turlar?.length) return toplam != null ? `${fmt(toplam)} cihaz` : "";
  const n = toplam ?? turlar.reduce((a, t) => a + t.adet, 0);
  return `${fmt(n)} cihaz: ` + turlar.map(t => `${fmt(t.adet)} ${fzTurAd(t.tur)}${t.vmli ? ` (VM'li: ${fmt(t.vmli)})` : ""}`).join(", ");
}

async function fzGet(url) {
  const r = await fetch(url);
  const b = await r.json().catch(() => ({ error: `HTTP ${r.status}` }));
  if (r.status === 404 && !b.error) throw new Error("Sunucudaki DeltaFlow sürümünde fiziksel envanter ucu yok (DLL güncel değil).");
  if (!r.ok) throw new Error(b.error || `HTTP ${r.status}`);
  return b;
}

function runFiziksel() {
  setUiMode("fiziksel");
  if (yetki && yetki.fiziksel === false) { fzYetkiYok(); return; }
  if (!$("#fzShell")) { fzShell(); fzDerinBaglanti(); }
}
// Yetki:Fiziksel (DokuPanel) yoksa sayfa açılır ama hiçbir şey çalışmaz: veri yüklenmez, arama / oda seçici / düğmeler / bölümler pasif
function fzYetkiYok() {
  if (uiMode !== "fiziksel") return;
  if (!$("#fzShell")) fzShell(true);
  const sh = $("#fzShell");
  sh.classList.add("fz-kapali");
  sh.querySelectorAll("input, select, button, textarea").forEach(x => { x.disabled = true; x.title = "Fiziksel envanter yetkiniz yok"; });
  sh.querySelectorAll("details").forEach(d => { d.open = false; d.querySelector("summary")?.addEventListener("click", e => e.preventDefault()); });
  sh.querySelectorAll("a").forEach(a => { a.removeAttribute("href"); a.tabIndex = -1; });
  fz.ozet = null; fz.kabinVeri = null; fz.odaVeri = null;
  $("#fzBilgi").textContent = "";
  $("#fzPlan").innerHTML = `<div class="unk" style="color:var(--err)">🔒 Fiziksel envanter yetkiniz yok — sayfa görüntülenemez. Yetki DokuPanel'den verilir.</div>`;
  $("#fzKabin").innerHTML = ""; $("#fzDetay").innerHTML = ""; $("#fzAra").innerHTML = ""; $("#fzIst").innerHTML = "";
}

function fzShell(kapali) {
  $("#main").innerHTML = `<section class="panel" id="fzShell">
    <div class="toolbar">
      <h2 style="margin:0">🏢 Fiziksel envanter</h2>
      <input type="search" id="fzQ" placeholder="ara: IP, VM, host, uygulama, kabin" style="min-width:260px">
      <label style="flex-direction:row;align-items:center;gap:6px">Oda <select id="fzOda"></select></label>
      <span class="small muted" id="fzBilgi"></span>
      <button type="button" class="mini" id="fzYenile" style="margin-left:auto" title="nlyte tablolarını veritabanından yeniden okur (tablolar sp_KabinZincirYenile ile dolar; DeltaFlow yazmaz)">↻ Yeniden oku</button>
    </div>
    <div id="fzAra"></div>
    <div class="fz-grid">
      <div id="fzPlan"><div class="small muted"><span class="spinner"></span> yükleniyor…</div></div>
      <div id="fzKabin"><div class="unk">Oda planından bir kabin seçin.</div></div>
      <div id="fzDetay"></div>
    </div>
    <details id="fzDisi" class="fz-eksik"><summary><b>Kabin dışı cihazlar</b> <span class="small muted" id="fzDisiSay"></span></summary><div id="fzDisiIc"></div></details>
    <details id="fzEsl" class="fz-eksik"><summary><b>Kabine yerleşmeyen MSY / ÇSY VM'leri</b> <span class="small muted">— VM başına satır; host'u, neden ve nlyte adaylarıyla</span></summary><div id="fzEslIc"></div></details>
    <details id="fzKural" class="fz-eksik"><summary><b>Değer kuralları</b> <span class="small muted">— VM'i olmayan cihazların (ağ, firewall, storage, PDU, Exadata…) değeri nasıl bulunuyor</span></summary><div id="fzKuralIc"></div></details>
    <details id="fzEksik" class="fz-eksik"><summary><b>nlyte'ta eksik host'lar</b> <span class="small muted" id="fzEksikSay"></span></summary><div id="fzEksikIc"></div></details>
    <div id="fzIst" class="fz-ist"></div>
  </section>`;
  let t = null;
  $("#fzQ").addEventListener("input", e => { clearTimeout(t); t = setTimeout(() => fzAra(e.target.value), 300); });
  $("#fzOda").addEventListener("change", e => fzOdaAc(e.target.value));
  $("#fzYenile").addEventListener("click", async () => {
    $("#fzBilgi").innerHTML = `<span class="spinner"></span> yeniden okunuyor…`;
    try { await fetch("api/fiziksel/yenile", { method: "POST" }); } catch { }
    fz.ozet = null; fz.eksik = null; fz.disi = null; fz.esl = null; fz.kural = null; fz.cihaz = {}; fzOzetYukle(true);
  });
  $("#fzEksik").addEventListener("toggle", e => { if (e.target.open && !fz.eksik) fzEksikYukle(); });
  $("#fzEsl").addEventListener("toggle", e => { if (e.target.open && !fz.esl) fzEslYukle(); });
  $("#fzDisi").addEventListener("toggle", e => { if (e.target.open && !fz.disi) fzDisiYukle(); });
  $("#fzKural").addEventListener("toggle", e => { if (e.target.open && !fz.kural) fzKuralYukle(); });
  if (!kapali) fzOzetYukle();
}

async function fzOzetYukle(yeniden) {
  try {
    fz.ozet = await fzGet("api/fiziksel/ozet");
  } catch (e) {
    $("#fzPlan").innerHTML = `<div class="unk" style="color:var(--err)">${esc(e.message)}</div>`;
    $("#fzBilgi").textContent = "";
    return;
  }
  const o = fz.ozet, ana = o.kapsamlar.find(k => k.kapsam === o.anaKapsam);
  const diger = o.kapsamlar.filter(k => k.kapsam !== o.anaKapsam);
  $("#fzBilgi").innerHTML = (o.toplamKabin ? `${fmt(o.toplamKabin)} kabin (VM'li host olan: ${fmt(o.vmliKabin)})${o.cihaz ? ` · <span title="${esc(fzTurOzet(o.turlar, o.cihaz))}${o.cihazTablosu ? " (" + esc(o.cihazTablosu) + ")" : ""}">${fmt(o.cihaz)} nlyte cihazı</span>` : ""} · ` : "")
    + (!o.nlyte ? `<span style="color:var(--warn)" title="NlyteKabin / NlyteCihaz okunamadı">yalnız VM'li kabinler</span> · ` : "")
    + (o.cihazTablosu && /sunucu/i.test(o.cihazTablosu) ? `<span style="color:var(--warn)" title="dbo.NlyteCihaz bulunamadı; yalnız sunucular (${esc(o.cihazTablosu)}) gösteriliyor">yalnız sunucular</span> · ` : "")
    + (ana ? `<b>${esc(ana.kapsam)}</b>: ${fmt(ana.vm)} VM, kabini bilinen %${ana.vm ? Math.round(ana.kabinli * 100 / ana.vm) : 0}` : "")
    + (diger.length ? ` · <span title="nlyte yalnız ana veri merkezini kapsar">${diger.map(k => `${esc(k.kapsam)} ${fmt(k.vm)}`).join(" · ")}</span>` : "")
    + (o.yenileme ? ` · son yenileme ${new Date(o.yenileme).toLocaleString("tr-TR", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" })}` : "");
  fzIstCiz(o.istatistik);
  $("#fzEksikSay").textContent = (o.eksikSayisi ? `(${fmt(o.eksikSayisi)} host)` : "") + (o.turFarkliSayisi ? ` · ${fmt(o.turFarkliSayisi)} host ek eşleştirmeyle kabine bağlandı` : "");
  $("#fzDisiSay").textContent = o.kabinDisi ? `(${fzTurOzet(o.kabinDisiTurlar, o.kabinDisi)})` : "(yok)";
  $("#fzOda").innerHTML = o.odalar.map(x => `<option value="${esc(x.ad)}">${esc(x.ad)}${x.konum && x.ad !== "Diğer" ? " · " + esc(x.konum) : ""} (${fmt(x.kabin)} kabin)</option>`).join("");
  const ilk = (!yeniden && fz.oda && o.odalar.some(x => x.ad === fz.oda)) ? fz.oda : o.odalar[0]?.ad;
  if (ilk) { $("#fzOda").value = ilk; fzOdaAc(ilk); } else $("#fzPlan").innerHTML = `<div class="unk">Kabin bilgisi olan kayıt yok.</div>`;
  if (yeniden && fz.kabin) fzKabinAc(fz.kabin);
}

// Alt istatistik kartları (ana veri merkezi)
function fzIstCiz(i) {
  const el = $("#fzIst");
  if (!el) return;
  if (!i) { el.innerHTML = ""; return; }
  const yuz = (a, b) => b ? `%${(a * 100 / b).toFixed(1).replace(".0", "")}` : "—";
  const kart = (baslik, ana, alt, ipucu, renk) => `<div class="fz-ist-k" title="${esc(ipucu ?? "")}"${renk ? ` style="border-top-color:${renk}"` : ""}>
      <div class="small muted">${baslik}</div><div class="fz-ist-n">${ana}</div><div class="small">${alt}</div></div>`;
  el.innerHTML = `<h3 style="margin:16px 0 8px">İstatistik <span class="small muted">${esc(fz.ozet?.anaKapsam ?? "Ana veri merkezi")}</span></h3>
    <div class="fz-ist-g">
      ${kart("Kabine yerleşen sunucu", fmt(i.sunucu), `U konumu bilinen: ${fmt(i.sunucuU)} · kabin dışı (depo vb.): ${fmt(i.kabinDisiSunucu)}`, "nlyte'ta bir kabine bağlı aktif sunucu cihazları (blade'ler chassis'in kabininde sayılır)", "var(--in)")}
      ${kart("ESX host yerleşimi", `${fmt(i.esxYerlesen)} <span class="small muted">/ ${fmt(i.esxHost)}</span>`, `${yuz(i.esxYerlesen, i.esxHost)} · ek eşleşme: ${fmt(i.esxEkEslesme)} · nlyte'ta yok: ${fmt(i.esxEksik)}`, "VM envanterindeki ESX host'larından kabini bilinenler", "var(--in)")}
      ${kart("VM yerleşimi", `${fmt(i.vmKabinli)} <span class="small muted">/ ${fmt(i.vm)}</span>`, `%${i.vmOran} kabini bilinen · uygulaması yok: ${fmt(i.vmUygulamasiz)}`, "Kabini bilinen VM'ler", "var(--in)")}
      ${kart("Kabine yerleşen uygulama", `${fmt(i.uygulamaYerlesen)} <span class="small muted">/ ${fmt(i.uygulama)}</span>`, `${yuz(i.uygulamaYerlesen, i.uygulama)} · tüm VM'leri yerleşen: ${fmt(i.uygulamaTamami)} · hiç yerleşmeyen: ${fmt(i.uygulamaHicKabin)}`, "En az bir VM'i kabini bilinen host'ta çalışan ERT uygulamaları", "#2e9d5b")}
      ${kart("Tek kabine bağlı uygulama", fmt(i.uygulamaTekKabin), "tüm VM'leri aynı kabinde — kabin kapanırsa tamamen durur", "Kabini bilinen tüm VM'leri tek bir kabinde olan uygulamalar", "var(--err)")}
      ${i.kapsamlar ? kart("Tüm VM'ler (MSY + ÇSY)", `${fmt(i.tumVmKabinli)} <span class="small muted">/ ${fmt(i.tumVm)}</span>`,
        i.kapsamlar.map(k => `${esc(k.kapsam)}: ${fmt(k.vm)}${k.ana ? ` (yerleşen ${fmt(k.kabinli)})` : k.kabinli ? ` (yerleşen ${fmt(k.kabinli)})` : ""}`).join(" · ")
        + (i.ekKapsamdan?.length ? ` · <span style="color:var(--in)">${i.ekKapsamdan.map(x => `${esc(x.kapsam)} kapsamından ${fmt(x.vm)} VM ana DC'ye yerleşti`).join(", ")}</span>` : ""),
        "nlyte yalnız ana veri merkezini (Esenyurt) kapsar; ODM / KKB VM'leri kabine yerleşemez", "var(--muted)") : ""}
      ${i.degerVar && i.kabinDeger ? kart("Kabin değerleri", (() => { const m = Math.max(...Object.keys(i.kabinDeger[2] ?? {}).filter(x => x !== "yok").map(Number), 0); return `${fmt(i.kabinDeger[2]?.[m] ?? 0)} <span class="small muted">kabin erişilebilirlik ${m}</span>`; })(),
        FZ_DEGER.map(([, , ad], j) => `${ad}: ${Object.entries(i.kabinDeger[j] ?? {}).map(([d, n]) => `${d === "yok" ? "değer yok" : d} → ${fmt(n)}`).join(", ")}`).join("<br>"),
        "Kabin değeri = kabindeki uygulamaların (ERT) ve cihazların (tür kuralları) en yüksek değeri; tüm kabinler", "#c0392b") : ""}
      ${i.kabloVar ? kart("Kablo kaydı", `${fmt(i.kabinKablolu)} / ${fmt(i.kabin)} kabin`,
        `sunucu: ${fmt(i.sunucuKablolu)} / ${fmt(i.sunucu)} (veri / fiber: ${fmt(i.sunucuVeri)})<br>VM'li host: ${fmt(i.hostVmliVeri)} / ${fmt(i.hostVmli)} veri / fiber kablolu<br>${fmt(i.kablo)} kablo`,
        "nlyte Connections: en az bir cihazında kablo kaydı olan kabin; oda planında Renk → Kablo kaydı", "#2e9d5b") : ""}
      ${kart("Kabin", fmt(i.kabin), `cihazlı: ${fmt(i.kabinCihazli)} · VM'li: ${fmt(i.kabinVmli)} · ${fmt(i.cihaz)} aktif cihaz`, "nlyte kabinleri", "var(--muted)")}
    </div>`;
}

async function fzOdaAc(ad) {
  fz.oda = ad;
  $("#fzPlan").innerHTML = `<div class="small muted"><span class="spinner"></span> ${esc(ad)}…</div>`;
  try { fz.odaVeri = await fzGet("api/fiziksel/oda?" + new URLSearchParams({ ad })); }
  catch (e) { $("#fzPlan").innerHTML = `<div class="unk" style="color:var(--err)">${esc(e.message)}</div>`; return; }
  fzPlanCiz();
}

// Oda planı: satır = sıra harfi, sütun = numara; renk VM sayısına göre (tek ton, açıktan koyuya)
function fzPlanCiz() {
  const v = fz.odaVeri, el = $("#fzPlan");
  if (!v) return;
  // ızgara: sıra + no belli olan asıl kayıtlar; aynı konumdaki diğer kayıtlar "ek" (ZB.… raf üstü vb.), sırası belli no'su yok, hiç kalıba uymayan
  const izgara = v.kabinler.filter(k => k.sira && k.no && !k.ek), ekler = v.kabinler.filter(k => k.ek);
  const siraliDiger = v.kabinler.filter(k => k.sira && !k.no), digerler = v.kabinler.filter(k => !k.sira);
  const adsiz = digerler.filter(k => /^Asset_/i.test(k.ad)), kalan = digerler.filter(k => !/^Asset_/i.test(k.ad));
  const ekSay = new Map(); for (const k of ekler) ekSay.set(k.ek, [...(ekSay.get(k.ek) ?? []), k.ad]);
  const maxVm = Math.max(1, ...v.kabinler.map(k => k.vm));
  // renk VM sayısına göre; VM'i olmayan ama cihazı olan kabin gri, hiç cihazı olmayan kesikli çerçeve
  const kb = fz.renk === "kablo", dg = fz.renk && fz.renk !== "vm" && !kb ? fz.renk : null;
  const tile = k => {
    const p = 12 + Math.round(78 * k.vm / maxVm), koyu = p > 55;
    const tur = k.vm > 0 ? "" : (k.cihaz ?? 0) > 0 ? " vmsiz" : " bosk";
    const dv = dg ? k.deger?.[dg] : null;
    const kp = k.cihaz ? k.kablo / k.cihaz : 0;
    const stil = kb ? (k.kablo > 0 ? ` style="background:color-mix(in srgb, #2e9d5b ${20 + Math.round(70 * kp)}%, var(--panel));color:${kp > 0.5 ? "#fff" : "var(--text)"}"` : "")
      : dg ? (dv != null ? ` style="background:${fzDegerRenk(dv)};color:#fff"` : "")
      : k.vm > 0 ? ` style="background:color-mix(in srgb, var(--in) ${p}%, var(--panel));color:${koyu ? "#fff" : "var(--text)"}"` : "";
    return `<button type="button" class="fz-tile${tur}${fz.kabin === k.ad ? " on" : ""}${fz.vurgu.has(k.ad) ? " vurgu" : ""}" data-kabin="${esc(k.ad)}"${stil}
      title="${esc(k.ad)} · ${k.turlar?.length ? esc(fzTurOzet(k.turlar, k.cihaz)) : fmt(k.cihaz ?? k.host) + " nlyte cihazı"} · ${fmt(k.vmHost ?? k.host)} VM'li host · ${fmt(k.vm)} VM · ${fmt(k.uygulama)} uygulama${k.kablo != null ? ` · kablolu cihaz ${fmt(k.kablo)} / ${fmt(k.cihaz ?? 0)}` : ""}${k.bosU != null ? ` · ${k.bosU} boş U` : ""}${k.vm ? "" : (k.cihaz ? " — VM bilgisi olan host yok" : " — nlyte'ta cihaz yok")}${k.deger ? " · değer " + esc(fzDegerMetin(k.deger)) : ""}${ekSay.has(k.ad) ? " · aynı konumda ek kayıt: " + esc(ekSay.get(k.ad).join(", ")) : ""}">${esc(k.sira ? k.sira + k.no : k.ad)}${ekSay.has(k.ad) ? `<sup class="fz-ek">+${ekSay.get(k.ad).length}</sup>` : ""}</button>`;
  };
  const btn = (k, not) => `<button type="button" class="mini${fz.kabin === k.ad ? " on" : ""}${fz.vurgu.has(k.ad) ? " vurgu" : ""}" data-kabin="${esc(k.ad)}" title="${fmt(k.cihaz ?? 0)} cihaz · ${fmt(k.vm)} VM${k.deger ? " · değer " + esc(fzDegerMetin(k.deger)) : ""}">${esc(k.ad)} <span class="muted">${not ? esc(not) + " · " : ""}${fmt(k.cihaz ?? 0)} cihaz${k.vm ? " · " + fmt(k.vm) + " VM" : ""}</span></button>`;
  let plan = "";
  if (izgara.length) {
    const siralar = [...new Set(izgara.map(k => k.sira))].sort((a, b) => a.length - b.length || a.localeCompare(b));
    const maxNo = Math.max(...izgara.map(k => k.no));
    const byKey = new Map(izgara.map(k => [k.sira + "|" + k.no, k]));
    plan = `<div class="fz-plan" style="grid-template-columns:22px repeat(${maxNo}, 40px)">
      <span></span>${Array.from({ length: maxNo }, (_, i) => `<span class="fz-ax">${i + 1}</span>`).join("")}
      ${siralar.map(s => `<span class="fz-ax">${esc(s)}</span>` + Array.from({ length: maxNo }, (_, i) => byKey.get(s + "|" + (i + 1)) ? tile(byKey.get(s + "|" + (i + 1))) : `<span class="fz-bos"></span>`).join("")).join("")}
    </div>`;
  }
  const vmli = v.kabinler.filter(k => k.vm > 0).length, cihazli = v.kabinler.filter(k => !k.vm && k.cihaz > 0).length, bos = v.kabinler.length - vmli - cihazli;
  el.innerHTML = `<div class="small muted" style="margin-bottom:6px">${esc(v.ad)} · ${fmt(v.kabinler.length)} kabin · ${fmt(v.kabinler.reduce((a, k) => a + k.vm, 0))} VM
      <div class="fz-lejant"><label style="flex-direction:row;align-items:center;gap:4px" title="Kabin değeri = kabindeki uygulamaların (ERT) ve cihazların (tür kuralları) en yüksek değeri">Renk
        <select id="fzRenk"><option value="vm">VM sayısı</option><option value="kablo"${kb ? " selected" : ""}>Kablo kaydı</option>${FZ_DEGER.map(([k, , ad]) => `<option value="${k}"${fz.renk === k ? " selected" : ""}>${ad} değeri</option>`).join("")}</select></label>
      ${kb ? `<span><i style="background:color-mix(in srgb, #2e9d5b 80%, var(--panel))"></i>cihazların çoğu kablolu (${v.kabinler.filter(k => k.cihaz && k.kablo / k.cihaz > 0.5).length})</span><span><i style="background:color-mix(in srgb, #2e9d5b 30%, var(--panel))"></i>bir kısmı (${v.kabinler.filter(k => k.kablo > 0 && k.kablo / k.cihaz <= 0.5).length})</span><span><i class="bosk"></i>kablo kaydı yok (${v.kabinler.filter(k => !k.kablo).length})</span>`
        : dg ? [...new Set(v.kabinler.map(k => k.deger?.[dg]).filter(n => n != null))].sort((a, b) => b - a).map(n => `<span><i style="background:${fzDegerRenk(n)}"></i>${n} (${v.kabinler.filter(k => k.deger?.[dg] === n).length})</span>`).join("")
          + `<span><i class="bosk"></i>değer yok (${v.kabinler.filter(k => k.deger?.[dg] == null).length})</span>`
        : `<span><i style="background:color-mix(in srgb, var(--in) 55%, var(--panel))"></i>VM'li (${vmli})</span><span><i class="vmsiz"></i>cihaz var, VM yok (${cihazli})</span><span><i class="bosk"></i>cihaz yok (${bos})</span>`}</div></div>
    <div class="fz-plan-wrap">${plan}</div>
    ${siraliDiger.length ? `<div class="small" style="margin-top:8px"><b>Sırası belli, numarası adında yok</b> <span class="muted">(ör. DR1-B-EMC-1, ZB.DR3.C.NS6)</span></div>
      ${[...new Set(siraliDiger.map(k => k.sira))].sort().map(s => `<div class="fz-diger"><span class="fz-ax" style="min-width:42px;text-align:left">Sıra ${esc(s)}</span>${siraliDiger.filter(k => k.sira === s).map(k => btn(k)).join("")}</div>`).join("")}` : ""}
    ${ekler.length ? `<details class="small" style="margin-top:8px"><summary><b>Aynı konumda ek kabin kayıtları</b> (${fmt(ekler.length)}) <span class="muted">— ızgaradaki kabinin yerine düşen ikinci kayıtlar (ör. ZB.… raf üstü)</span></summary>
      <div class="fz-diger">${ekler.map(k => btn(k, `→ ${k.sira}${k.no}`)).join("")}</div></details>` : ""}
    ${kalan.length ? `<div class="small" style="margin-top:8px"><b>${izgara.length || siraliDiger.length ? "Diğer kabinler" : "Kabinler"}</b> <span class="muted">(adından oda / sıra çıkmayan)</span></div>
      <div class="fz-diger">${kalan.map(k => btn(k)).join("")}</div>` : ""}
    ${adsiz.length ? `<details class="small" style="margin-top:8px"><summary><b>Adsız kabin kayıtları</b> (Asset_…, ${fmt(adsiz.length)}) <span class="muted">— nlyte'ta adı verilmemiş, ${fmt(adsiz.filter(k => k.cihaz || k.vm).length)} tanesinde cihaz var</span></summary>
      <div class="fz-diger">${adsiz.sort((a, b) => (b.cihaz ?? 0) - (a.cihaz ?? 0)).map(k => btn(k)).join("")}</div></details>` : ""}`;
  el.querySelectorAll("[data-kabin]").forEach(b => b.addEventListener("click", () => fzKabinAc(b.dataset.kabin)));
  $("#fzRenk").addEventListener("change", e => { fz.renk = e.target.value; try { localStorage.setItem("df.fzRenk", fz.renk); } catch { } fzPlanCiz(); });
}

async function fzKabinAc(ad, host) {
  if (fz.kabin !== ad) fz.yuz = "Front";   // yeni kabin ön yüzle açılır
  fz.kabin = ad; fz.host = host ?? null; fz.mod = host ? "host" : "uyg";
  fzPlanCiz();
  $("#fzKabin").innerHTML = `<div class="small muted"><span class="spinner"></span> ${esc(ad)}…</div>`;
  try { fz.kabinVeri = await fzGet("api/fiziksel/kabin?" + new URLSearchParams({ ad })); }
  catch (e) { $("#fzKabin").innerHTML = `<div class="unk" style="color:var(--err)">${esc(e.message)}</div>`; return; }
  // kabin başka odadaysa (arama, derin bağlantı, eksik listesi) o odayı aç
  const oda = fz.kabinVeri.oda;
  if (oda && oda !== fz.oda && fz.ozet?.odalar.some(o => o.ad === oda)) { $("#fzOda").value = oda; await fzOdaAc(oda); }
  // seçili host arka yüzdeyse yüzü ona göre aç
  const h = host && fz.kabinVeri.hostlar.find(x => x.ad.toLowerCase() === host.toLowerCase());
  if (h?.yuz) fz.yuz = /rear/i.test(h.yuz) ? "Rear" : "Front";
  fzKabinCiz(); fzDetayCiz();
}

// Ölçekli kabin çizimi: kabin yüksekliği uKapasite, host HostUAlt'tan HostUYukseklik kadar; U1 altta (u1Ustte ise üstte).
// Aynı chassis'teki blade'ler tek kutu, içinde yuva sırasıyla.
function fzKabinCiz() {
  const k = fz.kabinVeri, el = $("#fzKabin");
  if (!k) return;
  const U = k.uKapasite, H = U * FZ_UH;
  const yuzu = h => /rear/i.test(h.yuz ?? "") ? "Rear" : "Front";
  const uLu = k.hostlar.filter(h => h.uAlt != null), uSuz = k.hostlar.filter(h => h.uAlt == null);
  const buYuz = uLu.filter(h => yuzu(h) === fz.yuz);
  const top = (alt, yuk) => k.u1Ustte ? (alt - 1) * FZ_UH : (U - (alt + yuk - 1)) * FZ_UH;
  // kutular: chassis'liler gruplanır
  const kutular = [];
  const ch = new Map();
  // chassis cihazı (tur Chassis) ve blade'leri (chassisId = chassis AssetID) tek kutu; eski veride chassis adı + U ile
  const chKutu = (key, ad, uAlt, yuk) => {
    if (!ch.has(key)) { const c = { chassis: ad, uAlt, yuk, bladeler: [] }; ch.set(key, c); kutular.push(c); }
    const c = ch.get(key); c.yuk = Math.max(c.yuk, yuk); return c;
  };
  for (const h of buYuz) {
    const yuk = Math.max(1, h.uYukseklik ?? 1);
    if (h.tur === "Chassis") { const c = chKutu("id:" + (h.assetId ?? h.ad), h.ad, h.uAlt, yuk); c.cihaz = h; c.chassis = h.ad; c.uAlt = h.uAlt; }
    else if (h.chassis) chKutu(h.chassisId ? "id:" + h.chassisId : h.chassis + "|" + h.uAlt, h.chassis, h.uAlt, yuk).bladeler.push(h);
    else kutular.push({ host: h, uAlt: h.uAlt, yuk });
  }
  // Dikey (0U) PDU'lar: nlyte'ta U1'den kabin boyu kadar görünür (ör. AP88xx) → kutu değil, kabinin kenarında şerit
  const dikeyMi = c => c.host && ((c.host.tur === "PDU" && c.yuk >= Math.max(8, U * 0.4))
    || (!["Server", "Storage", "Chassis"].includes(c.host.tur || "Server") && c.yuk >= Math.max(12, U * 0.6)));
  const seritler = kutular.filter(dikeyMi), rafKutu = kutular.filter(c => !dikeyMi(c));
  const SW = 16, solS = Math.ceil(seritler.length / 2), sagS = Math.floor(seritler.length / 2);
  // Aynı U aralığına düşen kutular yan yana (şerit): çakışan küme başına açgözlü yerleşim
  rafKutu.sort((a, b) => a.uAlt - b.uAlt || b.yuk - a.yuk);
  let kume = [], kumeSon = -Infinity;
  const kumeBitir = () => { const n = Math.max(...kume.map(c => c.serit + 1)); kume.forEach(c => c.n = n); kume = []; };
  for (const c of rafKutu) {
    const bas = c.uAlt, son = c.uAlt + c.yuk - 1;
    if (kume.length && bas > kumeSon) kumeBitir();
    const dolu = new Set(kume.filter(x => x.uAlt + x.yuk - 1 >= bas).map(x => x.serit));
    let i = 0; while (dolu.has(i)) i++;
    c.serit = i; kume.push(c); kumeSon = Math.max(kumeSon, son);
  }
  if (kume.length) kumeBitir();
  const cakisan = rafKutu.filter(c => c.n > 1).length;
  const sol = 3 + solS * (SW + 2), sag = 3 + sagS * (SW + 2);
  const yatay = c => `left:calc(${sol}px + (100% - ${sol + sag}px) * ${c.serit} / ${c.n});width:calc((100% - ${sol + sag}px) / ${c.n} - ${c.n > 1 ? 2 : 0}px);right:auto;`;
  const sunucu = h => !h.tur || h.tur === "Server" || (h.vmBilgisi && h.vm > 0);   // VM'li cihaz türü yanlış kayıtlı olsa da sunucudur
  // sunucu: VM'li mavi, VM'siz gri çizgili; diğer türler kendi rengiyle
  const sinif = h => sunucu(h) ? (h.vmBilgisi === false ? " vmsiz" : " vmli") : " tur";
  const hostBtn = (h, cls, stil) => `<button type="button" class="${cls}${sinif(h)}${fz.host === h.ad ? " on" : ""}" data-host="${esc(h.ad)}" style="--tc:${fzTurRenk(h.tur)};${stil}"
      title="${esc(h.ad)} · ${esc(fzTurAd(h.tur))}${h.deger ? " · değer " + esc(fzDegerMetin(h.deger)) : ""}${h.bag ? ` · ${h.bag} kablo` : ""}${h.turUyari ? " ⚠ " + esc(h.turUyari) : ""}${h.tip ? " · " + esc(h.tip) : ""}${h.model ? " · " + esc(h.model) : ""} · ${h.vmBilgisi === false ? "VM bilgisi yok" : fmt(h.vm) + " VM"} · U${h.uAlt ?? "?"}${h.uYukseklik ? `–${h.uAlt + h.uYukseklik - 1}` : ""}${h.cluster ? " · " + esc(h.cluster) : ""}${h.slot ? " · yuva " + esc(h.slot) : ""}">`;
  const kutuHtml = c => {
    const t = top(c.uAlt, c.yuk), y = c.yuk * FZ_UH - 2;
    if (c.host) {
      const h = c.host, kisa = y < 24;
      const vmYazi = !sunucu(h) ? `<span class="fz-tur">${esc(fzTurAd(h.tur))}</span>` : h.vmBilgisi === false ? "VM yok" : `${fmt(h.vm)} VM`;
      return hostBtn(h, (y < 16 ? "fz-host u1" : "fz-host") + (c.n > 1 ? " cakis" : ""), `top:${t + 1}px;height:${y}px;${yatay(c)}`) + `<b>${esc(h.ad)}</b>${kisa ? ` <span class="muted">${vmYazi}</span>` : `<span class="small muted">${esc(h.model ?? "")}</span><span class="small">${vmYazi}</span>`}</button>`;
    }
    const bl = [...c.bladeler].sort((a, b) => (+a.slot || 0) - (+b.slot || 0) || String(a.slot).localeCompare(String(b.slot)));
    return `<div class="fz-chassis" style="top:${t + 1}px;height:${y}px;${yatay(c)}" title="${esc(c.chassis)} · ${bl.length} blade${c.cihaz?.model ? " · " + esc(c.cihaz.model) : ""}">
      ${c.cihaz ? `<button type="button" class="fz-chassis-ad btn" data-host="${esc(c.cihaz.ad)}">${esc(c.chassis)}</button>` : `<span class="fz-chassis-ad">${esc(c.chassis)}</span>`}
      ${!bl.length ? `<span class="small muted" style="position:absolute;top:1px;right:5px;font-size:10px">blade yok</span>` : ""}
      <div class="fz-bladeler">${bl.map(h => hostBtn(h, "fz-blade", "") + `<span class="fz-slot">${esc(h.slot ?? "?")}</span><span class="fz-bad">${esc(h.ad)}</span><span class="small">${fmt(h.vm)}</span></button>`).join("")}</div></div>`;
  };
  const etiketler = Array.from({ length: U }, (_, i) => { const n = i + 1; return n === 1 || n % 5 === 0 || n === U ? `<span style="top:${top(n, 1)}px">${n}</span>` : ""; }).join("");
  const nOn = uLu.filter(h => yuzu(h) === "Front").length, nArka = uLu.length - nOn;
  const vmSay = k.vmler.length, hostSay = k.hostlar.length;
  const ozet = k.cihazSayisi != null ? `${k.turlar?.length ? esc(fzTurOzet(k.turlar, k.cihazSayisi)) : fmt(k.cihazSayisi) + " cihaz"} · ${fmt(k.vmHostSayisi)} VM'li host · ${fmt(vmSay)} VM` : `${fmt(hostSay)} host · ${fmt(vmSay)} VM`;
  const turler = [...new Set(k.hostlar.map(h => h.tur || "Server"))];
  const lejant = `<div class="fz-lejant small">${k.hostlar.some(h => sunucu(h) && h.vmBilgisi !== false) ? `<span><i style="background:color-mix(in srgb, var(--in) 30%, var(--panel));border-left:3px solid var(--in)"></i>VM'li sunucu</span>` : ""}
    ${k.hostlar.some(h => sunucu(h) && h.vmBilgisi === false) ? `<span><i class="vmsiz"></i>sunucu, VM yok</span>` : ""}
    ${turler.filter(t => t !== "Server").map(t => `<span><i style="background:color-mix(in srgb, ${fzTurRenk(t)} 25%, var(--panel));border-left:3px solid ${fzTurRenk(t)}"></i>${esc(fzTurAd(t))}</span>`).join("")}</div>`;
  el.innerHTML = `<div class="fz-kabin-bas"><b>${esc(k.ad)}</b> · ${ozet}${k.konum ? ` <div class="small muted">${esc(k.konum)}</div>` : ""}
      <div class="small muted">${U}U${k.bosU != null ? ` · ${k.bosU} boş U` : ""} · U1 ${k.u1Ustte ? "üstte" : "altta"}</div>${k.deger ? `<div class="small" style="margin:2px 0" title="Kabindeki uygulamaların ve cihazların (tür kuralları dahil) en yüksek değerleri; rozetin üstüne gelince kaynağı">Kabin değeri: ${fzDeger(k.deger, true)}</div>` : ""}${k.disBaglanti?.length ? `<div class="small" style="margin:2px 0" title="Bu kabindeki cihazlardan başka kabinlere giden nlyte kabloları">Kabin dışı kablolar: ${k.disBaglanti.slice(0, 8).map(x => `<a href="#" data-fzk2="${esc(x.kabin)}">${esc(x.kabin)}</a> (${fmt(x.kablo)})`).join(", ")}${k.disBaglanti.length > 8 ? ` <span class="muted">+${k.disBaglanti.length - 8}</span>` : ""}</div>` : ""}${lejant}</div>
    <div class="toolbar" style="margin:6px 0">
      <div class="seg"><button type="button" data-fzyuz="Front" class="${fz.yuz === "Front" ? "on" : ""}">Ön (${nOn})</button><button type="button" data-fzyuz="Rear" class="${fz.yuz === "Rear" ? "on" : ""}">Arka (${nArka})</button></div>
    </div>
    <div class="toolbar" style="margin:0 0 8px">
      <button type="button" class="mini${fz.mod === "uyg" ? " on" : ""}" data-fzmod="uyg">Bu kabinin uygulamaları</button>
      <button type="button" class="mini${fz.mod === "etki" ? " on" : ""}" data-fzmod="etki" title="Kabin kapanırsa: yalnız bu kabinde çalışan uygulamalar önce">Kabin kapanırsa</button>
    </div>
    <div class="fz-rack-wrap"><div class="fz-u">${etiketler}</div>
      <div class="fz-rack" style="height:${H}px">${rafKutu.map(kutuHtml).join("")}${seritler.map((c, i) => {
        const h = c.host, sagda = i >= solS, j = sagda ? i - solS : i;
        return hostBtn(h, "fz-serit", `top:${top(c.uAlt, c.yuk) + 1}px;height:${c.yuk * FZ_UH - 2}px;${sagda ? "right" : "left"}:${3 + j * (SW + 2)}px;width:${SW}px`) + `<span>${esc(h.ad)}</span></button>`;
      }).join("")}${!buYuz.length ? `<div class="fz-bos-yuz">${fz.yuz === "Front" ? "Ön" : "Arka"} yüzde host yok</div>` : ""}</div></div>
    ${cakisan ? `<div class="small" style="margin-top:6px;color:var(--warn)" title="nlyte'ta aynı yüzde aynı U aralığına kayıtlı cihazlar yan yana çizildi (veri hatası ya da yarım genişlikte cihazlar olabilir)">⚠ ${fmt(cakisan)} cihaz aynı U aralığında başka cihazla çakışıyor (yan yana çizildi)</div>` : ""}
    ${seritler.length ? `<div class="small muted" style="margin-top:4px">Kenardaki şeritler: dikey (0U) PDU (${seritler.length})</div>` : ""}
    ${uSuz.length ? `<div class="small" style="margin-top:8px" title="nlyte'ta U konumu yok: yan montajlı (0U) PDU, kabine bağlı ama U'su girilmemiş cihaz ya da chassis'i bulunamayan blade"><b>0U / U bilgisi yok</b> (${uSuz.length})</div>
      <div class="fz-diger fz-0u">${uSuz.map(h => `<button type="button" class="mini${fz.host === h.ad ? " on" : ""}" data-host="${esc(h.ad)}" style="--tc:${fzTurRenk(h.tur)}">${esc(h.ad)} <span class="fz-tur">${esc(fzTurAd(h.tur))}</span>${sunucu(h) ? ` <span class="muted">${h.vmBilgisi === false ? "VM yok" : fmt(h.vm) + " VM"}</span>` : ""}</button>`).join("")}</div>` : ""}
    ${k.nlyteEslesmeyen?.length ? `<div class="small muted" style="margin-top:6px" title="KabinHostVm bu host'ları bu kabine bağlıyor ama NlyteSunucu'daki cihazlarla seri / ad eşleşmedi">nlyte cihaz listesinde eşleşmeyen VM'li host: ${esc(k.nlyteEslesmeyen.join(", "))}</div>` : ""}
    ${k.pasif?.length ? `<details class="small" style="margin-top:6px"><summary>Aktif olmayan nlyte kayıtları (${k.pasif.length})</summary>
      ${k.pasif.map(p => `<div>${esc(p.ad)} <span class="muted">· ${esc([fzTurAd(p.tur), p.durum, p.operasyon, p.model, p.uAlt != null ? "U" + p.uAlt : null].filter(Boolean).join(" · "))}</span></div>`).join("")}</details>` : ""}`;
  el.querySelectorAll("[data-host]").forEach(b => b.addEventListener("click", () => { fz.host = b.dataset.host; fz.mod = "host"; fzKabinCiz(); fzDetayCiz(); }));
  el.querySelectorAll("[data-fzk2]").forEach(a => a.addEventListener("click", ev => { ev.preventDefault(); fzKabinAc(a.dataset.fzk2); }));
  el.querySelectorAll("[data-fzyuz]").forEach(b => b.addEventListener("click", () => { fz.yuz = b.dataset.fzyuz; fzKabinCiz(); }));
  el.querySelectorAll("[data-fzmod]").forEach(b => b.addEventListener("click", () => { fz.mod = b.dataset.fzmod; fz.host = null; fzKabinCiz(); fzDetayCiz(); }));
}

const fzAppLink = a => `<a href="?${new URLSearchParams({ app: a })}" target="_blank" rel="noopener" title="Uygulama görünümünü yeni sekmede aç">${esc(a)}</a>`;
const fzIpLink = ip => ip ? `<a class="mono" href="${esc(ipTabUrl(ip))}" target="_blank" rel="noopener" title="IP topolojisini yeni sekmede aç">${esc(ip)}</a>` : "";

function fzDetayCiz() {
  const k = fz.kabinVeri, el = $("#fzDetay");
  if (!k) { el.innerHTML = ""; return; }
  if (fz.mod === "host" && fz.host) {
    const h = k.hostlar.find(x => x.ad === fz.host);
    if (!h) { el.innerHTML = ""; return; }
    const vmler = k.vmler.filter(v => (v.host ?? "").toLowerCase() === h.ad.toLowerCase());
    const srv = !h.tur || h.tur === "Server" || (h.vmBilgisi && h.vm > 0);
    el.innerHTML = `<h2 style="margin:0 0 6px">${esc(h.ad)} <span class="badge" style="background:${fzTurRenk(h.tur)};color:#fff">${esc(fzTurAd(h.tur))}</span></h2>
      ${h.turUyari ? `<div class="small" style="color:var(--warn);margin:0 0 6px">⚠ ${esc(h.turUyari)}</div>` : ""}
      ${h.deger ? `<div class="small" style="margin:0 0 6px" title="Boyut başına en yüksek değer; rozetin üstüne gelince değeri veren uygulama / kural">Değer: ${fzDeger(h.deger, true)}${h.deger.elle ? ` <span class="badge" style="background:#8e44ad;color:#fff">elle</span>` : ""}${h.deger.yontem ? `<div class="muted">nasıl: ${esc(h.deger.yontem)}</div>` : ""}</div>`
        : `<div class="small muted" style="margin:0 0 6px">Değer bulunamadı: VM'i yok, ERT'de adı / IP'si eşleşmedi, tür kuralı yok (alttaki "Değer kuralları").</div>`}
      ${h.assetId ? `<div style="margin:0 0 8px"><button type="button" class="mini" id="fzElleAc"${yetki?.fizikseldeger ? "" : ` disabled title="Elle değer yetkiniz yok (DokuPanel: Yetki:FizikselDeger)"`}>✎ Elle değer${h.deger?.elle ? " (düzenle / kaldır)" : ""}</button><div id="fzElle"></div></div>` : ""}
      <dl class="fz-dl">
        ${[["Malzeme tipi", h.tip], ["Durum", h.durum], ["Cluster", h.cluster], ["FQDN", h.fqdn], ["Model", h.model], ["Seri", h.seri], ["nlyte AssetID", h.assetId], ["Konum", h.uAlt != null ? `U${h.uAlt}${h.uYukseklik > 1 ? `–${h.uAlt + h.uYukseklik - 1}` : ""} · ${/rear/i.test(h.yuz ?? "") ? "arka" : "ön"} yüz` : "U bilgisi yok"],
           ["iLO", h.msy?.iloIp], ["Yönetim IP", h.msy?.dnsIp], ["OS", h.msy?.osName && `${h.msy.osName}${h.msy.osVersion ? " " + h.msy.osVersion : ""}`],
           ["Güç", h.msy?.powerState], ["Son reboot", h.msy?.lastReboot && new Date(h.msy.lastReboot).toLocaleString("tr-TR")], ["Chassis", h.chassis && `${h.chassis}${h.slot ? " · yuva " + h.slot : ""}${h.blade ? " (U chassis'ten)" : ""}`], ["Eşleşme", h.eslesme], ["Kaynak", h.kaynak]]
          .filter(x => x[1]).map(([a, b]) => `<dt>${a}</dt><dd>${esc(b)}</dd>`).join("")}
      </dl>
      ${!srv ? `<div class="small muted" style="margin:8px 0">${esc(fzTurAd(h.tur))} — VM barındırmaz.</div>`
        : h.vmBilgisi === false ? `<div class="unk" style="margin:8px 0">Bu cihaz için MSY / ÇSY'de VM bulunamadı: fiziksel sunucu, storage / appliance ya da VM envanterinde adı farklı geçen bir ESX host'u olabilir.</div>`
        : `<div class="small muted" style="margin:8px 0 4px">${fmt(vmler.length)} VM · ${fmt(h.uygulama)} uygulama${h.nlyte === false ? " · nlyte cihaz listesinde eşleşmedi" : ""}</div>`}
      ${h.vmBilgisi === false || !srv ? "" : `<div class="tbl-wrap" style="max-height:52vh"><table>
        <thead><tr><th>VM</th><th>IP</th><th>Uygulamalar</th><th title="VM'deki uygulamaların en yüksek değerleri">Değer</th><th>OS</th></tr></thead>
        <tbody>${vmler.map(v => `<tr><td class="mono small" title="${esc(v.dns ?? "")}">${esc(v.vm ?? "")}</td><td>${fzIpLink(v.ip)}</td>
          <td class="small">${v.uygulamalar.length ? v.uygulamalar.map(fzAppLink).join(", ") : `<span class="muted">ERT'de yok</span>`}</td><td>${fzDeger(v.deger)}</td><td class="small muted">${esc(v.os ?? "")}</td></tr>`).join("")
          || `<tr><td colspan="5" class="unk">VM yok</td></tr>`}</tbody></table></div>`}
      ${h.assetId && k.kabloVar ? `<div id="fzBag" style="margin-top:10px"><span class="small muted"><span class="spinner"></span> bağlantılar…</span></div>`
        : `<div class="small" style="margin-top:10px;color:var(--warn)">🔌 Kablo bağlantıları gösterilemiyor: ${k.kabloVar === undefined ? "sunucudaki DeltaFlow sürümü eski (FizikselEnvanter.cs kopyalanıp derlenmeli)"
          : !h.assetId ? "bu cihazın nlyte AssetID'si yok" : esc(k.kabloDurum ?? "dbo.NlyteBaglanti okunamadı ya da boş (tablo adı Fiziksel:Tablolar:NlyteBaglanti)")}</div>`}`;
    if (h.assetId && k.kabloVar) fzBagYukle(h.assetId);
    $("#fzElleAc")?.addEventListener("click", () => fzElleForm(h));
    return;
  }
  const etki = fz.mod === "etki";
  const list = etki ? k.uygulamalar : [...k.uygulamalar].sort((a, b) => b.vm - a.vm || a.ad.localeCompare(b.ad, "tr"));
  const tamami = k.uygulamalar.filter(u => u.etki === "tamamı bu kabinde").length;
  const rozet = u => u.etki === "tamamı bu kabinde" ? `<span class="badge fz-kritik" title="Uygulamanın kabini bilinen tüm VM'leri bu kabinde">tamamı bu kabinde</span>`
    : u.etki.startsWith("başka kabin yok") ? `<span class="badge fz-uyari" title="Başka kabinde VM'i yok; kabini bilinmeyen ${u.kabinsizVm} VM'i var">bu kabin + kabini bilinmeyen</span>`
    : `<span class="badge strong" title="${esc(u.digerKabinler.join(", "))}${u.digerKabinSayisi > u.digerKabinler.length ? " …" : ""}">${fmt(u.digerKabinSayisi)} kabinde daha</span>`;
  el.innerHTML = `<h2 style="margin:0 0 6px">${etki ? "Kabin kapanırsa" : "Bu kabinin uygulamaları"} <span class="small muted">${esc(k.ad)}</span></h2>
    <div class="small muted" style="margin-bottom:6px">${fmt(k.uygulamalar.length)} uygulama${etki ? ` · <b style="color:var(--err)">${fmt(tamami)}</b> uygulamanın kabini bilinen tüm VM'leri bu kabinde` : ""}
      ${etki ? `<div>Sıra: yalnız bu kabinde çalışanlar önce, sonra VM sayısı. Uygulamanın başka kabinlerde de VM'i olması tek başına yedekli olduğu anlamına gelmez. AI etki analizi bağlantısı sonraki adım.</div>` : ""}</div>
    <div class="tbl-wrap" style="max-height:60vh"><table>
      <thead><tr><th>Uygulama</th><th>Değer</th><th class="num" title="Bu kabindeki VM / ana veri merkezindeki toplam VM">VM</th>${etki ? "<th>Etki</th>" : ""}<th>Host'lar</th><th>Muhafız</th></tr></thead>
      <tbody>${list.map(u => `<tr${etki && u.etki === "tamamı bu kabinde" ? ` class="tls-eski-satir"` : ""}><td>${fzAppLink(u.ad)}</td><td>${fzDeger(u.deger)}</td><td class="num">${fmt(u.vm)}<span class="muted"> / ${fmt(u.toplamVm)}</span></td>
        ${etki ? `<td>${rozet(u)}</td>` : ""}<td class="small">${u.hostlar.map(h => `<a href="#" data-fzh="${esc(h)}">${esc(h)}</a>`).join(", ")}</td><td class="small muted">${esc(u.muhafizlar.join(", "))}</td></tr>`).join("")
        || `<tr><td colspan="6" class="unk">Bu kabindeki VM'lerin ERT'de uygulaması yok.</td></tr>`}</tbody></table></div>`;
  el.querySelectorAll("[data-fzh]").forEach(a => a.addEventListener("click", e => { e.preventDefault(); fz.host = a.dataset.fzh; fz.mod = "host"; fzKabinCiz(); fzDetayCiz(); }));
}

// Cihazın nlyte kablo bağlantıları (patch panel'ler üzerinden uçtan uca) + bu cihaz kapanırsa etkilenen sunucu / VM / uygulama
fz.cihaz = {};
async function fzBagYukle(id) {
  let d = fz.cihaz[id];
  if (!d) {
    try { d = fz.cihaz[id] = await fzGet("api/fiziksel/cihaz?" + new URLSearchParams({ id })); }
    catch (e) { const el = $("#fzBag"); if (el) el.innerHTML = `<div class="small" style="color:var(--err)">${esc(e.message)}</div>`; return; }
  }
  const el = $("#fzBag");
  if (!el) return;
  const b = d.baglantilar, e = d.etki;
  const karsi = z => z.uzakKabin ? `<a href="#" data-fzc="${esc(z.uzakKabin)}" data-fzch="${esc(z.uzakAd)}">${esc(z.uzakAd)}</a>` : esc(z.uzakAd);
  el.innerHTML = `<h3 style="margin:0 0 4px;font-size:14px">🔌 Bağlantılar <span class="small muted">(${fmt(b.length)}${d.chassisten ? " · chassis'in kabloları" : ""})</span></h3>
    ${b.length ? `<div class="tbl-wrap" style="max-height:32vh"><table><thead><tr><th>Port</th><th>Tür</th><th>Karşı cihaz</th><th>Karşı port</th><th>Kabin</th><th title="Arada geçilen patch panel'ler (panel:port)">Üzerinden</th></tr></thead>
      <tbody>${b.map(z => `<tr${z.kesik ? ` title="Patch panel'de devamı bulunamadı (port bilgisi yok ya da karşı kablo girilmemiş)"` : ""}><td class="mono small">${esc(z.yerelPort ?? "—")}</td>
        <td class="small" title="${esc(z.kablo ?? "")}">${z.sinif === "güç" ? "⚡ güç" : z.sinif === "fiber" ? "🔆 fiber" : "veri"}</td>
        <td><span class="badge" style="background:${fzTurRenk(z.uzakTur)};color:#fff">${esc(fzTurAd(z.uzakTur))}</span> ${karsi(z)}${z.kesik ? ` <span class="small" style="color:var(--warn)">iz kesik</span>` : ""}</td>
        <td class="mono small">${esc(z.uzakPort ?? "")}</td><td class="small">${esc(z.uzakKabin ?? "")}</td><td class="small muted">${esc(z.paneller.join(" → "))}</td></tr>`).join("")}</tbody></table></div>`
      : `<div class="small muted">nlyte'ta bu cihaz için kablo kaydı yok.</div>`}
    ${e ? `<h3 style="margin:10px 0 4px;font-size:14px">${d.guc ? "Bu cihazın gücü kesilirse" : "Bu cihaz kapanırsa"}</h3>
      <div class="small" style="margin-bottom:4px">${fmt(e.sunucu)} sunucu · ${fmt(e.vm)} VM · ${fmt(e.uygulamalar.length)} uygulama ${e.deger ? "· en yüksek " + fzDeger(e.deger, true) : ""}</div>
      ${e.sunucu ? `<div class="small" style="margin-bottom:4px">${e.tekYol ? `<b style="color:#c0392b">${fmt(e.tekYol)} sunucu (${fmt(e.tekYolVm)} VM) yalnız bu cihaza bağlı</b> — kapanırsa gerçekten durur; diğerlerinin` : "Tüm sunucuların"} kablo kaydına göre başka bir ${d.guc ? "güç kaynağı" : "ağ / SAN cihazı"} üzerinden yedek yolu var.</div>` : ""}
      ${e.sunucular.length ? `<div class="tbl-wrap" style="max-height:28vh"><table><thead><tr><th>Sunucu</th><th>Kabin</th><th class="num">VM</th><th>Değer</th><th>Bağlantı</th><th title="Sunucunun bu cihaz dışında aynı türden başka bir cihaza da kablosu var mı (ör. iki PDU, iki SAN / ağ switch'i)">Yedek yol</th></tr></thead>
        <tbody>${e.sunucular.map(x => `<tr><td class="mono small">${x.kabin ? `<a href="#" data-fzc="${esc(x.kabin)}" data-fzch="${esc(x.ad)}">${esc(x.ad)}</a>` : esc(x.ad)}</td><td class="small">${esc(x.kabin ?? "")}</td>
          <td class="num">${fmt(x.vm)}</td><td>${fzDeger(x.deger)}</td><td class="small muted">${esc(x.yol)}</td>
          <td class="small">${x.yedek == null ? "" : x.yedek.length ? `<span class="muted" title="${esc(x.yedek.join(", "))}">✓ ${esc(x.yedek.slice(0, 2).join(", "))}${x.yedek.length > 2 ? " +" + (x.yedek.length - 2) : ""}</span>` : `<b style="color:#c0392b">tek yol</b>`}</td></tr>`).join("")}</tbody></table></div>`
        : `<div class="small muted">Kablolarla bu cihaza bağlı sunucu bulunamadı${d.tur === "Firewall" ? " (firewall'lar genelde switch'lere bağlıdır)" : ""}.</div>`}
      ${e.uygulamalar.length ? `<div class="tbl-wrap" style="max-height:28vh;margin-top:6px"><table><thead><tr><th>Uygulama</th><th class="num">VM</th><th>Değer</th></tr></thead>
        <tbody>${e.uygulamalar.map(u => `<tr><td>${fzAppLink(u.ad)}</td><td class="num">${fmt(u.vm)}</td><td>${fzDeger(u.deger)}</td></tr>`).join("")}</tbody></table></div>` : ""}` : ""}`;
  el.querySelectorAll("[data-fzc]").forEach(a => a.addEventListener("click", ev => { ev.preventDefault(); fzKabinAc(a.dataset.fzc, a.dataset.fzch); }));
}

async function fzAra(q) {
  q = q.trim();
  const el = $("#fzAra");
  if (q.length < 2) { fz.ara = null; fz.vurgu = new Set(); el.innerHTML = ""; fzPlanCiz(); return; }
  let b;
  try { b = await fzGet("api/fiziksel/ara?" + new URLSearchParams({ q })); }
  catch (e) { el.innerHTML = `<div class="unk" style="color:var(--err)">${esc(e.message)}</div>`; return; }
  if ($("#fzQ")?.value.trim() !== q) return;   // yazmaya devam edildi
  fz.ara = b; fz.vurgu = new Set(b.kabinler);
  fzPlanCiz();
  el.innerHTML = !b.sonuclar.length ? `<div class="small muted" style="margin:4px 0 8px">"${esc(q)}" için sonuç yok.</div>` : `<div class="fz-ara">
    ${b.sonuclar.map((r, i) => `<button type="button" class="fz-ara-s" data-i="${i}" ${r.kabin ? "" : "disabled"} title="${r.kabin ? "Kabini aç" : esc(r.kapsam ?? "") + " — kabini bilinmiyor"}">
      <span class="badge"${r.cihazTuru ? ` style="background:${fzTurRenk(r.cihazTuru)};color:#fff"` : ""}>${esc(r.cihazTuru ? fzTurAd(r.cihazTuru) : r.tur)}</span> <b>${esc(r.deger)}</b>${r.adet > 1 ? ` <span class="muted">(${fmt(r.adet)} VM)</span>` : ""}
      <span class="small muted">→ ${r.kabin ? esc(r.kabin) : esc(r.kapsam && r.kapsam !== fz.ozet?.anaKapsam ? r.kapsam : "kabin bilinmiyor")}${r.host ? " · " + esc(r.host) : ""}</span></button>`).join("")}
    ${b.kesik ? `<span class="small muted">ilk ${b.sonuclar.length} sonuç</span>` : ""}</div>`;
  el.querySelectorAll("[data-i]").forEach(btn => btn.addEventListener("click", async () => {
    const r = b.sonuclar[+btn.dataset.i];
    if (!r.kabin) return;
    if (r.oda && r.oda !== fz.oda) { $("#fzOda").value = r.oda; await fzOdaAc(r.oda); }
    fzKabinAc(r.kabin, r.tur === "Kabin" || r.tur === "Uygulama" || r.tur === "Cluster" ? null : r.host);
  }));
}

// Değer kuralları: tür / model / kabin adına göre değer, miras (PDU, panel, KVM, UPS, klima), değeri bulunamayan cihazlar
async function fzKuralYukle() {
  const el = $("#fzKuralIc");
  el.innerHTML = `<div class="small muted"><span class="spinner"></span> yükleniyor…</div>`;
  try { fz.kural = await fzGet("api/fiziksel/kurallar"); }
  catch (e) { el.innerHTML = `<div class="unk" style="color:var(--err)">${esc(e.message)}</div>`; return; }
  const k = fz.kural, kosul = r => [r.turler?.length ? "tür: " + r.turler.map(fzTurAd).join(", ") : "", r.model ? `model ~ /${r.model}/` : "", r.isim ? `ad ~ /${r.isim}/` : "", r.kabin ? `kabin adı ~ /${r.kabin}/` : ""].filter(Boolean).join(" · ");
  const deger = r => r.degerler ? fzDeger({ v: r.degerler[0], i: r.degerler[1], e: r.degerler[2], g: r.degerler[3], b: r.degerler[4] })
    : r.miras ? `<span class="small">${r.miras === "oda" ? "odanın" : "kabinin"} değeri: ${esc([...(r.boyutlar ?? "E")].map(h => ({ E: "erişilebilirlik", G: "gizlilik", B: "bütünlük" })[h] ?? h).join(", "))} + İş</span>` : "";
  el.innerHTML = `<div class="small" style="margin:6px 0">
      ${fmt(k.cihaz)} aktif kabin cihazından <b>${fmt(k.degerli)}</b>'inin değeri var: ${fmt(k.vmden)} VM'lerinin uygulamalarından, ${fmt(k.ertEslesen)} VM'siz fiziksel sunucu ERT'deki adı / IP'siyle, kalanı aşağıdaki kurallarla.
      ${fmt(k.kabinDegerli)} / ${fmt(k.kabin)} kabinin değeri var. İlk uyan kural uygulanır; hesaplanan değer ile kural değerinin boyut başına en yükseği alınır.
      <div class="muted" style="margin-top:4px">Türetme (ERT analizi): Varlık = en yüksek(Gizlilik, Bütünlük, Erişilebilirlik); İş 0 / boşsa Erişilebilirlik — ${fmt(k.turetilenIs)} / ${fmt(k.uygulama)} uygulamada tamamlandı.
      Kabin dışı ${fmt(k.kabinDisi)} cihaz (depo / stok) değerlendirme dışı.${k.ayarli ? " Kurallar appsettings'ten (Fiziksel:DegerKurallari)." : " Varsayılan kurallar (appsettings Fiziksel:DegerKurallari ile değiştirilebilir)."}</div></div>
    <div class="toolbar" style="margin:4px 0;gap:6px;justify-content:flex-start">
      <button type="button" class="mini" id="fzKaydet"${yetki?.fizikseldeger ? "" : ` disabled title="Kaydetme yetkiniz yok (DokuPanel: Yetki:FizikselDeger)"`}>💾 Değerleri kaydet (denetim kaydı)</button>
      <a class="mini" data-csv href="api/fiziksel/deger/excel" title="Güncel hesap: Cihazlar, Kabinler, Kurallar, Elle değerler, Yöntem sayfaları">📊 Excel (güncel)</a>
      <a class="mini" data-csv href="api/fiziksel/kurallar?csv=true" title="Tüm kabin cihazları: değer, yöntem, kaynak">CSV</a>
      <span class="small muted" id="fzKaydetDurum"></span></div>
    <div id="fzKayitlar" class="small muted">kayıtlar yükleniyor…</div>
    <div id="fzElleListe"></div>
    <div class="tbl-wrap" style="max-height:40vh"><table><thead><tr><th>Kural</th><th>Koşul</th><th>Değer</th><th class="num">Cihaz</th><th>Gerekçe</th></tr></thead>
      <tbody>${k.kurallar.map(r => `<tr><td><b>${esc(r.ad)}</b></td><td class="small mono">${esc(kosul(r))}</td><td>${deger(r)}</td><td class="num">${fmt(r.cihaz)}</td><td class="small muted">${esc(r.aciklama ?? "")}</td></tr>`).join("")}</tbody></table></div>
    ${k.degersiz.length ? `<div class="small" style="margin:10px 0 4px"><b>Değeri bulunamayan cihazlar</b> <span class="muted">— VM'i yok, ERT'de eşleşmedi, kuralı yok (sunucularda büyük ihtimalle ERT'de adı farklı)</span></div>
      <div class="tbl-wrap" style="max-height:30vh"><table><thead><tr><th>Tür</th><th class="num">Cihaz</th><th>Örnek</th></tr></thead>
      <tbody>${k.degersiz.map(x => `<tr><td>${esc(fzTurAd(x.tur))}</td><td class="num">${fmt(x.adet)}</td><td class="small mono">${esc(x.ornek.join(", "))}</td></tr>`).join("")}</tbody></table></div>` : ""}`;
  $("#fzKaydet").addEventListener("click", fzDegerKaydet);
  fzKayitlarYukle(); fzElleListeYukle();
}

// Denetim kaydı: o anki hesap DF_FIZIKSEL_DEGER_* tablolarına tarihli yazılır; her kaydın Excel'i
async function fzDegerKaydet() {
  const ac = prompt("Kayıt açıklaması (ör. 2026 Q4 denetim):", "");
  if (ac === null) return;
  const d = $("#fzKaydetDurum"); d.innerHTML = `<span class="spinner"></span> kaydediliyor…`;
  try {
    const r = await fetch("api/fiziksel/deger/kaydet", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ aciklama: ac }) });
    const b = await r.json().catch(() => ({}));
    if (!r.ok) throw new Error(b.error || `HTTP ${r.status}`);
    d.innerHTML = `<span style="color:var(--ok, #2e9d5b)">✓ Kayıt #${b.kayitId}: ${fmt(b.cihaz)} cihaz, ${fmt(b.kabin)} kabin</span>`;
    fzKayitlarYukle();
  } catch (e) { d.innerHTML = `<span style="color:var(--err)">${esc(e.message)}</span>`; }
}
async function fzKayitlarYukle() {
  const el = $("#fzKayitlar"); if (!el) return;
  let b; try { b = await fzGet("api/fiziksel/deger/kayitlar"); } catch (e) { el.innerHTML = `<span style="color:var(--err)">${esc(e.message)}</span>`; return; }
  el.innerHTML = b.durum ? `<div style="color:var(--warn);margin:6px 0">⚠ ${esc(b.durum)}</div>`
    : !b.kayitlar.length ? `<div class="muted" style="margin:6px 0">Henüz denetim kaydı yok.</div>`
    : `<div class="small" style="margin:10px 0 4px"><b>Denetim kayıtları</b> <span class="muted">— veritabanında (DF_FIZIKSEL_DEGER_KAYIT); Excel kayıttaki değerlerle birebir</span></div>
      <div class="tbl-wrap" style="max-height:24vh"><table><thead><tr><th>#</th><th>Tarih</th><th>Kaydeden</th><th>Açıklama</th><th class="num">Cihaz</th><th class="num">Kabin</th><th>nlyte verisi</th><th></th></tr></thead>
      <tbody>${b.kayitlar.map(x => `<tr><td class="num">${x.kayitId}</td><td class="small">${new Date(x.tarih).toLocaleString("tr-TR")}</td><td class="small">${esc(x.kaydeden ?? "")}</td>
        <td class="small">${esc(x.aciklama ?? "")}</td><td class="num">${fmt(x.cihaz)}</td><td class="num">${fmt(x.kabin)}</td><td class="small muted">${x.veriTarihi ? new Date(x.veriTarihi).toLocaleString("tr-TR") : ""}</td>
        <td><a class="mini" data-csv href="api/fiziksel/deger/excel?kayit=${x.kayitId}">📊 Excel</a></td></tr>`).join("")}</tbody></table></div>`;
}
async function fzElleListeYukle() {
  const el = $("#fzElleListe"); if (!el) return;
  let b; try { b = await fzGet("api/fiziksel/deger/elle"); } catch (e) { el.innerHTML = `<span class="small" style="color:var(--err)">${esc(e.message)}</span>`; return; }
  const dv = x => fzDeger({ v: x.v, i: x.i, e: x.e, g: x.g, b: x.b });
  el.innerHTML = b.durum ? `<div class="small" style="color:var(--warn);margin:6px 0">⚠ Elle değerler: ${esc(b.durum)}</div>`
    : !b.elle.length ? "" : `<div class="small" style="margin:10px 0 4px"><b>Elle değerler (${fmt(b.elle.length)})</b> <span class="muted">— hesaplanan değerin yerine geçer; cihazın detayından değiştirilir</span></div>
      <div class="tbl-wrap" style="max-height:24vh"><table><thead><tr><th>Cihaz</th><th>Kabin</th><th>Değer</th><th>Gerekçe</th><th>Kaydeden</th><th>Tarih</th></tr></thead>
      <tbody>${b.elle.map(x => `<tr><td class="mono small">${x.var && x.kabin ? `<a href="#" data-fzc="${esc(x.kabin)}" data-fzch="${esc(x.cihaz ?? "")}">${esc(x.cihaz ?? x.assetId)}</a>` : esc(x.cihaz ?? x.assetId) + (x.var ? "" : ` <span class="muted">(nlyte'ta yok)</span>`)}</td>
        <td class="small">${esc(x.kabin ?? "")}</td><td>${dv(x)}</td><td class="small">${esc(x.gerekce)}</td><td class="small">${esc(x.kaydeden ?? "")}</td><td class="small muted">${new Date(x.tarih).toLocaleString("tr-TR")}</td></tr>`).join("")}</tbody></table></div>`;
  el.querySelectorAll("[data-fzc]").forEach(a => a.addEventListener("click", ev => { ev.preventDefault(); fzKabinAc(a.dataset.fzc, a.dataset.fzch); }));
}
// Elle değer formu (cihaz detayı): boş bırakılan boyut hesaplanandan gelir; Varlık / İş boşsa elle değerlerden türetilir
function fzElleForm(h) {
  const el = $("#fzElle"), d = h.deger ?? {};
  const sec = (k, ad) => `<label style="flex-direction:column;align-items:flex-start;gap:2px" class="small">${ad}<select data-ek="${k}"><option value="">—</option>${[5, 4, 3, 2, 1, 0].map(n => `<option${d.elle && d[k] === n && String(d.kaynak?.[k] ?? "").startsWith("elle") ? " selected" : ""}>${n}</option>`).join("")}</select></label>`;
  el.innerHTML = `<div class="panel" style="margin-top:6px;padding:8px">
    <div class="small muted" style="margin-bottom:6px">Boş (—) bırakılan boyut hesaplanan değerden gelir; Varlık boşsa en yüksek(G, B, E), İş boşsa Erişilebilirlik olur. Gerekçe denetim kaydına girer.</div>
    <div style="display:flex;gap:8px;flex-wrap:wrap">${FZ_DEGER.map(([k, , ad]) => sec(k, ad)).join("")}</div>
    <textarea id="fzElleGer" rows="2" style="width:100%;margin-top:6px" placeholder="Gerekçe (zorunlu): ör. test ortamı cihazı, üretim trafiği yok"></textarea>
    <div style="display:flex;gap:6px;margin-top:6px"><button type="button" class="mini" id="fzElleKaydet">Kaydet</button>
      ${d.elle ? `<button type="button" class="mini" id="fzElleSil">Elle değeri kaldır</button>` : ""}<button type="button" class="mini" id="fzElleKapat">Vazgeç</button>
      <span class="small" id="fzElleDurum"></span></div></div>`;
  const gonder = async sil => {
    const q = { assetId: h.assetId, gerekce: $("#fzElleGer").value, sil };
    el.querySelectorAll("[data-ek]").forEach(s => q[s.dataset.ek] = s.value === "" ? null : Number(s.value));
    $("#fzElleDurum").innerHTML = `<span class="spinner"></span>`;
    try {
      const r = await fetch("api/fiziksel/deger/elle", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(q) });
      const b = await r.json().catch(() => ({}));
      if (!r.ok) throw new Error(b.error || `HTTP ${r.status}`);
      fz.kural = null; fz.odaVeri = null;
      if (fz.oda) await fzOdaAc(fz.oda);
      await fzKabinAc(fz.kabin, h.ad);
    } catch (e) { $("#fzElleDurum").innerHTML = `<span style="color:var(--err)">${esc(e.message)}</span>`; }
  };
  $("#fzElleKaydet").addEventListener("click", () => gonder(false));
  $("#fzElleSil")?.addEventListener("click", () => { if (confirm("Elle değer kaldırılsın mı? Hesaplanan değere dönülür.")) gonder(true); });
  $("#fzElleKapat").addEventListener("click", () => el.innerHTML = "");
}

async function fzEksikYukle() {
  const el = $("#fzEksikIc");
  el.innerHTML = `<div class="small muted"><span class="spinner"></span> yükleniyor…</div>`;
  try { fz.eksik = await fzGet("api/fiziksel/eksik"); }
  catch (e) { el.innerHTML = `<div class="unk" style="color:var(--err)">${esc(e.message)}</div>`; return; }
  const l = fz.eksik.hostlar;
  el.innerHTML = `<div class="toolbar" style="margin:6px 0">
      <span class="small muted">${esc(fz.ozet?.anaKapsam ?? "Ana veri merkezi")}: nlyte'ta eşleşmesi olmayan ${fmt(l.length)} ESX host · ${fmt(fz.eksik.vm)} VM — büyük ihtimalle nlyte'a girilmemiş${fz.eksik.tablodan ? "" : " (NlyteHostEslesme tablosu yok, KabinHostVm'den türetildi)"}</span>
      <a class="mini" data-csv href="api/fiziksel/eksik?csv=true" style="margin-left:auto" title="nlyte ekibine gönderilecek liste">Excel (CSV)</a></div>
    ${fz.eksik.turFarkli?.length ? `<div class="small" style="margin:8px 0 4px;color:var(--warn)"><b>Ek eşleştirmeyle kabine bağlanan ESX host'ları (${fmt(fz.eksik.turFarkli.length)})</b>
      <span class="muted">— sp_KabinZincirYenile bulamadı; DeltaFlow adla (nlyte'ta başka türde kayıtlı) ya da seri numarasıyla (MSY fiziksel envanter → nlyte SerialNumber; nlyte'ta farklı adla kayıtlı) eşleyip VM'lerini kabine bağladı (bellekte). nlyte kaydı düzeltilmeli.</span></div>
      <div class="tbl-wrap" style="max-height:30vh"><table><thead><tr><th>Host</th><th class="num">VM</th><th>nlyte kaydı</th><th>Tür</th><th>Kabin</th><th>Nasıl</th><th>Cluster</th></tr></thead>
      <tbody>${fz.eksik.turFarkli.map(t => `<tr><td class="mono">${esc(t.host)}</td><td class="num">${fmt(t.vmSayisi)}</td><td class="mono small">${esc(t.cihazAd)}</td>
        <td><span class="badge" style="background:${fzTurRenk(t.tur)};color:#fff">${esc(fzTurAd(t.tur))}</span></td>
        <td><a href="#" data-fzk="${esc(t.kabin)}">${esc(t.kabin)}</a>${t.uAlt != null ? ` <span class="muted">U${t.uAlt}</span>` : ""}</td>
        <td class="small">${t.yontem === "ad" ? "ad · tür yanlış" : esc(t.yontem ?? "") + " · ad farklı"}${t.eskiKapsam && t.eskiKapsam !== fz.ozet?.anaKapsam ? ` <span class="muted">(${esc(t.eskiKapsam)})</span>` : ""}</td><td class="small">${esc(t.cluster ?? "")}</td></tr>`).join("")}</tbody></table></div>
      <div class="small" style="margin:8px 0 4px"><b>nlyte'ta hiç bulunamayanlar</b></div>` : ""}
    <div class="tbl-wrap" style="max-height:40vh"><table>
      <thead><tr><th>Host</th><th>FQDN</th><th class="num">VM</th><th>Cluster</th><th class="num" title="nlyte'ta benzer adlı aday kayıt sayısı">Aday</th></tr></thead>
      <tbody>${l.map(e => `<tr><td class="mono">${esc(e.host)}</td><td class="mono small">${esc(e.fqdn ?? "")}</td><td class="num">${fmt(e.vmSayisi)}</td><td class="small">${esc(e.cluster ?? "")}</td><td class="num">${e.adaySayisi ?? ""}</td></tr>`).join("")
        || `<tr><td colspan="5" class="unk">Eksik host yok.</td></tr>`}</tbody></table></div>`;
  el.querySelectorAll("[data-fzk]").forEach(a => a.addEventListener("click", async e => {
    e.preventDefault();
    fzKabinAc(a.dataset.fzk); $("#fzShell").scrollIntoView({ behavior: "smooth" });
  }));
}

// Kabine yerleşmeyen MSY / ÇSY VM'leri (VM başına satır + host bilgisi): kaynak / kapsam / neden süzgeci, arama, CSV
const fzEslF = { kaynak: "", kapsam: "", neden: "", q: "" };
async function fzEslYukle() {
  const el = $("#fzEslIc");
  el.innerHTML = `<div class="small muted"><span class="spinner"></span> yükleniyor…</div>`;
  try { fz.esl = await fzGet("api/fiziksel/eslesmeyen"); }
  catch (e) { el.innerHTML = `<div class="unk" style="color:var(--err)">${esc(e.message)}</div>`; return; }
  fzEslCiz();
}
function fzEslCiz() {
  const el = $("#fzEslIc"), d = fz.esl;
  if (!d) return;
  const say = f => { const m = new Map(); for (const v of d.vmler) { const k = f(v); m.set(k, (m.get(k) ?? 0) + 1); } return [...m].sort((a, b) => b[1] - a[1]); };
  const kaynaklar = say(v => v.kaynak ?? "(boş)"), kapsamlar = say(v => v.kapsam ?? "(boş)"), nedenler = say(v => v.neden);
  const l = d.vmler.filter(v => (!fzEslF.kaynak || (v.kaynak ?? "(boş)") === fzEslF.kaynak) && (!fzEslF.kapsam || (v.kapsam ?? "(boş)") === fzEslF.kapsam)
    && (!fzEslF.neden || v.neden === fzEslF.neden)
    && (!fzEslF.q || [v.vm, v.ip, v.dns, v.host, v.fqdn, v.cluster, v.seri, ...v.uygulamalar, ...v.adaylar].some(x => (x ?? "").toLowerCase().includes(fzEslF.q))));
  const sec = (id, ad, opts, deger) => `<label style="flex-direction:row;align-items:center;gap:4px">${ad} <select id="${id}"><option value="">Tümü</option>${opts.map(([k, n]) => `<option value="${esc(k)}"${k === deger ? " selected" : ""}>${esc(k)} (${fmt(n)})</option>`).join("")}</select></label>`;
  const hostSay = new Set(l.map(v => v.fqdn ?? v.host)).size;
  el.innerHTML = `<div class="toolbar" style="margin:6px 0;flex-wrap:wrap">
      ${sec("fzEslKaynak", "Kaynak", kaynaklar, fzEslF.kaynak)} ${sec("fzEslKapsam", "Kapsam", kapsamlar, fzEslF.kapsam)} ${sec("fzEslNeden", "Neden", nedenler, fzEslF.neden)}
      <input type="search" id="fzEslQ" placeholder="ara: VM, IP, host, cluster, uygulama" value="${esc(fzEslF.q)}" style="min-width:220px">
      <span class="small muted">${fmt(l.length)} VM · ${fmt(hostSay)} host</span>
      <a class="mini" data-csv href="api/fiziksel/eslesmeyen?csv=true" style="margin-left:auto">Excel (CSV)</a></div>
    <div class="tbl-wrap" style="max-height:55vh"><table>
      <thead><tr><th>VM</th><th>IP</th><th>Uygulamalar</th><th>Kaynak</th><th>Kapsam</th><th>Host</th><th>Cluster</th><th>Host seri / iLO</th><th>Neden</th><th>nlyte adayları</th></tr></thead>
      <tbody>${l.slice(0, 2000).map(v => `<tr><td class="mono small" title="${esc([v.dns, v.os].filter(Boolean).join(" · "))}">${esc(v.vm ?? "")}</td><td>${fzIpLink(v.ip)}</td>
        <td class="small">${v.uygulamalar.length ? v.uygulamalar.map(fzAppLink).join(", ") : `<span class="muted">ERT'de yok</span>`}</td>
        <td class="small">${esc(v.kaynak ?? "")}</td><td class="small">${esc(v.kapsam ?? "")}</td>
        <td class="mono small" title="${esc([v.fqdn, v.hostModel].filter(Boolean).join(" · "))}">${esc(v.host || "(boş)")} <span class="muted">(${fmt(v.hostVmSayisi)} VM)</span></td>
        <td class="small">${esc(v.cluster ?? "")}</td><td class="mono small">${esc(v.seri ?? "")}${v.ilo ? `<div class="muted">iLO ${esc(v.ilo)}</div>` : ""}</td>
        <td class="small">${esc(v.neden)}</td><td class="small">${v.adaylar.map(esc).join("<br>")}</td></tr>`).join("") || `<tr><td colspan="10" class="unk">Kabine yerleşmeyen VM yok.</td></tr>`}</tbody></table></div>
    ${l.length > 2000 ? `<div class="small muted">ilk 2000 VM gösteriliyor; tamamı CSV'de</div>` : ""}`;
  const yen = () => fzEslCiz();
  $("#fzEslKaynak").addEventListener("change", e => { fzEslF.kaynak = e.target.value; yen(); });
  $("#fzEslKapsam").addEventListener("change", e => { fzEslF.kapsam = e.target.value; yen(); });
  $("#fzEslNeden").addEventListener("change", e => { fzEslF.neden = e.target.value; yen(); });
  let t; $("#fzEslQ").addEventListener("input", e => { clearTimeout(t); t = setTimeout(() => { fzEslF.q = e.target.value.trim().toLowerCase(); yen(); const q = $("#fzEslQ"); q.focus(); q.setSelectionRange(q.value.length, q.value.length); }, 300); });
}

async function fzDisiYukle() {
  const el = $("#fzDisiIc");
  el.innerHTML = `<div class="small muted"><span class="spinner"></span> yükleniyor…</div>`;
  try { fz.disi = await fzGet("api/fiziksel/kabindisi"); }
  catch (e) { el.innerHTML = `<div class="unk" style="color:var(--err)">${esc(e.message)}</div>`; return; }
  fzDisiCiz();
}
function fzDisiCiz() {
  const el = $("#fzDisiIc"), d = fz.disi;
  const kg = d.kabinGruplari ?? [];
  el.innerHTML = `<div class="small muted" style="margin:6px 0">Kabine bağlı olmayan ya da kabin kaydı bulunamayan aktif nlyte cihazları (klimalar odada durur): ${esc(fzTurOzet(d.turlar, d.toplam))}${d.kesik ? ` · ilk ${fmt(d.cihazlar.length)}` : ""}
      ${d.kabinsiz != null ? `<div>CabinetAssetID boş: <b>${fmt(d.kabinsiz)}</b> · CabinetAssetID dolu ama NlyteKabin'de yok: <b style="color:${kg.length ? "var(--err)" : "inherit"}">${fmt(d.bilinmeyenKabinCihaz)}</b> cihaz, ${fmt(kg.length)} farklı kabin kimliği</div>` : ""}</div>
    ${kg.length ? `<details class="small" open style="margin:4px 0 8px"><summary><b>NlyteKabin'de olmayan kabin kimlikleri</b> — cihazlar bu kabinlere bağlı ama kabin kaydı okunamadı (Cabinets çekimi eksik olabilir)</summary>
      <div class="tbl-wrap" style="max-height:30vh"><table><thead><tr><th>CabinetAssetID</th><th class="num">Cihaz</th><th>Türler</th><th>Konum</th><th>Örnek cihazlar</th></tr></thead>
      <tbody>${kg.map(g => `<tr><td class="mono">${esc(g.cabinetAssetId)}</td><td class="num">${fmt(g.adet)}</td><td class="small">${esc(fzTurOzet(g.turlar, g.adet).replace(/^[^:]*: /, ""))}</td>
        <td class="small">${esc(g.konum ?? "")}</td><td class="small mono">${esc(g.ornek.join(", "))}</td></tr>`).join("")}</tbody></table></div></details>` : ""}
    <div class="small" style="margin:4px 0"><b>Konuma göre</b> <span class="muted">(nlyte Location — depo / stok alanındaki cihazlar kabine bağlı olmaz)</span></div>
    <div class="fz-diger" id="fzDisiKonum">${fzDisiKonumlar(d).map(([k, n]) => `<button type="button" class="mini${fz.disiKonum === k ? " on" : ""}" data-dk="${esc(k)}"${/depo|stok|storage|warehouse/i.test(k) ? ` style="border-color:var(--warn)"` : ""}>${esc(k)} <span class="muted">${fmt(n)}</span></button>`).join("")}
      ${fz.disiKonum != null ? `<button type="button" class="mini" data-dk="">× tümü</button>` : ""}</div>
    <div class="tbl-wrap" style="max-height:40vh"><table>
      <thead><tr><th>Cihaz</th><th>Tür</th><th>Model</th><th>Konum</th><th>Seri</th><th>Durum</th></tr></thead>
      <tbody>${d.cihazlar.filter(c => fz.disiKonum == null || fzDisiOda(c) === fz.disiKonum).map(c => `<tr><td class="mono small">${esc(c.ad)}</td><td><span class="badge" style="background:${fzTurRenk(c.tur)};color:#fff">${esc(fzTurAd(c.tur))}</span></td>
        <td class="small">${esc(c.model ?? "")}</td><td class="small">${esc(c.konum ?? "")}${c.cabinetAssetId ? ` <span class="muted" title="CabinetAssetID NlyteKabin'de yok">kabin ${esc(c.cabinetAssetId)}?</span>` : ""}</td>
        <td class="mono small">${esc(c.seri ?? "")}</td><td class="small muted">${esc(c.durum ?? "")}</td></tr>`).join("")
        || `<tr><td colspan="6" class="unk">Kabin dışı cihaz yok.</td></tr>`}</tbody></table></div>`;
  el.querySelectorAll("[data-dk]").forEach(b => b.addEventListener("click", () => { fz.disiKonum = b.dataset.dk || null; fzDisiCiz(); }));
}
// Konum "SARIYER DEPO (ESENYURT,ZDC,1)" / "SISTEM ODASI-2 - 2 - Area (…)" → oda / depo adı
const fzDisiOda = c => c.konum ? c.konum.split(" - ")[0].replace(/\s*\(.*$/, "").trim() || "(konum yok)" : "(konum yok)";
function fzDisiKonumlar(d) {
  const m = new Map();
  for (const c of d.cihazlar) { const k = fzDisiOda(c); m.set(k, (m.get(k) ?? 0) + 1); }
  return [...m].sort((a, b) => b[1] - a[1]);
}

// ---------- IP kartı: "Fiziksel konum" satırı (topoloji.html renderTarget → #fizKonum) ----------
async function loadFizIp(ip) {
  state.fizIp = { ip, status: "loading" };
  try {
    const r = await fetch("api/fiziksel/ip?" + new URLSearchParams({ ip }));
    const b = r.ok ? await r.json() : null;
    if (state.fizIp?.ip !== ip) return;
    state.fizIp = { ip, status: "ok", konum: b?.bulundu ? b.konum : null };
  } catch { if (state.fizIp?.ip === ip) state.fizIp = { ip, status: "ok", konum: null }; }
  const el = $("#fizKonum"); if (el) el.innerHTML = fizKonumHtml(ip);
}
function fizKonumHtml(ip) {
  const f = state.fizIp;
  if (!f || f.ip !== ip || !f.konum) return "";
  const k = f.konum;
  if (!k.anaDc) return `<div class="fz-konum small"><b>Fiziksel konum:</b> ${esc(k.kapsam ?? "?")} — nlyte kapsamı dışı${k.host ? ` · host ${esc(k.host)}` : ""}${k.cluster ? ` · cluster ${esc(k.cluster)}` : ""}</div>`;
  if (!k.kabin) return `<div class="fz-konum small"><b>Fiziksel konum:</b> kabin bilinmiyor (host nlyte'ta eşleşmedi)${k.host ? ` · host ${esc(k.host)}` : ""}${k.cluster ? ` · cluster ${esc(k.cluster)}` : ""}</div>`;
  const link = `?${new URLSearchParams({ fiziksel: "1", kabin: k.kabin, host: k.host ?? "" })}`;
  const kabinAd = yetki?.fiziksel === false ? `<b>${esc(k.kabin)}</b>` : `<a href="${esc(link)}" target="_blank" rel="noopener" title="Fiziksel envanterde aç"><b>${esc(k.kabin)}</b></a>`;
  return `<div class="fz-konum small"><b>Fiziksel konum:</b> Kabin ${kabinAd}${k.odaAdi ? " · " + esc(k.odaAdi) : ""}${k.host ? ` · host ${esc(k.host)}` : ""}${k.cluster ? ` · cluster ${esc(k.cluster)}` : ""}${k.uAlt != null ? ` · U${k.uAlt}` : ""}${k.kabinDeger ? ` · kabin değeri ${fzDeger(k.kabinDeger)}` : ""}</div>`;
}

// ?fiziksel=1&kabin=X&host=Y: doğrudan kabin (IP kartındaki bağlantı)
async function fzDerinBaglanti() {
  const p = new URLSearchParams(location.search), kabin = p.get("kabin");
  if (!kabin) return;
  for (let i = 0; i < 50 && !fz.ozet; i++) await new Promise(r => setTimeout(r, 100));
  fzKabinAc(kabin, p.get("host") || null);
}
