<#
.SYNOPSIS
    vROps / Aria Operations KESIF scripti. Uretim kodu DEGILDIR.
    Amaci tek sey: veriye bakip tasarim sorularini cevaplamak.

.DESCRIPTION
    Bu script hicbir sey yazmaz, sadece okur. Calistirdiktan sonra olusan
    cikti dosyasini (gozden gecirip) paylasirsaniz entegrasyon tasarimini
    gercek veriye gore yapabiliriz.

    CEVAP ARADIGIMIZ SORULAR
      1. API yolu dogru mu, hangi surum?
      2. Host ve VM listeleri nasil geliyor?
      3. Bir VM'in hangi host'ta calistigi nereden okunuyor?
      4. >> EN ONEMLISI: bir VM'in hangi SUBEYE/ULKEYE ait oldugunu
         ne belirliyor? Datacenter mi, cluster mi, custom tag mi?
         Bu cevap alinmadan veri envantere giremez.

    GUVENLIK
      - Parola ekrana yazilmaz, diske yazilmaz, cikti dosyasina girmez.
      - Script hicbir yere veri gondermez; sadece yerel dosya olusturur.
      - Salt okunur bir hesapla calistirin.

.PARAMETER VropsHost
    vROps/Aria sunucusunun adi, ornegin vrops.fintek.local
    (https:// yazmayin, sadece host adi.)

.PARAMETER AuthSource
    Kimlik kaynagi. Yerel hesap icin "Local" (varsayilan), AD hesabi icin
    domain adi, ornegin "fintek.local".

.PARAMETER SkipCertCheck
    Sertifika self-signed ise bunu ekleyin.

.EXAMPLE
    .\VropsKesif.ps1 -VropsHost vrops.fintek.local -SkipCertCheck

.EXAMPLE
    .\VropsKesif.ps1 -VropsHost vrops.fintek.local -AuthSource fintek.local
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$VropsHost,

    [string]$AuthSource = '',

    [switch]$SkipCertCheck,

    [string]$OutFile = "vrops-kesif-$(Get-Date -Format yyyyMMdd-HHmmss).txt"
)

$ErrorActionPreference = 'Stop'
$base = "https://$VropsHost/suite-api/api"

# --- sertifika ------------------------------------------------------------
$extra = @{}
if ($SkipCertCheck) {
    if ($PSVersionTable.PSVersion.Major -ge 6) {
        $extra['SkipCertificateCheck'] = $true
    } else {
        # PowerShell 5.1 yolu
        Add-Type -TypeDefinition @"
using System.Net;
using System.Security.Cryptography.X509Certificates;
public class TrustAll : ICertificatePolicy {
    public bool CheckValidationResult(ServicePoint sp, X509Certificate c, WebRequest r, int p) { return true; }
}
"@ -ErrorAction SilentlyContinue
        [System.Net.ServicePointManager]::CertificatePolicy = New-Object TrustAll
        [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
    }
}

# --- cikti ----------------------------------------------------------------
$sb = New-Object System.Text.StringBuilder
function Say {
    param([string]$Text, [string]$Color = 'Gray')
    Write-Host $Text -ForegroundColor $Color
    [void]$sb.AppendLine($Text)
}
function Dump {
    param([string]$Baslik, $Nesne, [int]$Derinlik = 6)
    Say ""
    Say ("=" * 70)
    Say $Baslik 'Cyan'
    Say ("=" * 70)
    [void]$sb.AppendLine(($Nesne | ConvertTo-Json -Depth $Derinlik))
}

Say "vROps kesif - $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
Say "Sunucu     : $VropsHost"
Say "AuthSource : $(if ([string]::IsNullOrWhiteSpace($AuthSource)) { '(gonderilmiyor)' } else { $AuthSource })"
Say "PowerShell : $($PSVersionTable.PSVersion)"

# --- kimlik dogrulama -----------------------------------------------------
$cred = Get-Credential -Message "vROps / Aria Operations kullanici adi ve parolasi"
$plain = [System.Net.NetworkCredential]::new('', $cred.Password).Password

# authSource BOSSA alani HIC koymuyoruz. Bu kurulumda yerel hesaplar
# alan yokken kabul ediliyor; 'Local' yazildiginda vROps 401 donuyor.
if ([string]::IsNullOrWhiteSpace($AuthSource)) {
    $body = @{ username = $cred.UserName; password = $plain } | ConvertTo-Json
} else {
    $body = @{ username = $cred.UserName; password = $plain; authSource = $AuthSource } | ConvertTo-Json
}

try {
    $tokenResp = Invoke-RestMethod -Method Post -Uri "$base/auth/token/acquire" `
        -Body $body -ContentType 'application/json' `
        -Headers @{ Accept = 'application/json' } @extra
} finally {
    $plain = $null; [System.GC]::Collect()   # parolayi bellekte tutma
}

$token = $tokenResp.token
if (-not $token) { throw "Token alinamadi. Donen cevap: $($tokenResp | ConvertTo-Json -Depth 4)" }

Say ""
Say "TOKEN ALINDI. Gecerlilik: $($tokenResp.validity) / $($tokenResp.expiresAt)" 'Green'

$H = @{
    Authorization = "vRealizeOpsToken $token"
    Accept        = 'application/json'   # bazi surumlerde varsayilan XML, bu sart
}

function Get-Api {
    param([string]$Path)
    Invoke-RestMethod -Method Get -Uri "$base$Path" -Headers $H @extra
}

# --- 1) surum -------------------------------------------------------------
try {
    $ver = Get-Api "/versions/current"
    Dump "1) SURUM" $ver
} catch {
    Say "1) SURUM okunamadi: $($_.Exception.Message)" 'Yellow'
}

# --- 2) hangi kaynak tipleri var ------------------------------------------
# Burada VMWARE adapter'inin kaynak tiplerini goruyoruz: HostSystem,
# VirtualMachine, ClusterComputeResource, Datacenter... isimler surume gore
# degisebilir, o yuzden varsaymak yerine listeliyoruz.
try {
    $kinds = Get-Api "/adapterkinds/VMWARE/resourcekinds"
    $kindNames = $kinds.'resource-kind' | ForEach-Object { $_.key }
    Dump "2) VMWARE ADAPTER KAYNAK TIPLERI" $kindNames
} catch {
    Say "2) Kaynak tipleri okunamadi: $($_.Exception.Message)" 'Yellow'
}

# --- 3) ESXi host'lari ----------------------------------------------------
$hosts = $null
try {
    $hosts = Get-Api "/resources?resourceKind=HostSystem&page=0&pageSize=5"
    Say ""
    Say "3) HOST SAYISI (toplam): $($hosts.pageInfo.totalCount)" 'Green'
    Dump "3) ILK 5 ESXi HOST" $hosts.resourceList
} catch {
    Say "3) Host listesi okunamadi: $($_.Exception.Message)" 'Yellow'
}

# --- 4) sanal makineler ---------------------------------------------------
$vms = $null
try {
    $vms = Get-Api "/resources?resourceKind=VirtualMachine&page=0&pageSize=5"
    Say ""
    Say "4) VM SAYISI (toplam): $($vms.pageInfo.totalCount)" 'Green'
    Dump "4) ILK 5 SANAL MAKINE" $vms.resourceList
} catch {
    Say "4) VM listesi okunamadi: $($_.Exception.Message)" 'Yellow'
}

# --- 5) bir VM'in TUM ozellikleri ----------------------------------------
# >> SUBE/ULKE bilgisini burada ariyoruz. Ciktida su alanlara bakin:
#    summary|parentDatacenter, summary|parentCluster, summary|parentHost,
#    config|tags, summary|customTag..., net:*|ip_address
$ornekVm = $vms.resourceList | Select-Object -First 1
if ($ornekVm) {
    $vmId = $ornekVm.identifier
    Say ""
    Say "5) ORNEK VM: $($ornekVm.resourceKey.name)  (id: $vmId)" 'Green'
    try {
        $props = Get-Api "/resources/$vmId/properties"
        Dump "5) BU VM'IN TUM OZELLIKLERI  <<< SUBE BILGISI BURADA ARANACAK" $props 8
    } catch {
        Say "5) Ozellikler okunamadi: $($_.Exception.Message)" 'Yellow'
    }

    # --- 6) VM -> host -> cluster -> datacenter zinciri -------------------
    try {
        $parents = Get-Api "/resources/$vmId/relationships?relationshipType=PARENT"
        Dump "6) BU VM'IN UST NESNELERI (host / cluster / datacenter)" $parents.resourceList
    } catch {
        Say "6) Iliskiler okunamadi: $($_.Exception.Message)" 'Yellow'
    }
}

# --- 7) datacenter ve cluster listesi ------------------------------------
# Sube ayrimi buradan cikiyorsa isimlerde ulke/sehir gorursunuz.
foreach ($k in 'Datacenter', 'ClusterComputeResource') {
    try {
        $r = Get-Api "/resources?resourceKind=$k&page=0&pageSize=50"
        $adlar = $r.resourceList | ForEach-Object { $_.resourceKey.name }
        Say ""
        Say "7) $k sayisi: $($r.pageInfo.totalCount)" 'Green'
        Dump "7) $k ADLARI  <<< ulke/sehir adi goruyor musunuz?" $adlar
    } catch {
        Say "7) $k okunamadi: $($_.Exception.Message)" 'Yellow'
    }
}

# --- 8) custom tag / grup ------------------------------------------------
# Sube ayrimi tag ile yapiliyorsa burada cikar.
try {
    $groups = Get-Api "/resources/groups"
    $gAdlar = $groups.groups | ForEach-Object { "$($_.resourceKey.name)  [$($_.membershipDefinition.'custom-group-properties'.Count) kural]" }
    Dump "8) CUSTOM GROUP'LAR  <<< sube gruplamasi burada olabilir" $gAdlar
} catch {
    Say "8) Gruplar okunamadi (normal olabilir): $($_.Exception.Message)" 'Yellow'
}

# --- kaydet --------------------------------------------------------------
$sb.ToString() | Out-File -FilePath $OutFile -Encoding UTF8
Write-Host ""
Write-Host "Cikti dosyasi: $(Resolve-Path $OutFile)" -ForegroundColor Cyan
Write-Host ""
Write-Host "PAYLASMADAN ONCE DOSYAYI ACIP OKUYUN." -ForegroundColor Yellow
Write-Host "Icinde sunucu adlari ve IP'ler var. Parola YOK, token YOK." -ForegroundColor Yellow
Write-Host "Kurum politikaniz izin vermiyorsa adlari maskeleyip oyle paylasin;" -ForegroundColor Yellow
Write-Host "tasarim icin ALAN ADLARI yeterli, degerler gizlenebilir." -ForegroundColor Yellow
