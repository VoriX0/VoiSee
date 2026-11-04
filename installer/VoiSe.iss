; VoiSee installer script
; Build with Inno Setup 6 using scripts\build-installer.ps1

#define MyAppName "VoiSee"
#ifndef MyAppVersion
#define MyAppVersion "14.9.1"
#endif
#define MyAppPublisher "VoriX"
#define MyAppURL "https://github.com/VoriX0/VoiSe"
#define MyAppExeName "VoiSe.App.exe"

#ifndef PublishSource
#define PublishSource "..\artifacts\publish\VoiSe"
#endif

[Setup]
AppId={{A6F859F8-6D25-4A5F-A215-343D63B8B1C9}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\VoiSee
DefaultGroupName=VoiSee
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\artifacts\installer\{#MyAppVersion}
OutputBaseFilename=VoiSee-Setup-{#MyAppVersion}-x64
SetupIconFile=..\src\VoiSe.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
DirExistsWarning=no
UsePreviousAppDir=yes
DisableWelcomePage=no
DisableReadyPage=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
russian.DesktopShortcut=Создать ярлык на рабочем столе
english.DesktopShortcut=Create a desktop shortcut
russian.Shortcuts=Ярлыки:
english.Shortcuts=Additional shortcuts:
russian.InstallBridge=Установить виртуальный микрофон VB-CABLE
english.InstallBridge=Install VB-CABLE virtual microphone bridge
russian.BridgeGroup=Виртуальный микрофон:
english.BridgeGroup=Virtual microphone bridge:
russian.InstallCable=Установить VB-CABLE
english.InstallCable=Install VB-CABLE
russian.StartingCable=Запуск установщика VB-CABLE…
english.StartingCable=Starting VB-CABLE installer...
russian.LaunchApp=Запустить VoiSee
english.LaunchApp=Launch VoiSee

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; GroupDescription: "{cm:Shortcuts}"
#ifdef VBCABLE_BUNDLED
Name: "installvbcable"; Description: "{cm:InstallBridge}"; GroupDescription: "{cm:BridgeGroup}"
#endif

#include "obsolete-desktop-files.iss"

[Files]
Source: "{#PublishSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "settings.json,soundboard.json,voice-presets.json,data\*,sounds\*,presets\*,scenes\*,*.user,*.suo"

[Icons]
Name: "{autoprograms}\VoiSee"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\AppIcon.ico"
Name: "{autodesktop}\VoiSee"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\AppIcon.ico"; Tasks: desktopicon

[Run]
#ifdef VBCABLE_BUNDLED
Filename: "{code:GetVBCableInstaller}"; WorkingDir: "{code:GetVBCableInstallerDir}"; Description: "{cm:InstallCable}"; StatusMsg: "{cm:StartingCable}"; Flags: shellexec waituntilterminated; Verb: runas; Tasks: installvbcable; Check: VBCableInstallerExists
#endif
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent; Check: ShouldLaunchVoiSeePostInstall

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C reg.exe delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v VoiSee /f"; Flags: runhidden; RunOnceId: "RemoveVoiSeeAutostart"

[UninstallDelete]
; Keep user settings and sound library data in %LOCALAPPDATA%\VoiSe by default.
; The installer must not install developer/user categories, presets, scenes, or sounds.


[Code]
function IsPreviousVoiSeeInstall(): Boolean;
var
  Dependencies: AnsiString;
begin
  { Only clean a recognized VoiSee folder. The exact file list never includes
    settings, user sounds, presets or scenes. [InstallDelete] runs before copy. }
  Result := FileExists(ExpandConstant('{app}\VoiSe.App.exe')) and
            FileExists(ExpandConstant('{app}\VoiSe.App.dll')) and
            FileExists(ExpandConstant('{app}\VoiSe.Audio.dll'));
  if Result then
    Result := LoadStringFromFile(ExpandConstant('{app}\VoiSe.App.deps.json'), Dependencies);
  if Result then
    Result := Pos('"VoiSe.App/', String(Dependencies)) > 0;
end;

function GetVBCableInstaller(Param: String): String;
var
  Candidate: String;
begin
  { Prefer the fully extracted VB-CABLE package. Running a copied setup EXE alone fails,
    because VB-CABLE setup expects its INF/CAT/SYS files next to the EXE. }
  Candidate := ExpandConstant('{app}\ThirdParty\VB-CABLE\_extracted\VBCABLE_Setup_x64.exe');
  if FileExists(Candidate) then
  begin
    Result := Candidate;
    exit;
  end;

  Candidate := ExpandConstant('{app}\ThirdParty\VB-CABLE\_extracted\VBCABLE_Setup.exe');
  if FileExists(Candidate) then
  begin
    Result := Candidate;
    exit;
  end;

  Candidate := ExpandConstant('{app}\ThirdParty\VB-CABLE\VBCABLE_Setup_x64.exe');
  if FileExists(Candidate) then
  begin
    Result := Candidate;
    exit;
  end;

  Candidate := ExpandConstant('{app}\ThirdParty\VB-CABLE\VBCABLE_Setup.exe');
  if FileExists(Candidate) then
  begin
    Result := Candidate;
    exit;
  end;

  Result := ExpandConstant('{app}\ThirdParty\VB-CABLE\_extracted\VBCABLE_Setup_x64.exe');
end;

function GetVBCableInstallerDir(Param: String): String;
begin
  Result := ExtractFileDir(GetVBCableInstaller(''));
end;

function VBCableInstallerExists(): Boolean;
begin
  Result := FileExists(GetVBCableInstaller(''));
end;

function ShouldLaunchVoiSeePostInstall(): Boolean;
begin
#ifdef VBCABLE_BUNDLED
  { VB-CABLE usually requires a Windows restart before the CABLE devices become visible.
    Do not auto-launch VoiSee immediately after driver installation, otherwise the app
    will correctly report that the bridge is still missing. }
  Result := not WizardIsTaskSelected('installvbcable');
#else
  Result := True;
#endif
end;

function NeedRestart(): Boolean;
begin
#ifdef VBCABLE_BUNDLED
  Result := WizardIsTaskSelected('installvbcable');
#else
  Result := False;
#endif
end;
