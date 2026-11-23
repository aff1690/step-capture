; Inno Setup script for StepCapture.
; Built by .github/workflows/build.yml:  ISCC /DAppVersion=1.2.3 /DSourceDir=..\publish\installer installer\StepCapture.iss

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\installer"
#endif

#define AppName "StepCapture"
#define AppExe "StepCapture.exe"

[Setup]
; Keep this AppId forever: Windows uses it to recognize upgrades of the same app.
AppId={{6F2B3C1E-6A4D-4E8B-9C1F-5A7D2E9B4C31}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=StepCapture
VersionInfoVersion={#AppVersion}
; Installs for the current user without needing admin rights; the installer offers "all users" as an option.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts
OutputBaseFilename=StepCapture-Setup-{#AppVersion}
SetupIconFile=..\src\StepCapture.App\Assets\StepCapture.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ChangesAssociations=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "association"; Description: "Open .stepguide files with StepCapture"; GroupDescription: "File types:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; .stepguide file association (per user, or machine-wide when installed for all users).
Root: HKA; Subkey: "Software\Classes\.stepguide"; ValueType: string; ValueName: ""; ValueData: "StepCapture.Guide"; Flags: uninsdeletevalue; Tasks: association
Root: HKA; Subkey: "Software\Classes\StepCapture.Guide"; ValueType: string; ValueName: ""; ValueData: "StepCapture guide"; Flags: uninsdeletekey; Tasks: association
Root: HKA; Subkey: "Software\Classes\StepCapture.Guide\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"; Tasks: association
Root: HKA; Subkey: "Software\Classes\StepCapture.Guide\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: association

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

; Settings and logs in %LOCALAPPDATA%\StepCapture are left in place on uninstall, so a reinstall keeps the user's choices.
