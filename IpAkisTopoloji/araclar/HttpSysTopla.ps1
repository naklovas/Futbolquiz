<#
.SYNOPSIS
    HTTP.sys URL kayıtlarını (hangi port / URL hangi exe'de) sunuculardan toplar ve DeltaFlow'un okuduğu tabloya yazar.

.DESCRIPTION
    Carbon Black, HTTP.sys kullanan uygulamalarda (IIS, HttpSys'li ASP.NET Core, WCF, WinRM…) portun sahibini ntoskrnl.exe / System
    görür. Gerçek eşleme sunucunun kendisinde: `netsh http show servicestate view=requestq verbose=yes` → istek kuyruğu, bağlı süreç
    (PID) ve kayıtlı URL'ler. Betik her sunucuda bunu çalıştırır (Invoke-Command / WinRM), PID'i exe yoluna çevirir ve
    dbo.DF_HTTPSYS_URL tablosuna yazar (sql/httpsys_kayit.sql). Her sunucu için önce o sunucunun eski satırları silinir.

    Gerekenler: sunucularda WinRM açık ve betiği çalıştıran hesap yerel yönetici (netsh servicestate yönetici ister);
    SQL'de tabloya INSERT / DELETE yetkisi (Windows kimliğiyle bağlanır). Zamanlanmış görev olarak günde bir çalıştırılabilir.

.PARAMETER Sunucular
    Sunucu adları. Boşsa yalnız bu makine. Dosyadan: -Sunucular (Get-Content .\sunucular.txt)

.PARAMETER SadeceGoster
    Tabloya yazmaz, bulunanları ekrana basar (deneme için).

.EXAMPLE
    .\HttpSysTopla.ps1 -SadeceGoster
.EXAMPLE
    .\HttpSysTopla.ps1 -Sunucular (Get-Content .\sunucular.txt) -SqlSunucu SQLSUNUCU -Veritabani doku
#>
[CmdletBinding()]
param(
    [string[]]$Sunucular = @(),
    [string]$SqlSunucu,
    [string]$Veritabani,
    [string]$Tablo = "dbo.DF_HTTPSYS_URL",
    [int]$Paralel = 16,
    [switch]$SadeceGoster
)

# ---- Sunucuda çalışan kısım: netsh çıktısını ayrıştırır ----
$topla = {
    $satirlar = @(netsh http show servicestate view=requestq verbose=yes 2>$null)
    $ips = @()
    try { $ips = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop | Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } | ForEach-Object IPAddress) }
    catch { $ips = @([System.Net.Dns]::GetHostAddresses($env:COMPUTERNAME) | Where-Object { $_.AddressFamily -eq 'InterNetwork' -and -not $_.ToString().StartsWith('127.') } | ForEach-Object { $_.ToString() }) }
    $surec = @{}
    Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | ForEach-Object { $surec[[int]$_.ProcessId] = $_ }

    $sonuc = New-Object System.Collections.Generic.List[object]
    $kuyruk = $null; $pids = New-Object System.Collections.Generic.List[int]; $urls = New-Object System.Collections.Generic.List[string]
    $bitir = {
        # Sunucu oturumu blokları (başlık 16 haneli onaltılık kimlik) kuyruk değildir
        if ($urls.Count -eq 0 -or ($kuyruk -match '^[0-9A-Fa-f]{16}$' -and $pids.Count -eq 0)) { return }
        foreach ($u in $urls) {
            if ($u -notmatch '^(?i)(https?)://([^/\s]+):(\d+)(/\S*)?') { continue }
            $proto = $Matches[1].ToLower(); $hostKismi = $Matches[2]; $port = [int]$Matches[3]
            $hedefIp = if ($hostKismi -match '^\d{1,3}(\.\d{1,3}){3}$') { @($hostKismi) } else { $ips }   # + / * / host adı → sunucunun tüm IP'leri
            $pl = if ($pids.Count) { $pids } else { @($null) }
            foreach ($ip in $hedefIp) { foreach ($p in $pl) {
                $pr = if ($p) { $surec[[int]$p] } else { $null }
                $sonuc.Add([pscustomobject]@{
                    Ip = $ip; Hostname = $env:COMPUTERNAME; Port = $port; Protokol = $proto; Url = $u; Kuyruk = $kuyruk
                    Pid = $p; ExeYol = if ($pr) { $pr.ExecutablePath } else { $null }; KomutSatiri = if ($pr) { $pr.CommandLine } else { $null }
                })
            } }
        }
    }
    foreach ($l in $satirlar) {
        if ($l -match '^\S[^:]*:\s*(.*)$' -and $l -notmatch '^-') {
            # girintisiz satır = yeni blok (İstek kuyruğu adı / Request queue name / Server session ID …)
            & $bitir
            $kuyruk = $Matches[1].Trim(); $pids.Clear(); $urls.Clear()
            if ($kuyruk -match '(?i)unnamed|adsız|adsiz|ad(ı)? yok') { $kuyruk = $null }   # "Request queue is unnamed." (ASP.NET Core / WCF self-host)
            continue
        }
        if ($l -match '^\s*ID:\s*(\d+)') { $pids.Add([int]$Matches[1]) }            # süreç satırı ("ID: 1234, image: …"); "Controller process ID" sayılmaz
        elseif ($l -match '^\s*(?i)(https?://\S+)\s*$') { $urls.Add($Matches[1]) }  # kayıtlı URL
    }
    & $bitir
    $sonuc
}

# ---- Toplama ----
$hatalar = @()
if ($Sunucular.Count -eq 0) { $kayitlar = @(& $topla) }
else {
    $kayitlar = @(Invoke-Command -ComputerName $Sunucular -ScriptBlock $topla -ThrottleLimit $Paralel -ErrorAction SilentlyContinue -ErrorVariable hatalar)
}
foreach ($h in $hatalar) { Write-Warning "$($h.TargetObject): $($h.Exception.Message)" }
Write-Host ("{0} kayıt, {1} sunucu" -f $kayitlar.Count, @($kayitlar | Select-Object -ExpandProperty Hostname -Unique).Count)

if ($SadeceGoster -or -not $SqlSunucu) {
    $kayitlar | Sort-Object Hostname, Port | Format-Table Hostname, Ip, Port, Kuyruk, Pid, ExeYol, Url -AutoSize
    if (-not $SadeceGoster) { Write-Warning "Tabloya yazmak için -SqlSunucu ve -Veritabani verin." }
    return
}
if ($Tablo -notmatch '^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*){0,2}$') { throw "Geçersiz tablo adı: $Tablo" }

# ---- Tabloya yazma (sunucu başına: eski satırları sil, yenileri ekle; tek işlem) ----
$baglanti = New-Object System.Data.SqlClient.SqlConnection "Server=$SqlSunucu;Database=$Veritabani;Integrated Security=SSPI;TrustServerCertificate=True"
$baglanti.Open()
try {
    $tx = $baglanti.BeginTransaction()
    $toplayan = "$env:USERDOMAIN\$env:USERNAME"
    foreach ($g in ($kayitlar | Group-Object Hostname)) {
        $sil = $baglanti.CreateCommand(); $sil.Transaction = $tx
        $sil.CommandText = "DELETE FROM $Tablo WHERE HOSTNAME = @h"; [void]$sil.Parameters.AddWithValue("@h", $g.Name); [void]$sil.ExecuteNonQuery()
        foreach ($k in $g.Group) {
            $c = $baglanti.CreateCommand(); $c.Transaction = $tx
            $c.CommandText = "INSERT INTO $Tablo (IP, HOSTNAME, PORT, PROTOKOL, URL, KUYRUK, PID, EXE_YOL, KOMUT_SATIRI, TOPLAYAN) VALUES (@ip, @h, @p, @pr, @u, @k, @pid, @e, @ks, @t)"
            foreach ($a in @(@("@ip", $k.Ip), @("@h", $k.Hostname), @("@p", $k.Port), @("@pr", $k.Protokol), @("@u", $k.Url), @("@k", $k.Kuyruk),
                             @("@pid", $k.Pid), @("@e", $k.ExeYol), @("@ks", $(if ($k.KomutSatiri) { $k.KomutSatiri.Substring(0, [Math]::Min(2000, $k.KomutSatiri.Length)) })), @("@t", $toplayan))) {
                [void]$c.Parameters.AddWithValue($a[0], $(if ($null -eq $a[1]) { [DBNull]::Value } else { $a[1] }))
            }
            [void]$c.ExecuteNonQuery()
        }
    }
    $tx.Commit()
    Write-Host "Tabloya yazıldı: $Tablo"
}
catch { if ($tx) { $tx.Rollback() }; throw }
finally { $baglanti.Close() }
