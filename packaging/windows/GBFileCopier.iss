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

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs; Excludes: "*.pdb"
Source: "{#RepoDir}\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\GBFileCopier.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\GBFileCopier.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\GBFileCopier.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
