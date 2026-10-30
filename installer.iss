; The version is defined once in RoAura.csproj. build-installer.ps1 reads it
; and passes /DMyAppVersion=<version> to ISCC; the fallback below only exists so
; the script still compiles when opened directly in the Inno Setup IDE.
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#define MyAppName "RoAura"
#define MyAppPublisher "Alerion"
#define MyAppExeName "RoAura.exe"
#define NpcapUrl "https://npcap.com/#download"

[Setup]
AppId={{7A9883C5-B2B8-4BEC-9066-C44D8BE8DB56}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://twitch.tv/im_alerion
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=artifacts
OutputBaseFilename=RoAura-{#MyAppVersion}-Setup
SetupIconFile=Assets\roaura.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=yes
; Must match the mutex name created in App.xaml.cs so Setup waits for a
; running RoAura instead of overwriting a locked executable.
AppMutex=Local\RoAura.SingleInstance
SetupMutex=RoAuraSetupMutex

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Everything comes from artifacts\publish, which build-release.ps1 fills
; (publish output + README/GUIDE/RELEASE_NOTES copies) and purges of private
; files before the installer is compiled.
Source: "artifacts\publish\RoAura.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "artifacts\publish\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "artifacts\publish\GUIDE.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "artifacts\publish\GUIDE_RU.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "artifacts\publish\GUIDE_EN.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "artifacts\publish\RELEASE_NOTES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "artifacts\publish\Data\*.json"; DestDir: "{app}\Data"; Flags: ignoreversion
; The offline ToC database is SQLite, not JSON; include it in installed builds.
Source: "artifacts\publish\Data\*.sqlite"; DestDir: "{app}\Data"; Flags: ignoreversion
Source: "artifacts\publish\Data\MvpImages\*.png"; DestDir: "{app}\Data\MvpImages"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{autoprograms}\RoAura"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\RoAura"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Description: "{cm:LaunchProgram,RoAura}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\RoAura\Updates"

[Code]
// Npcap is the only external runtime requirement. Warn (do not block) when it
// is missing so the user is not surprised by the in-app Npcap dialog later.
function NpcapInstalled(): Boolean;
begin
  Result := FileExists(ExpandConstant('{sys}\Npcap\wpcap.dll'))
    or FileExists(ExpandConstant('{sys}\wpcap.dll'));
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not NpcapInstalled() then
  begin
    if ActiveLanguage() = 'russian' then
      MsgBox('Npcap не найден. RoAura не сможет читать пакеты игры без него.' + #13#10 +
             'Установка продолжится; после неё скачайте и установите Npcap:' + #13#10 +
             '{#NpcapUrl}', mbInformation, MB_OK)
    else
      MsgBox('Npcap was not found. RoAura cannot read game packets without it.' + #13#10 +
             'Setup will continue; afterwards download and install Npcap from:' + #13#10 +
             '{#NpcapUrl}', mbInformation, MB_OK);
  end;
end;
