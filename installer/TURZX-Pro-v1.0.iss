#define MyAppName "TURZX Pro 4.6"
#define MyAppVersion "1.0.0"
#define MyAppExeName "Turzx.ControlPanel.Wpf.exe"

[Setup]
AppId={{6D672A0D-2A0F-4F51-98A7-FDA4D9FD09C7}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v1.0
AppPublisher=Mclarenlife
AppPublisherURL=https://github.com/Mclarenlife/TURZX-Pro-4.6
AppSupportURL=https://github.com/Mclarenlife/TURZX-Pro-4.6/issues
AppUpdatesURL=https://github.com/Mclarenlife/TURZX-Pro-4.6/releases
DefaultDirName={autopf}\TURZX Pro 4.6
DefaultGroupName=TURZX Pro 4.6
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=TURZX-Pro-v1.0-Setup
SetupIconFile=..\src\Turzx.Wpf\Assets\LOGO3.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no
VersionInfoVersion=1.0.0.0
VersionInfoProductName={#MyAppName}
VersionInfoDescription=TURZX 4.6-inch USB display control software

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked

[Files]
Source: "..\release\TURZX-Pro-v1.0\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\TURZX Pro 4.6"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\TURZX Pro 4.6"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 TURZX Pro 4.6"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\sensor-service\Turzx.SensorService.exe"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "RemoveTurzxSensorService"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run',
      'TURZX Screen Control');
end;
