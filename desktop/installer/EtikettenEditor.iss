#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\publish\win-x64"
#endif

[Setup]
AppId={{B447A06F-1DCE-4882-9C1A-A92FD73D444D}
AppName=Etiketten Editor
AppVersion={#MyAppVersion}
AppPublisher=Tariq Alsalem
AppPublisherURL=https://github.com/ShadierPond/Etiketten-Editor
DefaultDirName={localappdata}\Programs\Etiketten Editor
DefaultGroupName=Etiketten Editor
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\EtikettenEditor.exe
OutputDir=..\installer-output
OutputBaseFilename=EtikettenEditor-Setup-{#MyAppVersion}
SetupIconFile=..\assets\EtikettenEditor.ico
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
VersionInfoCompany=Tariq Alsalem
VersionInfoDescription=Etiketten Editor Setup
VersionInfoProductName=Etiketten Editor
VersionInfoVersion={#MyAppVersion}

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked
Name: "trustpublisher"; Description: "Dem Publisher-Zertifikat von Tariq Alsalem für diese private Anwendung vertrauen"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: files; Name: "{app}\FuniturenEditor.Desktop.exe"

[Icons]
Name: "{group}\Etiketten Editor"; Filename: "{app}\EtikettenEditor.exe"
Name: "{autodesktop}\Etiketten Editor"; Filename: "{app}\EtikettenEditor.exe"; Tasks: desktopicon

[Run]
Filename: "{sys}\certutil.exe"; Parameters: "-user -addstore ""Root"" ""{app}\EtikettenEditor-Tariq-Alsalem.cer"""; Tasks: trustpublisher; StatusMsg: "Publisher-Zertifikat als vertrauenswürdige Stammzertifizierungsstelle hinzufügen …"
Filename: "{sys}\certutil.exe"; Parameters: "-user -addstore ""TrustedPublisher"" ""{app}\EtikettenEditor-Tariq-Alsalem.cer"""; Tasks: trustpublisher; StatusMsg: "Publisher-Zertifikat als vertrauenswürdigen Herausgeber hinzufügen …"
Filename: "{app}\EtikettenEditor.exe"; Description: "{cm:LaunchProgram,Etiketten Editor}"; Flags: nowait postinstall skipifsilent
