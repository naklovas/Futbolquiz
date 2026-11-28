using DisErisimServisi;

// ---------------------------------------------------------------------------
// DeltaFlow Dış Erişim servisi: her gece bir önceki günü (00:00–24:00) tarar; internetten (public IP'lerden) erişilen iç servisleri
// (IP + port) Palo Alto firewall ve Carbon Black (Splunk) verisinden bulur, doku DB'deki DF_DIS_ERISIM* tablolarına yazar
// (sql/dis_erisim.sql). DeltaFlow'daki "Dış erişim" sayfası bu tabloları okur.
//
// Ayarlar: exe'nin yanındaki appsettings.json / appsettings.Production.json. "AyarDosyasi" verilirse (ör. DeltaFlow'un
// appsettings.Production.json'u) önce o okunur: Splunk, Firewall ve ConnectionStrings tek yerde kalır; servisin kendi dosyası üstüne yazar.
//
// Kullanım:
//   DisErisimServisi.exe                      → Windows servisi / konsol: her gün DisErisim:CalismaSaati'nde (varsayılan 02:30) çalışır
//   DisErisimServisi.exe --simdi              → bir kez çalışır (eksik kalan günler, en çok DisErisim:MaxGeriGun) ve çıkar
//   DisErisimServisi.exe --tarih=2026-11-27   → yalnız o günü (yeniden) tarar ve çıkar
//   DisErisimServisi.exe --deneme             → Splunk'a sorar, sonucu ekrana yazar, veritabanına YAZMAZ
// Servis kurulumu (yönetici PowerShell):
//   sc.exe create "DeltaFlowDisErisim" binPath= "D:\DeltaFlow\DisErisimServisi\DisErisimServisi.exe" start= delayed-auto obj= "DOMAIN\servis_hesabi" password= ...
// ---------------------------------------------------------------------------

string temel = AppContext.BaseDirectory;
var on = new ConfigurationBuilder().SetBasePath(temel)
    .AddJsonFile("appsettings.json", true).AddJsonFile("appsettings.Production.json", true).AddCommandLine(args).Build();

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = temel });
builder.Configuration.Sources.Clear();
if (on["AyarDosyasi"] is { Length: > 0 } ayar)
{
    string yol = Path.IsPathRooted(ayar) ? ayar : Path.GetFullPath(Path.Combine(temel, ayar));
    builder.Configuration.AddJsonFile(yol, optional: false, reloadOnChange: false);
}
builder.Configuration.AddJsonFile(Path.Combine(temel, "appsettings.json"), true)
    .AddJsonFile(Path.Combine(temel, "appsettings.Production.json"), true)
    .AddEnvironmentVariables("DISERISIM_")
    .AddCommandLine(args);

builder.Services.AddWindowsService(o => o.ServiceName = "DeltaFlowDisErisim");
builder.Services.AddSingleton<Tarayici>();

bool tekSefer = args.Any(a => a is "--simdi" or "--deneme" || a.StartsWith("--tarih"));
if (!tekSefer) builder.Services.AddHostedService<Zamanlayici>();

var host = builder.Build();
if (!tekSefer) { await host.RunAsync(); return; }

var t = host.Services.GetRequiredService<Tarayici>();
var log = host.Services.GetRequiredService<ILogger<Program>>();
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
try
{
    bool deneme = args.Contains("--deneme");
    if (args.FirstOrDefault(a => a.StartsWith("--tarih")) is { } ta)
    {
        if (!DateTime.TryParse(ta.Split('=', 2).ElementAtOrDefault(1), out var gun)) { log.LogError("--tarih=yyyy-aa-gg biçiminde olmalı."); Environment.ExitCode = 2; return; }
        await t.GunuTaraAsync(gun.Date, deneme, cts.Token);
    }
    else if (deneme) await t.GunuTaraAsync(DateTime.Today.AddDays(-1), true, cts.Token);
    else await t.EksikGunleriTaraAsync(cts.Token);
}
catch (Exception ex) { log.LogError(ex, "Tarama başarısız"); Environment.ExitCode = 1; }

// Her gün DisErisim:CalismaSaati'nde (yerel saat) eksik kalan günleri tarar; servis açılınca dünkü gün taranmamışsa 2 dk sonra hemen tarar
sealed class Zamanlayici(Tarayici t, IConfiguration cfg, ILogger<Zamanlayici> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(cfg.GetValue("DisErisim:AcilisBeklemeDakika", 2.0)), ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            try { await t.EksikGunleriTaraAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogError(ex, "Gece taraması başarısız"); }
            var saat = TimeSpan.TryParse(cfg["DisErisim:CalismaSaati"], out var s) ? s : new TimeSpan(2, 30, 0);
            var sonraki = DateTime.Today.Add(saat);
            if (sonraki <= DateTime.Now) sonraki = sonraki.AddDays(1);
            log.LogInformation("Sonraki tarama: {Zaman}", sonraki);
            try { await Task.Delay(sonraki - DateTime.Now, ct); } catch (OperationCanceledException) { return; }
        }
    }
}
