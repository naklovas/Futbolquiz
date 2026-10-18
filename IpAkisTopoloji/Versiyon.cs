using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

// ---------------------------------------------------------------------------
// Topoloji versiyonları: bir etiketin işlev haritası (kümeleme) ve küme topolojisi birlikte kaydedilir; her etiket için son
// Islev:VersiyonSayisi (3) tutulur, eskiler silinir. Sayfada versiyon seçilince ikisi de kayıttan yüklenir (trafik yeniden
// sorgulanmaz). Tablo Islev:VersiyonTablosu (varsayılan dbo.DF_TOPOLOJI_VERSIYON, sql/etiket_versiyon.sql). JSON'lar
// arayüzün gördüğü haliyle (api/islev ve api/islev/topoloji yanıtları) saklanır.
// ---------------------------------------------------------------------------
record VersiyonOzet(int Id, string Etiket, string? Kurum, string? Aciklama, string? Parametre, string Kaydeden, DateTime Tarih,
    int Sunucu, int Kume);
record VersiyonKaydet(string? Etiket, string? Kurum, string? Aciklama, JsonElement Parametre, JsonElement Harita, JsonElement Topoloji);

static class VersiyonService
{
    static readonly Regex SafeTable = new(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*){0,2}$");

    static string Table(IConfiguration cfg)
    {
        string t = cfg["Islev:VersiyonTablosu"] ?? "dbo.DF_TOPOLOJI_VERSIYON";
        return SafeTable.IsMatch(t) ? t : "dbo.DF_TOPOLOJI_VERSIYON";
    }
    public static int Keep(IConfiguration cfg) => Math.Clamp(cfg.GetValue("Islev:VersiyonSayisi", 3), 1, 20);

    static async Task<SqlConnection> OpenAsync(IConfiguration cfg, CancellationToken ct)
    {
        var conn = new SqlConnection(cfg.GetConnectionString("Envanter") ?? throw new InvalidOperationException("ConnectionStrings:Envanter tanımlı değil."));
        await conn.OpenAsync(ct);
        return conn;
    }
    static InvalidOperationException Missing(IConfiguration cfg) =>
        new($"Versiyon tablosu ({Table(cfg)}) yok; sql/etiket_versiyon.sql betiğini çalıştırın.");

    public static async Task<List<VersiyonOzet>> ListAsync(IConfiguration cfg, string tag, CancellationToken ct)
    {
        var list = new List<VersiyonOzet>();
        try
        {
            await using var conn = await OpenAsync(cfg, ct);
            await using var cmd = new SqlCommand($"""
                SELECT ID, ETIKET, KURUM, ACIKLAMA, PARAMETRE, KAYDEDEN, TARIH, SUNUCU, KUME
                FROM {Table(cfg)} WHERE ETIKET = @e ORDER BY TARIH DESC, ID DESC
                """, conn);
            cmd.Parameters.AddWithValue("@e", tag);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
            while (await r.ReadAsync(ct))
                list.Add(new VersiyonOzet(r.GetInt32(0), r.GetString(1), S(2), S(3), S(4), S(5) ?? "?", r.GetDateTime(6),
                    r.IsDBNull(7) ? 0 : r.GetInt32(7), r.IsDBNull(8) ? 0 : r.GetInt32(8)));
        }
        catch (SqlException ex) when (ex.Number == 208) { throw Missing(cfg); }
        return list;
    }

    // Döner: (harita JSON, topoloji JSON, özet) — JSON'lar ham metin, yanıta olduğu gibi gömülür
    public static async Task<(string Harita, string Topoloji, VersiyonOzet Ozet)?> GetAsync(IConfiguration cfg, int id, CancellationToken ct)
    {
        try
        {
            await using var conn = await OpenAsync(cfg, ct);
            await using var cmd = new SqlCommand($"""
                SELECT ID, ETIKET, KURUM, ACIKLAMA, PARAMETRE, KAYDEDEN, TARIH, SUNUCU, KUME, HARITA, TOPOLOJI
                FROM {Table(cfg)} WHERE ID = @id
                """, conn);
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;
            string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
            var oz = new VersiyonOzet(r.GetInt32(0), r.GetString(1), S(2), S(3), S(4), S(5) ?? "?", r.GetDateTime(6),
                r.IsDBNull(7) ? 0 : r.GetInt32(7), r.IsDBNull(8) ? 0 : r.GetInt32(8));
            return (S(9) ?? "null", S(10) ?? "null", oz);
        }
        catch (SqlException ex) when (ex.Number == 208) { throw Missing(cfg); }
    }

    // Kaydeder, sonra etiketin en yeni Keep(cfg) versiyonu dışındakileri siler. Döner: yeni ID.
    public static async Task<int> SaveAsync(IConfiguration cfg, string tag, VersiyonKaydet req, string user, ILogger log, CancellationToken ct)
    {
        if (req.Harita.ValueKind != JsonValueKind.Object || req.Topoloji.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Harita ve topoloji verisi gerekli.");
        int sunucu = req.Harita.TryGetProperty("toplam", out var t) && t.TryGetInt32(out int n) ? n : 0;
        int kume = req.Harita.TryGetProperty("kumeler", out var k) && k.ValueKind == JsonValueKind.Array ? k.GetArrayLength() : 0;
        string? Cut(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length > max ? s.Trim()[..max] : s.Trim();
        try
        {
            await using var conn = await OpenAsync(cfg, ct);
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
            int id;
            await using (var cmd = new SqlCommand($"""
                INSERT INTO {Table(cfg)} (ETIKET, KURUM, ACIKLAMA, PARAMETRE, KAYDEDEN, TARIH, SUNUCU, KUME, HARITA, TOPOLOJI)
                OUTPUT INSERTED.ID VALUES (@e, @k, @a, @p, @u, SYSDATETIME(), @s, @c, @h, @t)
                """, conn, tx))
            {
                cmd.Parameters.AddWithValue("@e", tag);
                cmd.Parameters.AddWithValue("@k", (object?)Cut(req.Kurum, 1000) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@a", (object?)Cut(req.Aciklama, 500) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@p", req.Parametre.ValueKind == JsonValueKind.Object ? Cut(req.Parametre.GetRawText(), 1000)! : DBNull.Value);
                cmd.Parameters.AddWithValue("@u", user);
                cmd.Parameters.AddWithValue("@s", sunucu);
                cmd.Parameters.AddWithValue("@c", kume);
                cmd.Parameters.Add("@h", System.Data.SqlDbType.NVarChar, -1).Value = req.Harita.GetRawText();
                cmd.Parameters.Add("@t", System.Data.SqlDbType.NVarChar, -1).Value = req.Topoloji.GetRawText();
                id = (int)(await cmd.ExecuteScalarAsync(ct))!;
            }
            int removed;
            await using (var cmd = new SqlCommand($"""
                DELETE FROM {Table(cfg)} WHERE ETIKET = @e AND ID NOT IN
                  (SELECT TOP (@n) ID FROM {Table(cfg)} WHERE ETIKET = @e ORDER BY TARIH DESC, ID DESC)
                """, conn, tx))
            {
                cmd.Parameters.AddWithValue("@e", tag);
                cmd.Parameters.AddWithValue("@n", Keep(cfg));
                removed = await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            log.LogInformation("Versiyon: {User} {Tag} kaydedildi (#{Id}, {Sunucu} sunucu, {Kume} küme; {Removed} eski silindi)", user, tag, id, sunucu, kume, removed);
            return id;
        }
        catch (SqlException ex) when (ex.Number == 208) { throw Missing(cfg); }
    }

    public static async Task<bool> DeleteAsync(IConfiguration cfg, int id, string user, ILogger log, CancellationToken ct)
    {
        try
        {
            await using var conn = await OpenAsync(cfg, ct);
            await using var cmd = new SqlCommand($"DELETE FROM {Table(cfg)} WHERE ID = @id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            bool ok = await cmd.ExecuteNonQueryAsync(ct) > 0;
            if (ok) log.LogInformation("Versiyon: {User} #{Id} sildi", user, id);
            return ok;
        }
        catch (SqlException ex) when (ex.Number == 208) { throw Missing(cfg); }
    }
}
