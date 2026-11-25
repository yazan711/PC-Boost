<#
.SYNOPSIS
    Generates or validates an Authenticode Code Signing certificate for PC Boost.
.DESCRIPTION
    Creates a SHA-256 Code Signing certificate (CN=PC Boost) and exports:
      - certs\PCBoostCodeSign.pfx (private key, gitignored)
      - certs\PCBoostCodeSign.cer (public cert, gitignored)
    Use -TrustCurrentUser to optionally add the exported .cer to your local
    CurrentUser\Root and CurrentUser\TrustedPublisher stores for local verification.
    For public distribution without Windows SmartScreen warnings, pass a commercial
    OV/EV certificate to build-release.ps1 via -CertPath and -CertPassword.
#>
param(
    [string]$Subject = "CN=PC Boost, O=PC Boost",
    [string]$Password = "PCBoost-Local-Sign-2026!",
    [int]$ValidYears = 3,
    [switch]$Force,
    [switch]$TrustCurrentUser
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$certDir = Join-Path $root "certs"
New-Item -ItemType Directory -Force -Path $certDir | Out-Null

$pfxPath = Join-Path $certDir "PCBoostCodeSign.pfx"
$cerPath = Join-Path $certDir "PCBoostCodeSign.cer"
$pwdFile = Join-Path $certDir "PCBoostCodeSign.pass"

if ((Test-Path $pfxPath) -and -not $Force) {
    Write-Host "Existing signing certificate found: $pfxPath" -ForegroundColor Green
    $existingCert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($pfxPath, $Password)
    Write-Host "  Subject   : $($existingCert.Subject)"
    Write-Host "  Thumbprint: $($existingCert.Thumbprint)"
    Write-Host "  Expires   : $($existingCert.NotAfter)"
} else {
    Write-Host "Creating new SHA-256 Authenticode Code Signing certificate ($Subject)..." -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Subject `
        -KeyAlgorithm RSA `
        -KeyLength 3072 `
        -HashAlgorithm SHA256 `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -NotAfter (Get-Date).AddYears($ValidYears)

    $secPwd = ConvertTo-SecureString -String $Password -Force -AsPlainText
    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $secPwd | Out-Null
    Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
    Set-Content -Path $pwdFile -Value $Password -NoNewline -Encoding UTF8

    Write-Host "Exported certificate files (excluded from Git via .gitignore):" -ForegroundColor Green
    Write-Host "  PFX       : $pfxPath"
    Write-Host "  CER       : $cerPath"
    Write-Host "  Thumbprint: $($cert.Thumbprint)"
    Write-Host "  Expires   : $($cert.NotAfter)"
}

if ($TrustCurrentUser) {
    Write-Host "Adding public certificate to CurrentUser Root & TrustedPublisher stores..." -ForegroundColor Cyan
    Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\CurrentUser\Root" | Out-Null
    Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\CurrentUser\TrustedPublisher" | Out-Null
    Write-Host "Certificate trusted for current Windows user." -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "Note: Self-signed certificates embed valid SHA-256 Authenticode signatures for testing/CI." -ForegroundColor Yellow
    Write-Host "  - To trust locally on this PC: .\scripts\Setup-CodeSigning.ps1 -TrustCurrentUser"
    Write-Host "  - For public SmartScreen trust: .\build-release.ps1 -CertPath 'commercial.pfx' -CertPassword '...'"
}
