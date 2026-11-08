# Installs HW Monitor Sensor Service.
# The service runs as LocalSystem, reads CPU and GPU sensors, and serves a
# read-only snapshot on the named pipe Ondery.HWMonitor.Sensors.
# Re-run the script to upgrade the binaries. It does not remove PawnIO.

#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet("x64", "arm64")]
    [string]$Platform = "x64",
    [string]$Configuration = "Release",
    [string]$SourceDir = "",
    [string]$InstallDir = "$env:ProgramFiles\Ondery\HWMonitor\SensorService"
)

$ErrorActionPreference = "Stop"
$ServiceName = "HWMonitorSensorService"
$DisplayName = "HW Monitor Sensor Service"
$PipeName = "Ondery.HWMonitor.Sensors"
$Description = "Reads processor and graphics sensors for HW Monitor. The service is read-only."

function Test-Administrator {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Administrator)) {
    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$PSCommandPath`"",
        "-Platform", $Platform,
        "-Configuration", $Configuration,
        "-InstallDir", "`"$InstallDir`""
    )
    if ($SourceDir) {
        $arguments += @("-SourceDir", "`"$SourceDir`"")
    }

    try {
        $process = Start-Process -FilePath "powershell.exe" -Verb RunAs -Wait -PassThru -ArgumentList $arguments
    }
    catch {
        Write-Error "Administrator approval is required to install the sensor service."
        exit 1
    }

    if (-not $process) { exit 1 }
    exit $process.ExitCode
}

function Invoke-ServiceControl {
    param([string]$Arguments)
    $output = & cmd.exe /c "sc.exe $Arguments" 2>&1
    if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 1073) {
        throw "sc.exe $Arguments failed with exit code ${LASTEXITCODE}: $output"
    }
}

function Test-PawnIoInstalled {
    $service = Get-Service -Name "PawnIO" -ErrorAction SilentlyContinue
    if ($service) { return $true }
    $candidates = @(
        "$env:ProgramFiles\PawnIO\PawnIOUtil.exe",
        "$env:ProgramFiles\PawnIO\PawnIO.sys",
        "$env:SystemRoot\System32\drivers\pawnio.sys",
        "$env:SystemRoot\System32\drivers\PawnIO.sys"
    )
    foreach ($path in $candidates) {
        if (Test-Path -LiteralPath $path) { return $true }
    }

    $keys = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*"
    )
    foreach ($key in $keys) {
        $match = Get-ItemProperty $key -ErrorAction SilentlyContinue |
            Where-Object { $_.DisplayName -eq "PawnIO" -or $_.PSChildName -eq "PawnIO" }
        if ($match) { return $true }
    }

    return $false
}

function Install-PawnIoIfMissing {
    if (Test-PawnIoInstalled) {
        Write-Host "PawnIO is already installed."
        return
    }

    $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
    if (-not $winget) {
        throw "PawnIO is not installed and winget is not available. Install it from https://pawnio.eu/ and run this script again."
    }

    Write-Host "Checking winget package namazso.PawnIO..."
    & winget.exe show --id namazso.PawnIO -e --accept-source-agreements
    if ($LASTEXITCODE -ne 0) {
        throw "Winget package namazso.PawnIO was not found. Install PawnIO from https://pawnio.eu/ and run this script again."
    }

    Write-Host "Installing PawnIO..."
    & winget.exe install --id namazso.PawnIO -e --scope machine --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne -1978335189) {
        throw "winget could not install namazso.PawnIO (exit $LASTEXITCODE). A reboot may be required. You can also install it from https://pawnio.eu/."
    }

    if (-not (Test-PawnIoInstalled)) {
        Write-Warning "PawnIO did not appear after installation. Reboot if the installer asked for one, then start $ServiceName."
    }
}

function Get-ServiceSource {
    if ($SourceDir) {
        $resolved = Resolve-Path -LiteralPath $SourceDir
        $exe = Join-Path $resolved "HWMonitor.SensorService.exe"
        if (-not (Test-Path -LiteralPath $exe)) {
            throw "HWMonitor.SensorService.exe was not found in $resolved"
        }

        return $resolved.Path
    }

    $project = Join-Path $PSScriptRoot "..\src\HWMonitor.SensorService\HWMonitor.SensorService.csproj"
    if (-not (Test-Path -LiteralPath $project)) {
        throw "The service project was not found. Pass -SourceDir with a published build."
    }

    $stage = Join-Path $PSScriptRoot "stage\win-$Platform"
    if (Test-Path -LiteralPath $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }

    Write-Host "Publishing the sensor service ($Platform)..."
    & dotnet publish $project -c $Configuration -r "win-$Platform" --self-contained true -o $stage
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }

    return (Resolve-Path -LiteralPath $stage).Path
}

$source = Get-ServiceSource
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Stopping $DisplayName..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}

Write-Host "Copying files to $InstallDir..."
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Path (Join-Path $source "*") -Destination $InstallDir -Recurse -Force
$exe = Join-Path $InstallDir "HWMonitor.SensorService.exe"
if (-not (Test-Path -LiteralPath $exe)) {
    throw "The service executable was not copied to $exe"
}

Install-PawnIoIfMissing

if ($existing) {
    Write-Host "Updating $DisplayName..."
    Invoke-ServiceControl "config $ServiceName binPath= `"$exe`" start= auto obj= LocalSystem DisplayName= `"$DisplayName`""
}
else {
    Write-Host "Registering $DisplayName..."
    Invoke-ServiceControl "create $ServiceName binPath= `"$exe`" start= auto obj= LocalSystem DisplayName= `"$DisplayName`""
}

Invoke-ServiceControl "description $ServiceName `"$Description`""
Invoke-ServiceControl "failure $ServiceName reset= 86400 actions= restart/5000/restart/15000/restart/60000"
Start-Service -Name $ServiceName
$started = Get-Service -Name $ServiceName
Write-Host ""
Write-Host "$DisplayName is $($started.Status)."
Write-Host "Pipe: \\.\pipe\$PipeName"
Write-Host "The extension reads that pipe automatically. Restart Command Palette if it is already open."
Write-Host "Temperature for an integrated GPU with no sensor of its own follows the processor package."
