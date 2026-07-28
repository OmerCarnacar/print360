; Print360 ISTEMCI kurulum paketi (Inno Setup)
; Derleme: ISCC.exe Print360-Client.iss  ->  dist\Print360-Client-Setup.exe
[Setup]
AppId={{7A3F2B10-360A-4B3B-9E01-PRINT360CLI}
AppName=Print360 Client
AppVersion=1.1
AppVerName=Print360 Client 1.1
AppPublisher=Ömer ÇARNAÇAR
AppContact=omer.carnacar@outlook.com.tr
AppSupportURL=mailto:omer.carnacar@outlook.com.tr
AppPublisherURL=https://www.linkedin.com/in/omercarnacar/
VersionInfoCompany=Ömer ÇARNAÇAR
VersionInfoDescription=Print360 Client - RDP yazdırma ajanı (ücretsiz sürüm)
LicenseFile=LICENSE-kurulum.txt
DefaultDirName={commonpf}\Print360Client
DisableProgramGroupPage=yes
DefaultGroupName=Print360
PrivilegesRequired=admin
OutputDir=..\dist
OutputBaseFilename=Print360-Client-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
SetupIconFile=bin-client\Print360.ico
UninstallDisplayIcon={app}\bin\Print360.ico
UninstallDisplayName=Print360 Client
; Profesyonel akis: karsilama + bitis sayfalari, sade ozet
DisableWelcomePage=no
DisableReadyPage=no
ShowLanguageDialog=no
AppCopyright=Copyright (c) 2026 Ömer ÇARNAÇAR - Ücretsiz sürüm, para ile satılamaz

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Files]
Source: "bin-client\Print360.ClientAgent.exe"; DestDir: "{app}\bin"; Flags: ignoreversion
Source: "bin-client\Print360.VC.dll";          DestDir: "{app}\bin"; Flags: ignoreversion skipifsourcedoesntexist
Source: "bin-client\Print360.ico";             DestDir: "{app}\bin"; Flags: ignoreversion skipifsourcedoesntexist
Source: "bin-client\Print360.Setup.exe";       DestDir: "{app}"; Flags: ignoreversion

[Code]
var
  SunucuPage: TInputQueryWizardPage;

{ Acik bir RDP oturumu varsa uzak sunucunun IP'sini netstat ciktisindan bul.
  Boylece kullanici kurulumda sunucu adresini elle yazmak zorunda kalmaz. }
function RdpSunucuBul(): String;
var
  tmp, satir: String;
  lines: TArrayOfString;
  i, j, p, rc: Integer;
begin
  Result := '';
  tmp := ExpandConstant('{tmp}\p360rdp.txt');
  if not Exec(ExpandConstant('{cmd}'),
       '/c netstat -n | findstr ":3389" | findstr "ESTABLISHED" > "' + tmp + '"',
       '', SW_HIDE, ewWaitUntilTerminated, rc) then Exit;
  if not LoadStringsFromFile(tmp, lines) then Exit;
  for i := 0 to GetArrayLength(lines) - 1 do
  begin
    satir := lines[i];
    p := Pos(':3389', satir);
    if p > 0 then
    begin
      j := p - 1;
      while (j > 0) and (satir[j] <> ' ') do j := j - 1;
      Result := Copy(satir, j + 1, p - j - 1);
      if Result <> '' then Exit;
    end;
  end;
end;

procedure InitializeWizard;
var
  rdp: String;
begin
  SunucuPage := CreateInputQueryPage(wpSelectDir,
    'Print360 Ayarları', 'Sunucu ve yazıcı bilgileri',
    'RDP sunucusunun adını/IP''sini girin (merkezi sayaç için önerilir, boş geçilebilir). ' +
    'Hedef yazıcı boş bırakılırsa Windows varsayılan yazıcısı kullanılır.');
  SunucuPage.Add('Sunucu adı veya IP:', False);
  SunucuPage.Add('Hedef yazıcı adı (boş = varsayılan):', False);
  SunucuPage.Add('İstemci şifresi (makine sunucuya bu şifreyle kaydolur):', True);
  SunucuPage.Add('Sunucu panel portu (boş = 8443; sunucuda özel port ayarlandıysa yazın):', False);
  SunucuPage.Add('Sunucu sertifika parmak izi (bulut/internet için önerilir; boş geçilebilir):', False);

  { Baglanti modu sorusu YOK: ajan "auto" calisir - RDP sanal kanali varsa
    her sey oradan gider (TSPrint mantigi, ayar gerekmez), yoksa HTTPS'e duser. }

  { Acik RDP oturumu varsa sunucu alanini otomatik doldur }
  rdp := RdpSunucuBul();
  if rdp <> '' then
  begin
    SunucuPage.Values[0] := rdp;
    SunucuPage.SubCaptionLabel.Caption :=
      'Açık RDP oturumu algılandı — sunucu otomatik bulundu: ' + rdp + #13#10 +
      'Alanı boş bırakırsanız ajan sunucuyu her bağlantıda otomatik tespit eder.';
  end;
end;

{ Kurulan ajanin yolu - bitis sayfasindaki "durum penceresini ac" secenegi icin }
function AjanYolu(Param: string): string;
begin
  Result := 'C:\Print360\Print360.ClientAgent.exe';
end;

{ Native yapilandiriciya gecirilen parametreler (PowerShell yok) }
function KurulumParam(Param: string): string;
begin
  Result := '--istemci'
    + ' --bin "'      + ExpandConstant('{app}') + '\bin"'
    + ' --server "'   + SunucuPage.Values[0] + '"'
    + ' --printer "'  + SunucuPage.Values[1] + '"'
    + ' --clientkey "'+ SunucuPage.Values[2] + '"'
    + ' --port "'     + SunucuPage.Values[3] + '"'
    + ' --certhash "' + SunucuPage.Values[4] + '"';
end;

[Run]
; NATIVE yapilandirici - PowerShell YOK, konsol penceresi yok
Filename: "{app}\Print360.Setup.exe"; Parameters: "{code:KurulumParam}"; \
  StatusMsg: "Print360 istemci ajanı kuruluyor (yazıcı motoru, RDP eklentisi)..."; \
  Flags: waituntilterminated runhidden
; Kurulum bitince istege bagli olarak durum penceresini ac
Filename: "{code:AjanYolu}"; Description: "Print360 durum penceresini aç"; \
  Flags: postinstall nowait skipifsilent unchecked

[UninstallRun]
; NATIVE kaldirma - PowerShell YOK
Filename: "{app}\Print360.Setup.exe"; Parameters: "--kaldir-istemci"; \
  Flags: waituntilterminated runhidden; RunOnceId: "P360CliClean"

; Kaldirmada masaustu kisayolunu da sil
[UninstallDelete]
Type: files; Name: "{commondesktop}\Print360 Durum.lnk"
