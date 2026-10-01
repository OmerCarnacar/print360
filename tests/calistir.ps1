# ============================================================
#  Print360 - TEST CALISTIRICI
#  build.ps1 ve GitHub CI AYNI betigi calistirir. Herhangi bir test
#  basarisiz olursa cikis kodu 1'dir: build.ps1 paket URETMEZ, CI KIRMIZI olur.
#
#  Her test URETIM KAYNAGIYLA birlikte derlenir (kopyasiyla degil):
#  test gecerse uretimdeki kod gercekten dogru calisiyor demektir.
#
#  Kullanim:  powershell -ExecutionPolicy Bypass -File tests\calistir.ps1
# ============================================================
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw "csc.exe bulunamadi: $csc" }
$out = Join-Path $root "tests\bin"
New-Item -ItemType Directory -Force $out | Out-Null

$sqlite = Join-Path $root "vendor\sqlite\System.Data.SQLite.dll"
$ortak  = "server\Print360.Ortak.cs"
$db     = "server\Print360.Db.cs"
$setup  = "setup\Print360.Setup.cs"

# Ad | kaynaklar | ek referanslar | giris sinifi (bos = tek Main)
$testler = @(
    @{ Ad = "RoundTrip";        Kaynak = @("tests\RoundTrip.cs", $ortak);        Ref = @();                        Main = "" },
    @{ Ad = "QsKodlama";        Kaynak = @("tests\QsKodlama.cs", $ortak);        Ref = @();                        Main = "" },
    @{ Ad = "EsZamanli";        Kaynak = @("tests\EsZamanli.cs", $ortak);        Ref = @();                        Main = "" },
    @{ Ad = "IstemciDegisimi";  Kaynak = @("tests\IstemciDegisimi.cs", $db);     Ref = @("System.Data.dll", $sqlite); Main = "" },
    @{ Ad = "KuyrukDurumu";     Kaynak = @("tests\KuyrukDurumu.cs", $db);        Ref = @("System.Data.dll", $sqlite); Main = "" },
    @{ Ad = "KuyrukKorumalari"; Kaynak = @("tests\KuyrukKorumalari.cs", $db);    Ref = @("System.Data.dll", $sqlite); Main = "" },
    @{ Ad = "SurumRaporu";      Kaynak = @("tests\SurumRaporu.cs", "server\Print360.Dashboard.cs", $db, $ortak, "server\Print360.License.cs", "server\Print360.Version.cs");
       Ref = @("System.Data.dll", "System.DirectoryServices.dll", $sqlite); Main = "SurumTest" },
    @{ Ad = "PeMimari";         Kaynak = @("tests\PeMimari.cs", $setup);
       Ref = @("System.Data.dll", "System.IO.Compression.dll", "System.IO.Compression.FileSystem.dll", "Microsoft.CSharp.dll");
       Main = "PeTest" },
    @{ Ad = "PanelGorevi";      Kaynak = @("tests\PanelGorevi.cs", $setup);
       Ref = @("System.Data.dll", "System.Xml.dll", "System.IO.Compression.dll", "System.IO.Compression.FileSystem.dll", "Microsoft.CSharp.dll");
       Main = "PanelGoreviTest" }
)

$basarisiz = @()
$ozet = @()
foreach ($t in $testler) {
    $exe = Join-Path $out ($t.Ad + ".exe")
    $arg = @("/nologo", "/codepage:65001", "/target:exe", "/out:$exe")
    foreach ($r in $t.Ref) { $arg += "/r:$r" }
    if ($t.Main) { $arg += "/main:$($t.Main)" }
    $arg += $t.Kaynak

    $derleme = & $csc @arg 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[DERLENEMEDI] $($t.Ad)" -ForegroundColor Red
        $derleme | Where-Object { $_ -match "error" } | ForEach-Object { Write-Host "    $_" }
        $basarisiz += $t.Ad; $ozet += "{0,-18} DERLENEMEDI" -f $t.Ad
        continue
    }
    # SQLite dogrudan kullanilmasa da derleme referansi calisma aninda aranabilir
    if ($t.Ref -contains $sqlite) { Copy-Item $sqlite $out -Force }

    $cikti = & $exe 2>&1
    $kod = $LASTEXITCODE
    $sonuc = ($cikti | Select-String "Sonuc:" | Select-Object -Last 1)
    if ($kod -ne 0) {
        Write-Host "[KALDI] $($t.Ad)" -ForegroundColor Red
        $cikti | Where-Object { $_ -match "KALDI|HATA|Sonuc" } | ForEach-Object { Write-Host "    $_" }
        $basarisiz += $t.Ad
    }
    $ozet += "{0,-18} {1}  {2}" -f $t.Ad, ($(if ($kod -eq 0) { "GECTI" } else { "KALDI" })), $sonuc
}

Write-Host ""
$ozet | ForEach-Object { Write-Host "    $_" }
Write-Host ""
if ($basarisiz.Count -gt 0) {
    Write-Host "TESTLER BASARISIZ: $($basarisiz -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Host "Tum testler gecti ($($testler.Count) test dosyasi)." -ForegroundColor Green
exit 0
