#ifndef AppVersion
  #error AppVersion must be supplied by the build script
#endif

#ifndef SourceDir
  #error SourceDir must be supplied by the build script
#endif

#ifndef SetupIcon
  #error SetupIcon must be supplied by the build script
#endif

#ifndef AppFileVersion
  #error AppFileVersion must be supplied by the build script
#endif

#define AppName "CrashScope"
#define AppExe "CrashScope.exe"

[Setup]
AppId={{08FC71E2-02C3-4A5A-B5EA-F9302EADE31A}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=CrashScope
DefaultDirName={localappdata}\Programs\CrashScope
DefaultGroupName=CrashScope
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
SetupArchitecture=x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=CrashScope-Setup-{#AppVersion}
SetupIconFile={#SetupIcon}
UninstallDisplayIcon={app}\desktop\CrashScope.Desktop.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern dynamic
CloseApplications=yes
CloseApplicationsFilter=CrashScope.exe,CrashScope.Desktop.exe
RestartApplications=no
RestartIfNeededByRun=no
SignedUninstaller=no
ChangesEnvironment=no
VersionInfoVersion={#AppFileVersion}
VersionInfoProductName=CrashScope
VersionInfoDescription=CrashScope Setup
VersionInfoCompany=CrashScope

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\CrashScope"; Filename: "{app}\CrashScope.exe"; WorkingDir: "{app}"; IconFilename: "{app}\desktop\CrashScope.Desktop.exe"
Name: "{userdesktop}\CrashScope"; Filename: "{app}\CrashScope.exe"; WorkingDir: "{app}"; IconFilename: "{app}\desktop\CrashScope.Desktop.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\CrashScope.exe"; WorkingDir: "{app}"; Description: "Launch CrashScope"; Flags: nowait postinstall skipifsilent

; CrashScopeStartupCleanup
[Code]
const
  CrashScopeStartupCleanupRunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  CrashScopeStartupCleanupValueName = 'CrashScope';

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  CurrentCommand: String;
  ExpectedCommand: String;
begin
  if CurUninstallStep <> usUninstall then
    Exit;

  if not RegQueryStringValue(
    HKCU,
    CrashScopeStartupCleanupRunKey,
    CrashScopeStartupCleanupValueName,
    CurrentCommand) then
    Exit;

  ExpectedCommand :=
    '"' + ExpandConstant('{app}\CrashScope.exe') + '" --no-browser';

  if CurrentCommand = ExpectedCommand then
    RegDeleteValue(
      HKCU,
      CrashScopeStartupCleanupRunKey,
      CrashScopeStartupCleanupValueName);
end;
