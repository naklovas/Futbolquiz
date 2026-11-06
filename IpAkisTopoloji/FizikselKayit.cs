// Fiziksel cihaz değerleri — denetim kaydı, elle değer ve Excel (.xlsx) çıktısı.
// Kullanıcı: "denetçi rapor istediğinde değerler yalnız ekranda; DB'ye alalım, gerekirse elle değiştirelim, Excel çıktısı da olsun,
// yetki DokuPanel'den". DeltaFlow yalnız kendi DF_ tablolarına yazar (sql/fiziksel_deger.sql); nlyte / doku tablolarına dokunmaz.
//   Kayıt  : DF_FIZIKSEL_DEGER_KAYIT (başlık) + DF_FIZIKSEL_DEGER (cihaz) + DF_FIZIKSEL_DEGER_KABIN (kabin) — o anki hesap, tarihli
//   Elle   : DF_FIZIKSEL_DEGER_ELLE (AssetID başına; dolu boyutlar hesaplananın yerine geçer, gerekçe zorunlu)
//   Excel  : Cihazlar, Kabinler, Kurallar, Elle değerler, Yöntem sayfaları — güncel hesaptan ya da kayıtlı bir dönemden
using System.Data;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

record FizElle(string AssetId, string? Cihaz, string? Kabin, int?[] D, string Gerekce, string? Kaydeden, DateTime Tarih);
record FizElleIstek(string AssetId, int? V, int? I, int? E, int? G, int? B, string? Gerekce, bool Sil = false);
record FizKayitIstek(string? Aciklama);

sealed partial class FizikselService
{
    static readonly string[] BoyutKolon = ["VARLIK", "IS_DEGERI", "ERISILEBILIRLIK", "GIZLILIK", "BUTUNLUK"];

    string DegerTablo(string ad, string varsayilan)
    {
        string t = cfg[$"Fiziksel:DegerTablolari:{ad}"] ?? varsayilan;
        if (!Regex.IsMatch(t, @"^[\w\.\[\]]+$")) throw new InvalidOperationException($"Fiziksel:DegerTablolari:{ad} geçersiz: {t}");
        return t;
    }
    string TKayit => DegerTablo("Kayit", "dbo.DF_FIZIKSEL_DEGER_KAYIT");
    string TCihaz => DegerTablo("Cihaz", "dbo.DF_FIZIKSEL_DEGER");
    string TKabin => DegerTablo("Kabin", "dbo.DF_FIZIKSEL_DEGER_KABIN");
    string TElle => DegerTablo("Elle", "dbo.DF_FIZIKSEL_DEGER_ELLE");
    string ConnStr => cfg.GetConnectionString(cfg["Fiziksel:Baglanti"] ?? "Envanter") ?? throw new InvalidOperationException("ConnectionStrings:Envanter tanımlı değil.");
    static string TabloYok(string t) => $"{t} tablosu yok — sql/fiziksel_deger.sql betiğini çalıştırın.";

    // Elle değerler snapshot yüklenirken okunur; tablo yoksa sessizce yok sayılır (ElleDurum)
    async Task ElleYukleAsync(SqlConnection conn, FizikselSnapshot s, CancellationToken ct)
    {
        try
        {
            await using var cmd = new SqlCommand($"SELECT ASSETID, CIHAZ, KABIN, {string.Join(", ", BoyutKolon)}, GEREKCE, KAYDEDEN, TARIH FROM {TElle}", conn) { CommandTimeout = 60 };
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var d = Enumerable.Range(3, 5).Select(i => r.IsDBNull(i) ? (int?)null : Convert.ToInt32(r.GetValue(i))).ToArray();
                string id = Convert.ToString(r.GetValue(0))!.Trim();
                s.Elle[id] = new FizElle(id, r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), d, r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9), r.GetDateTime(10));
            }
            s.ElleDurum = null;
        }
        catch (SqlException ex) when (ex.Number is 207 or 208 or 229) { s.ElleDurum = ex.Number == 208 ? TabloYok(TElle) : ex.Message; }
    }

    // Elle değer: dolu boyutlar hesaplanan / kural değerinin YERİNE geçer (düşürebilir de)
    static FizDv? ElleUygula(FizikselSnapshot s, FizCihaz c, FizDv? dv)
    {
        if (c.AssetId == null || !s.Elle.TryGetValue(c.AssetId, out var el)) return dv;
        var d = (dv?.D ?? new int?[5]).ToArray(); var k = (dv?.Kaynak ?? new string?[5]).ToArray();
        string not = $"elle: {el.Gerekce} ({el.Kaydeden}, {el.Tarih:dd.MM.yyyy})";
        for (int i = 0; i < 5; i++) if (el.D[i] != null) { d[i] = el.D[i]; k[i] = not; }
        // girilmeyen Varlık / İş, elle değerlerden yeniden türetilir (Varlık = en yüksek(G, B, E), İş = E)
        if (el.D[0] == null && new[] { d[2], d[3], d[4] }.Max() is int vm) { d[0] = vm; k[0] = "türetildi: en yüksek(G, B, E)"; }
        if (el.D[1] == null && el.D[2] != null) { d[1] = d[2]; k[1] = "türetildi: İş = Erişilebilirlik"; }
        return new FizDv(d, k, $"elle değer — {el.Gerekce} ({el.Kaydeden}, {el.Tarih:dd.MM.yyyy})" + (dv != null ? $" · hesaplanan: {dv.Yontem} [{DvMetin(dv)}]" : ""));
    }
    static string DvMetin(FizDv d) => string.Join(" ", new[] { "V", "İ", "E", "G", "B" }.Select((h, i) => d.D[i] is int x ? $"{h}{x}" : null).OfType<string>());

    public async Task<IResult> ElleYazAsync(FizikselSnapshot s, FizElleIstek q, string? kullanici, CancellationToken ct)
    {
        string id = (q.AssetId ?? "").Trim();
        if (!s.CihazId.TryGetValue(id, out var c)) return Results.BadRequest(new { error = $"Cihaz bulunamadı: {id}" });
        var d = new[] { q.V, q.I, q.E, q.G, q.B };
        if (!q.Sil)
        {
            if (d.Any(x => x is < 0 or > 5)) return Results.BadRequest(new { error = "Değerler 0–5 arası olmalı." });
            if (d.All(x => x == null)) return Results.BadRequest(new { error = "En az bir boyut için değer girin." });
            if ((q.Gerekce ?? "").Trim().Length < 5) return Results.BadRequest(new { error = "Gerekçe zorunlu (denetim kaydı)." });
        }
        string kabin = s.KabinCihaz.FirstOrDefault(kv => kv.Value.Contains(c)).Key ?? "";
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync(ct);
            string sql = q.Sil ? $"DELETE FROM {TElle} WHERE ASSETID = @id"
                : $@"MERGE {TElle} AS t USING (SELECT @id AS ASSETID) AS k ON t.ASSETID = k.ASSETID
                    WHEN MATCHED THEN UPDATE SET CIHAZ = @cihaz, KABIN = @kabin, VARLIK = @v0, IS_DEGERI = @v1, ERISILEBILIRLIK = @v2, GIZLILIK = @v3, BUTUNLUK = @v4,
                        GEREKCE = @gerekce, KAYDEDEN = @kim, TARIH = SYSDATETIME()
                    WHEN NOT MATCHED THEN INSERT (ASSETID, CIHAZ, KABIN, VARLIK, IS_DEGERI, ERISILEBILIRLIK, GIZLILIK, BUTUNLUK, GEREKCE, KAYDEDEN)
                        VALUES (@id, @cihaz, @kabin, @v0, @v1, @v2, @v3, @v4, @gerekce, @kim);";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            if (!q.Sil)
            {
                cmd.Parameters.AddWithValue("@cihaz", c.Ad); cmd.Parameters.AddWithValue("@kabin", kabin);
                for (int i = 0; i < 5; i++) cmd.Parameters.AddWithValue($"@v{i}", (object?)d[i] ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@gerekce", q.Gerekce!.Trim()); cmd.Parameters.AddWithValue("@kim", (object?)kullanici ?? DBNull.Value);
            }
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (SqlException ex) when (ex.Number == 208) { return Results.Json(new { error = TabloYok(TElle) }, statusCode: 409); }
        if (q.Sil) s.Elle.Remove(id);
        else s.Elle[id] = new FizElle(id, c.Ad, kabin, d, q.Gerekce!.Trim(), kullanici, DateTime.Now);
        s.DegerOnbellek = null;
        log.LogInformation("Fiziksel elle değer {Islem}: {Id} {Cihaz} [{D}] — {Kim}: {Gerekce}", q.Sil ? "silindi" : "yazıldı", id, c.Ad, string.Join(",", d), kullanici, q.Gerekce);
        return Results.Ok(new { ok = true, deger = CihazDeger(s, c) });
    }

    public object ElleListe(FizikselSnapshot s) => new
    {
        durum = s.ElleDurum,
        elle = s.Elle.Values.OrderByDescending(e => e.Tarih).Select(e => new
        {
            assetId = e.AssetId, cihaz = e.Cihaz, kabin = e.Kabin, v = e.D[0], i = e.D[1], e = e.D[2], g = e.D[3], b = e.D[4], gerekce = e.Gerekce, kaydeden = e.Kaydeden, tarih = e.Tarih,
            var = s.CihazId.ContainsKey(e.AssetId),
        }).ToList(),
    };

    // ---- Kayıt (denetim dönemi) ----
    sealed record CihazSatir(string Kabin, string Oda, string Cihaz, string? AssetId, string Tur, string? Model, int?[] D, string? Yontem, string? Kaynak, string? Kural, bool Elle);
    sealed record KabinSatir(string Kabin, string Oda, int?[] D, string? Kaynak, int Cihaz, int Vm);

    (List<CihazSatir> Cihazlar, List<KabinSatir> Kabinler) GuncelSatirlar(FizikselSnapshot s)
    {
        var r = Degerler(s);
        var cl = new List<CihazSatir>(); var kl = new List<KabinSatir>();
        foreach (var kabin in s.TumKabinler.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            string oda = Parcala(kabin).Oda ?? "Diğer";
            var cs = (s.KabinCihaz.GetValueOrDefault(kabin) ?? []).Where(c => c.Aktif).OrderBy(c => c.UAlt ?? 0).ToList();
            foreach (var c in cs)
            {
                var d = r.Cihaz.GetValueOrDefault(c);
                cl.Add(new CihazSatir(kabin, oda, c.Ad, c.AssetId, c.Tur, c.Model, d?.D ?? new int?[5], d?.Yontem ?? "değer bulunamadı",
                    d == null ? null : string.Join(" | ", d.Kaynak.Where(x => x != null).Distinct().Take(3)), r.Kural.GetValueOrDefault(c)?.Ad,
                    c.AssetId != null && s.Elle.ContainsKey(c.AssetId)));
            }
            var kd = r.Kabin.GetValueOrDefault(kabin);
            kl.Add(new KabinSatir(kabin, oda, kd?.D ?? new int?[5], kd == null ? null : string.Join(" | ", kd.Kaynak.Where(x => x != null).Distinct().Take(3)),
                cs.Count, (s.KabinVm.GetValueOrDefault(kabin) ?? []).Count(v => v.Vm != null)));
        }
        return (cl, kl);
    }

    public async Task<IResult> KaydetAsync(FizikselSnapshot s, string? aciklama, string? kullanici, CancellationToken ct)
    {
        var (cl, kl) = GuncelSatirlar(s);
        string kurallar = JsonSerializer.Serialize(Kurallar());
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync(ct);
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
            int id;
            await using (var cmd = new SqlCommand($"INSERT INTO {TKayit} (KAYDEDEN, ACIKLAMA, CIHAZ_SAYISI, KABIN_SAYISI, VERI_TARIHI, KURALLAR) OUTPUT INSERTED.KAYIT_ID VALUES (@kim, @ac, @cs, @ks, @vt, @kr)", conn, tx))
            {
                cmd.Parameters.AddWithValue("@kim", (object?)kullanici ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ac", (object?)(aciklama?.Trim() is { Length: > 0 } a ? (a.Length > 500 ? a[..500] : a) : null) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@cs", cl.Count); cmd.Parameters.AddWithValue("@ks", kl.Count);
                cmd.Parameters.AddWithValue("@vt", (object?)s.Yenileme ?? DBNull.Value); cmd.Parameters.AddWithValue("@kr", kurallar);
                id = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            }
            static string? K(string? x, int n) => x == null ? null : x.Length > n ? x[..n] : x;
            var dt = new DataTable();
            foreach (var n in new[] { "KAYIT_ID", "KABIN", "ODA", "CIHAZ", "ASSETID", "TUR", "MODEL" }.Concat(BoyutKolon).Concat(["YONTEM", "KAYNAK", "KURAL", "ELLE"]))
                dt.Columns.Add(n, n == "KAYIT_ID" ? typeof(int) : BoyutKolon.Contains(n) ? typeof(byte) : n == "ELLE" ? typeof(bool) : typeof(string));
            foreach (var c in cl)
                dt.Rows.Add([id, K(c.Kabin, 200), c.Oda, K(c.Cihaz, 300), c.AssetId, c.Tur, K(c.Model, 200), .. c.D.Select(x => x is int v ? (object)(byte)v : DBNull.Value),
                    K(c.Yontem, 500), K(c.Kaynak, 1000), K(c.Kural, 200), c.Elle]);
            using (var bc = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, tx) { DestinationTableName = TCihaz, BulkCopyTimeout = 300 })
            {
                foreach (DataColumn col in dt.Columns) bc.ColumnMappings.Add(col.ColumnName, col.ColumnName);
                await bc.WriteToServerAsync(dt, ct);
            }
            var kt = new DataTable();
            foreach (var n in new[] { "KAYIT_ID", "KABIN", "ODA" }.Concat(BoyutKolon).Concat(["KAYNAK", "CIHAZ_SAYISI", "VM_SAYISI"]))
                kt.Columns.Add(n, n is "KAYIT_ID" or "CIHAZ_SAYISI" or "VM_SAYISI" ? typeof(int) : BoyutKolon.Contains(n) ? typeof(byte) : typeof(string));
            foreach (var k in kl)
                kt.Rows.Add([id, K(k.Kabin, 200), k.Oda, .. k.D.Select(x => x is int v ? (object)(byte)v : DBNull.Value), K(k.Kaynak, 1000), k.Cihaz, k.Vm]);
            using (var bc = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, tx) { DestinationTableName = TKabin, BulkCopyTimeout = 300 })
            {
                foreach (DataColumn col in kt.Columns) bc.ColumnMappings.Add(col.ColumnName, col.ColumnName);
                await bc.WriteToServerAsync(kt, ct);
            }
            await tx.CommitAsync(ct);
            log.LogInformation("Fiziksel değer kaydı {Id}: {C} cihaz, {K} kabin — {Kim}", id, cl.Count, kl.Count, kullanici);
            return Results.Ok(new { kayitId = id, cihaz = cl.Count, kabin = kl.Count });
        }
        catch (SqlException ex) when (ex.Number == 208) { return Results.Json(new { error = TabloYok(TKayit) + " (" + ex.Message + ")" }, statusCode: 409); }
    }

    public async Task<IResult> KayitlarAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand($"SELECT TOP 200 KAYIT_ID, TARIH, KAYDEDEN, ACIKLAMA, CIHAZ_SAYISI, KABIN_SAYISI, VERI_TARIHI FROM {TKayit} ORDER BY KAYIT_ID DESC", conn);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var l = new List<object>();
            while (await r.ReadAsync(ct))
                l.Add(new { kayitId = r.GetInt32(0), tarih = r.GetDateTime(1), kaydeden = r.IsDBNull(2) ? null : r.GetString(2), aciklama = r.IsDBNull(3) ? null : r.GetString(3),
                    cihaz = r.IsDBNull(4) ? 0 : r.GetInt32(4), kabin = r.IsDBNull(5) ? 0 : r.GetInt32(5), veriTarihi = r.IsDBNull(6) ? (DateTime?)null : r.GetDateTime(6) });
            return Results.Ok(new { kayitlar = l });
        }
        catch (SqlException ex) when (ex.Number == 208) { return Results.Ok(new { kayitlar = Array.Empty<object>(), durum = TabloYok(TKayit) }); }
    }

    // ---- Excel ----
    public async Task<IResult> ExcelAsync(FizikselSnapshot s, int? kayitId, string? kullanici, CancellationToken ct)
    {
        List<CihazSatir> cl; List<KabinSatir> kl; FizKural[] kurallar; string baslik;
        if (kayitId is int id)
        {
            cl = []; kl = [];
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync(ct);
            DateTime tarih; string? kim, ac, kj; DateTime? vt;
            await using (var cmd = new SqlCommand($"SELECT TARIH, KAYDEDEN, ACIKLAMA, KURALLAR, VERI_TARIHI FROM {TKayit} WHERE KAYIT_ID = @id", conn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                if (!await r.ReadAsync(ct)) return Results.NotFound(new { error = $"Kayıt bulunamadı: {id}" });
                tarih = r.GetDateTime(0); kim = r.IsDBNull(1) ? null : r.GetString(1); ac = r.IsDBNull(2) ? null : r.GetString(2); kj = r.IsDBNull(3) ? null : r.GetString(3);
                vt = r.IsDBNull(4) ? null : r.GetDateTime(4);
            }
            static int?[] Dd(SqlDataReader r, int bas) => Enumerable.Range(bas, 5).Select(i => r.IsDBNull(i) ? (int?)null : Convert.ToInt32(r.GetValue(i))).ToArray();
            static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i));
            await using (var cmd = new SqlCommand($"SELECT KABIN, ODA, CIHAZ, ASSETID, TUR, MODEL, {string.Join(", ", BoyutKolon)}, YONTEM, KAYNAK, KURAL, ELLE FROM {TCihaz} WHERE KAYIT_ID = @id ORDER BY KABIN", conn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    cl.Add(new CihazSatir(S(r, 0) ?? "", S(r, 1) ?? "", S(r, 2) ?? "", S(r, 3), S(r, 4) ?? "", S(r, 5), Dd(r, 6), S(r, 11), S(r, 12), S(r, 13), !r.IsDBNull(14) && r.GetBoolean(14)));
            }
            await using (var cmd = new SqlCommand($"SELECT KABIN, ODA, {string.Join(", ", BoyutKolon)}, KAYNAK, CIHAZ_SAYISI, VM_SAYISI FROM {TKabin} WHERE KAYIT_ID = @id ORDER BY KABIN", conn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    kl.Add(new KabinSatir(S(r, 0) ?? "", S(r, 1) ?? "", Dd(r, 2), S(r, 7), r.IsDBNull(8) ? 0 : r.GetInt32(8), r.IsDBNull(9) ? 0 : r.GetInt32(9)));
            }
            kurallar = kj != null ? JsonSerializer.Deserialize<FizKural[]>(kj) ?? [] : [];
            baslik = $"Kayıt #{id} · {tarih:dd.MM.yyyy HH:mm} · kaydeden {kim}{(ac != null ? " · " + ac : "")}{(vt != null ? $" · nlyte verisi {vt:dd.MM.yyyy HH:mm}" : "")}";
        }
        else
        {
            (cl, kl) = GuncelSatirlar(s);
            kurallar = Kurallar();
            baslik = $"Güncel hesap (kaydedilmemiş) · {DateTime.Now:dd.MM.yyyy HH:mm} · indiren {kullanici}{(s.Yenileme != null ? $" · nlyte verisi {s.Yenileme:dd.MM.yyyy HH:mm}" : "")}";
        }
        string dosya = kayitId is int k2 ? $"fiziksel_degerler_kayit{k2}.xlsx" : $"fiziksel_degerler_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
        return Results.File(ExcelYap(s, cl, kl, kurallar, baslik, kayitId != null), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", dosya);
    }

    public byte[] ExcelGuncel(FizikselSnapshot s, string? kullanici)
    {
        var (cl, kl) = GuncelSatirlar(s);
        return ExcelYap(s, cl, kl, Kurallar(), $"Güncel hesap (kaydedilmemiş) · {DateTime.Now:dd.MM.yyyy HH:mm} · indiren {kullanici}", false);
    }

    byte[] ExcelYap(FizikselSnapshot s, List<CihazSatir> cl, List<KabinSatir> kl, FizKural[] kurallar, string baslik, bool kayitli)
    {
        string[] boy = ["Varlık", "İş", "Erişilebilirlik", "Gizlilik", "Bütünlük"];
        object?[] Bd(int?[] d) => d.Select(x => (object?)x).ToArray();
        var sayfalar = new List<(string, List<object?[]>)>
        {
            ("Cihazlar", [["Kabin", "Oda", "Cihaz", "nlyte AssetID", "Tür", "Model", .. boy, "Yöntem", "En yüksek değeri veren", "Kural", "Elle"],
                .. cl.Select(c => (object?[])[c.Kabin, c.Oda, c.Cihaz, c.AssetId, c.Tur, c.Model, .. Bd(c.D), c.Yontem, c.Kaynak, c.Kural, c.Elle ? "evet" : ""])]),
            ("Kabinler", [["Kabin", "Oda", .. boy, "En yüksek değeri veren", "Cihaz", "VM"],
                .. kl.Select(k => (object?[])[k.Kabin, k.Oda, .. Bd(k.D), k.Kaynak, k.Cihaz, k.Vm])]),
            ("Kurallar", [["Sıra", "Kural", "Tür", "Model (regex)", "Ad (regex)", "Kabin adı (regex)", "Değer (V İ E G B)", "Miras", "Gerekçe"],
                .. kurallar.Select((k, i) => (object?[])[i + 1, k.Ad, k.Turler == null ? null : string.Join(", ", k.Turler), k.Model, k.Isim, k.Kabin,
                    k.Degerler == null ? null : string.Join(" ", k.Degerler), k.Miras == null ? null : $"{k.Miras}: {k.Boyutlar ?? "E"} + İş", k.Aciklama])]),
            (kayitli ? "Elle değerler (güncel)" : "Elle değerler", [["nlyte AssetID", "Cihaz", "Kabin", .. boy, "Gerekçe", "Kaydeden", "Tarih"],
                .. s.Elle.Values.OrderBy(e => e.Kabin).Select(e => (object?[])[e.AssetId, e.Cihaz, e.Kabin, .. Bd(e.D), e.Gerekce, e.Kaydeden, e.Tarih.ToString("dd.MM.yyyy HH:mm")])]),
            ("Yöntem", [["Konu", "Açıklama"],
                ["Rapor", baslik],
                ["Ölçek", "0–5. Boş hücre: o boyut için değer yok ya da cihaz için anlamsız (ör. PDU'nun gizlilik değeri)."],
                ["Uygulama değerleri", "ERT_HOSTIPADDRESS: uygulama (APPNAME) başına dolu satırların en yüksek VARLIKDEGERI / ISDEGERI / ERISILEBILIRLIKDEGERI / GIZLILIKDEGERI / BUTUNLUKDEGERI."],
                ["Türetme", "İş değeri 0 / boşsa = Erişilebilirlik; Varlık boşsa = en yüksek(Gizlilik, Bütünlük, Erişilebilirlik). ERT analizi: Varlık hiçbir uygulamada en yüksek(G, B, E)'yi geçmiyor, İş ya E'ye eşit ya 0."],
                ["ESX / sunucu", "Üzerindeki VM'lerin uygulamalarının boyut başına en yüksek değeri (MSY / ÇSY envanteri → ERT)."],
                ["VM'siz fiziksel sunucu", "Cihaz adı ERT UNIXNAME ile; MSY fiziksel envanterindeki DNS adı / IP ERT ile eşleşen uygulamaların en yüksek değeri."],
                ["Chassis", "Blade'lerinin en yüksek değeri."],
                ["Tür kuralları", "Kurallar sayfası: ilk uyan kural; hesaplanan değer ile kural değerinin boyut başına en yükseği. VM'i olan cihaza türe bağlı kural uygulanmaz."],
                ["Miras", "PDU / patch panel: kabinin erişilebilirlik ve iş değeri; KVM: kabinin gizlilik / bütünlük ve iş değeri; UPS / statik switch / klima: odanın erişilebilirlik ve iş değeri. Varlık = en yüksek(G, B, E)."],
                ["Kabin", "Kabindeki VM'lerin uygulamaları ve cihaz değerlerinin boyut başına en yükseği."],
                ["Elle değer", "DF_FIZIKSEL_DEGER_ELLE: dolu boyutlar hesaplananın yerine geçer; gerekçe, kaydeden ve tarih tutulur."],
                ["Kapsam dışı", "nlyte'ta kabine bağlı olmayan (depo / stok) cihazlar değerlendirilmez."],
                ["Kaynak", "nlyte (dbo.NlyteKabin, NlyteCihaz, NlyteChassis), KabinHostVm (MSY / ÇSY VM → host), ERT_HOSTIPADDRESS; DeltaFlow bu tabloları yalnız okur."]]),
        };
        return Xlsx.Yaz(sayfalar);
    }
}

// Dış kütüphanesiz en küçük .xlsx yazıcı: satır içi metinler (inlineStr), sayılar, ilk satır kalın + dondurulmuş + filtreli
static class Xlsx
{
    public static byte[] Yaz(List<(string Ad, List<object?[]> Satirlar)> sayfalar)
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Dosya(string ad, string icerik) { using var w = new StreamWriter(z.CreateEntry(ad, CompressionLevel.Optimal).Open(), new UTF8Encoding(false)); w.Write(icerik); }
            Dosya("[Content_Types].xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>"""
                + string.Concat(sayfalar.Select((_, i) => $"""<Override PartName="/xl/worksheets/sheet{i + 1}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>""")) + "</Types>");
            Dosya("_rels/.rels", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            Dosya("xl/workbook.xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>"""
                + string.Concat(sayfalar.Select((s, i) => $"""<sheet name="{X(s.Ad.Length > 31 ? s.Ad[..31] : s.Ad)}" sheetId="{i + 1}" r:id="rId{i + 1}"/>""")) + "</sheets></workbook>");
            Dosya("xl/_rels/workbook.xml.rels", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">"""
                + string.Concat(sayfalar.Select((_, i) => $"""<Relationship Id="rId{i + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{i + 1}.xml"/>"""))
                + $"""<Relationship Id="rId{sayfalar.Count + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""");
            Dosya("xl/styles.xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="2"><font><sz val="10"/><name val="Calibri"/></font><font><b/><sz val="10"/><name val="Calibri"/></font></fonts><fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FFDDE6F0"/></patternFill></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>""");
            for (int i = 0; i < sayfalar.Count; i++) Dosya($"xl/worksheets/sheet{i + 1}.xml", Sayfa(sayfalar[i].Satirlar));
        }
        return ms.ToArray();
    }

    static string Sayfa(List<object?[]> satirlar)
    {
        int kolon = satirlar.Count == 0 ? 1 : satirlar.Max(r => r.Length);
        var gen = Enumerable.Range(0, kolon).Select(c => Math.Clamp(satirlar.Take(500).Max(r => c < r.Length ? (r[c]?.ToString()?.Length ?? 0) : 0) + 2, 6, 70)).ToArray();
        var sb = new StringBuilder("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        sb.Append("""<sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews><cols>""");
        for (int c = 0; c < kolon; c++) sb.Append($"""<col min="{c + 1}" max="{c + 1}" width="{gen[c]}" customWidth="1"/>""");
        sb.Append("</cols><sheetData>");
        for (int r = 0; r < satirlar.Count; r++)
        {
            sb.Append($"""<row r="{r + 1}">""");
            for (int c = 0; c < satirlar[r].Length; c++)
            {
                var v = satirlar[r][c];
                if (v == null) continue;
                string adr = Harf(c) + (r + 1), st = r == 0 ? " s=\"1\"" : "";
                if (v is int or long or double or decimal or byte) sb.Append($"""<c r="{adr}"{st}><v>{Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)}</v></c>""");
                else sb.Append($"""<c r="{adr}" t="inlineStr"{st}><is><t xml:space="preserve">{X(v.ToString())}</t></is></c>""");
            }
            sb.Append("</row>");
        }
        sb.Append("</sheetData>");
        if (satirlar.Count > 1) sb.Append($"""<autoFilter ref="A1:{Harf(kolon - 1)}{satirlar.Count}"/>""");
        return sb.Append("</worksheet>").ToString();
    }

    static string Harf(int c) { string s = ""; for (c++; c > 0; c = (c - 1) / 26) s = (char)('A' + (c - 1) % 26) + s; return s; }
    // XML kaçışı + XML'de geçersiz denetim karakterleri atılır
    static string X(string? s) => SecurityElement.Escape(Regex.Replace(s ?? "", @"[\x00-\x08\x0B\x0C\x0E-\x1F]", "")) ?? "";
}
