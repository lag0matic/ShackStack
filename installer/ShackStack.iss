#define MyAppName "ShackStack"
#define MyAppVersion "V-0.1 BETA"
#define MyAppPublisher "ShackStack"
#define MyAppExeName "ShackStack.Desktop.exe"
#define MySourceDir "..\\publish\\ShackStack-win-x64-v0.1-beta"
#define MyIconFile "..\\src\\ShackStack.Desktop\\Assets\\shackstack.ico"

[Setup]
AppId={{E8E76384-BA95-4F5F-9BE0-3D8F329EAF01}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\ShackStack
DefaultGroupName=ShackStack
AllowNoIcons=yes
OutputDir=..\publish
OutputBaseFilename=ShackStack-Setup-v0.1-beta
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile={#MyIconFile}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ShackStack"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall ShackStack"; Filename: "{uninstallexe}"
Name: "{autodesktop}\ShackStack"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch ShackStack"; Flags: nowait postinstall skipifsilent
