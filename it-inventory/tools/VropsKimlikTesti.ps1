<#
.SYNOPSIS
    vROps / Aria Operations kimlik dogrulamasini test eder: hangi authSource
    degeri kabul ediliyor?

.DESCRIPTION
    Uygulama "sifre olmadi" diyorsa sebep genelde parola degil, authSource
    degeridir. Bu script aday degerleri SIRAYLA dener ve vROps'un her birine
    verdigi CEVABI OLDUGU GIBI yazar. Hicbir seyi tahmin etmez, olcer.

    Sadece okur: token almayi dener, basarili olursa token'i hemen birakir.
    Envantere, vROps'a, hicbir yere yazmaz.

    >> HESAP KILITLENMESI <<
    Parola DOGRU ise reddedilen denemeler AD'ye kadar gitmez, kilitlenme
    riski yoktur. Parolayi YANLIS girdiyseniz her deneme AD'de bir basarisiz
    giris sayilir. O yuzden script en fazla -MaxDeneme kadar (varsayilan 5)
    istek atar ve ilk basarida durur. Parolanizdan emin olmadan tekrar
    tekrar calistirmayin.

.PARAMETER VropsIp
    vROps sunucusunun IP adresi veya adi. https:// ve sondaki / olmadan.

.PARAMETER Username
    Kullanici adi. AD hesabi icin domain'siz yazin (ornek: visikhan).

.PARAMETER AuthSourceAdaylari
    Denenecek authSource degerleri. Varsayilan liste yerel hesabi, bos degeri
    ve yaygin AD adlandirmalarini kapsar. vROps giris ekranindaki kaynak
    listesinde gordugunuz metni biliyorsaniz SADECE onu verin:
        -AuthSourceAdaylari 'fintek.local'

.PARAMETER SkipCertCheck
    Self-signed sertifikayi kabul et. Sadece test icin.

.PARAMETER MaxDeneme
    En fazla kac istek atilacagi (varsayilan 5).

.EXAMPLE
    .\VropsKimlikTesti.ps1 -VropsIp 10.20.30.40 -Username visikhan -SkipCertCheck

.EXAMPLE
    .\VropsKimlikTesti.ps1 -VropsIp 10.20.30.40 -Username visikhan -AuthSourceAdaylari 'fintek.local','FINTEK'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$VropsIp,
    [Parameter(Mandatory = $true)][string]$Username,
    [string[]]$AuthSourceAdaylari = @('', 'Local', 'fintek.local', 'FINTEK'),
    [switch]$SkipCertCheck,
    [int]$MaxDeneme = 5
)

$ErrorActionPreference = 'Stop'

# PowerShell 5.1 varsayilan olarak TLS 1.0 kullanir; vROps 1.2 ister.
# Bu satir KOSULSUZ calismali -- sertifika kontrolunden bagimsiz bir konu.
[System.Net.ServicePointManager]::SecurityProtocol =
    [System.Net.SecurityProtocolType]::Tls12 -bor [System.Net.SecurityProtocolType]::Tls11

$ekstra = @{}
if ($SkipCertCheck) {
    if ($PSVersionTable.PSVersion.Major -ge 6) {
        $ekstra['SkipCertificateCheck'] = $true
    } else {
        Add-Type @"
using System.Net;
using System.Security.Cryptography.X509Certificates;
public class HepsiniKabulEt {
    public static void Kur() {
        ServicePointManager.ServerCertificateValidationCallback =
            delegate { return true; };
    }
}
"@
        [HepsiniKabulEt]::Kur()
    }
}

$taban = "https://$VropsIp/suite-api/api"

Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host " vROps kimlik testi" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host " Sunucu      : $VropsIp"
Write-Host " Kullanici   : $Username"
Write-Host " Adaylar     : $($AuthSourceAdaylari.Count) adet, en fazla $MaxDeneme deneme"
Write-Host " Sertifika   : $(if ($SkipCertCheck) { 'dogrulama ATLANIYOR' } else { 'dogrulaniyor' })"
Write-Host " PowerShell  : $($PSVersionTable.PSVersion)"
Write-Host ""
Write-Host " Parola DOGRU ise reddedilen denemeler AD'ye gitmez." -ForegroundColor DarkGray
Write-Host " Parolanizdan emin degilseniz bu scripti tekrar tekrar" -ForegroundColor DarkGray
Write-Host " calistirmayin: kilitlenme riski olur." -ForegroundColor DarkGray
Write-Host ""

$kimlik = Get-Credential -UserName $Username `
    -Message "vROps parolasi ($Username)"
$parola = [System.Net.NetworkCredential]::new('', $kimlik.Password).Password

$sonuclar = @()
$kazanan  = $null
$deneme   = 0

try {
    foreach ($kaynak in $AuthSourceAdaylari) {

        if ($deneme -ge $MaxDeneme) {
            Write-Host "  -- deneme siniri ($MaxDeneme) doldu, duruyorum." -ForegroundColor Yellow
            break
        }
        $deneme++

        $etiket = if ([string]::IsNullOrEmpty($kaynak)) { '(bos / gonderilmiyor)' } else { $kaynak }
        Write-Host ("[{0}/{1}] authSource = {2}" -f $deneme, $MaxDeneme, $etiket) -ForegroundColor White

        # authSource bos ise alani HIC gondermiyoruz -- bos string gondermekle
        # ayni sey degil, vROps ikisine farkli davranabiliyor.
        if ([string]::IsNullOrEmpty($kaynak)) {
            $govde = @{ username = $kimlik.UserName; password = $parola } | ConvertTo-Json
        } else {
            $govde = @{ username = $kimlik.UserName; password = $parola; authSource = $kaynak } | ConvertTo-Json
        }

        $durum = ''
        $mesaj = ''
        try {
            $cevap = Invoke-RestMethod -Method Post -Uri "$taban/auth/token/acquire" `
                -Body $govde -ContentType 'application/json' `
                -Headers @{ Accept = 'application/json' } @ekstra

            if ($cevap.token) {
                $durum = 'BASARILI'
                $mesaj = "token alindi, gecerlilik: $($cevap.validity)"
                Write-Host "        >> BASARILI. Dogru deger bu." -ForegroundColor Green
                Write-Host "        >> token: $($cevap.token.Substring(0,[Math]::Min(12,$cevap.token.Length)))..." -ForegroundColor DarkGray
                $kazanan = $kaynak
                $sonuclar += [pscustomobject]@{ AuthSource = $etiket; Sonuc = $durum; Aciklama = $mesaj }
                break
            }

            $durum = 'TOKEN YOK'
            $mesaj = ($cevap | ConvertTo-Json -Depth 3 -Compress)
            Write-Host "        $durum -- $mesaj" -ForegroundColor Yellow
        }
        catch {
            $yanit = $null
            try { $yanit = $_.Exception.Response } catch { }

            if ($yanit) {
                $kod = 0
                try { $kod = [int]$yanit.StatusCode } catch { }
                $durum = "HTTP $kod"
                try {
                    $okuyucu = New-Object System.IO.StreamReader($yanit.GetResponseStream())
                    $mesaj = $okuyucu.ReadToEnd()
                    $okuyucu.Close()
                } catch {
                    $mesaj = '(cevap govdesi okunamadi)'
                }
            } else {
                $durum = 'BAGLANTI HATASI'
                $mesaj = $_.Exception.Message
            }

            # vROps'un kendi aciklamasi burada. Kirpmiyoruz, aynen yaziyoruz.
            Write-Host "        $durum" -ForegroundColor Red
            if ($mesaj) {
                foreach ($satir in ($mesaj -split "`n")) {
                    Write-Host "        | $satir" -ForegroundColor DarkGray
                }
            }
        }

        $sonuclar += [pscustomobject]@{ AuthSource = $etiket; Sonuc = $durum; Aciklama = $mesaj }
        Write-Host ""
    }
}
finally {
    # Parolayi bellekte tutma.
    $parola = $null
    [System.GC]::Collect()

    Write-Host ""
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host " OZET" -ForegroundColor Cyan
    Write-Host "================================================================" -ForegroundColor Cyan
    $sonuclar | Format-Table AuthSource, Sonuc -AutoSize | Out-String | Write-Host

    if ($kazanan -ne $null) {
        $yazilacak = if ([string]::IsNullOrEmpty($kazanan)) { '(bos birakin)' } else { $kazanan }
        Write-Host " appsettings.json icine yazin:" -ForegroundColor Green
        Write-Host ""
        Write-Host "   `"AuthSource`": `"$yazilacak`"," -ForegroundColor Green
        Write-Host "   `"Username`":   `"$($kimlik.UserName)`"," -ForegroundColor Green
        Write-Host ""
        if ([string]::IsNullOrEmpty($kazanan)) {
            Write-Host " DIKKAT: basarili olan deger BOS. Kodumuz authSource alanini" -ForegroundColor Yellow
            Write-Host " her zaman gonderiyor; bu durumda VropsService.cs duzeltilmeli." -ForegroundColor Yellow
            Write-Host " Bu ciktiyi bana gonderin, degisikligi yapayim." -ForegroundColor Yellow
        }
    } else {
        Write-Host " Hicbir aday kabul edilmedi." -ForegroundColor Red
        Write-Host ""
        Write-Host " SIRADA NE YAPMALI:" -ForegroundColor Yellow
        Write-Host " 1) vROps web arayuzune girin. Kullanici adi/parola alanlarinin" -ForegroundColor Yellow
        Write-Host "    altindaki KAYNAK (source/domain) listesine bakin." -ForegroundColor Yellow
        Write-Host " 2) Orada AD icin ne seciyorsaniz, o metni birebir verin:" -ForegroundColor Yellow
        Write-Host "      .\VropsKimlikTesti.ps1 -VropsIp $VropsIp -Username $Username ``" -ForegroundColor Yellow
        Write-Host "         -AuthSourceAdaylari 'ORADAKI_METIN'" -ForegroundColor Yellow
        Write-Host " 3) Yukaridaki gri satirlar vROps'un kendi aciklamasidir." -ForegroundColor Yellow
        Write-Host "    Onlari bana gonderin." -ForegroundColor Yellow
    }
    Write-Host ""
}
