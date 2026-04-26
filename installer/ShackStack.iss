#ifndef MyAppName
  #define MyAppName "ShackStack"
#endif
#ifndef MyAppVersion
  #define MyAppVersion "1.0"
#endif
#ifndef MyAppPublisher
  #define MyAppPublisher "ShackStack"
#endif
#ifndef MyAppExeName
  #define MyAppExeName "ShackStack.Desktop.exe"
#endif
#ifndef MySourceDir
  #define MySourceDir "..\\publish\\ShackStack-win-x64-v1.0"
#endif
#ifndef MyIconFile
  #define MyIconFile "..\\src\\ShackStack.Desktop\\Assets\\shackstack.ico"
#endif
#ifndef MyOutputBaseFilename
  #define MyOutputBaseFilename "ShackStack-Setup-v1.0"
#endif

[Setup]
AppId={{E8E76384-BA95-4F5F-9BE0-3D8F329EAF01}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\ShackStack
DefaultGroupName=ShackStack
AllowNoIcons=yes
OutputDir=..\publish
OutputBaseFilename={#MyOutputBaseFilename}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
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
