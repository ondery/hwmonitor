# Removes HW Monitor Sensor Service and its program files.
# PawnIO is left installed because other programs may use it.

#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$InstallDir = "$env:ProgramFiles\Ondery\HWMonitor\SensorService"
)

$ErrorActionPreference = "Stop"
$ServiceName = "HWMonitorSensorService"

function Test-Administrator {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Administrator)) {
    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$PSCommandPath`"",
        "-InstallDir", "`"$InstallDir`""
    )
    try {
        $process = Start-Process -FilePath "powershell.exe" -Verb RunAs -Wait -PassThru -ArgumentList $arguments
    }
    catch {
        Write-Error "Administrator approval is required to remove the sensor service."
        exit 1
    }

    if (-not $process) { exit 1 }
    exit $process.ExitCode
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Stopping $ServiceName..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    $output = & cmd.exe /c "sc.exe delete $ServiceName" 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe delete failed with exit code ${LASTEXITCODE}: $output"
    }

    Start-Sleep -Seconds 1
}

if (Test-Path -LiteralPath $InstallDir) {
    Write-Host "Removing $InstallDir..."
    Remove-Item -LiteralPath $InstallDir -Recurse -Force
}

Write-Host "HW Monitor Sensor Service was removed. PawnIO was not uninstalled."
