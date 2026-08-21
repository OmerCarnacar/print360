; Print360 SUNUCU kurulum paketi (Inno Setup)
; Derleme: ISCC.exe Print360-Server.iss  ->  dist\Print360-Server-Setup.exe
[Setup]
AppId={{7A3F2B10-360A-4B3B-9E01-PRINT360SRV}
AppName=Print360 Server
AppVersion=1.1
AppVerName=Print360 Server 1.1
AppPublisher=Ömer ÇARNAÇAR
AppCopyright=Copyright (c) 2026 Ömer ÇARNAÇAR - Ücretsiz sürüm, para ile satılamaz
AppContact=omer.carnacar@outlook.com.tr
AppSupportURL=mailto:omer.carnacar@outlook.com.tr
AppPublisherURL=https://www.linkedin.com/in/omercarnacar/
VersionInfoCompany=Ömer ÇARNAÇAR
VersionInfoDescription=Print360 Server - RDP yazdırma yönetimi (ücretsiz sürüm)
LicenseFile=LICENSE-kurulum.txt
DefaultDirName={commonpf}\Print360
DisableProgramGroupPage=yes
; Baslat menusu grubu (kaldirma kisayolu icin gerekli)
DefaultGroupName=Print360
PrivilegesRequired=admin
OutputDir=..\dist
OutputBaseFilename=Print360-Server-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
SetupIconFile=bin-server\Print360.ico
UninstallDisplayIcon={app}\bin\Print360.ico
UninstallDisplayName=Print360 Server
DisableWelcomePage=no
DisableReadyPage=no
ShowLanguageDialog=no

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Files]
Source: "bin-server\Print360.ServerAgent.exe"; DestDir: "{app}\bin"; Flags: ignoreversion
Source: "bin-server\Print360.Dashboard.exe";   DestDir: "{app}\bin"; Flags: ignoreversion
Source: "bin-server\Print360.Panel.exe";       DestDir: "{app}\bin"; Flags: ignoreversion
Source: "bin-server\Print360.ClientAgent.exe"; DestDir: "{app}\bin"; Flags: ignoreversion
Source: "bin-server\Print360.ico";             DestDir: "{app}\bin"; Flags: ignoreversion skipifsourcedoesntexist
Source: "bin-server\Print360.Setup.exe";       DestDir: "{app}"; Flags: ignoreversion
; SQLite calisma zamani (MSSQL yoksa yerel veritabani icin)
Source: "bin-server\System.Data.SQLite.dll";   DestDir: "{app}\bin"; Flags: ignoreversion skipifsourcedoesntexist
Source: "bin-server\x64\SQLite.Interop.dll";   DestDir: "{app}\bin\x64"; Flags: ignoreversion skipifsourcedoesntexist
Source: "bin-server\x86\SQLite.Interop.dll";   DestDir: "{app}\bin\x86"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\server\Print360-LetsEncrypt.ps1";  DestDir: "{app}"; Flags: ignoreversion

; Baslat menusu: panel + KALDIR kisayolu (kullanici kaldirmayi kolay bulsun)
[Icons]
Name: "{group}\Print360 Panel"; Filename: "C:\Print360\Print360.Panel.exe"; \
  IconFilename: "{app}\bin\Print360.ico"; Comment: "Print360 yönetim paneli"
Name: "{group}\Print360 Yönetim Paneli (Web)"; Filename: "http://localhost:8360/"; \
  Comment: "Tarayıcıda açılır"
Name: "{group}\Print360 Server'ı Kaldır"; Filename: "{uninstallexe}"; \
  IconFilename: "{app}\bin\Print360.ico"; Comment: "Print360 sunucu bileşenlerini kaldırır"

[Run]
; NATIVE yapilandirici - PowerShell YOK. Tum ayarlar sihirbazdan gelir.
Filename: "{app}\Print360.Setup.exe"; Parameters: "{code:KurulumParam}"; \
  StatusMsg: "Print360 sunucu bileşenleri kuruluyor (yazıcılar, ajan, panel, sertifika)..."; \
  Flags: waituntilterminated runhidden
; Kurulum bitince istege bagli olarak paneli ac
Filename: "{code:PanelYolu}"; Description: "Print360 yönetim panelini aç"; \
  Flags: postinstall nowait skipifsilent unchecked

[UninstallRun]
; NATIVE kaldirma - PowerShell YOK
Filename: "{app}\Print360.Setup.exe"; Parameters: "--kaldir-sunucu"; \
  Flags: waituntilterminated runhidden; RunOnceId: "P360SrvClean"

; Kaldirmada masaustu kisayolunu da sil
[UninstallDelete]
Type: files; Name: "{commondesktop}\Print360 Panel.lnk"

[Code]
var
  KullaniciPage: TInputQueryWizardPage;   { yazici olusturulacak kullanicilar + portlar }
  SqlPage: TInputQueryWizardPage;         { MSSQL bilgileri }
  SqlSecPage: TInputOptionWizardPage;     { MSSQL kurulsun mu + VC }
  ModPage: TInputOptionWizardPage;        { yazdirma modu (varsayilan yazici) }
  PanelPage: TInputQueryWizardPage;       { panel yoneticisi ve erisim sifresi }
  Isaret: String;                         { yapilandiricinin gercekten calistigini kanitlar }

procedure InitializeWizard;
begin
  { Her kurulumda benzersiz bir isaret: yapilandirici bunu dosyaya yazar,
    kurulum sonunda dogrulariz. Yazilmadiysa kullanici uyarilir. }
  Isaret := GetDateTimeString('yyyymmddhhnnss', #0, #0);

  KullaniciPage := CreateInputQueryPage(wpSelectDir,
    'Kullanıcılar ve Panel Portları', 'Sanal yazıcılar kimler için oluşturulsun?',
    'Her kullanıcı için TEK bir sanal yazıcı oluşturulur: "Print360 - <kullanıcı>". ' +
    'Kullanıcı adlarını virgülle ayırın; boş bırakırsanız oturum açan kullanıcı kullanılır.');
  KullaniciPage.Add('Kullanıcılar (virgülle):', False);
  KullaniciPage.Add('Panel HTTP portu (boş = 8360):', False);
  KullaniciPage.Add('Panel HTTPS portu (boş = 8443):', False);

  ModPage := CreateInputOptionPage(KullaniciPage.ID,
    'Yazdırma Modu', 'Çıktı istemciye nasıl gitsin?',
    'Her kullanıcı için TEK bir yazıcı oluşturulur: "Print360 - <kullanıcı>". ' +
    'Aşağıdaki seçim, o yazıcıya yazdırıldığında ne olacağını belirler.',
    True, False);   { exclusive = radio }
  ModPage.Add('Doğrudan istemcinin VARSAYILAN yazıcısına bas (önerilen, soru sorulmaz)');
  ModPage.Add('Yazıcı seçim penceresi açılsın (kullanıcı her baskıda yazıcı seçer)');
  ModPage.Add('Çıktıyı istemcide PDF olarak aç');
  ModPage.SelectedValueIndex := 0;

  SqlSecPage := CreateInputOptionPage(ModPage.ID,
    'Veritabanı ve Taşıma Modu', 'İsteğe bağlı bileşenler',
    'MSSQL kurulmazsa sistem CSV/dosya modunda tam olarak çalışır; veritabanını sonradan ' +
    'bu kurulumu tekrar çalıştırarak ekleyebilirsiniz.',
    False, False);
  SqlSecPage.Add('MSSQL veritabanını kur (İSTEĞE BAĞLI — kurulmazsa sistem tam çalışır)');
  SqlSecPage.Add('İşleri RDP kanalından taşı (kanal mantığı — IP/port/firewall gerekmez)');
  SqlSecPage.Values[0] := False;   { MSSQL artik zorunlu degil - varsayilan KAPALI }
  SqlSecPage.Values[1] := True;    { RDP kanali varsayilan ACIK }

  SqlPage := CreateInputQueryPage(SqlSecPage.ID,
    'MSSQL Bağlantısı', 'Veritabanı sunucu bilgileri',
    'Print360 veritabanı bu sunucuda oluşturulur. Boş bırakılan alanlar için varsayılanlar kullanılır.');
  SqlPage.Add('SQL sunucusu (boş = bu bilgisayar):', False);
  SqlPage.Add('SQL kullanıcısı (boş = sa):', False);
  SqlPage.Add('SQL şifresi:', True);

  PanelPage := CreateInputQueryPage(SqlPage.ID,
    'Yönetim Paneli', 'Panel erişim bilgileri',
    'Panel yöneticisi veritabanında oluşturulur. Panel erişim şifresi paneli korur. ' +
    'Boş bırakırsanız panel parolasız kalır ve GÜVENLİK GEREĞİ yalnızca sunucunun ' +
    'kendisinden açılabilir; ağdaki diğer bilgisayarlardan erişim reddedilir.');
  PanelPage.Add('Panel yönetici kullanıcı adı (boş = admin):', False);
  PanelPage.Add('Panel yönetici şifresi:', True);
  PanelPage.Add('Panel erişim şifresi (boş bırakılırsa panel yalnızca sunucudan açılır):', True);
end;

{ MSSQL secili degilse SQL bilgileri sayfasini atla }
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if (PageID = SqlPage.ID) and (not SqlSecPage.Values[0]) then Result := True;
end;

function EvetHayir(b: Boolean): String;
begin
  if b then Result := '1' else Result := '0';
end;

function YaziciModu(): String;
begin
  case ModPage.SelectedValueIndex of
    1: Result := 'sec';
    2: Result := 'pdf';
  else Result := 'dogrudan';
  end;
end;

{ Native yapilandiriciya gecirilen parametreler (PowerShell yok) }
function KurulumParam(Param: string): string;
var
  klar: String;
begin
  Result := '--sunucu'
    + ' --bin "'          + ExpandConstant('{app}') + '\bin"'
    + ' --yazicimodu '    + YaziciModu()
    + ' --httpport "'     + KullaniciPage.Values[1] + '"'
    + ' --httpsport "'    + KullaniciPage.Values[2] + '"'
    + ' --sql '           + EvetHayir(SqlSecPage.Values[0])
    + ' --vc '            + EvetHayir(SqlSecPage.Values[1])
    + ' --sqlserver "'    + SqlPage.Values[0] + '"'
    + ' --sqluser "'      + SqlPage.Values[1] + '"'
    + ' --sqlpwd "'       + SqlPage.Values[2] + '"'
    + ' --paneladmin "'   + PanelPage.Values[0] + '"'
    + ' --paneladminpwd "'+ PanelPage.Values[1] + '"'
    + ' --panelpwd "'     + PanelPage.Values[2] + '"'
    + ' --marker "'       + Isaret + '"';
  klar := Trim(KullaniciPage.Values[0]);
  if klar <> '' then Result := Result + ' --users "' + klar + '"';
end;

function PanelYolu(Param: string): string;
begin
  Result := 'C:\Print360\Print360.Panel.exe';
end;

{ Kurulum bitince yapilandiricinin GERCEKTEN calistigini dogrula.
  Calismadiysa sihirbaz "tamamlandi" der ama sistemde hicbir sey degismez;
  kullanici bunu ancak gunluge bakarak anlayabilirdi. Artik acikca soyluyoruz. }
procedure CurStepChanged(CurStep: TSetupStep);
var
  Icerik: AnsiString;
begin
  if CurStep = ssPostInstall then
  begin
    if not LoadStringFromFile('C:\Print360\logs\son-kurulum.txt', Icerik) then Icerik := '';
    if Pos(Isaret, String(Icerik)) = 0 then
      MsgBox('Dosyalar kopyalandi, ANCAK yapilandirma adimi calismadi.' + #13#10#13#10 +
             'Bu yuzden sanal yazicilar olusturulmamis, ajanlar baslatilmamis ve' + #13#10 +
             'panel guncellenmemis olabilir. Sistem eski haliyle calismaya devam eder.' + #13#10#13#10 +
             'Yapilacaklar:' + #13#10 +
             '  1. Kurulumu KAPATIN.' + #13#10 +
             '  2. Kurulum dosyasina SAG TIKLAYIP "Yonetici olarak calistir" secin.' + #13#10 +
             '  3. Sihirbazi sonuna kadar tamamlayin (Kur dugmesine basin).' + #13#10#13#10 +
             'Ayrinti icin: C:\Print360\logs\kurulum.log',
             mbCriticalError, MB_OK);
  end;
end;

// Sunucu bileseni yalnizca Windows Server sistemlerine kurulabilir.
// Masaustu (Client) Windows'ta kurulum reddedilir.
function InitializeSetup(): Boolean;
var
  InstallType: String;
begin
  Result := True;
  if RegQueryStringValue(HKLM,
       'SOFTWARE\Microsoft\Windows NT\CurrentVersion', 'InstallationType', InstallType) then
  begin
    if CompareText(InstallType, 'Server') <> 0 then
    begin
      MsgBox('Print360 SUNUCU bileseni yalnizca Windows Server sistemlerine kurulabilir.' + #13#10 +
             'Bu bilgisayarin surumu: "' + InstallType + '" (masaustu / istemci Windows).' + #13#10#13#10 +
             'Kullanici bilgisayarlarina "Print360-Client-Setup.exe" dosyasini kurun.',
             mbCriticalError, MB_OK);
      Result := False;
    end;
  end;
end;
