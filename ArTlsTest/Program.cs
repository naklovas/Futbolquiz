// AppResponse TLS sürümü deneme aracı (DeltaFlow'a eklemeden önce test için).
// Kullanım:
//   dotnet run -- kaynaklar
//        Cihazdaki rapor kaynaklarını (source) ve adında / etiketinde SSL veya TLS geçen kolonları listeler.
//        Ham yanıtı kaynaklar.dump.json dosyasına yazar.
//   dotnet run -- tls <sunucu-ip> [dakika=60] [kaynak=flow_tcp] [kolon1,kolon2,...]
//        Sunucuya gelen bağlantılarda TLS kolonlarını raporlar: port × TLS değeri × bağlantı sayısı.
//        Kolon verilmezse "kaynaklar" ile bulunan SSL/TLS kolonları tek tek denenir; geçersiz olan atlanır.
//   dotnet run -- kolonlar <kaynak> [regex]
//        Kaynağın tüm kolonları (ör: kolonlar tls_handshakes ip|port).
//   --grupsuz : tls'de gruplama yok, her iletişim ayrı satır (CSV'ye de yazılır)
//   --yon gelen | giden | ikisi (varsayılan) : IP sunucu olarak mı, istemci olarak mı
//   --port 443,8443 : yalnız bu portlar (büyük veride rapor hızlanır)
//   --bas / --bit "2026-11-01 09:00" : sabit aralık · --bekle <sn> : rapor bekleme (600)
//   --anahtar istemciIp,sunucuIp,sunucuPort  : tls / ham için kaynağın IP-port kolon adları (varsayılan cli_tcp.ip,srv_tcp.ip,srv_tcp.port)
//   dotnet run -- ham <sunucu-ip> <dakika> <kaynak> <kolon1,kolon2,...>
//        Verilen kolonlarla ham satırları yazar (ilk 50).
// Ayarlar: DeltaFlow'un appsettings.json'u (aynı yapı) bu klasöre kopyalanır. Şifre repoya girmez.
//   Raporlar (tls / ham) Servers listesindeki TÜM cihazlardan alınıp birleştirilir. Tek cihaz: --cihaz 1 (sıra 0'dan) ya da --cihaz Riverbed-2.
//   kaynaklar / kolonlar ilk (ya da seçilen) cihazdan okunur.
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

// Ayarlar: DeltaFlow'un appsettings.json'u ile AYNI yapı (dosya olduğu gibi kopyalanabilir). Kullanılan anahtarlar:
//   Credentials:Username / Password, Servers[{Name, Ip}], VifgIds, SourcePathType, AppResponseIgnoreSslErrors
// appsettings.Production.json varsa üzerine yazılır (DeltaFlow gibi). Birden fazla cihaz: --cihaz <sıra (0'dan) | ad>, yoksa ilki.
const int TopN = 100000;
const string Metrik = "sum_tls.session_attempted";   // tls_handshakes: Handshakes Attempted
var jsonSec = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
JsonObject? Oku(string ad) =>
    new[] { Path.Combine(AppContext.BaseDirectory, ad), ad }.FirstOrDefault(File.Exists) is string y ? JsonNode.Parse(File.ReadAllText(y), null, jsonSec) as JsonObject : null;
static void Birlestir(JsonObject hedef, JsonObject ek)
{
    foreach (var kv in ek.ToList())
        if (kv.Value is JsonObject eo && hedef[kv.Key] is JsonObject ho) Birlestir(ho, eo);
        else hedef[kv.Key] = kv.Value?.DeepClone();
}
var ayar = Oku("appsettings.json") ?? throw new FileNotFoundException("appsettings.json bulunamadı (DeltaFlow'unkini bu klasöre kopyalayın).");
if (Oku("appsettings.Production.json") is JsonObject prod) Birlestir(ayar, prod);

// Visual Studio'dan argümansız başlatılırsa komut sorulur
if (args.Length == 0)
{
    Console.WriteLine("Komut girin (ör: kaynaklar  |  kolonlar tls_handshakes ip|port  |  tls 10.1.2.3 60  |  --cihaz 1 kaynaklar  |  ham 10.1.2.3 30 flow_tcp cli_tcp.ip,srv_tcp.ip,srv_tcp.port):");
    Console.Write("> ");
    // tırnaklı parçalar ("2026-11-01 09:00") tek argüman
    args = Regex.Matches(Console.ReadLine() ?? "", "\"([^\"]*)\"|(\\S+)").Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToArray();
}
var argl = args.ToList();
string? cihazSec = null;
if (argl.IndexOf("--cihaz") is int ci and >= 0 && ci + 1 < argl.Count) { cihazSec = argl[ci + 1]; argl.RemoveRange(ci, 2); }
// --bas / --bit "2026-11-01 08:00" (yerel saat): sabit zaman aralığı (tekrar edilebilir karşılaştırma); --bekle <sn>: rapor bekleme (varsayılan 600)
string? Opsiyon(string ad) { int i = argl.IndexOf(ad); if (i < 0 || i + 1 >= argl.Count) return null; var v = argl[i + 1]; argl.RemoveRange(i, 2); return v; }
// "2026-11-01 09:00", "2026-11-01T09:00", "2026-11-01_09:00", "01.11.2026 09:00" kabul edilir
long? Zaman(string? v)
{
    if (v == null) return null;
    v = v.Trim().Trim('"').Replace('_', ' ').Replace('T', ' ');
    if (!DateTime.TryParse(v, System.Globalization.CultureInfo.GetCultureInfo("tr-TR"), System.Globalization.DateTimeStyles.AssumeLocal, out var d))
        throw new FormatException($"Tarih anlaşılamadı: '{v}' (örnek: 2026-11-01 09:00 ya da 2026-11-01_09:00)");
    return new DateTimeOffset(d).ToUnixTimeSeconds();
}
long? sabitBas = Zaman(Opsiyon("--bas")), sabitBit = Zaman(Opsiyon("--bit"));
int BekleSn = int.TryParse(Opsiyon("--bekle"), out int bk) ? bk : 600;
// --port 443  ya da  --port 443,8443 : yalnız bu sunucu portları (veri azalır, rapor hızlanır)
string yon = (Opsiyon("--yon") ?? "ikisi").ToLowerInvariant();
bool grupsuz = argl.Remove("--grupsuz");   // tls: gruplama denemeden ham satırlar (her iletişim ayrı satır)
if (yon is not ("gelen" or "giden" or "ikisi")) { Console.WriteLine("--yon gelen | giden | ikisi"); return 1; }
var portSec = Opsiyon("--port")?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
if (portSec?.Any(x => !int.TryParse(x, out _)) == true) { Console.WriteLine("--port sayı olmalı (ör: --port 443,8443)"); return 1; }
string PortF(string kolon) => portSec is { Count: > 0 } ? " and (" + string.Join(" or ", portSec.Select(x => $"{kolon} == {x}")) + ")" : "";
(long, long) Simdi(int dk) { long b = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); return (b - dk * 60L, b); }
(long, long) Pencere(int dk)
{
    var (b0, b1) = Simdi(dk);
    if (sabitBas != null) { b0 = sabitBas.Value; b1 = sabitBit ?? (sabitBas.Value + dk * 60L); }
    Console.WriteLine($"  zaman: {DateTimeOffset.FromUnixTimeSeconds(b0).ToLocalTime():yyyy-MM-dd HH:mm} – {DateTimeOffset.FromUnixTimeSeconds(b1).ToLocalTime():yyyy-MM-dd HH:mm}");
    return (b0, b1);
}
// --anahtar istemciIp,sunucuIp,sunucuPort : kaynağın IP / port kolon adları (varsayılan flow_tcp'ninkiler)
string[] anahtar = ["cli_tcp.ip", "srv_tcp.ip", "srv_tcp.port"];
if (argl.IndexOf("--anahtar") is int ai and >= 0 && ai + 1 < argl.Count)
{
    anahtar = argl[ai + 1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    argl.RemoveRange(ai, 2);
    if (anahtar.Length != 3) { Console.WriteLine("--anahtar üç kolon ister: istemciIp,sunucuIp,sunucuPort"); return 1; }
}
args = argl.ToArray();

var sunucular = (ayar["Servers"] as JsonArray ?? []).OfType<JsonObject>().ToList();
var secili = cihazSec == null ? sunucular.FirstOrDefault()
    : int.TryParse(cihazSec, out int si) ? sunucular.ElementAtOrDefault(si)
    : sunucular.FirstOrDefault(x => string.Equals(x["Name"]?.ToString(), cihazSec, StringComparison.OrdinalIgnoreCase));
string cihaz = secili?["Ip"]?.ToString()?.Trim() ?? "", cihazAd = secili?["Name"]?.ToString() ?? "AppResponse";
string kul = ayar["Credentials"]?["Username"]?.ToString() ?? "", sifre = ayar["Credentials"]?["Password"]?.ToString() ?? "";
if (cihaz == "") { Console.WriteLine($"Servers listesinde {(cihazSec == null ? "Ip'si dolu cihaz yok" : $"'{cihazSec}' yok")}. Cihazlar: {string.Join(", ", sunucular.Select((x, i) => $"{i}={x["Name"]}"))}"); return 1; }
if (kul == "" || sifre == "") { Console.WriteLine("Credentials:Username / Password boş (appsettings.json ya da appsettings.Production.json)."); return 1; }
var vifgler = (ayar["VifgIds"] as JsonArray ?? []).Select(x => x?.ToString()?.Trim() ?? "").Where(x => x != "").ToList();
string yolTuru = ayar["SourcePathType"]?.ToString() ?? "jobs";

var handler = new HttpClientHandler();
if (ayar["AppResponseIgnoreSslErrors"]?.ToString()?.ToLowerInvariant() != "false") handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
string baseUrl = $"https://{cihaz}";
// Raporlar varsayılan olarak TÜM cihazlardan alınır (DeltaFlow QueryAnyAsync gibi); --cihaz verilirse yalnız o
var hedefler = (cihazSec == null ? sunucular : [secili!])
    .Select(x => (Ad: x["Name"]?.ToString() ?? "AppResponse", Ip: x["Ip"]?.ToString()?.Trim() ?? "")).Where(x => x.Ip != "").ToList();
var tokenler = new Dictionary<string, string>();

if (args.Length == 0) { Console.WriteLine("Komut: kaynaklar | tls <ip> [dakika] [kaynak] [kolonlar] | ham <ip> <dakika> <kaynak> <kolonlar>"); return 1; }

string jwt = await TokenAsync();
tokenler[cihaz] = jwt;
Console.WriteLine($"Token alındı ({cihazAd} / {cihaz}). Raporlar: {(hedefler.Count > 1 ? $"{hedefler.Count} cihaz ({string.Join(", ", hedefler.Select(h => h.Ad))})" : hedefler[0].Ad)}");

switch (args[0].ToLowerInvariant())
{
    case "kaynaklar":
        {
            var (_, kolonlar) = await KaynaklarAsync(true);
            Console.WriteLine($"\nSSL / TLS kolonları ({kolonlar.Count}):");
            foreach (var k in kolonlar) Console.WriteLine($"  {k.Kaynak,-22} {k.Id,-45} {k.Etiket}");
            if (kolonlar.Count == 0) Console.WriteLine("  bulunamadı — kaynaklar.dump.json'da 'ssl' / 'tls' aratın.");
            break;
        }
    case "tls":
        {
            if (args.Length < 2) { Console.WriteLine("tls <sunucu-ip> [dakika] [kaynak] [kolonlar]"); return 1; }
            string ip = args[1]; int dk = args.Length > 2 ? int.Parse(args[2]) : 60; string kaynak = args.Length > 3 ? args[3] : "flow_tcp";
            List<string> aday;
            if (args.Length > 4) aday = args[4].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            else
            {
                var (_, kolonlar) = await KaynaklarAsync(false);
                aday = kolonlar.Where(k => k.Kaynak == kaynak).Select(k => k.Id).Distinct().ToList();
                if (aday.Count == 0)
                {
                    // Kaynak bilgisi alınamadıysa tahmini adlar denenir
                    aday = ["ssl.version", "ssl.protocol", "ssl.protocol_version", "tls.version", "cli_ssl.version", "srv_ssl.version",
                            "ssl.cipher", "ssl.cipher_suite", "srv_tcp.ssl_version", "cli_tcp.ssl_version"];
                    Console.WriteLine($"'{kaynak}' için SSL/TLS kolonu bulunamadı; tahmini adlar denenecek: {string.Join(", ", aday)}");
                }
                else Console.WriteLine($"'{kaynak}' SSL/TLS kolonları: {string.Join(", ", aday)}");
            }
            // --yon gelen (IP sunucu) | giden (IP istemci) | ikisi (varsayılan)
            string gelenF = $"{anahtar[1]} == {ip}" + PortF(anahtar[2]), gidenF = $"{anahtar[0]} == {ip}" + PortF(anahtar[2]);
            string filtre = yon switch { "gelen" => gelenF, "giden" => gidenF, _ => $"({gelenF}) or ({gidenF})" };
            Console.WriteLine($"  yön: {yon} · filtre: {filtre}");
            if (portSec != null) Console.WriteLine($"  port süzgeci: {string.Join(", ", portSec)}");
            // önce anahtar kolonların kendisi (bu kaynakta yoksa her TLS kolonu boşuna ✗ olur)
            try { await RaporAsync(kaynak, anahtar, filtre, Simdi(5)); Console.WriteLine($"  ✓ anahtar kolonlar: {string.Join(", ", anahtar)}"); }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ anahtar kolonlar ({string.Join(", ", anahtar)}) bu kaynakta geçersiz: {Kisalt(ex.Message, 300)}");
                Console.WriteLine($"    → 'kolonlar {kaynak} ip|port' ile doğru adları bulup --anahtar a,b,c verin.");
                return 2;
            }
            // önce her kolonu tek tek dene (geçersiz kolon tüm raporu bozar)
            var gecerli = new List<string>();
            foreach (var k in aday)
            {
                try { await RaporAsync(kaynak, [.. anahtar, k], filtre, Simdi(5)); gecerli.Add(k); Console.WriteLine($"  ✓ {k}"); }
                catch (Exception ex) { Console.WriteLine($"  ✗ {k}: {Kisalt(ex.Message, 160)}"); }
            }
            if (gecerli.Count == 0) { Console.WriteLine("Geçerli TLS kolonu yok. 'kaynaklar' çıktısındaki diğer kaynakları deneyin (4. parametre)."); return 2; }
            // Cihaz tarafında gruplama: istemci × sunucu × port × TLS kolonları + el sıkışması sayısı (metrik)
            // Cihaz tarafında gruplama. Her kolon grup anahtarı olamıyor (ör. "invalid group by: tls.version_name"), sırayla denenir:
            //  1) anahtar + TLS kolonları  2) *_name yerine değer kolonu (tls.version: 771 = TLS 1.2)  3) yalnız tls.version
            //  4) yalnız anahtar (IP / port) grup, TLS kolonu değer  5) ham satırlar (100.000 sınırı)
            List<string> AdsizKol(IEnumerable<string> k) => k.Select(x => x.EndsWith("_name") ? x[..^5] : x).Distinct().ToList();
            var denemeler = new List<(string Ad, List<string> Tls, string[]? Grup)>
            {
                ("anahtar + TLS kolonları", gecerli, [.. anahtar, .. gecerli]),
                ("*_name yerine değer kolonları", AdsizKol(gecerli), [.. anahtar, .. AdsizKol(gecerli)]),
                ("yalnız tls.version", ["tls.version"], [.. anahtar, "tls.version"]),
                ("yalnız IP / port grup", gecerli, anahtar),
            };
            List<List<JsonNode?>> satirlar = [];
            bool metrikVar = false, oldu = false;
            var zaman = Pencere(dk);
            foreach (var d in grupsuz ? [] : denemeler.DistinctBy(d => string.Join(",", d.Tls) + "|" + string.Join(",", d.Grup ?? [])))
            {
                try
                {
                    satirlar = await RaporAsync(kaynak, [.. anahtar, .. d.Tls, Metrik], filtre, zaman, true, d.Grup);
                    gecerli = d.Tls; metrikVar = true; oldu = true;
                    Console.WriteLine($"  ✓ cihaz tarafında gruplandı: {d.Ad} · group_by = {string.Join(", ", d.Grup!)} · {Metrik} = el sıkışması sayısı");
                    break;
                }
                catch (Exception ex) { Console.WriteLine($"  ✗ gruplama '{d.Ad}' olmadı: {Kisalt(HataMetni(ex.Message), 400)}"); }
            }
            if (!oldu)
            {
                Console.WriteLine(grupsuz ? "  gruplamasız: her iletişim ayrı satır (cihaz başına en fazla 100.000 — --port ve kısa aralık kullanın)"
                    : "  ⚠ hiçbir gruplama olmadı; ham satırlar alınıyor (100.000 sınırı — --port ve kısa aralık kullanın)");
                satirlar = await RaporAsync(kaynak, [.. anahtar, .. gecerli], filtre, zaman, true);
                CsvYaz($"tls_{ip}", [.. anahtar, .. gecerli], satirlar);
            }
            string[] grupKol = [.. anahtar, .. gecerli];
            int mi = grupKol.Length;   // metrik kolonun sırası (varsa); son kolon her zaman cihaz adı
            long Sayi(List<JsonNode?> r) => metrikVar && r.Count > mi + 1 && double.TryParse(Deger(r[mi]), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double d) ? (long)d : 1;
            Console.WriteLine($"\n{ip} · {(sabitBas != null ? "sabit aralık" : $"son {dk} dk")} · {satirlar.Count} satır (kaynak {kaynak})");
            // port × TLS değerleri → bağlantı sayısı, istemci sayısı
            // Gelen: IP sunucu (port = kendi portu, karşı = istemci). Giden: IP istemci (port = karşı sunucunun portu, karşı = sunucu IP'si)
            var belirsiz = satirlar.Where(r => r.Count > 2 && YonBul(Deger(r[0]), Deger(r[1]), ip) == "belirsiz").ToList();
            if (belirsiz.Count > 0)
                Console.WriteLine($"  ⚠ {belirsiz.Count} satırda {ip} ne istemci ({anahtar[0]}) ne sunucu ({anahtar[1]}) kolonunda; örnek: " +
                    string.Join(" | ", belirsiz.Take(3).Select(r => $"{anahtar[0]}={Deger(r[0])} {anahtar[1]}={Deger(r[1])}")) + " → --anahtar ile doğru kolonları verin");
            foreach (bool gelenMi in new[] { true, false })
            {
            if (yon == (gelenMi ? "giden" : "gelen")) continue;
            var yonSatir = satirlar.Where(r => r.Count >= 3 + gecerli.Count && YonBul(Deger(r[0]), Deger(r[1]), ip) == (gelenMi ? "gelen" : "giden")).ToList();
            string Kar(List<JsonNode?> r) => gelenMi ? Deger(r[0]) : Deger(r[1]);
            string Anah(List<JsonNode?> r) => gelenMi ? Deger(r[2]) : $"{Deger(r[1])}:{Deger(r[2])}";
            var ozet = yonSatir
                .GroupBy(r => (Port: Anah(r), Tls: string.Join(" | ", gecerli.Select((k, i) => $"{k}={(k == "tls.version" ? TlsAd(Deger(r[3 + i])) : Deger(r[3 + i]))}"))))
                .Select(g => (g.Key.Port, g.Key.Tls, Adet: g.Sum(Sayi), Istemci: g.Select(Kar).Distinct().Count(), Cihaz: string.Join(",", g.Select(r => Deger(r[^1])).Distinct()),
                    Ilk: string.Join(" ", g.GroupBy(r => gelenMi ? Deger(r[0]) : Deger(r[0])).OrderByDescending(c => c.Sum(Sayi)).Take(3).Select(c => $"{c.Key}({c.Sum(Sayi)})"))))
                .OrderBy(x => int.TryParse(x.Port.Split(':').Last(), out int p) ? p : 99999).ThenByDescending(x => x.Adet).ToList();
            Console.WriteLine(gelenMi ? $"\n▼ GELEN (bu IP sunucu) · {yonSatir.Count} satır" : $"\n▲ GİDEN (bu IP istemci, başka sunuculara bağlanıyor) · {yonSatir.Count} satır");
            if (ozet.Count == 0) { Console.WriteLine("  kayıt yok"); continue; }
            Console.WriteLine($"{(gelenMi ? "Port" : "Hedef:port"),-21} {(metrikVar ? "ElSıkış" : "Satır"),8} {(gelenMi ? "İstemci" : "Hedef"),8}  {"Cihaz",-14} TLS{(gelenMi ? "   ‖ en çok bağlanan istemciler" : "")}");
            foreach (var o in ozet) Console.WriteLine($"{o.Port,-21} {o.Adet,8} {o.Istemci,8}  {o.Cihaz,-14} {o.Tls}{(gelenMi ? "   ‖ " + o.Ilk : "")}");
            }
            if (satirlar.Count >= TopN) Console.WriteLine($"  ⚠ {TopN} satır sınırına ulaşıldı: bazı portlar kesilmiş olabilir (süreyi kısaltın).");

            // Karşılaştırma: aynı aralıkta flow_tcp'de gelen trafiği olan ama TLS el sıkışması görülmeyen portlar
            if (kaynak != "flow_tcp")
            {
                try
                {
                    var tlsPort = satirlar.Where(r => r.Count > 2 && AyniIp(Deger(r[1]), ip)).Select(r => Deger(r[2])).ToHashSet();   // yalnız gelen
                    string[] fk = ["cli_tcp.ip", "srv_tcp.ip", "srv_tcp.port"];
                    var fz = Pencere(dk);
                    List<List<JsonNode?>> akis;
                    try { akis = await RaporAsync("flow_tcp", fk, $"srv_tcp.ip == {ip}" + PortF("srv_tcp.port"), fz, true, fk); }
                    catch (Exception exg) { Console.WriteLine($"  flow_tcp gruplu olmadı ({Kisalt(exg.Message, 120)}); ham"); akis = await RaporAsync("flow_tcp", fk, $"srv_tcp.ip == {ip}" + PortF("srv_tcp.port"), fz, true); }
                    var portlar = akis.Where(r => r.Count > 2).GroupBy(r => Deger(r[2]))
                        .Select(g => (Port: g.Key, Satir: g.Count(), Istemci: g.Select(r => Deger(r[0])).Distinct().Count(), Cihaz: string.Join(",", g.Select(r => Deger(r[^1])).Distinct())))
                        .OrderBy(x => int.TryParse(x.Port, out int p) ? p : 99999).ToList();
                    Console.WriteLine($"\nflow_tcp karşılaştırması (aynı aralık): {portlar.Count} portta gelen trafik var, {portlar.Count(x => tlsPort.Contains(x.Port))} tanesinde TLS el sıkışması görüldü.");
                    Console.WriteLine($"{"Port",-7} {"Satır",7} {"İstemci",8}  {"Cihaz",-14} Durum");
                    foreach (var x in portlar)
                        Console.WriteLine($"{x.Port,-7} {x.Satir,7} {x.Istemci,8}  {x.Cihaz,-14} {(tlsPort.Contains(x.Port) ? "TLS ✓" : "el sıkışması yok" + Ipucu(x.Port))}");
                    if (akis.Count >= TopN) Console.WriteLine($"  ⚠ flow_tcp {TopN} satır sınırına ulaştı.");
                }
                catch (Exception ex) { Console.WriteLine("flow_tcp karşılaştırması yapılamadı: " + Kisalt(ex.Message, 200)); }
            }
            break;
        }
    case "kolonlar":
        {
            if (args.Length < 2) { Console.WriteLine("kolonlar <kaynak> [arama regex, ör: ip|port]"); return 1; }
            var liste = await KolonlarAsync(args[1]);
            var rx = args.Length > 2 ? new Regex(args[2], RegexOptions.IgnoreCase) : null;
            var sec = liste.Where(k => rx == null || rx.IsMatch(k.Id + " " + k.Etiket)).ToList();
            Console.WriteLine($"{args[1]}: {liste.Count} kolon{(rx != null ? $", '{args[2]}' ile eşleşen {sec.Count}" : "")}");
            foreach (var k in sec) Console.WriteLine($"  {k.Id,-45} {k.Etiket}");
            break;
        }
    case "ham":
        {
            if (args.Length < 5) { Console.WriteLine("ham <ip> <dakika> <kaynak> <kolon1,kolon2>"); return 1; }
            var kol = args[4].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToArray();
            string hip = args[1];
            string hg = $"{anahtar[1]} == {hip}" + PortF(anahtar[2]), hd = $"{anahtar[0]} == {hip}" + PortF(anahtar[2]);
            string hf = yon switch { "gelen" => hg, "giden" => hd, _ => $"({hg}) or ({hd})" };
            Console.WriteLine($"  filtre: {hf}");
            var satirlar = await RaporAsync(args[3], kol, hf, Pencere(int.Parse(args[2])), true);
            // yön kolonu: istemci / sunucu IP kolonları listede varsa
            int ci2 = Array.IndexOf(kol, anahtar[0]), si2 = Array.IndexOf(kol, anahtar[1]);
            bool yonVar = ci2 >= 0 && si2 >= 0;
            if (yonVar)
            {
                foreach (var r in satirlar) r.Add(JsonValue.Create(YonBul(Deger(r[ci2]), Deger(r[si2]), hip)));
                // ekranda ve CSV'de önce gelenler (az olan taraf ilk 50'nin dışında kalmasın)
                int Sira(string y) => y switch { "gelen" => 0, "kendine" => 1, "belirsiz" => 2, _ => 3 };
                satirlar = satirlar.OrderBy(r => Sira(Deger(r[^1]))).ToList();
            }
            else Console.WriteLine($"  (yön kolonu için {anahtar[0]} ve {anahtar[1]} kolonları da listede olmalı)");
            Console.WriteLine(string.Join(" ; ", kol) + " ; cihaz" + (yonVar ? " ; yön" : ""));
            // gelenlerin hepsi + toplam en az 50 satır
            int goster = Math.Max(50, yonVar ? satirlar.Count(r => Deger(r[^1]) != "giden") : 0);
            foreach (var r in satirlar.Take(goster)) Console.WriteLine(string.Join(" ; ", r.Select((x, i) => i < kol.Length && kol[i] == "tls.version" ? TlsAd(Deger(x)) : Deger(x))));
            Console.WriteLine($"({satirlar.Count} satır; ilk {Math.Min(goster, satirlar.Count)} gösterildi, gelenler önce — tamamı CSV'de)");
            CsvYaz($"ham_{hip}", kol, satirlar, yonVar ? ["yön"] : []);
            if (yonVar) Console.WriteLine("  yön: " + string.Join(", ", satirlar.GroupBy(r => Deger(r[^1])).Select(g => $"{g.Key} {g.Count()}")));
            break;
        }
    default: Console.WriteLine("Bilinmeyen komut."); return 1;
}
return 0;

// ---------------------------------------------------------------------------
async Task<string> TokenAsync()
{
    var body = JsonSerializer.Serialize(new { user_credentials = new { username = kul, password = sifre }, generate_refresh_token = false });
    using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/mgmt.aaa/1.0/token") { Content = new StringContent(body, Encoding.UTF8) };
    req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");   // charset'siz (bazı sürümler 415)
    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    using var r = await http.SendAsync(req);
    string s = await r.Content.ReadAsStringAsync();
    if (!r.IsSuccessStatusCode) throw new Exception($"Token alınamadı HTTP {(int)r.StatusCode}: {Kisalt(s, 300)}");
    return JsonNode.Parse(s)?["access_token"]?.ToString() ?? throw new Exception("access_token yok");
}

HttpRequestMessage Req(HttpMethod m, string url, object? json = null)
{
    var r = new HttpRequestMessage(m, url);
    r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
    r.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    if (json != null) r.Content = new StringContent(JsonSerializer.Serialize(json), Encoding.UTF8, "application/json");
    return r;
}

// Kaynak listesi: sürüme göre adres farklı olabilir, sırayla denenir. Kolonlar kaynak öğesinin içinde değilse ayrıca istenir.
async Task<(List<string> Kaynaklar, List<(string Kaynak, string Id, string Etiket)> Kolonlar)> KaynaklarAsync(bool yaz)
{
    var dump = new JsonObject();
    JsonNode? liste = null;
    foreach (var u in new[] { "/api/npm.reports.sources/1.0/sources", "/api/npm.reports.sources/1.0/sources/items", "/api/npm.reports/1.0/sources" })
    {
        using var r = await http.SendAsync(Req(HttpMethod.Get, baseUrl + u));
        string s = await r.Content.ReadAsStringAsync();
        if (yaz) Console.WriteLine($"GET {u} → HTTP {(int)r.StatusCode}");
        if (!r.IsSuccessStatusCode) continue;
        try { liste = JsonNode.Parse(s); dump[u] = liste?.DeepClone(); break; } catch { }
    }
    var adlar = new List<string>();
    if (liste != null)
        foreach (var it in (liste["items"] as JsonArray) ?? (liste as JsonArray) ?? [])
            if (it?["name"]?.ToString() is string n) adlar.Add(n);
    if (yaz) Console.WriteLine($"Kaynaklar ({adlar.Count}): {string.Join(", ", adlar)}");

    // ayrıntı: flow_tcp, wtapages ve adında ssl / tls geçenler
    foreach (var n in adlar.Where(n => n is "flow_tcp" or "wtapages" || Regex.IsMatch(n, "ssl|tls", RegexOptions.IgnoreCase)))
    {
        foreach (var u in new[] { $"/api/npm.reports.sources/1.0/sources/items/{n}", $"/api/npm.reports.sources/1.0/sources/items/{n}/columns" })
        {
            using var r = await http.SendAsync(Req(HttpMethod.Get, baseUrl + u));
            if (!r.IsSuccessStatusCode) continue;
            try { dump[u] = JsonNode.Parse(await r.Content.ReadAsStringAsync()); } catch { }
        }
    }
    if (yaz) { File.WriteAllText("kaynaklar.dump.json", dump.ToJsonString(new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine("Ham yanıt: kaynaklar.dump.json"); }

    // JSON'un her yerinde id / name'inde ya da label'ında ssl / tls geçen kolon nesneleri
    var kolonlar = new List<(string, string, string)>();
    void Gez(JsonNode? n, string kaynak)
    {
        switch (n)
        {
            case JsonObject o:
                string k2 = o["name"]?.GetValueKind() == JsonValueKind.String && o["columns"] != null ? o["name"]!.ToString() : kaynak;
                string? id = o["id"]?.GetValueKind() == JsonValueKind.String ? o["id"]!.ToString() : null;
                string lbl = o["label"]?.ToString() ?? o["description"]?.ToString() ?? "";
                if (id != null && Regex.IsMatch(id + " " + lbl, @"ssl|tls", RegexOptions.IgnoreCase)) kolonlar.Add((k2, id, lbl));
                foreach (var kv in o)
                {
                    // kolonlar sözlük olarak gelirse ({"ssl.version": {...}}) anahtar kolon adıdır
                    if (kv.Value is JsonObject && o.Count > 3 && kv.Key.Contains('.') && Regex.IsMatch(kv.Key, "ssl|tls", RegexOptions.IgnoreCase))
                        kolonlar.Add((k2, kv.Key, kv.Value["label"]?.ToString() ?? ""));
                    Gez(kv.Value, k2);
                }
                break;
            case JsonArray a: foreach (var x in a) Gez(x, kaynak); break;
        }
    }
    foreach (var kv in dump)
    {
        var m = Regex.Match(kv.Key, @"sources/items/([^/]+)");
        Gez(kv.Value, m.Success ? m.Groups[1].Value : "?");
    }
    return (adlar, kolonlar.Distinct().ToList());
}

// Bir kaynağın TÜM kolonları (id + etiket)
async Task<List<(string Id, string Etiket)>> KolonlarAsync(string kaynak)
{
    var bulunan = new List<(string, string)>();
    // etiketin yanına kolonun kısa özellikleri (type, grouped_by / key / metric gibi bayraklar) eklenir
    static string Ek(JsonObject o) => string.Join(" ", o.Where(kv => kv.Key is not ("id" or "label" or "description" or "name") && kv.Value is JsonValue)
        .Select(kv => $"{kv.Key}={kv.Value}").Take(8));
    void Gez(JsonNode? n)
    {
        if (n is JsonObject o)
        {
            if (o["id"]?.GetValueKind() == JsonValueKind.String && (o["label"] != null || o["type"] != null))
                bulunan.Add((o["id"]!.ToString(), (o["label"]?.ToString() ?? "") + (Ek(o) is { Length: > 0 } e ? "  [" + e + "]" : "")));
            foreach (var kv in o) Gez(kv.Value);
        }
        else if (n is JsonArray a) foreach (var x in a) Gez(x);
    }
    foreach (var u in new[] { $"/api/npm.reports.sources/1.0/sources/items/{kaynak}", $"/api/npm.reports.sources/1.0/sources/items/{kaynak}/columns" })
    {
        using var r = await http.SendAsync(Req(HttpMethod.Get, baseUrl + u));
        if (r.IsSuccessStatusCode) try { Gez(JsonNode.Parse(await r.Content.ReadAsStringAsync())); } catch { }
    }
    if (bulunan.Count == 0)
        foreach (var u in new[] { "/api/npm.reports.sources/1.0/sources", "/api/npm.reports/1.0/sources" })
        {
            using var r = await http.SendAsync(Req(HttpMethod.Get, baseUrl + u));
            if (!r.IsSuccessStatusCode) continue;
            var j = JsonNode.Parse(await r.Content.ReadAsStringAsync());
            foreach (var it in (j?["items"] as JsonArray) ?? (j as JsonArray) ?? [])
                if (it?["name"]?.ToString() == kaynak) Gez(it);
            if (bulunan.Count > 0) break;
        }
    return bulunan.Distinct().ToList();
}

// Tek data_def'li rapor: oluştur → bekle → veri → sil
// Tüm hedef cihazlarda çalıştırır, satırları birleştirir. Bir cihaz hata verirse diğerleri devam eder; hepsi hata verirse ilk hata fırlatılır.
async Task<List<List<JsonNode?>>> RaporAsync(string kaynak, string[] kolonlar, string filtre, (long Bas, long Bit) zaman, bool yaz = false, string[]? grup = null)
{
    var tum = new List<List<JsonNode?>>();
    var hatalar = new List<string>();
    string anaUrl = baseUrl, anaJwt = jwt;
    try
    {
        foreach (var h in hedefler)
        {
            try
            {
                baseUrl = $"https://{h.Ip}";
                if (!tokenler.TryGetValue(h.Ip, out var t)) tokenler[h.Ip] = t = await TokenAsync();
                jwt = t;
                var r = await RaporTekAsync(kaynak, kolonlar, filtre, zaman, yaz ? h.Ad : null, grup);
                foreach (var satir in r) satir.Add(JsonValue.Create(h.Ad));   // son kolon: cihaz
                if (yaz && hedefler.Count > 1) Console.WriteLine($"  [{h.Ad}] {r.Count} satır");
                tum.AddRange(r);
            }
            catch (Exception ex)
            {
                hatalar.Add($"[{h.Ad}] {ex.Message}");
                if (yaz) Console.WriteLine($"  [{h.Ad}] HATA: {Kisalt(HataMetni(ex.Message), 300)}");
            }
        }
    }
    finally { baseUrl = anaUrl; jwt = anaJwt; }
    if (hatalar.Count == hedefler.Count) throw new Exception(string.Join(" | ", hatalar));
    return tum;
}

async Task<List<List<JsonNode?>>> RaporTekAsync(string kaynak, string[] kolonlar, string filtre, (long Bas, long Bit) zaman, string? yazAd, string[]? grup = null)
{
    var (bas, bitis) = zaman;
    var sonuc = new List<List<JsonNode?>>();
    foreach (var vifg in vifgler.Count > 0 ? vifgler : [""])
    {
        object src = vifg == "" ? new { name = kaynak } : new { name = kaynak, path = $"{yolTuru}/{vifg}" };
        // grup verilirse cihaz kendisi toplar (group_by): her kombinasyon tek satır → 100.000 satır sınırına takılmaz
        var dd = JsonSerializer.SerializeToNode(new { source = src, columns = kolonlar, time = new { start = bas.ToString(), end = bitis.ToString() },
            filters = new object[] { new { id = "f", type = "STEELFILTER", value = filtre } }, topn = TopN })!.AsObject();
        if (grup != null) dd["group_by"] = JsonSerializer.SerializeToNode(grup);
        var payload = new JsonObject { ["info"] = new JsonObject { ["name"] = "DeltaFlow TLS deneme", ["description"] = "" }, ["data_defs"] = new JsonArray(dd) };
        using var cr = await http.SendAsync(Req(HttpMethod.Post, $"{baseUrl}/api/npm.reports/1.0/instances", payload));
        string cb = await cr.Content.ReadAsStringAsync();
        if (!cr.IsSuccessStatusCode) throw new Exception($"Rapor oluşturulamadı HTTP {(int)cr.StatusCode}: {cb}");
        string id = JsonNode.Parse(cb)?["id"]?.ToString() ?? throw new Exception("Rapor ID yok");
        try
        {
            bool bitti = false;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            JsonNode? sonDurum = null;
            for (int i = 0; sw.Elapsed.TotalSeconds < BekleSn && !bitti; i++)
            {
                await Task.Delay(2000);
                using var st = await http.SendAsync(Req(HttpMethod.Get, $"{baseUrl}/api/npm.reports/1.0/instances/items/{id}"));
                var def = JsonNode.Parse(await st.Content.ReadAsStringAsync())?["data_defs"]?.AsArray()?[0];
                sonDurum = def?["status"];
                string? durum = sonDurum?["state"]?.ToString();
                if (durum == "error") throw new Exception("Rapor hata: " + sonDurum?.ToJsonString());
                bitti = durum == "completed";
                if (yazAd != null && !bitti && i % 10 == 9) Console.WriteLine($"  [{yazAd}] bekleniyor {sw.Elapsed.TotalSeconds:N0} sn · {Kisalt(sonDurum?.ToJsonString() ?? "", 150)}");
            }
            if (!bitti) throw new Exception($"Rapor {BekleSn} sn'de tamamlanmadı (son durum: {Kisalt(sonDurum?.ToJsonString() ?? "", 200)}) — --bekle ile süreyi artırın");
            // tamamlandı ama durumda state / progress dışında bilgi varsa (uyarı, kısaltma) göster
            if (yazAd != null && sonDurum is JsonObject so && so.Any(kv => kv.Key is not ("state" or "progress")))
                Console.WriteLine($"  [{yazAd}] rapor durumu: {Kisalt(so.ToJsonString(), 300)}");
            using var dr = await http.SendAsync(Req(HttpMethod.Get, $"{baseUrl}/api/npm.reports/1.0/instances/items/{id}/data_defs/items/1/data?limit=100000"));
            var dj = JsonNode.Parse(await dr.Content.ReadAsStringAsync());
            int once = sonuc.Count;
            foreach (var row in dj?["data"]?.AsArray() ?? [])
                if (row is JsonArray a) sonuc.Add(a.Select(x => x?.DeepClone()).ToList());
            if (yazAd != null && dj?["meta"] is JsonNode meta) Console.WriteLine($"  [{yazAd}{(vifg != "" ? "/" + vifg : "")}] {sonuc.Count - once} satır · meta: {Kisalt(meta.ToJsonString(), 200)}");
        }
        finally
        {
            try { using var _ = await http.SendAsync(Req(HttpMethod.Delete, $"{baseUrl}/api/npm.reports/1.0/instances/items/{id}")); } catch { }
        }
    }
    return sonuc;
}

// Bilinen portlarda TLS neden el sıkışması olarak görünmeyebilir
static string Ipucu(string port) => port switch
{
    "1433" => " (SQL Server: TLS TDS paketinin içinde, ayrı el sıkışması görünmez)",
    "3389" => " (RDP: TLS CredSSP / X.224 içinde)",
    "389" or "3268" => " (LDAP: StartTLS ya da düz)",
    "445" or "139" => " (SMB: TLS değil, SMB3 şifrelemesi)",
    "25" or "587" or "21" => " (StartTLS: düz başlar)",
    "80" or "8080" => " (büyük ihtimalle düz HTTP)",
    "135" or "5985" => " (RPC / WinRM HTTP: TLS değil)",
    _ => ""
};
// Tüm satırlar CSV'ye (Excel için ; ayraçlı, UTF-8 BOM); son kolon cihaz
static void CsvYaz(string ad, string[] kol, List<List<JsonNode?>> satirlar, string[]? ek = null)
{
    string dosya = $"{ad}_{DateTime.Now:yyyyMMdd_HHmmss}.csv".Replace(':', '_');
    static string Q(string v) => v.Contains(';') || v.Contains('"') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    var sb = new StringBuilder();
    sb.AppendLine(string.Join(";", kol.Append("cihaz").Concat(ek ?? [])));
    foreach (var r in satirlar)
        sb.AppendLine(string.Join(";", r.Select((x, i) => Q(i < kol.Length && kol[i] == "tls.version" ? TlsAd(Deger(x)) : Deger(x)))));
    File.WriteAllText(dosya, sb.ToString(), new UTF8Encoding(true));
    Console.WriteLine($"  CSV: {Path.GetFullPath(dosya)} ({satirlar.Count} satır)");
}

// IP karşılaştırması biçimden bağımsız (::ffff:10.1.2.3, boşluk, baştaki sıfır vb.)
static bool AyniIp(string a, string b)
{
    static System.Net.IPAddress? P(string x) => System.Net.IPAddress.TryParse(x.Trim(), out var i) ? (i.IsIPv4MappedToIPv6 ? i.MapToIPv4() : i) : null;
    var (x, y) = (P(a), P(b));
    return x != null && y != null ? x.Equals(y) : string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
// Sorgulanan IP sunucu kolonundaysa gelen, istemci kolonundaysa giden; ikisinde de değilse belirsiz (kolon adı / biçim sorunu)
static string YonBul(string cli, string srv, string ip) =>
    AyniIp(srv, ip) ? (AyniIp(cli, ip) ? "kendine" : "gelen") : AyniIp(cli, ip) ? "giden" : "belirsiz";

// AppResponse hata JSON'undan okunur metin: messages[].text + details
static string HataMetni(string m)
{
    int i = m.IndexOf('{');
    if (i < 0) return m;
    try
    {
        var j = JsonNode.Parse(m[i..]);
        var msj = (j?["messages"] as JsonArray)?.Select(x => x?["text"] + (x?["details"] is JsonNode dt ? " " + dt.ToJsonString() : ""));
        return msj != null && msj.Any() ? m[..i] + string.Join(" | ", msj) : m;
    }
    catch { return m; }
}
// tls.version değeri → ad (0x0300 SSL 3.0 … 0x0304 TLS 1.3)
static string TlsAd(string v) => v switch
{
    "768" or "0x0300" => "SSL 3.0", "769" or "0x0301" => "TLS 1.0", "770" or "0x0302" => "TLS 1.1",
    "771" or "0x0303" => "TLS 1.2", "772" or "0x0304" => "TLS 1.3", _ => v
};
static string Deger(JsonNode? n) => n switch { null => "", JsonValue v => v.ToString(), _ => n.ToJsonString() } is { Length: > 0 } s ? s : "(boş)";
static string Kisalt(string s, int n) => s.Length <= n ? s : s[..n] + "…";
