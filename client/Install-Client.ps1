#Requires -RunAsAdministrator
# Print360 - ISTEMCI (yerel bilgisayar) kurulumu
# Yaptigi isler:
#  1. C:\Print360 klasor yapisini olusturur (jobs / done / logs)
#  2. Ajani derler (Print360.ClientAgent.exe)
#  3. SumatraPDF (sessiz yazdirma motoru) indirir - onay sorar
#  4. Print360.ini olusturur (hedef yazici secimi)
#  5. Oturum acilisinda ajani baslatacak kaydi ekler ve ajani hemen baslatir
param(
    [string]$Printer = "",   # bos birakilirsa Windows varsayilan yazicisi kullanilir
    [string]$Server = "",    # RDP sunucusunun adi/IP'si (merkezi sayac icin, ornek: SRV01)
    [string]$ClientKey = "", # istemci sifresi: bu makine sunucuya bu sifreyle kaydolur
    [string]$Port = "",      # sunucu ozel port kullaniyorsa (bos = HTTPS 8443 / HTTP 8360)
    [string]$CertHash = "",  # bulut/internet: sunucu sertifika parmak izi (sabitleme/MITM koruması)
    [string]$VCMode = "auto", # auto = kanal varsa RDP'den, yoksa HTTPS (kanal mantigi)
    [switch]$Quiet           # Setup.exe icinden: soru sormadan kur (SumatraPDF otomatik iner)
)

$ErrorActionPreference = "Stop"
$base = "C:\Print360"

Write-Host "== Print360 Istemci Kurulumu ==" -ForegroundColor Cyan

# Acik bir RDP oturumu varsa sunucuyu kurulum aninda otomatik tespit et
function Bul-RdpSunucu {
    try {
        $c = Get-NetTCPConnection -RemotePort 3389 -State Established -ErrorAction SilentlyContinue |
             Select-Object -First 1
        if ($c) { return $c.RemoteAddress }
    } catch { }
    try {   # eski sistemler icin yedek: netstat
        $m = (netstat -n | Select-String ':3389\s' | Select-String 'ESTABLISHED' | Select-Object -First 1)
        if ($m -and $m -match '\s(\d+\.\d+\.\d+\.\d+):3389\s') { return $Matches[1] }
    } catch { }
    return $null
}
$rdpSunucu = Bul-RdpSunucu
if ($rdpSunucu) {
    Write-Host "RDP oturumu algilandi -> sunucu: $rdpSunucu" -ForegroundColor Green
    Write-Host "Ajan bu sunucuyu otomatik kullanacak (elle IP girmeye gerek yok)."
}

# Setup.cmd ile cift tiklamali kurulumda sorular
if (-not $PSBoundParameters.ContainsKey("Server")) {
    $ipuc = if ($rdpSunucu) { "BOS = otomatik ($rdpSunucu)" } else { "BOS = RDP baglantisindan OTOMATIK bul" }
    $Server = (Read-Host "RDP sunucusunun adi/IP'si ($ipuc)").Trim()
}
if (-not $PSBoundParameters.ContainsKey("Printer")) {
    $Printer = (Read-Host "Hedef yazici adi (bos = varsayilan yazici)").Trim()
}
if (-not $PSBoundParameters.ContainsKey("ClientKey")) {
    $ClientKey = (Read-Host "Istemci sifresi (bu makine sunucuya bu sifreyle kaydolur; bos gecilebilir)").Trim()
}
if (-not $PSBoundParameters.ContainsKey("Port")) {
    $Port = (Read-Host "Sunucu panel portu (sunucuda varsayilan disi port ayarlandiysa; bos = 8443)").Trim()
}
if (-not $PSBoundParameters.ContainsKey("CertHash")) {
    $CertHash = (Read-Host "Sunucu sertifika parmak izi (BULUT/internet icin onerilir; sunucudaki cert-thumbprint.txt; bos gecilebilir)").Trim()
}
# VCMode sorusu YOK: "auto" dogru olani kendi yapar - RDP kanali varsa oradan
# (kanal mantigi), yoksa HTTPS. Tanilama icin elle -VCMode 1/0 verilebilir.

# 1) Klasorler
New-Item -ItemType Directory -Force -Path $base, "$base\jobs", "$base\done", "$base\logs", "$base\stats" | Out-Null
icacls $base /grant "*S-1-5-32-545:(OI)(CI)M" | Out-Null   # Users grubu yazabilsin

# 2) Onceden derlenmis ajani yerlestir - once calisan surumu durdur (dosya kilidi)
Stop-Process -Name "Print360.ClientAgent" -Force -ErrorAction SilentlyContinue
Start-Sleep 1
$exe = "$base\Print360.ClientAgent.exe"
$src = Join-Path $PSScriptRoot "bin\Print360.ClientAgent.exe"
if (-not (Test-Path $src)) { throw "Bilesen bulunamadi: $src" }
Copy-Item $src $exe -Force
Write-Host "Ajan yerlestirildi: $exe"

# 2b) RDP Virtual Channel eklentisi (varsa) - RDP kanalı tabanlı ayarsiz is tasima.
#     mstsc.exe bu DLL'i registry AddIns kaydindan yukler; sonraki RDP oturumunda etkinlesir.
$vcSrc = Join-Path $PSScriptRoot "bin\Print360.VC.dll"
if (Test-Path $vcSrc) {
    $vcDll = "$base\Print360.VC.dll"
    Copy-Item $vcSrc $vcDll -Force
    foreach ($rk in @(
        "HKLM:\SOFTWARE\Microsoft\Terminal Server Client\Default\AddIns\Print360",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Terminal Server Client\Default\AddIns\Print360")) {
        New-Item -Path $rk -Force | Out-Null
        Set-ItemProperty -Path $rk -Name "Name" -Value $vcDll
    }
    Write-Host "RDP Virtual Channel eklentisi kuruldu: $vcDll (sonraki RDP oturumunda etkinlesir)"
} else {
    Write-Host "Not: Virtual Channel eklentisi (Print360.VC.dll) pakette yok; isler HTTPS kanalindan tasinir." -ForegroundColor Yellow
}

# 2c) Logo (tepsi simgesi + durum penceresi ikonu) ve masaustu kisayolu
$icoSrc = Join-Path $PSScriptRoot "bin\Print360.ico"
$ico = "$base\Print360.ico"
if (Test-Path $icoSrc) { Copy-Item $icoSrc $ico -Force }
# Kisayol: ajan zaten calisiyorsa ikinci calistirma durum penceresini acar
$ws = New-Object -ComObject WScript.Shell
$lnk = $ws.CreateShortcut("$env:PUBLIC\Desktop\Print360 Durum.lnk")
$lnk.TargetPath = $exe
$lnk.Description = "Print360 istemci durumu (baglanti, yazicilar, gorevler)"
if (Test-Path $ico) { $lnk.IconLocation = "$ico,0" }
$lnk.Save()
Write-Host "Masaustu kisayolu olusturuldu: Print360 Durum"

# 3) SumatraPDF
$sumatra = "$base\SumatraPDF.exe"
if (-not (Test-Path $sumatra)) {
    $url = "https://www.sumatrapdfreader.org/dl/rel/3.5.2/SumatraPDF-3.5.2-64.zip"
    $ans = if ($Quiet) { "E" } else { Read-Host "SumatraPDF (sessiz yazdirma motoru, ~8 MB) indirilsin mi? [E/h]" }
    if ($ans -eq "" -or $ans -match "^[Ee]") {
        # Internet gecici yoksa kurulum kesilmesin (yazdirma motoru sonra da eklenebilir)
        try {
            $zip = "$env:TEMP\SumatraPDF.zip"
            Invoke-WebRequest -Uri $url -OutFile $zip -ErrorAction Stop
            Expand-Archive -Path $zip -DestinationPath "$env:TEMP\SumatraPDF_x" -Force
            Copy-Item (Get-ChildItem "$env:TEMP\SumatraPDF_x" -Filter *.exe -Recurse | Select-Object -First 1).FullName $sumatra
            Remove-Item $zip, "$env:TEMP\SumatraPDF_x" -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "SumatraPDF kuruldu: $sumatra"
        } catch {
            Write-Host "UYARI: SumatraPDF indirilemedi ($_)." -ForegroundColor Yellow
            Write-Host "Internet baglaninca SumatraPDF.exe dosyasini elle $base klasorune kopyalayin." -ForegroundColor Yellow
        }
    } else {
        Write-Host "Atlandi. SumatraPDF.exe dosyasini elle $base klasorune kopyalayin." -ForegroundColor Yellow
    }
}

# 4) Ayar dosyasi
@"
; Print360 istemci ayarlari
; Printer bos ise Windows varsayilan yazicisi kullanilir. Ornek: Printer=HP LaserJet 1020
; Server: RDP sunucusunun adi/IP'si. BOS birakilirsa (veya "auto") client,
;         kullanicinin RDP ile bagli oldugu sunucuyu (aktif 3389 baglantisi)
;         OTOMATIK bulur - ayar girmeye gerek kalmaz.
Printer=$Printer
Server=$Server
ClientKey=$ClientKey
; Port: sunucu varsayilan disi port kullaniyorsa yazin (bos = HTTPS 8443 / HTTP 8360)
Port=$Port
; YedekMotor=1: SumatraPDF basarisiz olursa Windows 'printto' kanali denenir (0 = kapali)
YedekMotor=1
; UseHttps=1: sunucuya sifreli (HTTPS) baglanir (varsayilan). 0 = HTTP.
UseHttps=1
; CertHash: sunucudaki C:\Print360\cert-thumbprint.txt icerigi yazilirsa yalnizca
; o sertifika kabul edilir (sabitleme). Bos = tum self-signed kabul (kanal yine sifreli).
CertHash=$CertHash
; VCMode: auto (VARSAYILAN) | 1 (zorla RDP kanali) | 0 (zorla HTTPS)
; auto = TAM KANAL MANTIGI: mstsc eklentisi (Print360.VC.dll) kanali acinca
; her sey RDP tunelinden gider - IP/port/HTTPS/Server ayari HIC gerekmez.
; Eklenti yoksa veya RDP kapaliysa OTOMATIK HTTPS'e dusulur (sistem durmaz).
VCMode=$VCMode
; Arayuz=1: tepsi simgesi + durum penceresi (baglanti, yazicilar, gorevler) acilir.
; 0 = tamamen sessiz arka plan ajani (eski davranis).
Arayuz=1
SumatraPath=$sumatra
"@ | Set-Content "$base\Print360.ini" -Encoding utf8
# ini icinde istemci sifresi var - yalnizca yoneticiler degistirebilsin, kullanicilar okuyabilsin
icacls "$base\Print360.ini" /inheritance:r /grant "*S-1-5-32-544:F" "*S-1-5-18:F" "*S-1-5-32-545:R" | Out-Null

# 5) Baslangic kaydi + hemen baslat
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" `
    -Name "Print360ClientAgent" -Value "`"$exe`""
Start-Process $exe

Write-Host ""
Write-Host "Kurulum tamamlandi. Ajan calisiyor." -ForegroundColor Green
Write-Host "ONEMLI: RDP baglantisini kurarken 'Yerel Kaynaklar > Diger > Suruculer > C:' isaretli olmali."
Write-Host "Hedef yazici / sunucu ayarlari: $base\Print360.ini"
if (-not $Quiet) {
    Write-Host ""
    Read-Host "Kapatmak icin Enter'a basin"
}
