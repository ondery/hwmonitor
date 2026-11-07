# Builds self-contained x64 and ARM64 installers for HW Monitor.
# The published sample names its architecture parameter incorrectly and
# concatenates two Write-Host calls. This script keeps the same steps and
# accepts -Platforms so the release workflow can pass x64 and arm64.

param(
    [string]$ExtensionName = "HWMonitor",
    [string]$Configuration = "Release",
    [string]$Version = "0.1.0.0",
    [string[]]$Platforms = @("x64", "arm64")
)

$ErrorActionPreference = "Stop"

Write-Host "Building $ExtensionName EXE installer..." -ForegroundColor Green
Write-Host "Version: $Version" -ForegroundColor Yellow
Write-Host "Platforms: $($Platforms -join ', ')" -ForegroundColor Yellow

$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectFile = Join-Path $ProjectDir "$ExtensionName.csproj"

Write-Host "Cleaning previous builds..." -ForegroundColor Yellow
if (Test-Path (Join-Path $ProjectDir "bin")) {
    Remove-Item -Path (Join-Path $ProjectDir "bin") -Recurse -Force -ErrorAction SilentlyContinue
}
if (Test-Path (Join-Path $ProjectDir "obj")) {
    Remove-Item -Path (Join-Path $ProjectDir "obj") -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore $ProjectFile
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE"
}

foreach ($Platform in $Platforms) {
    Write-Host "`n=== Building $Platform ===" -ForegroundColor Cyan

    Write-Host "Building and publishing $Platform application..." -ForegroundColor Yellow
    dotnet publish $ProjectFile `
        --configuration $Configuration `
        --runtime "win-$Platform" `
        --self-contained true `
        --output (Join-Path $ProjectDir "bin\$Configuration\win-$Platform\publish")

    if ($LASTEXITCODE -ne 0) {
        throw "Build failed for $Platform with exit code $LASTEXITCODE"
    }

    $publishDir = Join-Path $ProjectDir "bin\$Configuration\win-$Platform\publish"
    $fileCount = (Get-ChildItem -Path $publishDir -Recurse -File).Count
    Write-Host "Published $fileCount files to $publishDir" -ForegroundColor Green

    Write-Host "Creating installer script for $Platform..." -ForegroundColor Yellow
    $setupTemplate = Get-Content (Join-Path $ProjectDir "setup-template.iss") -Raw
    $setupScript = $setupTemplate -replace '#define AppVersion ".*"', "#define AppVersion `"$Version`""
    $setupScript = $setupScript -replace 'OutputBaseFilename=(.*?)\{#AppVersion\}', "OutputBaseFilename=`$1{#AppVersion}-$Platform"
    $setupScript = $setupScript -replace 'Source: "bin\\Release\\win-x64\\publish', "Source: `"bin\$Configuration\win-$Platform\publish"
    if ($Platform -eq "arm64") {
        $setupScript = $setupScript -replace '(\[Setup\][^\[]*)(MinVersion=)', "`$1ArchitecturesAllowed=arm64`r`nArchitecturesInstallIn64BitMode=arm64`r`n`$2"
    }
    else {
        $setupScript = $setupScript -replace '(\[Setup\][^\[]*)(MinVersion=)', "`$1ArchitecturesAllowed=x64compatible`r`nArchitecturesInstallIn64BitMode=x64compatible`r`n`$2"
    }

    $setupPath = Join-Path $ProjectDir "setup-$Platform.iss"
    $setupScript | Out-File -FilePath $setupPath -Encoding utf8

    Write-Host "Creating $Platform installer with Inno Setup..." -ForegroundColor Yellow
    $InnoSetupPath = "${env:ProgramFiles(x86)}\Inno Setup 6\iscc.exe"
    if (-not (Test-Path $InnoSetupPath)) {
        $InnoSetupPath = "${env:ProgramFiles}\Inno Setup 6\iscc.exe"
    }
    if (-not (Test-Path $InnoSetupPath)) {
        throw "Inno Setup was not found."
    }

    & $InnoSetupPath $setupPath
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup failed for $Platform with exit code $LASTEXITCODE"
    }

    $installer = Get-ChildItem (Join-Path $ProjectDir "bin\$Configuration\installer\*-$Platform.exe") -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $installer) {
        throw "Installer file was not found for $Platform"
    }

    $sizeMB = [math]::Round($installer.Length / 1MB, 2)
    Write-Host "Created $Platform installer: $($installer.Name) ($sizeMB MB)" -ForegroundColor Green
}

Write-Host "`nBuild completed successfully." -ForegroundColor Green
