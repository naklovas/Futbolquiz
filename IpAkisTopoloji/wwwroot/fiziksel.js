// ============================================================================
// DeltaFlow · Fiziksel envanter (oda planı → kabin → host → VM → IP → uygulama)
// "Bu kabin kapanırsa hangi uygulamalar etkilenir?" — veri doku DB'deki nlyte tablolarından (FizikselEnvanter.cs), yalnız okunur.
// topoloji.html'deki ortak yardımcıları ($, esc, fmt, ipTabUrl, yetki, yetkiYok, setUiMode) kullanır.
// ============================================================================

const fz = { ozet: null, oda: null, odaVeri: null, kabin: null, kabinVeri: null, yuz: "Front", host: null, mod: "host", ara: null, vurgu: new Set(), eksik: null };
const FZ_UH = 13;   // 1U yüksekliği (px)

async function fzGet(url) {
  const r = await fetch(url);
  const b = await r.json().catch(() => ({ error: `HTTP ${r.status}` }));
  if (r.status === 404 && !b.error) throw new Error("Sunucudaki DeltaFlow sürümünde fiziksel envanter ucu yok (DLL güncel değil).");
  if (!r.ok) throw new Error(b.error || `HTTP ${r.status}`);
  return b;
}

function runFiziksel() {
  setUiMode("fiziksel");
  if (!$("#fzShell")) { fzShell(); fzDerinBaglanti(); }
}

function fzShell() {
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
    <details id="fzEksik" class="fz-eksik"><summary><b>nlyte'ta eksik host'lar</b> <span class="small muted" id="fzEksikSay"></span></summary><div id="fzEksikIc"></div></details>
  </section>`;
  let t = null;
  $("#fzQ").addEventListener("input", e => { clearTimeout(t); t = setTimeout(() => fzAra(e.target.value), 300); });
  $("#fzOda").addEventListener("change", e => fzOdaAc(e.target.value));
  $("#fzYenile").addEventListener("click", async () => {
    $("#fzBilgi").innerHTML = `<span class="spinner"></span> yeniden okunuyor…`;
    try { await fetch("api/fiziksel/yenile", { method: "POST" }); } catch { }
    fz.ozet = null; fz.eksik = null; fzOzetYukle(true);
  });
  $("#fzEksik").addEventListener("toggle", e => { if (e.target.open && !fz.eksik) fzEksikYukle(); });
  fzOzetYukle();
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
  $("#fzBilgi").innerHTML = (o.toplamKabin ? `${fmt(o.toplamKabin)} kabin (VM'li host olan: ${fmt(o.vmliKabin)})${o.cihaz ? ` · ${fmt(o.cihaz)} nlyte cihazı` : ""} · ` : "")
    + (!o.nlyte ? `<span style="color:var(--warn)" title="NlyteKabin / NlyteSunucu okunamadı">yalnız VM'li kabinler</span> · ` : "")
    + (ana ? `<b>${esc(ana.kapsam)}</b>: ${fmt(ana.vm)} VM, kabini bilinen %${ana.vm ? Math.round(ana.kabinli * 100 / ana.vm) : 0}` : "")
    + (diger.length ? ` · <span title="nlyte yalnız ana veri merkezini kapsar">${diger.map(k => `${esc(k.kapsam)} ${fmt(k.vm)}`).join(" · ")}</span>` : "")
    + (o.yenileme ? ` · son yenileme ${new Date(o.yenileme).toLocaleString("tr-TR", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" })}` : "");
  $("#fzEksikSay").textContent = o.eksikSayisi ? `(${fmt(o.eksikSayisi)} host)` : "";
  $("#fzOda").innerHTML = o.odalar.map(x => `<option value="${esc(x.ad)}">${esc(x.ad)}${x.konum && x.ad !== "Diğer" ? " · " + esc(x.konum) : ""} (${fmt(x.kabin)} kabin)</option>`).join("");
  const ilk = (!yeniden && fz.oda && o.odalar.some(x => x.ad === fz.oda)) ? fz.oda : o.odalar[0]?.ad;
  if (ilk) { $("#fzOda").value = ilk; fzOdaAc(ilk); } else $("#fzPlan").innerHTML = `<div class="unk">Kabin bilgisi olan kayıt yok.</div>`;
  if (yeniden && fz.kabin) fzKabinAc(fz.kabin);
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
  const izgara = v.kabinler.filter(k => k.sira && k.no), digerler = v.kabinler.filter(k => !(k.sira && k.no));
  const maxVm = Math.max(1, ...v.kabinler.map(k => k.vm));
  // renk VM sayısına göre; VM'i olmayan ama cihazı olan kabin gri, hiç cihazı olmayan kesikli çerçeve
  const tile = k => {
    const p = 12 + Math.round(78 * k.vm / maxVm), koyu = p > 55;
    const tur = k.vm > 0 ? "" : (k.cihaz ?? 0) > 0 ? " vmsiz" : " bosk";
    const stil = k.vm > 0 ? ` style="background:color-mix(in srgb, var(--in) ${p}%, var(--panel));color:${koyu ? "#fff" : "var(--text)"}"` : "";
    return `<button type="button" class="fz-tile${tur}${fz.kabin === k.ad ? " on" : ""}${fz.vurgu.has(k.ad) ? " vurgu" : ""}" data-kabin="${esc(k.ad)}"${stil}
      title="${esc(k.ad)} · ${fmt(k.cihaz ?? k.host)} nlyte cihazı · ${fmt(k.vmHost ?? k.host)} VM'li host · ${fmt(k.vm)} VM · ${fmt(k.uygulama)} uygulama${k.bosU != null ? ` · ${k.bosU} boş U` : ""}${k.vm ? "" : (k.cihaz ? " — VM bilgisi olan host yok" : " — nlyte'ta cihaz yok")}">${esc(k.sira ? k.sira + k.no : k.ad)}</button>`;
  };
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
      <div class="fz-lejant"><span><i style="background:color-mix(in srgb, var(--in) 55%, var(--panel))"></i>VM'li (${vmli})</span><span><i class="vmsiz"></i>cihaz var, VM yok (${cihazli})</span><span><i class="bosk"></i>cihaz yok (${bos})</span></div></div>
    <div class="fz-plan-wrap">${plan}</div>
    ${digerler.length ? `<div class="small" style="margin-top:8px"><b>${izgara.length ? "Diğer kabinler" : "Kabinler"}</b> <span class="muted">(DR&lt;oda&gt;-&lt;sıra&gt;&lt;no&gt; kalıbına uymayan)</span></div>
      <div class="fz-diger">${digerler.map(k => `<button type="button" class="mini${fz.kabin === k.ad ? " on" : ""}${fz.vurgu.has(k.ad) ? " vurgu" : ""}" data-kabin="${esc(k.ad)}" title="${fmt(k.host)} host · ${fmt(k.vm)} VM">${esc(k.ad)} <span class="muted">${fmt(k.vm)}</span></button>`).join("")}</div>` : ""}`;
  el.querySelectorAll("[data-kabin]").forEach(b => b.addEventListener("click", () => fzKabinAc(b.dataset.kabin)));
}

async function fzKabinAc(ad, host) {
  if (fz.kabin !== ad) fz.yuz = "Front";   // yeni kabin ön yüzle açılır
  fz.kabin = ad; fz.host = host ?? null; fz.mod = host ? "host" : "uyg";
  fzPlanCiz();
  $("#fzKabin").innerHTML = `<div class="small muted"><span class="spinner"></span> ${esc(ad)}…</div>`;
  try { fz.kabinVeri = await fzGet("api/fiziksel/kabin?" + new URLSearchParams({ ad })); }
  catch (e) { $("#fzKabin").innerHTML = `<div class="unk" style="color:var(--err)">${esc(e.message)}</div>`; return; }
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
  for (const h of buYuz) {
    const yuk = Math.max(1, h.uYukseklik ?? 1);
    if (h.chassis) {
      const key = h.chassis + "|" + h.uAlt;
      if (!ch.has(key)) { const c = { chassis: h.chassis, uAlt: h.uAlt, yuk, bladeler: [] }; ch.set(key, c); kutular.push(c); }
      const c = ch.get(key); c.yuk = Math.max(c.yuk, yuk); c.bladeler.push(h);
    } else kutular.push({ host: h, uAlt: h.uAlt, yuk });
  }
  const hostBtn = (h, cls, stil) => `<button type="button" class="${cls}${h.vmBilgisi === false ? " vmsiz" : ""}${fz.host === h.ad ? " on" : ""}" data-host="${esc(h.ad)}" style="${stil}"
      title="${esc(h.ad)}${h.tip ? " · " + esc(h.tip) : ""}${h.model ? " · " + esc(h.model) : ""} · ${h.vmBilgisi === false ? "VM bilgisi yok" : fmt(h.vm) + " VM"} · U${h.uAlt ?? "?"}${h.uYukseklik ? `–${h.uAlt + h.uYukseklik - 1}` : ""}${h.cluster ? " · " + esc(h.cluster) : ""}${h.slot ? " · yuva " + esc(h.slot) : ""}">`;
  const kutuHtml = c => {
    const t = top(c.uAlt, c.yuk), y = c.yuk * FZ_UH - 2;
    if (c.host) {
      const h = c.host, kisa = y < 24;
      const vmYazi = h.vmBilgisi === false ? "VM yok" : `${fmt(h.vm)} VM`;
      return hostBtn(h, "fz-host", `top:${t + 1}px;height:${y}px`) + `<b>${esc(h.ad)}</b>${kisa ? ` <span class="muted">${vmYazi}</span>` : `<span class="small muted">${esc(h.model ?? "")}</span><span class="small">${vmYazi}</span>`}</button>`;
    }
    const bl = [...c.bladeler].sort((a, b) => (+a.slot || 0) - (+b.slot || 0) || String(a.slot).localeCompare(String(b.slot)));
    return `<div class="fz-chassis" style="top:${t + 1}px;height:${y}px" title="${esc(c.chassis)} · ${bl.length} blade">
      <span class="fz-chassis-ad">${esc(c.chassis)}</span>
      <div class="fz-bladeler">${bl.map(h => hostBtn(h, "fz-blade", "") + `<span class="fz-slot">${esc(h.slot ?? "?")}</span><span class="fz-bad">${esc(h.ad)}</span><span class="small">${fmt(h.vm)}</span></button>`).join("")}</div></div>`;
  };
  const etiketler = Array.from({ length: U }, (_, i) => { const n = i + 1; return n === 1 || n % 5 === 0 || n === U ? `<span style="top:${top(n, 1)}px">${n}</span>` : ""; }).join("");
  const nOn = uLu.filter(h => yuzu(h) === "Front").length, nArka = uLu.length - nOn;
  const vmSay = k.vmler.length, hostSay = k.hostlar.length;
  const ozet = k.cihazSayisi != null ? `${fmt(k.cihazSayisi)} cihaz · ${fmt(k.vmHostSayisi)} VM'li host · ${fmt(vmSay)} VM` : `${fmt(hostSay)} host · ${fmt(vmSay)} VM`;
  el.innerHTML = `<div class="fz-kabin-bas"><b>${esc(k.ad)}</b> · ${ozet}${k.konum ? ` <div class="small muted">${esc(k.konum)}</div>` : ""}
      <div class="small muted">${U}U${k.bosU != null ? ` · ${k.bosU} boş U` : ""} · U1 ${k.u1Ustte ? "üstte" : "altta"}</div></div>
    <div class="toolbar" style="margin:6px 0">
      <div class="seg"><button type="button" data-fzyuz="Front" class="${fz.yuz === "Front" ? "on" : ""}">Ön (${nOn})</button><button type="button" data-fzyuz="Rear" class="${fz.yuz === "Rear" ? "on" : ""}">Arka (${nArka})</button></div>
    </div>
    <div class="toolbar" style="margin:0 0 8px">
      <button type="button" class="mini${fz.mod === "uyg" ? " on" : ""}" data-fzmod="uyg">Bu kabinin uygulamaları</button>
      <button type="button" class="mini${fz.mod === "etki" ? " on" : ""}" data-fzmod="etki" title="Kabin kapanırsa: yalnız bu kabinde çalışan uygulamalar önce">Kabin kapanırsa</button>
    </div>
    <div class="fz-rack-wrap"><div class="fz-u">${etiketler}</div>
      <div class="fz-rack" style="height:${H}px">${kutular.map(kutuHtml).join("")}${!buYuz.length ? `<div class="fz-bos-yuz">${fz.yuz === "Front" ? "Ön" : "Arka"} yüzde host yok</div>` : ""}</div></div>
    ${uSuz.length ? `<div class="small" style="margin-top:8px"><b>U bilgisi yok</b> (${uSuz.length})</div>
      <div class="fz-diger">${uSuz.map(h => `<button type="button" class="mini${fz.host === h.ad ? " on" : ""}" data-host="${esc(h.ad)}">${esc(h.ad)} <span class="muted">${h.vmBilgisi === false ? "VM yok" : fmt(h.vm) + " VM"}</span></button>`).join("")}</div>` : ""}
    ${k.nlyteEslesmeyen?.length ? `<div class="small muted" style="margin-top:6px" title="KabinHostVm bu host'ları bu kabine bağlıyor ama NlyteSunucu'daki cihazlarla seri / ad eşleşmedi">nlyte cihaz listesinde eşleşmeyen VM'li host: ${esc(k.nlyteEslesmeyen.join(", "))}</div>` : ""}
    ${k.pasif?.length ? `<details class="small" style="margin-top:6px"><summary>Aktif olmayan nlyte kayıtları (${k.pasif.length})</summary>
      ${k.pasif.map(p => `<div>${esc(p.ad)} <span class="muted">· ${esc([p.durum, p.operasyon, p.model, p.uAlt != null ? "U" + p.uAlt : null].filter(Boolean).join(" · "))}</span></div>`).join("")}</details>` : ""}`;
  el.querySelectorAll("[data-host]").forEach(b => b.addEventListener("click", () => { fz.host = b.dataset.host; fz.mod = "host"; fzKabinCiz(); fzDetayCiz(); }));
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
    el.innerHTML = `<h2 style="margin:0 0 6px">${esc(h.ad)}</h2>
      <dl class="fz-dl">
        ${[["Tür", h.tip], ["Durum", h.durum], ["Cluster", h.cluster], ["FQDN", h.fqdn], ["Model", h.model], ["Seri", h.seri], ["nlyte AssetID", h.assetId], ["Konum", h.uAlt != null ? `U${h.uAlt}${h.uYukseklik > 1 ? `–${h.uAlt + h.uYukseklik - 1}` : ""} · ${/rear/i.test(h.yuz ?? "") ? "arka" : "ön"} yüz` : "U bilgisi yok"],
           ["Chassis", h.chassis && `${h.chassis}${h.slot ? " · yuva " + h.slot : ""}`], ["Eşleşme", h.eslesme], ["Kaynak", h.kaynak]]
          .filter(x => x[1]).map(([a, b]) => `<dt>${a}</dt><dd>${esc(b)}</dd>`).join("")}
      </dl>
      ${h.vmBilgisi === false ? `<div class="unk" style="margin:8px 0">Bu cihaz için MSY / ÇSY'de VM bulunamadı: fiziksel sunucu, storage / appliance ya da VM envanterinde adı farklı geçen bir ESX host'u olabilir.</div>`
        : `<div class="small muted" style="margin:8px 0 4px">${fmt(vmler.length)} VM · ${fmt(h.uygulama)} uygulama${h.nlyte === false ? " · nlyte cihaz listesinde eşleşmedi" : ""}</div>`}
      ${h.vmBilgisi === false ? "" : `<div class="tbl-wrap" style="max-height:52vh"><table>
        <thead><tr><th>VM</th><th>IP</th><th>Uygulamalar</th><th>OS</th></tr></thead>
        <tbody>${vmler.map(v => `<tr><td class="mono small" title="${esc(v.dns ?? "")}">${esc(v.vm ?? "")}</td><td>${fzIpLink(v.ip)}</td>
          <td class="small">${v.uygulamalar.length ? v.uygulamalar.map(fzAppLink).join(", ") : `<span class="muted">ERT'de yok</span>`}</td><td class="small muted">${esc(v.os ?? "")}</td></tr>`).join("")
          || `<tr><td colspan="4" class="unk">VM yok</td></tr>`}</tbody></table></div>`}`;
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
      <thead><tr><th>Uygulama</th><th class="num" title="Bu kabindeki VM / ana veri merkezindeki toplam VM">VM</th>${etki ? "<th>Etki</th>" : ""}<th>Host'lar</th><th>Muhafız</th></tr></thead>
      <tbody>${list.map(u => `<tr${etki && u.etki === "tamamı bu kabinde" ? ` class="tls-eski-satir"` : ""}><td>${fzAppLink(u.ad)}</td><td class="num">${fmt(u.vm)}<span class="muted"> / ${fmt(u.toplamVm)}</span></td>
        ${etki ? `<td>${rozet(u)}</td>` : ""}<td class="small">${u.hostlar.map(h => `<a href="#" data-fzh="${esc(h)}">${esc(h)}</a>`).join(", ")}</td><td class="small muted">${esc(u.muhafizlar.join(", "))}</td></tr>`).join("")
        || `<tr><td colspan="5" class="unk">Bu kabindeki VM'lerin ERT'de uygulaması yok.</td></tr>`}</tbody></table></div>`;
  el.querySelectorAll("[data-fzh]").forEach(a => a.addEventListener("click", e => { e.preventDefault(); fz.host = a.dataset.fzh; fz.mod = "host"; fzKabinCiz(); fzDetayCiz(); }));
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
      <span class="badge">${esc(r.tur)}</span> <b>${esc(r.deger)}</b>${r.adet > 1 ? ` <span class="muted">(${fmt(r.adet)} VM)</span>` : ""}
      <span class="small muted">→ ${r.kabin ? esc(r.kabin) : esc(r.kapsam && r.kapsam !== fz.ozet?.anaKapsam ? r.kapsam : "kabin bilinmiyor")}${r.host ? " · " + esc(r.host) : ""}</span></button>`).join("")}
    ${b.kesik ? `<span class="small muted">ilk ${b.sonuclar.length} sonuç</span>` : ""}</div>`;
  el.querySelectorAll("[data-i]").forEach(btn => btn.addEventListener("click", async () => {
    const r = b.sonuclar[+btn.dataset.i];
    if (!r.kabin) return;
    if (r.oda && r.oda !== fz.oda) { $("#fzOda").value = r.oda; await fzOdaAc(r.oda); }
    fzKabinAc(r.kabin, r.tur === "Kabin" || r.tur === "Uygulama" || r.tur === "Cluster" ? null : r.host);
  }));
}

async function fzEksikYukle() {
  const el = $("#fzEksikIc");
  el.innerHTML = `<div class="small muted"><span class="spinner"></span> yükleniyor…</div>`;
  try { fz.eksik = await fzGet("api/fiziksel/eksik"); }
  catch (e) { el.innerHTML = `<div class="unk" style="color:var(--err)">${esc(e.message)}</div>`; return; }
  const l = fz.eksik.hostlar;
  el.innerHTML = `<div class="toolbar" style="margin:6px 0">
      <span class="small muted">${esc(fz.ozet?.anaKapsam ?? "Ana veri merkezi")}: nlyte'ta eşleşmesi olmayan ${fmt(l.length)} ESX host · ${fmt(fz.eksik.vm)} VM — büyük ihtimalle nlyte'a girilmemiş${fz.eksik.tablodan ? "" : " (NlyteHostEslesme tablosu yok, KabinHostVm'den türetildi)"}</span>
      <a class="mini" data-csv href="api/fiziksel/eksik?csv=1" style="margin-left:auto" title="nlyte ekibine gönderilecek liste">Excel (CSV)</a></div>
    <div class="tbl-wrap" style="max-height:40vh"><table>
      <thead><tr><th>Host</th><th>FQDN</th><th class="num">VM</th><th>Cluster</th><th class="num" title="nlyte'ta benzer adlı aday kayıt sayısı">Aday</th></tr></thead>
      <tbody>${l.map(e => `<tr><td class="mono">${esc(e.host)}</td><td class="mono small">${esc(e.fqdn ?? "")}</td><td class="num">${fmt(e.vmSayisi)}</td><td class="small">${esc(e.cluster ?? "")}</td><td class="num">${e.adaySayisi ?? ""}</td></tr>`).join("")
        || `<tr><td colspan="5" class="unk">Eksik host yok.</td></tr>`}</tbody></table></div>`;
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
  return `<div class="fz-konum small"><b>Fiziksel konum:</b> Kabin <a href="${esc(link)}" target="_blank" rel="noopener" title="Fiziksel envanterde aç"><b>${esc(k.kabin)}</b></a>${k.odaAdi ? " · " + esc(k.odaAdi) : ""}${k.host ? ` · host ${esc(k.host)}` : ""}${k.cluster ? ` · cluster ${esc(k.cluster)}` : ""}${k.uAlt != null ? ` · U${k.uAlt}` : ""}</div>`;
}

// ?fiziksel=1&kabin=X&host=Y: doğrudan kabin (IP kartındaki bağlantı)
async function fzDerinBaglanti() {
  const p = new URLSearchParams(location.search), kabin = p.get("kabin");
  if (!kabin) return;
  for (let i = 0; i < 50 && !fz.ozet; i++) await new Promise(r => setTimeout(r, 100));
  const m = kabin.match(/^(DR\d+)-[A-Z]+\d+$/i), oda = m ? m[1].toUpperCase() : "Diğer";
  if (fz.ozet?.odalar.some(o => o.ad === oda) && oda !== fz.oda) { $("#fzOda").value = oda; await fzOdaAc(oda); }
  fzKabinAc(kabin, p.get("host") || null);
}
