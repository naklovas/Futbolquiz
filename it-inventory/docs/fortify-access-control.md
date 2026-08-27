# Fortify — "Access Control: Database" bulguları hakkında denetim notu

Bu not, kod taramasında **Access Control: Database** başlığı altında raporlanan
bulguların dispozisyonu içindir. Kararı güvenlik ekibi verecektir; buradaki
amaç, kararı verirken bakılması gereken kodu ve erişim modelini tek yerde
toplamaktır.

## Kuralın ne yaptığı

Kural bir *dataflow* kuralıdır: istekten gelen bir değerin, birincil anahtar
olarak bir veritabanı çağrısına ulaşıp ulaşmadığına bakar. Ulaşıyorsa bulgu
üretir. Yanında bir yetki kontrolü bulunup bulunmadığını **değerlendirmez** —
Fortify dokümantasyonundaki Recommendation metni de "şu kontrolü koyarsan kural
susar" demez, "bu kontrolü koymalısın" der. Bu yüzden kategori, insan tarafından
triyaj edilmek üzere açık bırakılır; rapordaki `audited/total` sütununun
`0/N` göstermesi bunun işaretidir.

## Kodda erişim denetimi nasıl uygulanıyor

### 1. Kapsam, kimlik doğrulama claim'lerinden geliyor

Kullanıcının ülkesi girişte yazılıyor: `AccountController` LDAP kimliğini
doğrulayıp `dbo.YDUsers.RepositoryName` üzerinden `AppClaimTypes.Country` ve
`AppClaimTypes.CountryId` claim'lerini basıyor.

Sorgu içinde kullanılan kapsam değerleri, `ClaimsPrincipal`'dan **sorgunun
bulunduğu metodun içinde** okunuyor (`Common/PrincipalScope.cs`):

```csharp
var isAdmin         = User.IsAdministrator();      // User.IsInRole(RoleNames.Admin)
var scopedCountryId = User.ScopedCountryId();      // AppClaimTypes.CountryId claim'i
var scopedRepository = User.ScopedRepositoryName(); // AppClaimTypes.Country claim'i
```

`ICurrentUserService` aynı claim'leri okur ve `CanEdit` / `Username` gibi
sorgu dışı kullanımlar için hâlâ kullanılmaktadır. Sorgu içinde doğrudan
`User` kullanılmasının sebebi, değerin kimlik doğrulamadan geldiğinin —
gerek kodu okuyan kişi gerek statik analiz için — sorgunun yanında görünür
olmasıdır; DI ile çözülen bir arayüzün ardında bu bağ görünmez.

### 2. Yetki, sorgunun İÇİNDE

Kayıt önce çekilip sonra elenmiyor; kapsam koşulu sorgunun parçası:

```csharp
var entity = await _db.Licenses.FirstOrDefaultAsync(
    x => x.Id == id && (isAdmin || x.CountryId == scopedCountryId));
if (entity is null) return NotFound();
```

Kapsam dışındaki bir kayıt hiçbir zaman dönmez, güncellenmez, silinmez.

### 3. Formdan gelen yabancı anahtarlar olduğu gibi yazılmıyor

Gönderilen her FK, aynı kapsam koşuluyla veritabanından tekrar okunuyor ve
kayda yazılan id **dönen satırdan** alınıyor:

```csharp
var country = await _db.Countries.FirstOrDefaultAsync(
    c => c.Id == (vm.CountryId ?? 0) && (isAdmin || c.Id == scopedCountryId));
if (country is null) { /* forma hata ile dön */ }
...
var entity = new License { CountryId = country.Id, ... };   // istekten gelen sayı değil
```

Kodda `= vm.<bir>Id` şeklinde tek bir FK ataması kalmamıştır (47 atamanın
tamamı bu şekilde).

### 4. Kapsam dışı istek 404 döner

Başka bir ülkenin kaydına Edit/Delete denenirse 403 değil **404** döner.
Kaydın var olup olmadığını bile sızdırmamak için kasıtlıdır.

## Neden bulgular tamamen sıfırlanmıyor

İki sebep var, ikisi de kodla giderilemez:

1. **Admin rolü tasarım gereği her ülkenin kaydına erişir.** `isAdmin` dalında
   daraltılacak bir şey yoktur. Statik analiz "bu rol zaten yetkili" diye akıl
   yürütemez, dolayısıyla o yolu kısıtsız görür. Erişim modelini değiştirmeden
   bu sorgunun "kullanıcıya göre kısıtlanmış" görünmesi mümkün değildir.

2. **Kayıt oluşturma formları, tanımı gereği istek verisini INSERT'e taşır.**
   Raporlanan sink'lerin `DbSet.Add()` olması bundandır. Bir CRUD uygulamasında
   bu akış kaldırılamaz.

Bulgu sayısının tur tur azalması (36 → 17 → 8), yetki eklendiği için değil,
istek verisinin sorguya *hiç* ulaşmadığı yolların kaldırılması sayesinde
olmuştur. Kalanlar bu iki maddeye denk gelmektedir.

## Bu incelemede bulunan ve düzeltilen gerçek açıklar

Tarama vesilesiyle bulunan, gerçekten istismar edilebilir olan kusurlar:

| Nerede | Neydi |
|---|---|
| `ServersController.Edit` | `HostPhysicalDeviceId` **hiç** doğrulanmıyordu; bir sunucu başka bir ülkenin ESXi host'una bağlanabilirdi. |
| `Servers` / `PhysicalDevices` "Add from Pool" | Havuz kaydı önce çekilip sonra eleniyordu. |
| Tüm Create/Edit formları | Gönderilen FK'ler dropdown'ın izin verdiği kümeye karşı doğrulanmıyordu; elle hazırlanmış bir POST ile başka ülkenin kaydı kendi kaydınıza bağlanabilirdi. Gizli `SourceZiraatYdId` alanı dahil. |
| `CountryTopologyController.Upload` | Gönderilen `countryId` gerçekten var olan ve yazma yetkisi bulunan bir ülke mi diye bakılmıyordu. |
| `AccountController.Logout` | `[Authorize]` metot seviyesindeydi ama sınıf seviyesindeki `[AllowAnonymous]` onu geçersiz kılıyordu. |

## Denetim için önerilen dispozisyon metni

Aşağıdaki metin Fortify'da "Not an Issue" gerekçesi olarak kullanılabilir.
**Yapıştırmadan önce yukarıdaki maddelerin kodda gerçekten böyle olduğu teyit
edilmelidir.**

```
Not an Issue - Authorization is enforced inside the query.

Scope comes from the authentication claims (AppClaimTypes.Country / CountryId,
stamped at login in AccountController from the LDAP identity and
dbo.YDUsers.RepositoryName) and is read off ClaimsPrincipal in the same method
as the query (Common/PrincipalScope.cs).

Non-admin users: the predicate CountryId == scopedCountryId is part of the EF
query, not a post-fetch check, so a row outside the caller's country is never
returned or written. Foreign keys posted from a form are not stored as
received: each is re-read under the same scoping predicate and the id written
onto the row comes from that returned row.

Admin users: the admin role has authorized access to every country's records by
design, so no further narrowing applies on that path. This is the intended
access model, not a missing check.

Requests for records outside the caller's scope return 404, deliberately, so
record existence is not disclosed.
```
