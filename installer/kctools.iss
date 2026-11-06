; ---------------------------------------------------------------------------
;  KC Tools — Inno Setup kurulum betiği
;
;  Önce:  dotnet publish src\KCTools -c Release -r win-x64 --self-contained false -o dist\app
;  Sonra: "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\kctools.iss
;
;  Çıktı: dist\KCTools-Setup-1.0.0.exe
; ---------------------------------------------------------------------------

#define AppName "KC Tools"
#define AppVersion "1.0.0"
#define AppPublisher "KC Tools"
#define AppExeName "KCTools.exe"
#define SourceDir "..\dist\app"

[Setup]
AppId={{8F3A2C1B-4D5E-4F60-9A71-2C6B8E4D1F23}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=KCTools-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=..\src\KCTools\Assets\kctools.ico
MinVersion=10.0

[Languages]
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Masaüstü kısayolu oluştur"; GroupDescription: "Kısayollar:"
Name: "drivers"; Description: "Qualcomm / MediaTek sürücülerini kur (drivers klasörü)"; GroupDescription: "Sürücüler:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; platform-tools zaten dist\app\platform-tools içinde (csproj kopyalıyor)
Source: "..\drivers\*"; DestDir: "{app}\drivers"; Flags: ignoreversion recursesubdirs skipifsourcedoesntexist
Source: "..\docs\preview.png"; DestDir: "{app}\docs"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Günlük klasörü"; Filename: "%LOCALAPPDATA%\KCTools\Logs"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Sürücüler: pnputil 10 ile klasördeki tüm .inf dosyaları
Filename: "pnputil.exe"; Parameters: "/add-driver ""{app}\drivers"" /subdirs /install"; Flags: runhidden waituntilterminated; Tasks: drivers; StatusMsg: "Sürücüler kuruluyor..."
Filename: "{app}\{#AppExeName}"; Description: "{#AppName} başlat"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\platform-tools"

[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;

  // .NET 8 Masaüstü Çalışma Zamanı kontrolü (dotnet komutu üzerinden)
  if not Exec('dotnet', '--list-runtimes', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    if MsgBox('KC Tools için .NET 8 Masaüstü Çalışma Zamanı gerekli.' + #13#10 +
              'Şimdi indirme sayfası açılsın mı?', mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/8.0', '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);

    Result := False;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // Kullanıcı günlük klasörünü önceden oluştur (izin sorunlarını erken görürüz)
    ForceDirectories(ExpandConstant('{localappdata}\KCTools\Logs'));
  end;
end;
