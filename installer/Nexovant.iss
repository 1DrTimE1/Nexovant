; Inno Setup script for Nexovant. Built by .github/workflows/release.yml:
;   ISCC.exe /DAppVersion=1.2.3 installer\Nexovant.iss
; Expects the published app in out\app\Nexovant.exe and writes out\NexovantSetup.exe.
;
; Installs per user (no admin prompt) into %LocalAppData%\Programs\Nexovant, adds a Start menu
; shortcut, an optional desktop shortcut and an entry under Settings > Apps for uninstalling.
; The app's auto-updater runs this setup with /VERYSILENT to install a new version.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{6E3B9F2A-4C1D-4E8B-9A57-2F0C8D1B7E64}
AppName=Nexovant
AppVersion={#AppVersion}
AppVerName=Nexovant {#AppVersion}
AppPublisher=Nexovant
AppPublisherURL=https://github.com/1DrTimE1/Nexovant
AppUpdatesURL=https://github.com/1DrTimE1/Nexovant/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\Nexovant
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\out
OutputBaseFilename=NexovantSetup
SetupIconFile=..\Assets\Nexovant.ico
UninstallDisplayIcon={app}\Nexovant.exe
UninstallDisplayName=Nexovant
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
RestartApplications=no

[Languages]
#if FileExists(CompilerPath + "Languages\Swedish.isl")
Name: "swedish"; MessagesFile: "compiler:Languages\Swedish.isl"
#else
Name: "english"; MessagesFile: "compiler:Default.isl"
#endif

[Tasks]
Name: "desktopicon"; Description: "Skapa en genväg på skrivbordet"; GroupDescription: "Genvägar:"

[Files]
Source: "..\out\app\Nexovant.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Nexovant"; Filename: "{app}\Nexovant.exe"
Name: "{autodesktop}\Nexovant"; Filename: "{app}\Nexovant.exe"; Tasks: desktopicon

[Run]
; Also runs after a silent auto-update, so the new version starts by itself.
Filename: "{app}\Nexovant.exe"; Description: "Starta Nexovant"; Flags: nowait postinstall
