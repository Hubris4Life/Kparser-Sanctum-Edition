#define MyAppName "KParser - Sanctum Edition"
#define MyAppVersion "Preview 22"
#define MyAppExeName "KParser-Sanctum-Modern.exe"

[Setup]
AppId={{9028E6D2-CEFE-498A-B3E5-87CBF37EA047}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=Sanctum Edition contributors
AppPublisherURL=https://github.com/Hubris4Life/Kparser-Sanctum-Edition
AppSupportURL=https://github.com/Hubris4Life/Kparser-Sanctum-Edition/issues
AppUpdatesURL=https://github.com/Hubris4Life/Kparser-Sanctum-Edition/releases
DefaultDirName={localappdata}\Programs\KParser Sanctum Modern
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes
PrivilegesRequired=lowest
SetupArchitecture=x64
MinVersion=10.0.17763
OutputDir=output
OutputBaseFilename=KParser-Sanctum-Setup-Preview-22
SetupIconFile=..\src\legacy-engine\FFXILogParser\Gobby.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
VersionInfoVersion=0.22.0.0
VersionInfoProductName={#MyAppName}
VersionInfoDescription=Installs KParser - Sanctum Edition and its complete parser engine
VersionInfoCompany=Sanctum Edition contributors
VersionInfoCopyright=Copyright (C) 2026 Sanctum Edition contributors

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "payload\current\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
