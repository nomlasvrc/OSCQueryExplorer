#ifndef AppVersion
  #define AppVersion "0.1.1"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\framework-dependent"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

#define DotNetRuntimeVersion "10.0.12"
#define DotNetRuntimeFileName "windowsdesktop-runtime-" + DotNetRuntimeVersion + "-win-x64.exe"
#define DotNetRuntimeUrl "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/" + DotNetRuntimeVersion + "/" + DotNetRuntimeFileName
#define DotNetRuntimeSha256 "55a67d8476cde95a9cc43a95803b4f54446e7c9251ed22aa6421d4908174ae84"

[Setup]
AppId={{B3D097D5-0E9D-4ACC-AF25-08F5D48D6370}
AppName=OSCQuery Explorer
AppVersion={#AppVersion}
AppPublisher=nomlas
LicenseFile=..\LICENSE
DefaultDirName={autopf}\OSCQuery Explorer
DefaultGroupName=OSCQuery Explorer
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=OSCQueryExplorer-{#AppVersion}-win-x64-setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\OSCQueryExplorer.exe
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[CustomMessages]
english.RuntimeDownloading=Downloading Microsoft .NET 10 Desktop Runtime...
english.RuntimeInstalling=Installing Microsoft .NET 10 Desktop Runtime. A Windows confirmation may appear.
english.RuntimeDownloadFailed=Failed to download Microsoft .NET 10 Desktop Runtime.%n%n%1
english.RuntimeLaunchFailed=Failed to start the Microsoft .NET 10 Desktop Runtime installer. System error: %1
english.RuntimeInstallFailed=Microsoft .NET 10 Desktop Runtime installation failed with exit code %1.
english.RuntimeNotDetected=Microsoft .NET 10 Desktop Runtime could not be detected after installation.
japanese.RuntimeDownloading=Microsoft .NET 10 Desktop Runtimeをダウンロードしています...
japanese.RuntimeInstalling=Microsoft .NET 10 Desktop Runtimeをインストールしています。Windowsの確認画面が表示される場合があります。
japanese.RuntimeDownloadFailed=Microsoft .NET 10 Desktop Runtimeのダウンロードに失敗しました。%n%n%1
japanese.RuntimeLaunchFailed=Microsoft .NET 10 Desktop Runtimeインストーラーを起動できませんでした。%n%n%1
japanese.RuntimeInstallFailed=Microsoft .NET 10 Desktop Runtimeのインストールに失敗しました。終了コード: %1
japanese.RuntimeNotDetected=インストール後にMicrosoft .NET 10 Desktop Runtimeを検出できませんでした。

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\OSCQuery Explorer"; Filename: "{app}\OSCQueryExplorer.exe"
Name: "{autodesktop}\OSCQuery Explorer"; Filename: "{app}\OSCQueryExplorer.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Run]
Filename: "{app}\OSCQueryExplorer.exe"; Description: "{cm:LaunchProgram,OSCQuery Explorer}"; Flags: nowait postinstall skipifsilent

[Code]
const
  DotNetDesktopRuntimeRegistryKey = 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';

var
  DotNetRuntimeRestartRequired: Boolean;

function RegistryHasDotNet10DesktopRuntime(const RootKey: Integer): Boolean;
var
  Versions: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if not RegGetValueNames(RootKey, DotNetDesktopRuntimeRegistryKey, Versions) then
    Exit;

  for I := 0 to GetArrayLength(Versions) - 1 do
  begin
    if Pos('10.', Versions[I]) = 1 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function IsDotNet10DesktopRuntimeInstalled: Boolean;
begin
  { The .NET installer can register shared frameworks in either registry view. }
  Result :=
    RegistryHasDotNet10DesktopRuntime(HKEY_LOCAL_MACHINE_64) or
    RegistryHasDotNet10DesktopRuntime(HKEY_LOCAL_MACHINE_32);
end;

function OnRuntimeDownloadProgress(const Url, FileName: String;
  const Progress, ProgressMax: Int64): Boolean;
begin
  WizardForm.StatusLabel.Caption := CustomMessage('RuntimeDownloading');
  Result := True;
end;

function RunDotNetDesktopRuntimeInstaller(const FileName: String;
  var ResultCode: Integer): Boolean;
begin
  if IsAdminInstallMode then
  begin
    Result := Exec(FileName, '/install /quiet /norestart', '', SW_SHOW,
      ewWaitUntilTerminated, ResultCode);
  end
  else
  begin
    { The .NET runtime is installed machine-wide. Request elevation only for
      the prerequisite when OSCQuery Explorer itself is installed per-user. }
    Result := ShellExec('runas', FileName, '/install /quiet /norestart', '',
      SW_SHOW, ewWaitUntilTerminated, ResultCode);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  RuntimeInstaller: String;
  RuntimeResultCode: Integer;
begin
  Result := '';
  if IsDotNet10DesktopRuntimeInstalled then
    Exit;

  RuntimeInstaller := ExpandConstant('{tmp}\{#DotNetRuntimeFileName}');
  try
    WizardForm.StatusLabel.Caption := CustomMessage('RuntimeDownloading');
    DownloadTemporaryFile(
      '{#DotNetRuntimeUrl}',
      '{#DotNetRuntimeFileName}',
      '{#DotNetRuntimeSha256}',
      @OnRuntimeDownloadProgress);
  except
    Result := FmtMessage(CustomMessage('RuntimeDownloadFailed'), [GetExceptionMessage]);
    Exit;
  end;

  WizardForm.StatusLabel.Caption := CustomMessage('RuntimeInstalling');
  if not RunDotNetDesktopRuntimeInstaller(RuntimeInstaller,
    RuntimeResultCode) then
  begin
    Result := FmtMessage(CustomMessage('RuntimeLaunchFailed'), [
      SysErrorMessage(RuntimeResultCode)]);
    Exit;
  end;

  if (RuntimeResultCode = 3010) or (RuntimeResultCode = 1641) then
  begin
    DotNetRuntimeRestartRequired := True;
    Exit;
  end
  else if RuntimeResultCode <> 0 then
  begin
    Result := FmtMessage(CustomMessage('RuntimeInstallFailed'), [
      IntToStr(RuntimeResultCode)]);
    Exit;
  end;

  if not IsDotNet10DesktopRuntimeInstalled then
    Result := CustomMessage('RuntimeNotDetected');
end;

function NeedRestart: Boolean;
begin
  Result := DotNetRuntimeRestartRequired;
end;
