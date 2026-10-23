# Builds the mod package into dist\BetterSpeech (the folder users extract into the game folder).
#   .\build.ps1             # build only
#   .\build.ps1 -Deploy     # also copy it into the game folder (keeps models, settings and backups)
#   .\build.ps1 -Zip        # also create dist\BetterSpeech-<version>.zip for Nexus
#   .\build.ps1 -Exe        # also create dist\BetterSpeech-Setup-<version>.exe (one file, installs with a click)
param(
    [string]$GameDir = "D:\Spiele\Steam\steamapps\common\Scam With Your Friends",
    [switch]$Deploy,
    [switch]$Zip,
    [switch]$Exe
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$out = Join-Path $root "dist\BetterSpeech"

foreach ($project in @("src\Runtime\BetterSpeech.Runtime.csproj", "src\Patcher\BetterSpeech.Patcher.csproj")) {
    dotnet build (Join-Path $root $project) -c Release -p:GameDir="$GameDir" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
}

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $out "lib"), (Join-Path $out "catalog") | Out-Null
Copy-Item (Join-Path $root "scripts\*") $out
Copy-Item (Join-Path $root "src\Runtime\bin\Release\net472\BetterSpeech.Runtime.dll") (Join-Path $out "lib")
foreach ($dll in @("BetterSpeech.Patcher.dll", "Mono.Cecil.dll", "Mono.Cecil.Rocks.dll")) {
    Copy-Item (Join-Path $root "src\Patcher\bin\Release\net472\$dll") (Join-Path $out "lib")
}
Copy-Item (Join-Path $root "catalog\models.json") (Join-Path $out "catalog")
Copy-Item (Join-Path $root "README.md") $out
Copy-Item (Join-Path $root "LICENSE") (Join-Path $out "LICENSE.txt")
Write-Host "== Package: $out"

if ($Zip) {
    $version = ([xml](Get-Content (Join-Path $root "Directory.Build.props"))).Project.PropertyGroup.Version
    $zipPath = Join-Path $root "dist\BetterSpeech-$version.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath }
    Compress-Archive $out $zipPath
    Write-Host "== Zip: $zipPath"
}

if ($Exe) {
    $version = ([xml](Get-Content (Join-Path $root "Directory.Build.props"))).Project.PropertyGroup.Version
    $installer = Join-Path $root "src\Installer"
    # The finished package goes into the setup as one embedded zip.
    $payload = Join-Path $installer "obj\payload.zip"
    New-Item -ItemType Directory -Force (Split-Path $payload) | Out-Null
    if (Test-Path $payload) { Remove-Item $payload }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($out, $payload)
    dotnet build (Join-Path $installer "BetterSpeech.Installer.csproj") -c Release -p:GameDir="$GameDir" -p:PayloadZip="$payload" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Build failed: src\Installer\BetterSpeech.Installer.csproj" }
    $exePath = Join-Path $root "dist\BetterSpeech-Setup-$version.exe"
    Copy-Item (Join-Path $installer "bin\Release\net472\BetterSpeech-Setup.exe") $exePath -Force
    Write-Host "== Setup: $exePath"
}

if ($Deploy) {
    $target = Join-Path $GameDir "BetterSpeech"
    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item (Join-Path $out "*") $target -Recurse -Force
    Write-Host "== Deployed to $target"
}
