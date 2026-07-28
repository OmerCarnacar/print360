#Requires -RunAsAdministrator
# Print360 - RDP SUNUCU kurulumu
# Yaptigi isler:
#  1. C:\Print360 klasor yapisini olusturur
#  2. Ajani derler (Print360.ServerAgent.exe)
#  3. Belirtilen kullanici(lar) icin "Print360 - <kullanici>" sanal yazicisini olusturur
#     (Microsoft Print to PDF suruculu, ciktiyi dogrudan spool dosyasina yazar - dialog acilmaz)
#  4. Ajani tum kullanicilarin oturum acilisinda baslatacak kaydi ekler
param(
    [string[]]$Users,           # ornek: .\Install-Server.ps1 -Users ali,ayse,muhasebe
    # Setup.exe sihirbazindan gelen parametreler (-Quiet ile hic soru sorulmaz)
    [string]$HttpPort   = "",
    [string]$HttpsPort  = "",
    [string]$VC         = "",   # "1" = RDP Virtual Channel acik
    [string]$YaziciModu = "",   # dogrudan | sec | pdf  (RDP oturumunda varsayilan yazici)
    [string]$SqlKur     = "",   # "1" = MSSQL simdi kurulsun, "0" = CSV modu
    [string]$SqlServer  = "",
    [string]$SqlUser    = "",
    [string]$SqlPwd     = "",
    [string]$PanelAdmin = "",   # panel yonetici kullanicisi
    [string]$PanelAdminPwd = "",
    [string]$PanelPwd   = "",   # panel erisim sifresi (panel.pwd)
    [switch]$Quiet              # Setup.exe icinden: soru sorma
)

$ErrorActionPreference = "Stop"
$base = "C:\Print360"
$spool = "$base\spool"

Write-Host "== Print360 Sunucu Kurulumu ==" -ForegroundColor Cyan

# Sunucu bileseni yalnizca Windows Server'a kurulabilir (ProductType: 1=Workstation, 2=DC, 3=Server)
$osTip = (Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue).ProductType
if (-not $osTip) { $osTip = (Get-WmiObject Win32_OperatingSystem).ProductType }
if ($osTip -eq 1) {
    Write-Host ""
    Write-Host "HATA: Print360 SUNUCU bileseni yalnizca Windows Server sistemlerine kurulabilir." -ForegroundColor Red
    Write-Host "Bu bilgisayar bir masaustu (istemci) Windows surumudur." -ForegroundColor Red
    Write-Host "Kullanici bilgisayarlarina 'Print360-Client-Setup.exe' kurun." -ForegroundColor Yellow
    Write-Host ""
    if (-not $Quiet) { Read-Host "Kapatmak icin Enter'a basin" }
    exit 1
}
if (-not $Users) {
    if ($Quiet) { $Users = @($env:USERNAME) }
    else {
        $ans = Read-Host "Yazici olusturulacak kullanici adlari (virgulle ayirin; bos = $env:USERNAME)"
        if ($ans.Trim()) { $Users = $ans.Split(",") | ForEach-Object { $_.Trim() } | Where-Object { $_ } }
        else { $Users = @($env:USERNAME) }
    }
}

# 1) Klasorler
New-Item -ItemType Directory -Force -Path $base, $spool, "$base\logs", "$base\stats", "$base\stats\clients" | Out-Null
# Tum kullanicilar spool ve log klasorlerine yazabilmeli
icacls $base /grant "*S-1-5-32-545:(OI)(CI)M" | Out-Null   # Users grubu (SID ile, dil bagimsiz)

# 1b) PrintService olay gunlugunu ac (belge adi + sayfa sayisi buradan okunur)
wevtutil sl Microsoft-Windows-PrintService/Operational /e:true

# 2) Onceden derlenmis binary'leri yerlestir - once calisan surumleri durdur (dosya kilidi)
Stop-Process -Name "Print360.ServerAgent", "Print360.Dashboard", "Print360.Panel" -Force -ErrorAction SilentlyContinue
Start-Sleep 1
$exe = "$base\Print360.ServerAgent.exe"
$dashExe = "$base\Print360.Dashboard.exe"
$panelExe = "$base\Print360.Panel.exe"
$binDir = Join-Path $PSScriptRoot "bin"
foreach ($b in "Print360.ServerAgent.exe", "Print360.Dashboard.exe", "Print360.Panel.exe") {
    $src = Join-Path $binDir $b
    if (-not (Test-Path $src)) { throw "Bilesen bulunamadi: $src" }
    Copy-Item $src (Join-Path $base $b) -Force
}
# Masaustu paneli SAF WPF'tir: harici bilesen (DevExpress vb.) gerektirmez.
# Onceki surumlerden kalan DevExpress dosyalari varsa temizle (gereksiz ~55 MB).
$eski = @(Get-ChildItem (Join-Path $base "DevExpress.*.dll") -ErrorAction SilentlyContinue)
if ($eski.Count -gt 0) {
    $eski | Remove-Item -Force -ErrorAction SilentlyContinue
    Write-Host "Eski surumden kalan $($eski.Count) DevExpress dosyasi temizlendi (artik gerekmiyor)."
}
# Guncel istemci ajani -> update klasoru (sunucu otomatik guncelleme icin dagitir)
$updDir = Join-Path $base "update"
New-Item -ItemType Directory -Force $updDir | Out-Null
$cliSrc = Join-Path $binDir "Print360.ClientAgent.exe"
if (Test-Path $cliSrc) { Copy-Item $cliSrc (Join-Path $updDir "Print360.ClientAgent.exe") -Force }
Write-Host "Bilesenler yerlestirildi: $base (istemci guncelleme: $updDir)"

# Panel dinleme portlari (mevcut portlar doluysa degistirin)
$httpPort = $HttpPort
if (-not $httpPort.Trim() -and -not $Quiet) { $httpPort = Read-Host "Panel HTTP portu (bos = 8360)" }
if (-not $httpPort.Trim()) { $httpPort = "8360" }
$httpsPort = $HttpsPort
if (-not $httpsPort.Trim() -and -not $Quiet) { $httpsPort = Read-Host "Panel HTTPS portu (bos = 8443)" }
if (-not $httpsPort.Trim()) { $httpsPort = "8443" }
# Port bos mu kontrol et (baska uygulama tutuyorsa uyar)
foreach ($pr in @($httpPort, $httpsPort)) {
    $dolu = Get-NetTCPConnection -LocalPort $pr -State Listen -ErrorAction SilentlyContinue
    if ($dolu) {
        $pn = (Get-Process -Id $dolu[0].OwningProcess -ErrorAction SilentlyContinue).ProcessName
        Write-Host "UYARI: $pr portu zaten kullaniliyor (surec: $pn). Kurulum devam ediyor ama panel bu portta acilmayabilir." -ForegroundColor Yellow
    }
}

# RDP Virtual Channel (kanal mantigi) - VARSAYILAN ACIK.
# Isler RDP tunelinden gider; istemcide eklenti yoksa otomatik HTTPS'e dusulur.
if ($VC.Trim()) { $vc = if ($VC.Trim() -eq "0") { "0" } else { "1" } }
elseif ($Quiet) { $vc = "1" }
else {
    $vcAns = Read-Host "Isler RDP kanalindan mi tasinsin? (kanal mantigi; onerilen) [E/h]"
    $vc = if ($vcAns -match "^[Hh]") { "0" } else { "1" }
}

# --- MSSQL veritabani (ISTEGE BAGLI) ---
# Kurulmazsa sistem CSV/dosya modunda calisir; SQL sonradan bu kurulumu tekrar
# calistirip bu adima 'E' diyerek (ya da db.ini'yi duzenleyip ajani yeniden
# baslatarak) eklenebilir. Boylece SQL zorunlu degildir.
Write-Host ""
Write-Host "== MSSQL Ayarlari (istege bagli) ==" -ForegroundColor Cyan
# Varsayilanlar: SQL sonradan bu bilgilerle kurulursa otomatik devreye girer
$sqlServer = $env:COMPUTERNAME; $sqlUser = "sa"; $sqlPwd = ""   # varsayilan sifre YOK
if ($SqlKur.Trim()) { $sqlSimdi = ($SqlKur.Trim() -eq "1") }
elseif ($Quiet)     { $sqlSimdi = $false }   # sihirbazdan gelmediyse guvenli taraf: CSV modu
else {
    $sqlAns = Read-Host "MSSQL veritabani simdi kurulsun mu? (Hayir = CSV modu, SQL sonradan eklenebilir) [E/h]"
    $sqlSimdi = ($sqlAns.Trim() -eq "" -or $sqlAns -match "^[Ee]")
}
if ($sqlSimdi) {
    if ($SqlServer.Trim()) { $sqlServer = $SqlServer }
    elseif (-not $Quiet) { $r = Read-Host "SQL sunucusu (bos = $env:COMPUTERNAME)"; if ($r.Trim()) { $sqlServer = $r } }
    if ($SqlUser.Trim()) { $sqlUser = $SqlUser }
    elseif (-not $Quiet) { $r = Read-Host "SQL kullanicisi (bos = sa)"; if ($r.Trim()) { $sqlUser = $r } }
    if ($SqlPwd.Trim()) { $sqlPwd = $SqlPwd }
    elseif (-not $Quiet) { $r = Read-Host "SQL sifresi"; if ($r.Trim()) { $sqlPwd = $r } }
}

# RDP oturumunda varsayilan yazici modu (dogrudan = istemcinin varsayilan yazicisi)
$yMod = $YaziciModu.Trim().ToLower()
if ($yMod -notin @("dogrudan","sec","pdf")) {
    if ($Quiet) { $yMod = "dogrudan" }
    else {
        $yAns = Read-Host "Yazdirma modu: [1] Dogrudan varsayilan yaziciya  [2] Yazici secim penceresi (bos = 1)"
        $yMod = if ($yAns.Trim() -eq "2") { "sec" } else { "dogrudan" }
    }
}

@"
Server=$sqlServer
Database=Print360
User=$sqlUser
Password=$sqlPwd
HttpPort=$httpPort
HttpsPort=$httpsPort
VirtualChannel=$vc
VarsayilanYazici=$yMod
"@ | Set-Content "$base\db.ini" -Encoding ascii
icacls "$base\db.ini" /inheritance:r /grant "*S-1-5-32-544:F" "*S-1-5-18:F" "*S-1-5-32-545:R" | Out-Null

if (-not $sqlSimdi) {
    Write-Host "MSSQL atlandi: sistem CSV/dosya modunda calisir (tum temel islevler calisir)." -ForegroundColor Yellow
    Write-Host "SQL sonradan eklemek icin: bu kurulumu tekrar calistirip MSSQL adimina 'E' deyin" -ForegroundColor Yellow
    Write-Host "  (ya da $base\db.ini icindeki Server/User/Password'u duzenleyip ajani yeniden baslatin)." -ForegroundColor Yellow
}
if ($sqlSimdi) {
  try {
    $cs = "Server=$sqlServer;Database=master;User ID=$sqlUser;Password=$sqlPwd;Connect Timeout=8"
    $cn = New-Object System.Data.SqlClient.SqlConnection $cs
    $cn.Open()
    $cmd = $cn.CreateCommand()
    $cmd.CommandText = "IF DB_ID('Print360') IS NULL CREATE DATABASE Print360"
    [void]$cmd.ExecuteNonQuery()
    $cn.Close()
    Write-Host "Veritabani hazir: Print360 @ $sqlServer" -ForegroundColor Green

    # Sema + yonetici kullanicisi (dashboard ilk aciliste da EnsureSchema calistirir)
    $cs2 = "Server=$sqlServer;Database=Print360;User ID=$sqlUser;Password=$sqlPwd;Connect Timeout=8"
    $cn2 = New-Object System.Data.SqlClient.SqlConnection $cs2
    $cn2.Open()
    $cmd2 = $cn2.CreateCommand()
    $cmd2.CommandText = @"
IF OBJECT_ID('dbo.PanelUsers','U') IS NULL CREATE TABLE dbo.PanelUsers(
  Kullanici NVARCHAR(100) PRIMARY KEY, SifreHash CHAR(64) NOT NULL, Rol NVARCHAR(20) DEFAULT 'admin');
"@
    [void]$cmd2.ExecuteNonQuery()

    $adminU = $PanelAdmin
    if (-not $adminU.Trim() -and -not $Quiet) { $adminU = Read-Host "Panel yonetici kullanici adi (bos = admin)" }
    if (-not $adminU.Trim()) { $adminU = "admin" }
    $adminP = $PanelAdminPwd
    if (-not $adminP.Trim() -and -not $Quiet) { $adminP = Read-Host "Panel yonetici sifresi (bos = degistirme)" }
    if ($adminP.Trim()) {
        $sha2 = [System.Security.Cryptography.SHA256]::Create()
        $h2 = ($sha2.ComputeHash([Text.Encoding]::UTF8.GetBytes($adminP)) | ForEach-Object { $_.ToString("x2") }) -join ""
        $cmd2.CommandText = "IF EXISTS(SELECT 1 FROM PanelUsers WHERE Kullanici=@u) UPDATE PanelUsers SET SifreHash=@h WHERE Kullanici=@u ELSE INSERT INTO PanelUsers(Kullanici,SifreHash,Rol) VALUES(@u,@h,'admin')"
        [void]$cmd2.Parameters.AddWithValue("@u", $adminU)
        [void]$cmd2.Parameters.AddWithValue("@h", $h2)
        [void]$cmd2.ExecuteNonQuery()
        Write-Host "Panel kullanicisi hazir: $adminU" -ForegroundColor Green
    }
    $cn2.Close()
  } catch {
    Write-Host "UYARI: SQL'e baglanilamadi ($_). Sistem CSV modunda calisir; db.ini'yi duzeltip ajanlari yeniden baslatin." -ForegroundColor Yellow
  }
}

# Modern logo (kisayol ikonu icin) - bin klasoru veya script yaninda olabilir
$icoSrc = @("$binDir\Print360.ico", "$PSScriptRoot\Print360.ico") | Where-Object { Test-Path $_ } | Select-Object -First 1
$ico = "$base\Print360.ico"
if ($icoSrc) { Copy-Item $icoSrc $ico -Force }

# Ortak masaustune kisayol (modern logolu)
$ws = New-Object -ComObject WScript.Shell
$lnk = $ws.CreateShortcut("$env:PUBLIC\Desktop\Print360 Panel.lnk")
$lnk.TargetPath = $panelExe
$lnk.Description = "Print360 yazdirma paneli"
if (Test-Path $ico) { $lnk.IconLocation = "$ico,0" }
$lnk.Save()
Write-Host "Masaustu kisayolu olusturuldu: Print360 Panel"

# Dashboard'un yonetici olmadan da portu dinleyebilmesi icin (WD = Everyone, dil bagimsiz)
netsh http add urlacl url=http://+:$httpPort/ sddl="D:(A;;GX;;;WD)" | Out-Null

# Guvenlik duvari: istemci ajanlarinin sayac gonderebilmesi ve panele agdan erisim icin
netsh advfirewall firewall delete rule name="Print360 Dashboard" | Out-Null
netsh advfirewall firewall add rule name="Print360 Dashboard" dir=in action=allow protocol=TCP localport=$httpPort | Out-Null
netsh advfirewall firewall delete rule name="Print360 Dashboard HTTPS" | Out-Null
netsh advfirewall firewall add rule name="Print360 Dashboard HTTPS" dir=in action=allow protocol=TCP localport=$httpsPort | Out-Null

# 2c) HTTPS: self-signed sertifika olustur ve HTTPS portuna bagla
$cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Subject -eq "CN=Print360" } | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Subject "CN=Print360" -DnsName $env:COMPUTERNAME, "localhost" `
        -CertStoreLocation Cert:\LocalMachine\My -NotAfter (Get-Date).AddYears(10) -KeyAlgorithm RSA -KeyLength 2048
    Write-Host "Self-signed sertifika olusturuldu (10 yil gecerli)."
}
# Sunucunun kendi tarayicisi uyari vermesin diye guvenilen kok deposuna da ekle
try {
    $rootStore = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root", "LocalMachine")
    $rootStore.Open("ReadWrite")
    if (-not ($rootStore.Certificates | Where-Object { $_.Thumbprint -eq $cert.Thumbprint })) { $rootStore.Add($cert) }
    $rootStore.Close()
} catch { Write-Host "UYARI: Sertifika kok deposuna eklenemedi: $_" -ForegroundColor Yellow }

netsh http delete sslcert ipport=0.0.0.0:$httpsPort 2>&1 | Out-Null
netsh http add sslcert ipport=0.0.0.0:$httpsPort certhash=$($cert.Thumbprint) appid="{7A3F2B10-360A-4B3B-9E01-000000008443}" | Out-Null
netsh http add urlacl url=https://+:$httpsPort/ sddl="D:(A;;GX;;;WD)" 2>&1 | Out-Null
Set-Content "$base\cert-thumbprint.txt" $cert.Thumbprint -Encoding ascii
Write-Host "HTTPS hazir: https://$($env:COMPUTERNAME):$httpsPort  (parmak izi: $($cert.Thumbprint))" -ForegroundColor Green
Write-Host "Istemcilerde sertifika sabitleme icin bu parmak izi Print360.ini > CertHash alanina yazilabilir."

# 2b) Panel sifresi
$pwdMsg = if (Test-Path "$base\panel.pwd") { "Panel sifresi (bos = mevcut sifre korunur, KALDIR = sifresiz erisim)" }
          else { "Panel sifresi belirleyin (bos = sifresiz erisim)" }
$panelPwd = $PanelPwd
if (-not $panelPwd.Trim() -and -not $Quiet) { $panelPwd = Read-Host $pwdMsg }
if ($panelPwd -ceq "KALDIR") {
    Remove-Item "$base\panel.pwd" -Force -ErrorAction SilentlyContinue
    Write-Host "Panel sifresi kaldirildi."
} elseif ($panelPwd.Trim()) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $hash = ($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($panelPwd)) | ForEach-Object { $_.ToString("x2") }) -join ""
    Set-Content "$base\panel.pwd" $hash -Encoding ascii
    # Yalnizca yoneticiler degistirebilsin, kullanicilar okuyabilsin (dashboard kullanici oturumunda calisir)
    icacls "$base\panel.pwd" /inheritance:r /grant "*S-1-5-32-544:F" "*S-1-5-18:F" "*S-1-5-32-545:R" | Out-Null
    Write-Host "Panel sifresi ayarlandi."
}

# 3) Kullanici basina UC sanal yazici (is turu modeli)
foreach ($u in $Users) {
    $yazicilar = @(
        @{ Ad = "Print360 - $u";            Port = "$spool\$u.pdf";         Aciklama = "atanan yaziciya sessiz baski" },
        @{ Ad = "Print360 Yazici Sec - $u"; Port = "$spool\$u.sec.pdf";     Aciklama = "istemcide yazici secim penceresi" },
        @{ Ad = "Print360 PDF - $u";        Port = "$spool\$u.pdfview.pdf"; Aciklama = "istemcide PDF olarak ac/kaydet" }
    )
    foreach ($y in $yazicilar) {
        if (-not (Get-PrinterPort -Name $y.Port -ErrorAction SilentlyContinue)) {
            Add-PrinterPort -Name $y.Port
        }
        if (-not (Get-Printer -Name $y.Ad -ErrorAction SilentlyContinue)) {
            Add-Printer -Name $y.Ad -DriverName "Microsoft Print to PDF" -PortName $y.Port
        }
        Write-Host "Yazici hazir: '$($y.Ad)'  ($($y.Aciklama))"
    }
}

# 3b) Gunluk e-posta raporu (istege bagli)
if (-not (Test-Path "$base\mail.ini") -and -not $Quiet) {
    $mailAns = Read-Host "Gunluk e-posta raporu kurulsun mu? [e/H]"
    if ($mailAns -match "^[Ee]") {
        $smtp   = Read-Host "  SMTP sunucusu (ornek: smtp.office365.com)"
        $mport  = Read-Host "  Port (bos = 587)"
        if (-not $mport.Trim()) { $mport = "587" }
        $muser  = Read-Host "  SMTP kullanici (e-posta adresi)"
        $mpwd   = Read-Host "  SMTP sifresi"
        $kime   = Read-Host "  Rapor kime gonderilsin (virgulle coklu adres)"
        $saat   = Read-Host "  Gonderim saati (bos = 08:00)"
        if (-not $saat.Trim()) { $saat = "08:00" }
@"
Smtp=$smtp
Port=$mport
TLS=1
Kullanici=$muser
Sifre=$mpwd
Kimden=$muser
Kime=$kime
Saat=$saat
"@ | Set-Content "$base\mail.ini" -Encoding utf8
        icacls "$base\mail.ini" /inheritance:r /grant "*S-1-5-32-544:F" "*S-1-5-18:F" "*S-1-5-32-545:R" | Out-Null
        Write-Host "E-posta raporu ayarlandi. Panel > Uyarilar sayfasindan 'Test gonder' ile deneyebilirsiniz." -ForegroundColor Green
    }
}

# 4) Oturum acilisinda ajani + dashboard'u baslat (tum kullanicilar icin, HKLM Run)
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" `
    -Name "Print360ServerAgent" -Value "`"$exe`""
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" `
    -Name "Print360Dashboard" -Value "`"$dashExe`""

# 4b) KANAL MANTIGI: Ajan HER ZAMAN ayakta olmali.
# HKLM\Run yalnizca oturum ACILISINDA bir kez calisir; RDP'ye yeniden baglanmada
# veya ajan cokerse geri gelmez. Zamanlanmis gorev ile:
#   - her kullanicinin oturum acilisinda
#   - her RDP YENIDEN BAGLANMASINDA (SessionStateChange: RemoteConnect)
#   - cokerse 1 dk sonra yeniden (RestartOnFailure)
# ajan kullanicinin KENDI oturumunda baslatilir (InteractiveToken).
# Ajan per-kullanici mutex kullandigi icin cift baslatma zararsizdir.
$taskXml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.3" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>Print360 yazdirma ajani - oturum acilisinda ve RDP baglantisinda baslar</Description>
    <URI>\Print360 Yazdirma Ajani</URI>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger><Enabled>true</Enabled></LogonTrigger>
    <SessionStateChangeTrigger>
      <Enabled>true</Enabled>
      <StateChange>RemoteConnect</StateChange>
    </SessionStateChangeTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <GroupId>S-1-5-32-545</GroupId>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <DisallowStartOnRemoteAppSession>false</DisallowStartOnRemoteAppSession>
    <UseUnifiedSchedulingEngine>true</UseUnifiedSchedulingEngine>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
    <RestartOnFailure><Interval>PT1M</Interval><Count>3</Count></RestartOnFailure>
  </Settings>
  <Actions Context="Author">
    <Exec><Command>$exe</Command></Exec>
  </Actions>
</Task>
"@
$xmlYol = Join-Path $env:TEMP "Print360Ajan.xml"
[System.IO.File]::WriteAllText($xmlYol, $taskXml, (New-Object System.Text.UnicodeEncoding $false, $true))
schtasks /Delete /TN "Print360 Yazdirma Ajani" /F 2>&1 | Out-Null
$tOut = schtasks /Create /TN "Print360 Yazdirma Ajani" /XML "$xmlYol" /F 2>&1
Remove-Item $xmlYol -Force -ErrorAction SilentlyContinue
if ($LASTEXITCODE -eq 0) {
    Write-Host "Zamanlanmis gorev kuruldu: ajan her oturum acilisinda ve RDP baglantisinda otomatik baslar." -ForegroundColor Green
} else {
    Write-Host "UYARI: Zamanlanmis gorev kurulamadi ($tOut)." -ForegroundColor Yellow
    Write-Host "  Ajan yine de oturum acilisinda (HKLM\Run) baslar." -ForegroundColor Yellow
}

# ONEMLI: Ikisini de HEMEN baslat. Ajan calismazsa yazdirilan isler
# spool'da bekler, istemciye HIC gitmez (kurulumdan sonra oturum kapatip
# acmayi beklemek gerekmemeli).
Start-Process $dashExe
Start-Process $exe
Start-Sleep 2
$ajanCalisiyor = @(Get-Process "Print360.ServerAgent" -ErrorAction SilentlyContinue).Count -gt 0
if ($ajanCalisiyor) {
    Write-Host "Yazdirma ajani baslatildi (Print360.ServerAgent)." -ForegroundColor Green
} else {
    Write-Host "UYARI: Yazdirma ajani baslatilamadi! Isler istemciye gitmez." -ForegroundColor Red
    Write-Host "  Elle baslatin: $exe" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Kurulum tamamlandi." -ForegroundColor Green
Write-Host "NOTLAR:"
Write-Host " - Her kullanici RDP'de 'Print360 - <kendi adi>' yazicisina yazdirmali."
Write-Host " - Yeni kullanici eklemek icin: .\Install-Server.ps1 -Users yeniKullanici"
Write-Host " - RDP baglanti ayarlarinda 'Yerel Kaynaklar > Diger > Suruculer' isaretli olmali."
Write-Host " - Yazdirma ajani SIMDI calisiyor; her oturum acilisinda da otomatik baslar."
Write-Host "   ONEMLI: Ajan, yazdiran kullanicinin KENDI RDP oturumunda calismalidir."
Write-Host "   Baska bir kullanici yazdiracaksa o kullanici oturum actiginda ajan otomatik baslar."
Write-Host " - RAPORLAMA PANELI: https://$($env:COMPUTERNAME):$httpsPort  (yerel: https://localhost:$httpsPort, HTTP: $httpPort)" -ForegroundColor Cyan
if ($httpsPort -ne "8443" -or $httpPort -ne "8360") {
    Write-Host " - NOT: Ozel port kullaniyorsunuz. Istemci kurulumunda 'Port' alanina $httpsPort yazin (HTTPS icin)." -ForegroundColor Yellow
}
Write-Host ""
if (-not $Quiet) { Read-Host "Kapatmak icin Enter'a basin" }
