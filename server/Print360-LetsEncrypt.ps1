#Requires -RunAsAdministrator
# ============================================================
#  Print360 - Let's Encrypt sertifika kurulumu
#  Gercek (guvenilir) sertifika alir ve panel HTTPS portuna baglar.
#  Yenilemede (90 gunde bir) otomatik yeniden baglanir.
#
#  ONKOSUL:
#   - Sunucunun gercek bir DOMAIN adi olmali (or. print360.firmam.com)
#     ve bu domain bu sunucunun public IP'sine yonlenmis olmali (DNS A kaydi).
#   - Dogrulama sirasinda gecici olarak 80 (HTTP) portu internete acik olmali
#     (Let's Encrypt HTTP-01 dogrulamasi buradan yapilir).
#
#  Kullanim:
#   .\Print360-LetsEncrypt.ps1 -Domain print360.firmam.com -Email siz@firma.com
#   (ozel port: -HttpsPort 9443 ; win-acme elde ise: -WacsPath C:\wacs\wacs.exe)
# ============================================================
param(
    [Parameter(Mandatory = $true)][string]$Domain,
    [Parameter(Mandatory = $true)][string]$Email,
    [string]$HttpsPort = "8443",
    [string]$WacsPath = ""
)

$ErrorActionPreference = "Stop"
$base = "C:\Print360"
$appId = "{7A3F2B10-360A-4B3B-9E01-000000008443}"

Write-Host "== Print360 Let's Encrypt Sertifika Kurulumu ==" -ForegroundColor Cyan
Write-Host "Domain: $Domain   Port: $HttpsPort"
Write-Host ""

# --- 1) On kontroller ---
if (-not (Test-Path $base)) { throw "Print360 kurulu degil ($base yok). Once sunucu kurulumunu yapin." }

# Domain bu sunucuya cozumleniyor mu? (bilgi amacli)
try {
    $ip = [System.Net.Dns]::GetHostAddresses($Domain) | Where-Object { $_.AddressFamily -eq "InterNetwork" } | Select-Object -First 1
    Write-Host "DNS: $Domain -> $ip (bu sunucunun public IP'si olmali)"
} catch { Write-Host "UYARI: $Domain DNS'te cozumlenemedi. A kaydini kontrol edin." -ForegroundColor Yellow }

# Port 80 baska bir sey tutuyor mu? (Let's Encrypt HTTP-01 icin gerekli)
$p80 = Get-NetTCPConnection -LocalPort 80 -State Listen -ErrorAction SilentlyContinue
if ($p80) {
    $pn = (Get-Process -Id $p80[0].OwningProcess -ErrorAction SilentlyContinue).ProcessName
    Write-Host "UYARI: 80 portu kullaniliyor (surec: $pn). win-acme dogrulama icin 80'e ihtiyac duyar." -ForegroundColor Yellow
    Write-Host "Dogrulama basarisiz olursa bu uygulamayi gecici durdurun." -ForegroundColor Yellow
}
Write-Host "NOT: Bulut guvenlik duvarinda (NSG/SG) dogrulama suresince 80 portu ACIK olmali." -ForegroundColor Yellow
Write-Host ""

# --- 2) win-acme (wacs.exe) bul/indir ---
if (-not $WacsPath) {
    $wacsDir = "$base\win-acme"
    $WacsPath = Join-Path $wacsDir "wacs.exe"
    if (-not (Test-Path $WacsPath)) {
        Write-Host "win-acme indiriliyor..."
        New-Item -ItemType Directory -Force $wacsDir | Out-Null
        # Sabit, dogrulanmis surum (gerekirse guncelleyin)
        $url = "https://github.com/win-acme/win-acme/releases/download/v2.2.9.1701/win-acme.v2.2.9.1701.x64.pluggable.zip"
        $zip = "$env:TEMP\win-acme.zip"
        try {
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest -Uri $url -OutFile $zip -ErrorAction Stop
            Expand-Archive -Path $zip -DestinationPath $wacsDir -Force
            Remove-Item $zip -Force -ErrorAction SilentlyContinue
        } catch {
            throw "win-acme indirilemedi ($_). Elle indirip -WacsPath ile yol verin: https://www.win-acme.com/"
        }
    }
}
if (-not (Test-Path $WacsPath)) { throw "wacs.exe bulunamadi: $WacsPath" }
Write-Host "win-acme: $WacsPath" -ForegroundColor Green

# --- 3) Yenileme sonrasi 8443'e yeniden baglayan script'i uret ---
# win-acme her yenilemede bunu cagirir; boylece 90 gunde bir sertifika
# otomatik yenilenir ve panel portuna otomatik yeniden baglanir.
$rebind = "$base\cert-rebind.ps1"
@"
param([string]`$Thumbprint)
`$port = "$HttpsPort"
`$appId = "$appId"
netsh http delete sslcert ipport=0.0.0.0:`$port 2>`$null | Out-Null
netsh http add sslcert ipport=0.0.0.0:`$port certhash=`$Thumbprint appid="`$appId" | Out-Null
Set-Content "$base\cert-thumbprint.txt" `$Thumbprint -Encoding ascii
Add-Content "$base\logs\letsencrypt.log" ("`$(Get-Date -Format 'yyyy-MM-dd HH:mm')  Sertifika `$port portuna baglandi: `$Thumbprint")
"@ | Set-Content $rebind -Encoding utf8
New-Item -ItemType Directory -Force "$base\logs" | Out-Null

# --- 4) Sertifika al + kur + bagla (win-acme selfhosting HTTP-01) ---
Write-Host ""
Write-Host "Sertifika aliniyor (Let's Encrypt)..." -ForegroundColor Cyan
$args = @(
    "--source", "manual", "--host", $Domain,
    "--validation", "selfhosting",
    "--store", "certificatestore",
    "--installation", "script",
    "--script", "powershell.exe",
    "--scriptparameters", "-NoProfile -ExecutionPolicy Bypass -File `"$rebind`" {CertThumbprint}",
    "--emailaddress", $Email,
    "--accepttos",
    "--notaskscheduler:false"
)
& $WacsPath @args
if ($LASTEXITCODE -ne 0) {
    throw "win-acme sertifika alimi basarisiz (cikis $LASTEXITCODE). 80 portu acik mi, DNS dogru mu kontrol edin."
}

# --- 5) Sonuc dogrulama ---
Start-Sleep 2
$bound = netsh http show sslcert ipport=0.0.0.0:$HttpsPort 2>$null | Select-String "Certificate Hash"
Write-Host ""
if ($bound) {
    Write-Host "BASARILI: Let's Encrypt sertifikasi $HttpsPort portuna baglandi." -ForegroundColor Green
    Write-Host $bound.ToString().Trim()
    Write-Host ""
    Write-Host "ONEMLI: Gercek (guvenilir) sertifika kullaniyorsunuz. Istemcilerde artik" -ForegroundColor Yellow
    Write-Host "sertifika SABITLEME (CertHash) KULLANMAYIN - yenilemede parmak izi degisir." -ForegroundColor Yellow
    Write-Host "Istemci Print360.ini'de CertHash= satirini BOS birakin." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Yenileme: win-acme 90 gunde bir otomatik yeniler ve porta yeniden baglar." -ForegroundColor Green
    Write-Host "Panel: https://$Domain`:$HttpsPort"
} else {
    Write-Host "UYARI: Sertifika alindi ama porta baglanmamis gorunuyor. cert-rebind.ps1 loglarini kontrol edin:" -ForegroundColor Yellow
    Write-Host "  $base\logs\letsencrypt.log"
}
Write-Host ""
Read-Host "Kapatmak icin Enter'a basin"
