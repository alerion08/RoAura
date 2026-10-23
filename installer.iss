#define MyAppName "RoAura"
#define MyAppVersion "2.12.2"
#define MyAppPublisher "Alerion"
#define MyAppExeName "RoAura.exe"

[Setup]
AppId={{7A9883C5-B2B8-4BEC-9066-C44D8BE8DB56}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://twitch.tv/im_alerion
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=artifacts
OutputBaseFilename=RoAura-{#MyAppVersion}-Setup
SetupIconFile=Assets\roaura.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "artifacts\publish\RoAura.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "GUIDE.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "GUIDE_RU.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "GUIDE_EN.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "RELEASE_NOTES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "artifacts\publish\Data\monsters.rtm.json"; DestDir: "{app}\Data"; Flags: ignoreversion
Source: "artifacts\publish\Data\monsters.springro.json"; DestDir: "{app}\Data"; Flags: ignoreversion
Source: "artifacts\publish\Data\items.rtm.json"; DestDir: "{app}\Data"; Flags: ignoreversion
Source: "artifacts\publish\Data\items.springro.json"; DestDir: "{app}\Data"; Flags: ignoreversion
Source: "artifacts\publish\Data\database.springro.json"; DestDir: "{app}\Data"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\RoAura"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\RoAura"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Description: "{cm:LaunchProgram,RoAura}"; Flags: nowait postinstall skipifsilent shellexec
