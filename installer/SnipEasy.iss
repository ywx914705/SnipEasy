; SnipEasy Inno Setup Script
; Requires Inno Setup 6 (https://jrsoftware.org/isinfo.php)
; Build script passes /DSourceDir=<path> to point at the staged app files.

#ifndef SourceDir
  #define SourceDir "..\SnipEasy.App\bin\Release\net9.0-windows"
#endif

#ifndef AppIcon
  #define AppIcon "..\SnipEasy.App\Assets\SnipEasy.ico"
#endif

#ifndef LicenseFile
  #define LicenseFile "..\LICENSE"
#endif

#ifndef OutputDir
  #define OutputDir "..\website\downloads"
#endif

#ifndef OutputBaseFilename
  #define OutputBaseFilename "SnipEasy-Setup"
#endif

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName "SnipEasy"
#define AppPublisher "SnipEasy"
#define AppURL "https://snipe.cc.cd"
#define AppExeName "SnipEasy.exe"

[Setup]
AppId={{B2F7D4A1-8E3C-4F59-A1D6-9C8B7E2F3A10}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
LicenseFile={#LicenseFile}
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
SetupIconFile={#AppIcon}
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
CloseApplications=yes
RestartApplications=no
AppMutex=SnipEasy.Desktop.SingleInstance

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
Source: "{#SourceDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#SourceDir}\*.json"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#SourceDir}\runtimes\*"; DestDir: "{app}\runtimes"; Flags: ignoreversion skipifsourcedoesntexist recursesubdirs createallsubdirs
#ifdef FfmpegDir
Source: "{#FfmpegDir}\ffmpeg.exe"; DestDir: "{app}\tools\ffmpeg"; Flags: ignoreversion
#endif

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent unchecked

[Registry]
; Register in Windows Apps & Features
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppName}_is1"; ValueType: string; ValueName: "DisplayIcon"; ValueData: "{app}\{#AppExeName}"; Flags: uninsdeletekey

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
Type: filesandordirs; Name: "{localappdata}\SnipEasy\RecordingDrafts"

[Code]
function IsDotNet9DesktopRuntimeInstalled: Boolean;
var
  ResultCode: Integer;
  Output: AnsiString;
  TempFile: String;
  ExecResult: Boolean;
begin
  Result := False;
  TempFile := ExpandConstant('{tmp}\dotnet_check.txt');

  ExecResult := Exec(
    ExpandConstant('{cmd}'),
    '/c dotnet --list-runtimes > "' + TempFile + '" 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if ExecResult and (ResultCode = 0) then
  begin
    if LoadStringFromFile(TempFile, Output) then
    begin
      if Pos('Microsoft.WindowsDesktop.App 9.', String(Output)) > 0 then
      begin
        Result := True;
      end;
    end;
  end;

  if FileExists(TempFile) then
    DeleteFile(TempFile);
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;

  if not IsDotNet9DesktopRuntimeInstalled then
  begin
    if MsgBox(
      'SnipEasy 需要 Microsoft .NET 9 Desktop Runtime 才能运行。' + #13#10 + #13#10 +
      '点击"是"将打开下载页面，请安装运行时后重新运行此安装程序。' + #13#10 +
      '点击"否"取消安装。',
      mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/9.0', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;
    Result := False;
  end;
end;
