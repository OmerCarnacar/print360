# ============================================================
#  Print360 - RDP Virtual Channel PROTOKOL TESTI
#  Gelistirici: Omer CARNACAR <omer.carnacar@outlook.com.tr>
#
#  Kanal olmadan, uctan uca protokol dogrulamasi:
#    1. C# tarafi (VChannel.Cerceveler) cerceveleri uretir
#    2. C++ tarafi (P360_CerceveIsle) cerceveleri cozup dosyayi yazar
#    3. Sonuc dosyasi kaynakla BYTE BYTE karsilastirilir (SHA-256)
#
#  Kullanim: powershell -ExecutionPolicy Bypass -File test-protokol.ps1
# ============================================================
$ErrorActionPreference = "Stop"
$vc   = $PSScriptRoot
$root = Split-Path $vc -Parent
# NOT: $env:TEMP 8.3 kisa yol dondurebilir (Push-Location basarisiz olur) -> tam yola cevir
$tmp  = Join-Path ([System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())) "p360vctest"
$fw   = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"

Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $tmp, "$tmp\cikti" | Out-Null
Write-Host "== Print360 Virtual Channel Protokol Testi ==" -ForegroundColor Cyan

# --- 1) C++ tarafini TEST modunda derle (kanal yok, saf protokol) ---
$cl = Get-ChildItem "$env:ProgramFiles\Microsoft Visual Studio" -Recurse -Filter cl.exe -ErrorAction SilentlyContinue |
      Where-Object { $_.FullName -match "Hostx64\\x64" } | Select-Object -First 1
if (-not $cl) { throw "cl.exe bulunamadi (MSVC gerekli)." }
$vcTools = (Get-Item $cl.FullName).Directory.Parent.Parent.Parent.FullName   # ...\MSVC\<ver>
$inc = @("$vcTools\include")
$lib = @("$vcTools\lib\x64")
$sdkRoot = "${env:ProgramFiles(x86)}\Windows Kits\10"
$sdkVer = (Get-ChildItem "$sdkRoot\Include" -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1).Name
if ($sdkVer) {
    $inc += "$sdkRoot\Include\$sdkVer\ucrt", "$sdkRoot\Include\$sdkVer\um", "$sdkRoot\Include\$sdkVer\shared"
    $lib += "$sdkRoot\Lib\$sdkVer\ucrt\x64", "$sdkRoot\Lib\$sdkVer\um\x64"
}
$env:INCLUDE = ($inc -join ";"); $env:LIB = ($lib -join ";")
Push-Location $tmp
& $cl.FullName /nologo /EHsc /DP360_TEST /Fe:"$tmp\VCTest.exe" "$vc\Print360.VirtualChannel.cpp" /link /SUBSYSTEM:CONSOLE | Out-Null
Pop-Location
if (-not (Test-Path "$tmp\VCTest.exe")) { throw "C++ test derlemesi basarisiz." }
Write-Host "  [OK] C++ cozucu derlendi (P360_CerceveIsle)" -ForegroundColor Green

# --- 2) C# tarafi: cerceve ureteci ---
$uretici = @'
using System; using System.IO; using System.Collections.Generic;
static class Uret {
    static void Main(string[] a) {
        byte[] pdf = File.ReadAllBytes(a[0]);
        var liste = VChannel.Cerceveler(Path.GetFileName(a[2]), pdf, 7);
        using (var fs = File.Create(a[1]))
            foreach (var f in liste) {
                fs.Write(BitConverter.GetBytes(f.Length), 0, 4);   // [uzunluk][cerceve]
                fs.Write(f, 0, f.Length);
            }
        Console.WriteLine(liste.Count);
    }
}
'@
Set-Content "$tmp\uret.cs" $uretici -Encoding UTF8
& "$fw\csc.exe" /nologo /out:"$tmp\Uret.exe" "$tmp\uret.cs" "$root\server\Print360.VChannel.cs" | Out-Null
if (-not (Test-Path "$tmp\Uret.exe")) { throw "C# ureteci derlenemedi." }
Write-Host "  [OK] C# cerceve ureteci derlendi (VChannel.Cerceveler)" -ForegroundColor Green

# --- 3) Farkli boyutlarda test ---
$testler = @(
    @{ Ad = "kucuk (1 KB)";        Boyut = 1024 },
    @{ Ad = "tam blok (30000 B)";  Boyut = 30000 },
    @{ Ad = "blok+1 (30001 B)";    Boyut = 30001 },
    @{ Ad = "orta (256 KB)";       Boyut = 262144 },
    @{ Ad = "buyuk (5 MB)";        Boyut = 5242880 }
)
$basarili = 0; $basarisiz = 0
foreach ($t in $testler) {
    $kaynak = "$tmp\kaynak.bin"
    $rnd = New-Object byte[] $t.Boyut
    (New-Object Random 42).NextBytes($rnd)
    [System.IO.File]::WriteAllBytes($kaynak, $rnd)
    $hedefAd = "test_$($t.Boyut).pdf"

    & "$tmp\Uret.exe" $kaynak "$tmp\cerceve.bin" $hedefAd | Out-Null
    $cerceveSayi = (& "$tmp\Uret.exe" $kaynak "$tmp\cerceve.bin" $hedefAd)
    & "$tmp\VCTest.exe" "$tmp\cerceve.bin" "$tmp\cikti" | Out-Null
    $rc = $LASTEXITCODE

    $cikti = "$tmp\cikti\$hedefAd"
    if ($rc -eq 0 -and (Test-Path $cikti)) {
        $h1 = (Get-FileHash $kaynak -Algorithm SHA256).Hash
        $h2 = (Get-FileHash $cikti  -Algorithm SHA256).Hash
        if ($h1 -eq $h2) {
            Write-Host ("  [GECTI] {0,-22} {1,3} cerceve, SHA-256 ayni" -f $t.Ad, $cerceveSayi) -ForegroundColor Green
            $basarili++
        } else {
            Write-Host ("  [KALDI] {0,-22} icerik BOZUK" -f $t.Ad) -ForegroundColor Red; $basarisiz++
        }
    } else {
        Write-Host ("  [KALDI] {0,-22} cikti uretilmedi (rc=$rc)" -f $t.Ad) -ForegroundColor Red; $basarisiz++
    }
    Remove-Item $cikti -Force -ErrorAction SilentlyContinue
}

# --- 4) Bozuk/eksik veri testi: yarim is BASILMAMALI ---
Write-Host "`n  -- Dayaniklilik testleri --"
& "$tmp\Uret.exe" "$tmp\kaynak.bin" "$tmp\cerceve.bin" "yarim.pdf" | Out-Null
$ham = [System.IO.File]::ReadAllBytes("$tmp\cerceve.bin")
[System.IO.File]::WriteAllBytes("$tmp\yarim.bin", $ham[0..([int]($ham.Length * 0.6))])  # BITTI gelmeden kes
& "$tmp\VCTest.exe" "$tmp\yarim.bin" "$tmp\cikti" | Out-Null
if (Test-Path "$tmp\cikti\yarim.pdf") {
    Write-Host "  [KALDI] Yarim is dosyasi olusturuldu - BASILABILIRDI!" -ForegroundColor Red; $basarisiz++
} else {
    Write-Host "  [GECTI] Yarim is .part olarak kaldi, basilmaz" -ForegroundColor Green; $basarili++
}

Write-Host "`n============================================================"
if ($basarisiz -eq 0) { Write-Host "  TUM TESTLER GECTI ($basarili/$($basarili))" -ForegroundColor Green }
else { Write-Host "  BASARISIZ: $basarisiz test kaldi" -ForegroundColor Red }
Write-Host "============================================================"
exit $basarisiz
