<#
.SYNOPSIS
    Bir VM ve bir ESXi host icin vROps'ta HANGI ALANLAR VAR, onlari doker.

.DESCRIPTION
    "Envantere ekle" formunu on-doldurmak icin vROps'taki property adlarini
    bilmek gerekiyor. Bu adlar surume gore degisiyor ve tahmin edilemiyor --
    nitekim relationshipType=DESCENDANT tahmini HTTP 400 dondurdu. Bu script
    tahmin etmek yerine OLCUYOR: adi verilen VM ve host icin vROps'un
    dondurdugu tum property'leri yazar.

    Ciktinin tamami bir dosyaya yazilir; ekranda ise form alanlarina
    karsilik gelmesi muhtemel olanlar ayrica listelenir.

    SALT OKUNUR. Hicbir yere yazmaz, envantere dokunmaz.

.PARAMETER VropsIp
    vROps sunucusu. https:// ve sondaki / olmadan.

.PARAMETER Username
    vROps kullanici adi.

.PARAMETER AuthSource
    Kimlik kaynagi. BOS BIRAKIN -- bu kurulumda yerel hesaplar alan
    gonderilmediginde kabul ediliyor.

.PARAMETER VmAdi
    Ornek alinacak bir sanal makinenin adi. VERMEYE GEREK YOK: bos
    birakirsaniz script ilk uygun VM'leri kendisi secer.

.PARAMETER HostAdi
    Ornek alinacak bir ESXi host'un adi. Bos birakilabilir.

.PARAMETER OrnekSayisi
    Ad verilmediginde kac ornek alinacagi (varsayilan 3). Birden fazla
    ornek lazim, cunku property ADLARI her kaynakta ayni ama DEGERLERI
    degisiyor: VMware Tools kurulu olmayan bir VM'de konuk IP'si bos
    gelir. Hangi alanlarin gercekten dolu oldugunu ancak birkac orneğe
    bakarak anlariz.

.PARAMETER SkipCertCheck
    Self-signed sertifikayi kabul et.

.EXAMPLE
    .\VropsAlanKesif.ps1 -VropsIp 10.6.18.155 -Username KDYAPI -SkipCertCheck

.EXAMPLE
    .\VropsAlanKesif.ps1 -VropsIp 10.6.18.155 -Username KDYAPI `
        -VmAdi ZALG-APP-01 -HostAdi esx-alg-01 -SkipCertCheck
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$VropsIp,
    [Parameter(Mandatory = $true)][string]$Username,
    [string]$AuthSource = '',
    [string]$VmAdi,
    [string]$HostAdi,
    [int]$OrnekSayisi = 3,
    [switch]$SkipCertCheck,
    [string]$OutFile = "vrops-alanlar-$(Get-Date -Format yyyyMMdd-HHmmss).txt"
)

$ErrorActionPreference = 'Stop'

# PowerShell 5.1 varsayilan olarak TLS 1.0 kullanir; vROps 1.2 ister.
[System.Net.ServicePointManager]::SecurityProtocol =
    [System.Net.SecurityProtocolType]::Tls12 -bor [System.Net.SecurityProtocolType]::Tls11

$ekstra = @{}
if ($SkipCertCheck) {
    if ($PSVersionTable.PSVersion.Major -ge 6) {
        $ekstra['SkipCertificateCheck'] = $true
    } else {
        Add-Type @"
using System.Net;
public class SertifikaKabul {
    public static void Kur() {
        ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
    }
}
"@
        [SertifikaKabul]::Kur()
    }
}

$taban = "https://$VropsIp/suite-api/api"
$sb    = New-Object System.Text.StringBuilder

function Say {
    param([string]$Metin, [string]$Renk = 'Gray')
    Write-Host $Metin -ForegroundColor $Renk
    [void]$sb.AppendLine($Metin)
}

function Baslik {
    param([string]$Metin)
    Say ""
    Say ("=" * 70) 'Cyan'
    Say $Metin 'Cyan'
    Say ("=" * 70) 'Cyan'
}

# Form alanlarina karsilik gelmesi muhtemel property adlari.
# Bu liste sadece EKRANDA one cikarmak icin; dosyaya her sey yaziliyor.
$ilgiKaliplari = @(
    'ip', 'address', 'adres',
    'guest', 'os', 'operating',
    'host', 'name', 'dns',
    'serial', 'seri',
    'vendor', 'manufacturer', 'brand',
    'model', 'hardware',
    'version', 'build',
    'cpu', 'mem',
    'parent', 'cluster', 'datacenter',
    'power', 'state'
)

try {
    Say "vROps alan kesfi - $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
    Say "Sunucu     : $VropsIp"
    Say "Kullanici  : $Username"
    Say "AuthSource : $(if ([string]::IsNullOrWhiteSpace($AuthSource)) { '(gonderilmiyor)' } else { $AuthSource })"
    Say "PowerShell : $($PSVersionTable.PSVersion)"

    # ---------------------------------------------------------------- token
    $kimlik = Get-Credential -UserName $Username -Message "vROps parolasi ($Username)"
    $parola = [System.Net.NetworkCredential]::new('', $kimlik.Password).Password

    if ([string]::IsNullOrWhiteSpace($AuthSource)) {
        $govde = @{ username = $kimlik.UserName; password = $parola } | ConvertTo-Json
    } else {
        $govde = @{ username = $kimlik.UserName; password = $parola; authSource = $AuthSource } | ConvertTo-Json
    }

    try {
        $tokenCevap = Invoke-RestMethod -Method Post -Uri "$taban/auth/token/acquire" `
            -Body $govde -ContentType 'application/json' `
            -Headers @{ Accept = 'application/json' } @ekstra
    } finally {
        $parola = $null; [System.GC]::Collect()
    }

    if (-not $tokenCevap.token) { throw "Token alinamadi." }
    Say ""
    Say "TOKEN ALINDI." 'Green'

    $authBasliklari = @{
        Authorization = "vRealizeOpsToken $($tokenCevap.token)"
        Accept        = 'application/json'
    }

    function Cagir {
        param([string]$Yol)
        return Invoke-RestMethod -Method Get -Uri "$taban$Yol" -Headers $authBasliklari @ekstra
    }

    # ------------------------------------------------------- kaynagi bulma
    function KaynakBul {
        param([string]$Ad, [string]$Tur)
        $kacis = [System.Uri]::EscapeDataString($Ad)
        $cevap = Cagir "/resources?resourceKind=$Tur&name=$kacis"
        $tam = $cevap.resourceList | Where-Object { $_.resourceKey.name -eq $Ad } | Select-Object -First 1
        if ($tam) { return $tam }
        # Tam esleme yoksa ilk sonucu veriyoruz, adini ekrana yaziyoruz.
        return $cevap.resourceList | Select-Object -First 1
    }

    # Ad verilmediyse ORNEGI KENDIMIZ SECIYORUZ. Aria Pool henuz veri
    # getirmiyorsa kullanicinin elinde ad olmuyor; ad istemek onu cikmaz
    # sokaga sokuyordu.
    #
    # vCLS-* makinelerini atliyoruz: onlar vSphere'in kendi yarattigi
    # yardimci makineler, konuk IP'si ve isletim sistemi hic gelmiyor,
    # yani alanlarin dolu olup olmadigini onlardan anlayamayiz.
    function OrnekSec {
        param([string]$Tur, [int]$Adet)

        $cevap = Cagir "/resources?resourceKind=$Tur&page=0&pageSize=50"
        $hepsi = @($cevap.resourceList)

        if ($Tur -eq 'VirtualMachine') {
            $hepsi = @($hepsi | Where-Object { "$($_.resourceKey.name)" -notlike 'vCLS-*' })
        }

        return @($hepsi | Select-Object -First $Adet)
    }

    function AlanlariDok {
        param($Kaynak, [string]$Tur, [string]$Etiket)

        $Ad = $Kaynak.resourceKey.name
        Baslik "$Etiket : $Ad   (resourceKind=$Tur)"

        Say "  Identifier : $($Kaynak.identifier)"
        Say ""

        # --- property'ler
        $props = $null
        try {
            $props = Cagir "/resources/$($Kaynak.identifier)/properties"
        } catch {
            Say "  properties cagrisi basarisiz: $($_.Exception.Message)" 'Red'
        }

        $liste = @()
        if ($props) {
            # Surume gore 'property' ya da 'values' gelebiliyor; ikisini de deniyoruz.
            if ($props.property) { $liste = $props.property }
            elseif ($props.values) { $liste = $props.values }
        }

        if ($liste.Count -eq 0) {
            Say "  Hic property donmedi." 'Yellow'
        } else {
            Say "  $($liste.Count) property dondu." 'Green'
            Say ""
            Say "  --- FORM ALANLARI ICIN MUHTEMEL OLANLAR ---" 'Yellow'

            $onemli = $liste | Where-Object {
                $ad = "$($_.name)".ToLower()
                $eslesti = $false
                foreach ($k in $ilgiKaliplari) { if ($ad -like "*$k*") { $eslesti = $true; break } }
                $eslesti -and -not [string]::IsNullOrWhiteSpace("$($_.value)")
            }

            if ($onemli.Count -eq 0) {
                Say "  (kalip eslesmedi -- asagidaki tam listeye bakin)" 'DarkGray'
            } else {
                foreach ($p in ($onemli | Sort-Object name)) {
                    Say ("    {0,-52} = {1}" -f $p.name, $p.value)
                }
            }

            Say ""
            Say "  --- TAM LISTE (dosyada) ---" 'DarkGray'
            foreach ($p in ($liste | Sort-Object name)) {
                [void]$sb.AppendLine(("    {0,-52} = {1}" -f $p.name, $p.value))
            }
            Write-Host "    ($($liste.Count) satir dosyaya yazildi)" -ForegroundColor DarkGray
        }

        # --- VM ise: hangi host'ta calisiyor (PARENT ile)
        if ($Tur -eq 'VirtualMachine') {
            Say ""
            Say "  --- PARENT ILISKILERI (hangi host / cluster) ---" 'Yellow'
            try {
                $ebeveynler = Cagir "/resources/$($Kaynak.identifier)/relationships?relationshipType=PARENT"
                foreach ($e in $ebeveynler.resourceList) {
                    Say ("    {0,-28} {1}" -f $e.resourceKey.resourceKindKey, $e.resourceKey.name)
                }
            } catch {
                Say "    PARENT cagrisi basarisiz: $($_.Exception.Message)" 'Red'
            }
        }
    }

    # ------------------------------------------------------- ornekleri topla
    $vmler = @()
    if (-not [string]::IsNullOrWhiteSpace($VmAdi)) {
        $bulunan = KaynakBul -Ad $VmAdi -Tur 'VirtualMachine'
        if ($bulunan) { $vmler = @($bulunan) }
        else { Say ""; Say "'$VmAdi' adinda bir VM bulunamadi, ornegi kendim seciyorum." 'Yellow' }
    }
    if ($vmler.Count -eq 0) { $vmler = OrnekSec -Tur 'VirtualMachine' -Adet $OrnekSayisi }

    $hostlar = @()
    if (-not [string]::IsNullOrWhiteSpace($HostAdi)) {
        $bulunan = KaynakBul -Ad $HostAdi -Tur 'HostSystem'
        if ($bulunan) { $hostlar = @($bulunan) }
        else { Say ""; Say "'$HostAdi' adinda bir host bulunamadi, ornegi kendim seciyorum." 'Yellow' }
    }
    if ($hostlar.Count -eq 0) { $hostlar = OrnekSec -Tur 'HostSystem' -Adet $OrnekSayisi }

    Say ""
    Say "Ornek sayisi: $($vmler.Count) VM, $($hostlar.Count) host" 'Green'

    foreach ($vm in $vmler)   { AlanlariDok -Kaynak $vm   -Tur 'VirtualMachine' -Etiket 'SANAL MAKINE' }
    foreach ($h  in $hostlar) { AlanlariDok -Kaynak $h    -Tur 'HostSystem'     -Etiket 'ESXi HOST' }

    Baslik "BITTI"
    Say "Tam ciktiyi bana gonderin: $OutFile" 'Green'
    Say "Form alanlarini hangi property'den dolduracagimizi ona gore yazacagim." 'Green'
}
catch {
    Say ""
    Say "HATA: $($_.Exception.Message)" 'Red'
    $yanit = $null
    try { $yanit = $_.Exception.Response } catch { }
    if ($yanit) {
        try {
            $kod = [int]$yanit.StatusCode
            Say "HTTP : $kod" 'Red'
            $okuyucu = New-Object System.IO.StreamReader($yanit.GetResponseStream())
            Say "Cevap: $($okuyucu.ReadToEnd())" 'DarkGray'
            $okuyucu.Close()
        } catch { }
    }
}
finally {
    # Ne olursa olsun dosyayi yaz -- ilk surumde bunu yapmayi atlayip
    # cikti kaybetmistik.
    $sb.ToString() | Out-File -FilePath $OutFile -Encoding UTF8
    Write-Host ""
    Write-Host "Cikti dosyasi: $OutFile" -ForegroundColor Cyan
}
