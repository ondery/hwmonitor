; Optional Inno Setup script for HW Monitor Sensor Service.
; The PowerShell installer is the supported path because it also installs
; PawnIO when winget package namazso.PawnIO is missing.
;
; Before compiling:
;   dotnet publish src\HWMonitor.SensorService\HWMonitor.SensorService.csproj -c Release -r win-x64 --self-contained true -o scripts\stage\win-x64
; Then compile this script with Inno Setup 6 on that machine.
; The package does not include PawnIO. Install PawnIO from https://pawnio.eu/
; or with: winget install --id namazso.PawnIO -e

#define ServiceName "HWMonitorSensorService"
#define DisplayName "HW Monitor Sensor Service"
#define SourceDir "stage\win-x64"
#define AppVersion "0.2.0.0"

[Setup]
AppId={{8F4B6E21-5C0A-4D33-9B17-6A2E0C8D4F90}
AppName={#DisplayName}
AppVersion={#AppVersion}
AppPublisher=Ondery
AppPublisherURL=https://github.com/ondery/hwmonitor
DefaultDirName={autopf}\Ondery\HWMonitor\SensorService
DisableProgramGroupPage=yes
OutputBaseFilename=HWMonitor-SensorService-{#AppVersion}-x64
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
UninstallDisplayName={#DisplayName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden; RunOnceId: "StopService"
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden; RunOnceId: "DeleteService"

[Code]
procedure RegisterService();
var
  ResultCode: Integer;
  Binary: String;
  Sc: String;
begin
  Sc := ExpandConstant('{sys}\sc.exe');
  Binary := ExpandConstant('{app}\HWMonitor.SensorService.exe');
  Exec(Sc, 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if not Exec(Sc, 'create {#ServiceName} binPath= "' + Binary + '" start= auto obj= LocalSystem DisplayName= "{#DisplayName}"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    RaiseException('sc.exe create could not be started.');
  if ResultCode = 1073 then
  begin
    if not Exec(Sc, 'config {#ServiceName} binPath= "' + Binary + '" start= auto obj= LocalSystem', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      RaiseException('sc.exe config could not be started.');
  end
  else if ResultCode <> 0 then
    RaiseException('sc.exe create failed with exit code ' + IntToStr(ResultCode));
  Exec(Sc, 'description {#ServiceName} "Reads processor and graphics sensors for HW Monitor. The service is read-only."', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(Sc, 'failure {#ServiceName} reset= 86400 actions= restart/5000/restart/15000/restart/60000', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(Sc, 'start {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    RegisterService();
end;
