#define MyAppName "TimeBet"
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#define MyAppPublisher "TimeBet"
#define MyAppExeName "TimeBet.Desktop.exe"

[Setup]
; Keep this AppId stable across releases so a newer setup upgrades the existing installation.
AppId={{C83177F5-76E4-49A7-9B59-FA7E632355DE}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoDescription={#MyAppName} installer
DefaultDirName={localappdata}\Programs\TimeBet
DefaultGroupName=TimeBet
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\output\installer
OutputBaseFilename=TimeBet-Setup-{#MyAppVersion}-win-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE
Uninstallable=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: checkedonce

[Files]
; Keep this first: PrepareToInstall extracts it before the main payload is decompressed.
Source: ".cache\MicrosoftEdgeWebview2Setup.exe"; Flags: dontcopy noencryption
Source: "..\output\TimeBet-win-x64-single\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\TimeBet"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Uninstall TimeBet"; Filename: "{uninstallexe}"
Name: "{userdesktop}\TimeBet"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch TimeBet"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; WebView2 creates this disposable browser cache beside the executable by default.
; User data under %LocalAppData%\TimeBet is intentionally retained on uninstall.
Type: filesandordirs; Name: "{app}\{#MyAppExeName}.WebView2"

[Code]
const
  WebView2ClientId = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

function IsUsableWebView2Version(const Version: String): Boolean;
begin
  Result :=
    (Trim(Version) <> '') and
    (CompareText(Trim(Version), '0.0.0.0') <> 0);
end;

function IsWebView2Installed(): Boolean;
var
  Version: String;
begin
  Result := False;

  { Per-user Evergreen Runtime. }
  if RegQueryStringValue(
    HKCU,
    'Software\Microsoft\EdgeUpdate\Clients\' + WebView2ClientId,
    'pv',
    Version) and IsUsableWebView2Version(Version) then
  begin
    Result := True;
    Exit;
  end;

  { Per-machine Evergreen Runtime on 64-bit Windows. }
  if RegQueryStringValue(
    HKLM,
    'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\' + WebView2ClientId,
    'pv',
    Version) and IsUsableWebView2Version(Version) then
  begin
    Result := True;
    Exit;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  BootstrapperPath: String;
begin
  Result := '';
  NeedsRestart := False;

  if IsWebView2Installed() then
    Exit;

  ExtractTemporaryFile('MicrosoftEdgeWebview2Setup.exe');
  BootstrapperPath := ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe');

  if not Exec(
    BootstrapperPath,
    '/silent /install',
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) then
  begin
    Result := 'Unable to start the Microsoft Edge WebView2 Runtime installer.';
    Exit;
  end;

  if ResultCode <> 0 then
  begin
    Result :=
      'Microsoft Edge WebView2 Runtime installation failed with exit code ' +
      IntToStr(ResultCode) + '. Check the network connection and try again.';
    Exit;
  end;

  if not IsWebView2Installed() then
  begin
    Result :=
      'WebView2 Runtime is still unavailable. Restart Windows and run ' +
      'the TimeBet installer again.';
  end;
end;
