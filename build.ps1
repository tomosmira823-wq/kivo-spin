# Builds Kivo Spin and its installer on your own PC.
#   Needs: .NET 8 SDK  (https://dotnet.microsoft.com/download/dotnet/8.0)
#   Usage: .\build.ps1                      -> Releases\KivoSpin-win-Setup.exe
#          .\build.ps1 -Version 1.0.3       -> stamps that version
#          .\build.ps1 -Repo https://github.com/you/kivo-spin   -> builds with auto-updates pointed at that repo
param(
    [string]$Version = "1.0.0",
    [string]$Repo = ""
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 8 SDK not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0 and run this again."
}

Write-Host "Publishing Kivo Spin $Version..." -ForegroundColor Cyan
if (Test-Path publish) { Remove-Item publish -Recurse -Force }
dotnet publish src/KivoSpin/KivoSpin.csproj -c Release -r win-x64 --self-contained true -o publish `
    -p:Version=$Version -p:UpdateRepo=$Repo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Host "Installing the Velopack packer (vpk)..." -ForegroundColor Cyan
    dotnet tool install -g vpk --version 0.0.1298
    $env:PATH += ";$env:USERPROFILE\.dotnet\tools"
}

Write-Host "Building installer..." -ForegroundColor Cyan
vpk pack --packId KivoSpin --packVersion $Version --packDir publish --mainExe KivoSpin.exe --runtime win-x64 `
    --packTitle "Kivo Spin" --packAuthors "Kivo" --icon src/KivoSpin/Assets/KivoSpin.ico --releaseNotes RELEASE_NOTES.md
if ($LASTEXITCODE -ne 0) { throw "Packing failed." }

Write-Host ""
Write-Host "Done. Installer: $PSScriptRoot\Releases\KivoSpin-win-Setup.exe" -ForegroundColor Green
