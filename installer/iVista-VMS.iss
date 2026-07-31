#ifndef StageDir
  #define StageDir "stage"
#endif
#ifndef OutputDir
  #define OutputDir "output"
#endif
#ifndef PrerequisiteDir
  #define PrerequisiteDir "prerequisites"
#endif

#define AppName "iVMS"
#define AppVersion "2.6.5.19"
#define AppPublisher "iVista Tech"
#define AppExeName "iVMS.exe"

[Setup]
AppId={{D65DA08B-5957-4F25-84E4-D0150E7F53B1}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf64}\iVMS
DefaultGroupName=iVMS
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
OutputDir={#OutputDir}
OutputBaseFilename=iVMS-Setup-{#AppVersion}-x64
SetupIconFile={#StageDir}\App\icon.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
MinVersion=10.0.10240

[Files]
Source: "{#StageDir}\App\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StageDir}\GStreamer\*"; DestDir: "{app}\gstreamer"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PrerequisiteDir}\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "{#PrerequisiteDir}\ndp48-x86-x64-allos-enu.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "{#PrerequisiteDir}\MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{autoprograms}\iVMS"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\iVMS"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Tạo biểu tượng ngoài màn hình"; GroupDescription: "Biểu tượng bổ sung:"

[Run]
Filename: "{tmp}\MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; Parameters: "/silent /install"; Flags: waituntilterminated
Filename: "{tmp}\ndp48-x86-x64-allos-enu.exe"; Parameters: "/q /norestart"; StatusMsg: "Đang cài đặt .NET Framework 4.8..."; Check: not IsDotNet48Installed; Flags: waituntilterminated
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Đang cài đặt Microsoft Visual C++ Runtime..."; Flags: waituntilterminated
Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Description: "Mở iVista VMS"; Flags: nowait postinstall skipifsilent

[Code]
function IsDotNet48Installed: Boolean;
var
  Release: Cardinal;
begin
  Result :=
    RegQueryDWordValue(
      HKLM64,
      'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full',
      'Release',
      Release
    ) and (Release >= 528040);
end;
