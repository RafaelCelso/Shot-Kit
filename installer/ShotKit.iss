; Instalador do ShotKit (Inno Setup 6).
; Não compile direto: use installer\build.ps1, que publica o app e passa
; AppVersion e SourceDir para este script.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif

#define AppName "ShotKit"
#define AppExe "ShotKit.exe"

[Setup]
; Não altere o AppId: é ele que liga versões novas à instalação existente.
AppId={{B6E2F4A1-6C3D-4E8B-9A57-2F1D0C8E7A34}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
VersionInfoVersion={#AppVersion}
; Instala só para o usuário atual, sem pedir administrador
; (vai para %LocalAppData%\Programs\ShotKit).
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 2004+ (exigido pelo OCR do Windows).
MinVersion=10.0.19041
; Pede para fechar o ShotKit se ele estiver aberto (mesmo mutex do App.xaml.cs).
AppMutex=Local\ShotKit-SingleInstance-7C1E2B9A
CloseApplications=yes
SetupIconFile=..\src\ShotKit\ShotKit.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=..\artifacts\installer
OutputBaseFilename=ShotKit-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
// A opção "Iniciar com o Windows" do app grava em HKCU\...\Run; remove ao desinstalar,
// mas só se a entrada apontar para esta instalação (não mexe em outra cópia do app).
const RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Value: String;
begin
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKEY_CURRENT_USER, RunKey, 'ShotKit', Value) then
      if Pos(Lowercase(ExpandConstant('{app}\{#AppExe}')), Lowercase(Value)) > 0 then
        RegDeleteValue(HKEY_CURRENT_USER, RunKey, 'ShotKit');
end;
