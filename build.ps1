# ============================================================
#  Print360 - Yapim (build) scripti
#  Binary'leri derler, Setup.exe'leri uretir, musteri ZIP'ini paketler.
#  Kullanim:  powershell -ExecutionPolicy Bypass -File build.ps1
#             (surum icin: -Version 1.1)
# ============================================================
param([string]$Version = "1.0")

$ErrorActionPreference = "Stop"
$root   = $PSScriptRoot
$fw     = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$csc    = "$fw\csc.exe"
$binS   = Join-Path $root "setup\bin-server"
$binC   = Join-Path $root "setup\bin-client"
$dist   = Join-Path $root "dist"
$db     = Join-Path $root "server\Print360.Db.cs"
$lic    = Join-Path $root "server\Print360.License.cs"
$verCs  = Join-Path $root "server\Print360.Version.cs"
# SQLite (MSSQL yoksa yerel veritabani) - vendor\sqlite altinda
$sqliteRef = Join-Path $root "vendor\sqlite\System.Data.SQLite.dll"

function Adim($n) { Write-Host "`n>>> $n" -ForegroundColor Cyan }
function Ok($n)   { Write-Host "    [OK] $n" -ForegroundColor Green }

# Inno Setup derleyicisini bul
$iscc = @(
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
  "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup (ISCC.exe) bulunamadi. https://jrsoftware.org/isdl.php adresinden kurun." }

# Her build'de otomatik artan build numarasi -> tam surum X.Y.build
$buildFile = Join-Path $root "setup\buildno.txt"
$buildNo = 0
if (Test-Path $buildFile) { [int]::TryParse((Get-Content $buildFile -Raw).Trim(), [ref]$buildNo) | Out-Null }
$buildNo++
Set-Content $buildFile $buildNo -Encoding ascii
$fullVer = "$Version.$buildNo"
# Yapim damgasi: GUN.AY SAAT:DK -> hangi paketin ne zaman uretildigi bir bakista
# anlasilsin (surum numarasi tek basina ayirt edici degildi).
$yapimDamga = (Get-Date).ToString("dd.MM HH:mm")
$yapimTam   = (Get-Date).ToString("dd.MM.yyyy HH:mm:ss")
$yapimKisa  = (Get-Date).ToString("ddMM-HHmm")
@"
// build.ps1 tarafindan otomatik uretildi - elle duzenlemeyin.
static class Surum
{
    public const string V = "$fullVer";              // karsilastirma icin (otomatik guncelleme)
    public const string Yapim = "$yapimDamga";        // gun.ay saat:dk
    public const string YapimTam = "$yapimTam";       // tam tarih
    // Ekranlarda gosterilecek tam etiket
    public const string Etiket = "$fullVer ($yapimDamga)";
}
"@ | Set-Content $verCs -Encoding utf8

# Tum binary'lerin dosya ozelliklerine surum + gelistirici + lisans bilgisi
# (Windows'ta sag tik > Ozellikler > Ayrintilar altinda gorunur)
$asmCs = Join-Path $root "server\Print360.AssemblyInfo.cs"
@"
// build.ps1 tarafindan otomatik uretildi - elle duzenlemeyin.
using System.Reflection;
[assembly: AssemblyProduct("Print360 - RDP Yazdirma ve Yonetim Cozumu")]
[assembly: AssemblyCompany("Omer CARNACAR")]
[assembly: AssemblyCopyright("(c) 2026 Omer CARNACAR - Ucretsiz surum, para ile satilamaz")]
[assembly: AssemblyTrademark("omer.carnacar@outlook.com.tr")]
[assembly: AssemblyVersion("$fullVer.0")]
[assembly: AssemblyFileVersion("$fullVer.0")]
"@ | Set-Content $asmCs -Encoding utf8

Write-Host "============================================================"
Write-Host "  PRINT360 YAPIM  -  Surum $fullVer  (build #$buildNo)"
Write-Host "============================================================"

# --- 1) Binary derleme ---
Adim "1/3  Bilesenler derleniyor"
New-Item -ItemType Directory -Force $binS, $binC | Out-Null

& $csc /nologo /target:winexe /r:System.Data.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:"$sqliteRef" `
    /out:"$binS\Print360.ServerAgent.exe" "$root\server\Print360.ServerAgent.cs" $db $lic $asmCs $verCs "$root\server\Print360.VChannel.cs"
if ($LASTEXITCODE -ne 0) { throw "ServerAgent derlemesi basarisiz." }
Ok "Print360.ServerAgent.exe"

& $csc /nologo /target:winexe /r:System.DirectoryServices.dll /r:System.Data.dll /r:"$sqliteRef" `
    /out:"$binS\Print360.Dashboard.exe" "$root\server\Print360.Dashboard.cs" $db $lic $verCs $asmCs
if ($LASTEXITCODE -ne 0) { throw "Dashboard derlemesi basarisiz." }
Ok "Print360.Dashboard.exe"

# WPF Panel - SAF WPF (harici bilesen YOK; yalnizca .NET Framework 4.x gerekir).
# DevExpress bagimliligi kaldirildi: ~55 MB DLL yok, panel her sunucuda acilir.
& $csc /nologo /target:winexe /lib:"$fw\WPF" `
    /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll `
    /r:System.DirectoryServices.dll /r:System.Data.dll /r:"$sqliteRef" `
    /out:"$binS\Print360.Panel.exe" "$root\server\Print360.Panel.cs" $db $lic $asmCs $verCs
if ($LASTEXITCODE -ne 0) { throw "Panel derlemesi basarisiz." }
Ok "Print360.Panel.exe (saf WPF - harici bilesen yok)"

& $csc /nologo /target:winexe /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll `
    /out:"$binC\Print360.ClientAgent.exe" "$root\client\Print360.ClientAgent.cs" $verCs $asmCs
if ($LASTEXITCODE -ne 0) { throw "ClientAgent derlemesi basarisiz." }
Ok "Print360.ClientAgent.exe (v$fullVer)"

# Guncel istemci ajanini sunucu paketine de koy (sunucu otomatik guncelleme icin dagitir)
Copy-Item "$binC\Print360.ClientAgent.exe" (Join-Path $binS "Print360.ClientAgent.exe") -Force
Ok "Istemci ajani sunucu dagitimina eklendi (otomatik guncelleme)"

# NATIVE YAPILANDIRICI - kurulum isini bu yapar (PowerShell GEREKMEZ)
& $csc /nologo /target:exe /r:System.Data.dll /r:System.IO.Compression.dll `
    /r:System.IO.Compression.FileSystem.dll /r:Microsoft.CSharp.dll `
    /out:"$binS\Print360.Setup.exe" "$root\setup\Print360.Setup.cs" $asmCs
if ($LASTEXITCODE -ne 0) { throw "Print360.Setup derlemesi basarisiz." }
Copy-Item "$binS\Print360.Setup.exe" (Join-Path $binC "Print360.Setup.exe") -Force
Ok "Print360.Setup.exe (native yapilandirici - PowerShell'siz)"

# SQLite calisma zamani: yonetilen DLL exe'nin yaninda, native interop ise
# x64\ ve x86\ alt klasorlerinde olmali (System.Data.SQLite oradan yukler).
$sqDir = Join-Path $root "vendor\sqlite"
if (Test-Path $sqliteRef) {
    Copy-Item $sqliteRef (Join-Path $binS "System.Data.SQLite.dll") -Force
    foreach ($mim in "x64", "x86") {
        $h = Join-Path $binS $mim
        New-Item -ItemType Directory -Force $h | Out-Null
        Copy-Item (Join-Path $sqDir "SQLite.Interop.$mim.dll") (Join-Path $h "SQLite.Interop.dll") -Force
    }
    Ok "SQLite calisma zamani eklendi (MSSQL yoksa yerel veritabani)"
} else {
    Write-Host "    [ATLA] vendor\sqlite yok - MSSQL yoksa CSV moduna dusulur" -ForegroundColor Yellow
}

# Modern logo (.ico) - kisayol ve Setup ikonu icin her iki pakete de koy
$ico = "$root\assets\Print360.ico"
if (Test-Path $ico) {
    Copy-Item $ico (Join-Path $binS "Print360.ico") -Force
    Copy-Item $ico (Join-Path $binC "Print360.ico") -Force
    Ok "Logo (Print360.ico) sunucu + istemci paketine eklendi"
} else {
    Write-Host "    [ATLA] assets\Print360.ico yok - kisayol varsayilan ikonla kalir" -ForegroundColor Yellow
}

# RDP Virtual Channel eklentisi (varsa) - istemci paketine dahil et
$vcDll = "$root\vc\Print360.VC.dll"
if (Test-Path $vcDll) {
    Copy-Item $vcDll (Join-Path $binC "Print360.VC.dll") -Force
    Ok "Virtual Channel eklentisi (Print360.VC.dll) istemci paketine eklendi"
} else {
    Write-Host "    [ATLA] vc\Print360.VC.dll yok - istemci HTTPS kanaliyla calisir" -ForegroundColor Yellow
}

# --- 2) Setup.exe uretimi ---
# NOT: Urun UCRETSIZ surumdur; lisans anahtari uretimi (vendor\KeyGen) artik
# yapim akisinda degildir. Kaynagi tarihsel olarak vendor\ altinda durur.
Adim "2/3  Kurulum paketleri uretiliyor (Inno Setup)"
& $iscc /Q "$root\setup\Print360-Server.iss"
if ($LASTEXITCODE -ne 0) { throw "Server Setup uretimi basarisiz." }
Ok "Print360-Server-Setup.exe"
& $iscc /Q "$root\setup\Print360-Client.iss"
if ($LASTEXITCODE -ne 0) { throw "Client Setup uretimi basarisiz." }
Ok "Print360-Client-Setup.exe"

# --- 3) Dagitim paketi + guvenlik kontrolu ---
Adim "3/3  Dagitim paketi olusturuluyor"
# Lisans metni pakete dahil (ucretsiz surum kosullari kullaniciya ulassin)
Copy-Item "$root\LICENSE" (Join-Path $dist "LICENSE.txt") -Force
Ok "LICENSE.txt pakete eklendi"
# dist'e hassas dosya sizmis mi?
$hassas = Get-ChildItem $dist -Recurse -Include *.cs, *.key, *.xml, *.iss -ErrorAction SilentlyContinue
if ($hassas) {
    Write-Host "    [DUR] dist icinde hassas dosya var:" -ForegroundColor Red
    $hassas | ForEach-Object { Write-Host "      $($_.FullName)" }
    throw "Guvenlik: kaynak/anahtar dist'e sizmis. Paket olusturulmadi."
}
Ok "dist temiz (kaynak kod / ozel anahtar yok)"

# Paket adinda TAM SURUM olsun: ayni adli eski bir kopyanin yanlislikla
# kurulmasi (ve "kurdum ama eski surum calisiyor" karisikligi) onlensin.
$zip = Join-Path $root "Print360-Kurulum-v$fullVer-$yapimKisa.zip"
Get-ChildItem $root -Filter "Print360-Kurulum-v*.zip" -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne (Split-Path $zip -Leaf) } |
    ForEach-Object { Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue }
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path "$dist\*" -DestinationPath $zip -CompressionLevel Optimal
Ok "$(Split-Path $zip -Leaf)  ($([int]((Get-Item $zip).Length/1KB)) KB)"

Write-Host "`n============================================================"
Write-Host "  TAMAMLANDI" -ForegroundColor Green
Write-Host "  Dagitim paketi : $zip"
Write-Host "  Icerik         : Server + Client Setup.exe, KURULUM.txt, SURUM.txt"
Write-Host "  Lisans         : UCRETSIZ SURUM - para ile satilamaz (bkz. LICENSE)"
Write-Host "  Gelistirici    : Omer CARNACAR  <omer.carnacar@outlook.com.tr>"
Write-Host "============================================================"
