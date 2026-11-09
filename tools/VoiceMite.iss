#ifndef SourceDir
  #error SourceDir must point to the self-contained publish directory.
#endif
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{B09FDC83-04D7-4515-94D6-54776D0E9196}
AppName=VoiceMite
AppVersion={#AppVersion}
AppPublisher=VoiceMite contributors
AppMutex=Local\VoiceInk.Desktop.v1
DefaultDirName={localappdata}\Programs\VoiceMite
DefaultGroupName=VoiceMite
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=VoiceMite-Setup-{#AppVersion}-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\VoiceMite.exe
CloseApplications=no
RestartApplications=no
SetupLogging=no
LicenseFile={#SourceDir}\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; Remove only known binaries/shortcuts of this same AppId during a VoiceInk upgrade.
; Never delete {app}, a profile directory, or an arbitrary computed path recursively.
Type: files; Name: "{app}\VoiceInk.exe"
Type: files; Name: "{app}\VoiceInk.dll"
Type: files; Name: "{app}\VoiceInk.deps.json"
Type: files; Name: "{app}\VoiceInk.runtimeconfig.json"
Type: files; Name: "{app}\VoiceInk.Core.dll"
Type: files; Name: "{app}\VoiceInk.Windows.dll"
Type: files; Name: "{autoprograms}\VoiceInk.lnk"
Type: files; Name: "{autodesktop}\VoiceInk.lnk"

[Icons]
Name: "{autoprograms}\VoiceMite"; Filename: "{app}\VoiceMite.exe"
Name: "{autodesktop}\VoiceMite"; Filename: "{app}\VoiceMite.exe"; Tasks: desktopicon

[CustomMessages]
english.DeleteData=Also delete this Windows user's VoiceMite settings, encrypted API keys and language preferences, including the compatible legacy VoiceInk profile? This deletes both VoiceMite and VoiceInk profile folders and cannot be undone. Choose No to keep them. Demo profiles are kept separately.
chinesesimplified.DeleteData=是否同时删除本 Windows 用户的 VoiceMite 设置、加密 API 密钥及语言偏好（包括兼容的旧 VoiceInk 配置）？这会删除 VoiceMite 和 VoiceInk 两个正式配置文件夹，不可撤销。选择“否”保留这些数据。演示配置单独保留。

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    if not UninstallSilent then
      if MsgBox(CustomMessage('DeleteData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        DelTree(ExpandConstant('{localappdata}\VoiceMite'), True, True, True);
        DelTree(ExpandConstant('{localappdata}\VoiceInk'), True, True, True);
      end;
end;
