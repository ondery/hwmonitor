; Inno Setup script for the HW Monitor Command Palette extension.
; build-exe.ps1 fills the version and architecture before compiling.
; Processor temperature needs the separate sensor service. See scripts/install-sensor-service.ps1.

#define AppVersion "0.2.1.0"

[Setup]
AppId={{C9C07999-73E6-498F-AD96-049A06D708DE}
AppName=HW Monitor
AppVersion={#AppVersion}
AppPublisher=Ondery
AppPublisherURL=https://github.com/ondery/hwmonitor
AppSupportURL=https://github.com/ondery/hwmonitor
AppUpdatesURL=https://github.com/ondery/hwmonitor/releases
DefaultDirName={autopf}\HWMonitor
OutputDir=bin\Release\installer
OutputBaseFilename=HWMonitor-Setup-{#AppVersion}
Compression=lzma
SolidCompression=yes
MinVersion=10.0.19041
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\HWMonitor.exe
UninstallDisplayName=HW Monitor

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "bin\Release\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\HW Monitor"; Filename: "{app}\HWMonitor.exe"

[Registry]
Root: HKCU; Subkey: "SOFTWARE\Classes\CLSID\{{37dd9813-4cb9-458b-bc43-41e89443f396}"; ValueType: string; ValueName: ""; ValueData: "HWMonitor"; Flags: uninsdeletekey
Root: HKCU; Subkey: "SOFTWARE\Classes\CLSID\{{37dd9813-4cb9-458b-bc43-41e89443f396}\LocalServer32"; ValueType: string; ValueName: ""; ValueData: "{app}\HWMonitor.exe -RegisterProcessAsComServer"; Flags: uninsdeletekey
