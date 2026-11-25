<#
.SYNOPSIS
    Automated build, test, and release packaging pipeline for PC Boost.
.DESCRIPTION
    1. Builds CleanRamBoosterApp and CleanRamBoosterApp.Tests in Release mode.
    2. Runs the full automated test suite (fails build if any test fails).
    3. Syncs framework-dependent ReadyToRun_App binaries.
    4. Publishes self-contained win-x64 ReadyToRun bundle into CleanRamBoosterApp\publish\win-x64.
    5. Optionally Authenticode-signs binaries if -CertPath or $env:SIGN_CERT_PFX is provided.
    6. Refreshes "Portable PC Boost", "Portable PC Boost.zip", and "Source Code".
    7. Compiles standalone setup.exe via Inno Setup 6 (ISCC.exe).
#>
param(
    [switch]$SkipTests,
    [switch]$SkipZip,
    [string]$CertPath = $env:SIGN_CERT_PFX,
    [string]$CertPassword = $env:SIGN_CERT_PASSWORD,
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

function Invoke-CodeSign {
    param([string]$FilePath)
    if ([string]::IsNullOrWhiteSpace($CertPath) -or -not (Test-Path $CertPath)) { return }
    $signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if (-not $signtool) {
        Write-Warning "Certificate supplied, but signtool.exe was not found in PATH. Skipping Authenticode signing."
        return
    }
    Write-Host "Signing $FilePath ..." -ForegroundColor Cyan
    $signArgs = @("sign", "/f", $CertPath, "/fd", "SHA256", "/tr", $TimestampUrl, "/td", "SHA256")
    if (-not [string]::IsNullOrWhiteSpace($CertPassword)) {
        $signArgs += @("/p", $CertPassword)
    }
    $signArgs += $FilePath
    & $signtool.Source @signArgs
    if ($LASTEXITCODE -ne 0) { throw "signtool failed for $FilePath" }
}

Write-Host "=== [1/7] Stopping any running PC Boost instance ===" -ForegroundColor Cyan
Get-Process CleanRamBoosterApp -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300

Write-Host "=== [2/7] Building Solution (Release) ===" -ForegroundColor Cyan
dotnet build "PCBoost.sln" -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Solution build failed." }

if (-not $SkipTests) {
    Write-Host "=== [3/7] Running Automated Test Suite ===" -ForegroundColor Cyan
    dotnet run --project "CleanRamBoosterApp.Tests\CleanRamBoosterApp.Tests.csproj" -c Release --no-build --nologo
    if ($LASTEXITCODE -ne 0) { throw "Automated test suite failed!" }
} else {
    Write-Host "=== [3/7] Skipping tests (-SkipTests) ===" -ForegroundColor Yellow
}

Write-Host "=== [4/7] Syncing ReadyToRun_App ===" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path "ReadyToRun_App\Resources" | Out-Null
Copy-Item "CleanRamBoosterApp\bin\Release\net8.0-windows\CleanRamBoosterApp.*" "ReadyToRun_App\" -Force
Copy-Item "CleanRamBoosterApp\Resources\app.*" "ReadyToRun_App\Resources\" -Force
Copy-Item "CleanRamBoosterApp\Resources\app.ico" "ReadyToRun_App\app.ico" -Force
Copy-Item "CleanRamBoosterApp\Resources\app_icon.png" "ReadyToRun_App\app_icon.png" -Force

Write-Host "=== [5/7] Publishing Self-Contained win-x64 ReadyToRun Bundle ===" -ForegroundColor Cyan
$publishDir = Join-Path $root "CleanRamBoosterApp\publish\win-x64"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish "CleanRamBoosterApp\CleanRamBoosterApp.csproj" -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o $publishDir --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

Invoke-CodeSign (Join-Path $publishDir "CleanRamBoosterApp.exe")

Write-Host "=== [6/7] Updating Portable PC Boost & Source Code ===" -ForegroundColor Cyan
$portableDir = Join-Path $root "Portable PC Boost"
if (Test-Path $portableDir) { Remove-Item $portableDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $portableDir | Out-Null
Copy-Item "$publishDir\*" $portableDir -Recurse -Force

if (-not $SkipZip) {
    $portableZip = Join-Path $root "Portable PC Boost.zip"
    if (Test-Path $portableZip) { Remove-Item $portableZip -Force }
    Compress-Archive -Path "$portableDir\*" -DestinationPath $portableZip -CompressionLevel Optimal
}

$srcOut = Join-Path $root "Source Code"
if (Test-Path $srcOut) { Remove-Item $srcOut -Recurse -Force }
New-Item -ItemType Directory -Force -Path $srcOut | Out-Null

# Copy clean source projects excluding bin/obj/publish
foreach ($proj in @("CleanRamBoosterApp", "CleanRamBoosterApp.Tests")) {
    $srcProj = Join-Path $root $proj
    $dstProj = Join-Path $srcOut $proj
    robocopy $srcProj $dstProj /E /XD bin obj publish .vs /NFL /NDL /NJH /NJS /NC /NS | Out-Null
}
Copy-Item "PCBoost.sln", "build-release.ps1", ".gitignore", "README.md", "HANDOVER.md" $srcOut -ErrorAction SilentlyContinue

Write-Host "=== [7/7] Compiling Inno Setup 6 Installer (setup.exe) ===" -ForegroundColor Cyan
$isccCandidates = @(
    (Get-Command iscc.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) }

$iscc = $isccCandidates | Select-Object -First 1
if (-not $iscc) {
    Write-Warning "Inno Setup 6 (ISCC.exe) not found. Skipping setup.exe build."
} else {
    & $iscc "CleanRamBoosterApp\Installer\PCBoostSetup.iss"
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed." }
    Invoke-CodeSign (Join-Path $root "setup.exe")
}

Write-Host "`n=== Release Checksums (SHA-256) ===" -ForegroundColor Green
$artifacts = @(
    "ReadyToRun_App\CleanRamBoosterApp.dll",
    "Portable PC Boost\CleanRamBoosterApp.exe",
    "setup.exe"
) | Where-Object { Test-Path $_ }
Get-FileHash $artifacts -Algorithm SHA256 | Format-Table Algorithm, Hash, Path -AutoSize
Write-Host "RELEASE PIPELINE SUCCEEDED!" -ForegroundColor Green
