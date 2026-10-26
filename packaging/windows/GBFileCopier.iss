; Inno Setup script for the Windows installer. Built by packaging/windows/build.ps1, which passes
; AppVersion, NumericVersion, SourceDir (the published files), RepoDir and OutputDir.

#define AppName "GBFileCopier"

[Setup]
; Never change AppId: Windows uses it to recognize upgrades of the same app
AppId={{54493A46-2263-4879-9C28-891CFA2C0B2F}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Giulio Banterla
AppPublisherURL=https://github.com/giuliobanterla/GBFileCopier
AppSupportURL=https://github.com/giuliobanterla/GBFileCopier/issues
VersionInfoVersion={#NumericVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Per-user install by default (no administrator needed); the dialog offers an install for all users
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile={#RepoDir}\LICENSE
SetupIconFile={#RepoDir}\assets\icon\icon.ico
UninstallDisplayIcon={app}\GBFileCopier.exe
OutputDir={#OutputDir}
OutputBaseFilename=GBFileCopier-{#AppVersion}-windows-x64-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
; Explorer reads the context menu entries again after the install
ChangesAssociations=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"

[CustomMessages]
english.ContextMenu=Add GBFileCopier to the Explorer context menu and to "Send to"
italian.ContextMenu=Aggiungi GBFileCopier al menu contestuale di Esplora risorse e a "Invia a"
english.CopyOrMove=Copy or Move with GBFileCopier...
italian.CopyOrMove=Copia o sposta con GBFileCopier...
english.PasteHere=Paste Here with GBFileCopier
italian.PasteHere=Incolla qui con GBFileCopier
english.MoveHere=Move Here with GBFileCopier
italian.MoveHere=Sposta qui con GBFileCopier

[Tasks]
Name: "contextmenu"; Description: "{cm:ContextMenu}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

; Classic context menu entries (on Windows 11 under "Show more options"). HKA is the current user's
; registry for a per-user install and the machine's for an install for all users. Explorer starts
; the app once per selected item: the app gathers them in one window (see SingleInstance).
[Registry]
Root: HKA; Subkey: "Software\Classes\*\shell\GBFileCopier"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{cm:CopyOrMove}"; Tasks: contextmenu; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\*\shell\GBFileCopier"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\GBFileCopier.exe"",0"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\*\shell\GBFileCopier\command"; ValueType: string; ValueName: ""; ValueData: """{app}\GBFileCopier.exe"" --add ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\GBFileCopier"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{cm:CopyOrMove}"; Tasks: contextmenu; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Directory\shell\GBFileCopier"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\GBFileCopier.exe"",0"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\GBFileCopier\command"; ValueType: string; ValueName: ""; ValueData: """{app}\GBFileCopier.exe"" --add ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\GBFileCopierPaste"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{cm:PasteHere}"; Tasks: contextmenu; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\GBFileCopierPaste"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\GBFileCopier.exe"",0"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\GBFileCopierPaste\command"; ValueType: string; ValueName: ""; ValueData: """{app}\GBFileCopier.exe"" --paste ""%V"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\GBFileCopierMove"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{cm:MoveHere}"; Tasks: contextmenu; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\GBFileCopierMove"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\GBFileCopier.exe"",0"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\GBFileCopierMove\command"; ValueType: string; ValueName: ""; ValueData: """{app}\GBFileCopier.exe"" --paste ""%V"" --move"; Tasks: contextmenu
; Written by the app itself (Ctrl+V in Explorer starts it at sign-in): removed with it
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "GBFileCopier"; Flags: uninsdeletevalue dontcreatekey

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs; Excludes: "*.pdb"
Source: "{#RepoDir}\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\GBFileCopier.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\GBFileCopier.exe"; Tasks: desktopicon
; "Send to" passes all the selected items in a single start
Name: "{usersendto}\{#AppName}"; Filename: "{app}\GBFileCopier.exe"; Parameters: "--add"; Tasks: contextmenu

[Run]
Filename: "{app}\GBFileCopier.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; The app's own updater runs setup silently with /RELAUNCH=1: start the new version afterwards, as the
; user who was running it (not as the administrator, for an install for all users)
Filename: "{app}\GBFileCopier.exe"; Flags: nowait runasoriginaluser; Check: RelaunchRequested

[Code]
function RelaunchRequested: Boolean;
begin
  Result := WizardSilent and (ExpandConstant('{param:relaunch|0}') = '1');
end;
