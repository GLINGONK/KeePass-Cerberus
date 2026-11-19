; KeePass Cerberus - Inno Setup script
; Build: ISCC.exe installer\KeePassCerberus.iss  (output: distrib\installer)

#define AppName "KeePass Cerberus"
#define AppVersion "1.0.1"
#define AppExe "KeePassCerberus.exe"
#define SrcDir "..\distrib\app"

[Setup]
AppId={{6B1E2A7C-4F3D-4C1B-9E8A-CE7B5A11D0C4}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=KeePass Cerberus
AppCopyright=KeePass (c) Dominik Reichl - KeePass Cerberus fork - GPL v2+
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
; The user chooses the installation folder during setup
DisableDirPage=no
DisableProgramGroupPage=yes
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=6.1sp1
LicenseFile={#SrcDir}\License.txt
SetupIconFile=..\assets\KeePassCerberus.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=..\distrib\installer
OutputBaseFilename=KeePassCerberus-{#AppVersion}-setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ChangesAssociations=yes
CloseApplications=yes

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"
Name: "dutch"; MessagesFile: "compiler:Languages\Dutch.isl"
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "assockdbx"; Description: "Ouvrir les fichiers .kdbx avec KeePass Cerberus"; GroupDescription: "Associations :"; Flags: unchecked

[Files]
Source: "{#SrcDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{#AppName} (interface classique)"; Filename: "{app}\classic\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\.kdbx\OpenWithProgids"; ValueType: string; ValueName: "KeePassCerberus.kdbx"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assockdbx
Root: HKA; Subkey: "Software\Classes\KeePassCerberus.kdbx"; ValueType: string; ValueName: ""; ValueData: "Base KeePass"; Flags: uninsdeletekey; Tasks: assockdbx
Root: HKA; Subkey: "Software\Classes\KeePassCerberus.kdbx\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"; Tasks: assockdbx
Root: HKA; Subkey: "Software\Classes\KeePassCerberus.kdbx\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: assockdbx

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
// .NET Framework 4.8 (Release >= 528040) is required. Built into Windows 10 1903+ / 11;
// must be installed on Windows 7 SP1 / 8.1 / older Windows 10.
function IsDotNet48Installed(): Boolean;
var
  Rel: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Rel) and (Rel >= 528040);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not IsDotNet48Installed() then
  begin
    if MsgBox('KeePass Cerberus nécessite Microsoft .NET Framework 4.8, qui n''est pas installé sur ce PC.' + #13#10#13#10 +
              'Installez-le depuis le site de Microsoft (gratuit), puis relancez cette installation.' + #13#10#13#10 +
              'Continuer quand même ?', mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;
