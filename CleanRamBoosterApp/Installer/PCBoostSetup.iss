; PC Boost Installer Script (Inno Setup 6)
; Bundles self-contained win-x64 .NET 8 Runtime + ReadyToRun app into a single standalone setup.exe

#define MyAppName "PC Boost"
#define MyAppVersion "2.0.0"
#define MyAppPublisher "PC Boost"
#define MyAppExeName "CleanRamBoosterApp.exe"
#define MyPublishDir "..\publish\win-x64"

[Setup]
AppId={{7D8A4E21-9B3C-4F18-A2D9-4810F9E4A102}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableDirPage=no
AlwaysShowDirOnReadyPage=yes
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
OutputDir=..\..
OutputBaseFilename=PC-Boost-Setup
SetupIconFile=..\Resources\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern dynamic
ShowLanguageDialog=auto
AppMutex=Global\CleanRamBooster_SingleInstance_Mutex_99F
CloseApplications=force
RestartApplications=no
VersionInfoVersion=2.0.0.0
VersionInfoProductName={#MyAppName}
VersionInfoDescription={#MyAppName} Installer
#ifdef SignToolCmd
SignTool=pcboostsign
SignedUninstaller=yes
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Resources\app.ico"
Name: "{autoprograms}\{#MyAppName}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Resources\app.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/IM {#MyAppExeName} /F"; Flags: runhidden; RunOnceId: "KillPCBoostOnUninstall"
Filename: "schtasks.exe"; Parameters: "/delete /tn ""PCBoost_AutoStart_Admin"" /f"; Flags: runhidden; RunOnceId: "RemoveAdminTaskOnUninstall"
Filename: "reg.exe"; Parameters: "delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Run"" /v ""PCBoost"" /f"; Flags: runhidden; RunOnceId: "RemoveRunKeyPCBoost"
Filename: "reg.exe"; Parameters: "delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Run"" /v ""CleanRamSuite"" /f"; Flags: runhidden; RunOnceId: "RemoveRunKeyLegacy"
