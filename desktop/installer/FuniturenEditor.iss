#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\publish\win-x64"
#endif

[Setup]
AppId={{B447A06F-1DCE-4882-9C1A-A92FD73D444D}
AppName=Furnituren Editor
AppVersion={#MyAppVersion}
AppPublisher=Tariq Alsalem
AppPublisherURL=https://github.com/ShadierPond/Funituren-Editor
DefaultDirName={localappdata}\Programs\Furnituren Editor
DefaultGroupName=Furnituren Editor
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\FuniturenEditor.Desktop.exe
OutputDir=..\installer-output
OutputBaseFilename=FuniturenEditor-Setup-{#MyAppVersion}
SetupIconFile=..\assets\FuniturenEditor.ico
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
VersionInfoCompany=Tariq Alsalem
VersionInfoDescription=Furnituren Editor Setup
VersionInfoProductName=Furnituren Editor
VersionInfoVersion={#MyAppVersion}

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked
Name: "trustpublisher"; Description: "Dem Publisher-Zertifikat von Tariq Alsalem für diese private Anwendung vertrauen"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Furnituren Editor"; Filename: "{app}\FuniturenEditor.Desktop.exe"
Name: "{autodesktop}\Furnituren Editor"; Filename: "{app}\FuniturenEditor.Desktop.exe"; Tasks: desktopicon

[Run]
Filename: "{sys}\certutil.exe"; Parameters: "-user -addstore ""Root"" ""{app}\FuniturenEditor-Tariq-Alsalem.cer"""; Tasks: trustpublisher; StatusMsg: "Publisher-Zertifikat als vertrauenswürdige Stammzertifizierungsstelle hinzufügen …"
Filename: "{sys}\certutil.exe"; Parameters: "-user -addstore ""TrustedPublisher"" ""{app}\FuniturenEditor-Tariq-Alsalem.cer"""; Tasks: trustpublisher; StatusMsg: "Publisher-Zertifikat als vertrauenswürdigen Herausgeber hinzufügen …"
Filename: "{app}\FuniturenEditor.Desktop.exe"; Description: "{cm:LaunchProgram,Furnituren Editor}"; Flags: nowait postinstall skipifsilent
