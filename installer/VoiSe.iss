; VoiSee installer script
; Build with Inno Setup 6 using scripts\build-installer.ps1

#define MyAppName "VoiSee"
#define MyAppVersion "8.2.0"
#define MyAppPublisher "VoriX"
#define MyAppURL "https://github.com/VoriX0/VoiSe"
#define MyAppExeName "VoiSe.App.exe"

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
OutputDir=..\artifacts\installer
OutputBaseFilename=VoiSee-Setup-{#MyAppVersion}-x64
SetupIconFile=..\src\VoiSe.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
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
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
#ifdef VBCABLE_BUNDLED
Name: "installvbcable"; Description: "Install VB-CABLE virtual microphone bridge"; GroupDescription: "Virtual microphone bridge:"; Flags: unchecked
#endif

[Files]
Source: "..\artifacts\publish\VoiSe\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "settings.json,soundboard.json,voice-presets.json,data\*,sounds\*,presets\*,scenes\*,*.user,*.suo"

[Icons]
Name: "{autoprograms}\VoiSee"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\AppIcon.ico"
Name: "{autodesktop}\VoiSee"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\AppIcon.ico"; Tasks: desktopicon

[Run]
#ifdef VBCABLE_BUNDLED
Filename: "{code:GetVBCableInstaller}"; Description: "Install VB-CABLE"; StatusMsg: "Starting VB-CABLE installer..."; Flags: waituntilterminated; Tasks: installvbcable; Check: VBCableInstallerExists
#endif
Filename: "{app}\{#MyAppExeName}"; Description: "Launch VoiSee"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Keep user settings and sound library data in %LOCALAPPDATA%\VoiSe by default.
; The installer must not install developer/user categories, presets, scenes, or sounds.


[Code]
function GetVBCableInstaller(Param: String): String;
var
  Candidate: String;
begin
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

  Result := ExpandConstant('{app}\ThirdParty\VB-CABLE\VBCABLE_Setup_x64.exe');
end;

function VBCableInstallerExists(): Boolean;
begin
  Result := FileExists(GetVBCableInstaller(''));
end;
