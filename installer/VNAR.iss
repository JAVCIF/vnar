#define MyAppName "VNAR"
#define MyAppVersion "1.0.0-beta.1.2"
#define MyAppPublisher "JAVCIF"
#define MyAppExeName "VNAR.exe"

[Setup]
AppId={{E70A6F5A-21F0-4DF9-96B7-317E05FC0432}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\VNAR
DefaultGroupName=VNAR
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=VNAR-Setup
SetupIconFile=..\LocaleGameHub\Resources\VNAR.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\dist\portable\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\VNAR"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; AppUserModelID: "JAVCIF.VNAR.Launcher"
Name: "{autodesktop}\VNAR"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon; AppUserModelID: "JAVCIF.VNAR.Launcher"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,VNAR}"; Flags: nowait postinstall skipifsilent
