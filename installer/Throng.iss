; Throng installer (Inno Setup 6). Built by build\publish.ps1 (or publish.bat), which passes /DAppVersion from
; Directory.Build.props and compiles from the self-contained publish it has just made in artifacts\app\.
; Round 026, D-407 to D-418. Per user, no admin rights.
;
;   App:        %LocalAppData%\Programs\Throng\ (fixed, so [InstallDelete] can safely start the folder clean)
;   .throng:    opens in Throng (HKCU\Software\Classes, removed on uninstall); the other Throng files are imported from
;               inside the app
;   Shortcuts:  Start menu always; desktop as an option, unticked
;   Kept:       %AppData%\Throng (your library) and %LocalAppData%\Throng\Logs are never removed by uninstall
;
; AppId never changes: it is how a newer setup finds and upgrades an older install.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#define AppExe "Throng.exe"
#define ProgId "Throng.Study"

[Setup]
AppId={{4F693B47-3FE5-43E3-B724-77382442AD08}
AppName=Throng
AppVersion={#AppVersion}
AppVerName=Throng {#AppVersion}
AppPublisher=Gavin Nicholls
AppPublisherURL=https://github.com/aussieBIMguru/Throng
AppSupportURL=https://github.com/aussieBIMguru/Throng/issues
AppUpdatesURL=https://github.com/aussieBIMguru/Throng/releases
AppCopyright=Copyright (c) 2026 Gavin Nicholls (MIT)
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName=Throng
VersionInfoDescription=Throng setup
DefaultDirName={localappdata}\Programs\Throng
DefaultGroupName=Throng
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; MIT needs no acceptance: shown as an information page (D-412)
InfoBeforeFile=..\LICENSE
SetupIconFile=..\src\Throng.App\Assets\throng.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName=Throng {#AppVersion}
OutputBaseFilename=Throng-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no
ChangesAssociations=yes
UsePreviousTasks=yes
; Throng running is checked in [Code]: it's never closed for the user (unsaved work)
CloseApplications=no
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[InstallDelete]
; The app folder holds nothing but the app: start clean (an older self-contained build may leave dlls a new one doesn't use)
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "..\artifacts\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Throng"; Filename: "{app}\{#AppExe}"; Comment: "Conceptual pedestrian simulation"
Name: "{autodesktop}\Throng"; Filename: "{app}\{#AppExe}"; Comment: "Conceptual pedestrian simulation"; Tasks: desktopicon

[Registry]
; .throng opens in Throng (per user, D-409). The extension key keeps any other app's values; only ours are removed.
Root: HKCU; Subkey: "Software\Classes\.throng"; ValueType: string; ValueName: ""; ValueData: "{#ProgId}"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.throng\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueName: ""; ValueData: "Throng study"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\{#ProgId}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"
Root: HKCU; Subkey: "Software\Classes\{#ProgId}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "Throng"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".throng"; ValueData: ""

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start Throng"; Flags: postinstall nowait skipifsilent unchecked

[UninstallDelete]
; Only the app folder. %AppData%\Throng (library) and %LocalAppData%\Throng\Logs are kept (D-410).
Type: filesandordirs; Name: "{app}"

[Code]
function IsProcessRunning(const FileName: String): Boolean;
var
  Locator, Service, Items: Variant;
begin
  Result := False;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Items := Service.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = "' + FileName + '"');
    Result := Items.Count > 0;
  except
    Result := False; { can't tell: carry on (a locked file still makes setup ask) }
  end;
end;

{ Throng.exe holds the files this setup replaces: ask the user to close it (never closed for them) }
function ThrongClosed(const What: String): Boolean;
begin
  Result := True;
  while IsProcessRunning('{#AppExe}') do
  begin
    if MsgBox('Please close Throng before ' + What + ' (save your study first).' + #13#10#13#10 +
              'Then choose Retry.', mbError, MB_RETRYCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

function InitializeSetup: Boolean;
begin
  if WizardSilent then
    Result := not IsProcessRunning('{#AppExe}')
  else
    Result := ThrongClosed('installing');
end;

function InitializeUninstall: Boolean;
begin
  if UninstallSilent then
    Result := not IsProcessRunning('{#AppExe}')
  else
    Result := ThrongClosed('removing it');
end;
