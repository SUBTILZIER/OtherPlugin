#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "artifacts\stage"
#endif
#ifndef ProjectRoot
  #define ProjectRoot ".."
#endif
#ifndef InstallerOutputDir
  #define InstallerOutputDir "artifacts\release"
#endif
#ifndef InstallerBaseName
  #define InstallerBaseName "AutomationStudio-Setup"
#endif

[Setup]
AppId={{DA1B9FE1-FE96-460D-8C7E-E5F4E71338AD}
AppName=AutomationStudio
AppVersion={#MyAppVersion}
AppVerName=AutomationStudio {#MyAppVersion}
AppPublisher=SUBTILZIER
DefaultDirName={localappdata}\Programs\AutomationStudio
DefaultGroupName=AutomationStudio
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#InstallerOutputDir}
OutputBaseFilename={#InstallerBaseName}
SetupIconFile={#ProjectRoot}\Resources\AutomationStudio.ico
UninstallDisplayIcon={app}\AutomationStudioWpf.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no
SetupLogging=yes
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany=SUBTILZIER
VersionInfoDescription=AutomationStudio Installer
VersionInfoProductName=AutomationStudio
VersionInfoProductVersion={#MyAppVersion}
MinVersion=10.0.17763
UsedUserAreasWarning=no

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\AutomationStudio"; Filename: "{app}\AutomationStudioWpf.exe"
Name: "{autodesktop}\AutomationStudio"; Filename: "{app}\AutomationStudioWpf.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\AutomationStudioWpf.exe"; Description: "启动 AutomationStudio"; Flags: nowait postinstall skipifsilent

[Code]
const
  ApplicationMutex = 'Local\SUBTILZIER.AutomationStudioWpf.SingleInstance';

function IsAutomationStudioRunning(): Boolean;
begin
  Result := CheckForMutexes(ApplicationMutex);
end;

function RequestApplicationShutdown(): Boolean;
var
  ResultCode: Integer;
  ExecutablePath: String;
begin
  Result := False;
  ExecutablePath := ExpandConstant('{app}\AutomationStudioWpf.exe');
  if not FileExists(ExecutablePath) then
    Exit;

  Result := Exec(
    ExecutablePath,
    '--shutdown-for-update',
    ExpandConstant('{app}'),
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode);
end;

function WaitForApplicationExit(): Boolean;
var
  Counter: Integer;
begin
  for Counter := 1 to 120 do
  begin
    if not IsAutomationStudioRunning() then
    begin
      Result := True;
      Exit;
    end;
    Sleep(500);
  end;
  Result := not IsAutomationStudioRunning();
end;

function EnsureApplicationStopped(): String;
begin
  Result := '';
  if not IsAutomationStudioRunning() then
    Exit;

  if not RequestApplicationShutdown() then
  begin
    Result := 'AutomationStudio 正在运行，且无法发送安全退出请求。请先在程序中退出，再重试。';
    Exit;
  end;

  if not WaitForApplicationExit() then
    Result := 'AutomationStudio 未在 60 秒内退出。可能取消了未保存资产确认；安装已中止，未覆盖任何程序文件。';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := EnsureApplicationStopped();
end;

function InitializeUninstall(): Boolean;
var
  ErrorMessage: String;
begin
  ErrorMessage := EnsureApplicationStopped();
  Result := ErrorMessage = '';
  if not Result then
    MsgBox(ErrorMessage, mbError, MB_OK);
end;
