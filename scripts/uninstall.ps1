# Better Speech Recognition - uninstaller. Restores the original game files.
#   uninstall.bat            # restore the game; models and settings stay for a later reinstall
#   uninstall.ps1 -Purge     # also delete downloaded models and settings
param(
    [string]$GameDir = (Split-Path $PSScriptRoot -Parent),
    [switch]$Purge
)
$ErrorActionPreference = "Stop"

try {
    if (-not (Test-Path (Join-Path $GameDir "Scam With Your Friends.exe"))) {
        throw "Game not found in '$GameDir'. Pass -GameDir."
    }
    if (Get-Process "Scam With Your Friends" -ErrorAction SilentlyContinue) {
        throw "The game is running. Close it first."
    }

    $lib = Join-Path $PSScriptRoot "lib"
    Add-Type -Path (Join-Path $lib "Mono.Cecil.dll")
    Add-Type -Path (Join-Path $lib "BetterSpeech.Patcher.dll")
    [BetterSpeech.Patcher.GamePatcher]::Uninstall($GameDir)

    if ($Purge) {
        foreach ($item in @("models", "BetterSpeech.cfg")) {
            $path = Join-Path $PSScriptRoot $item
            if (Test-Path $path) {
                Remove-Item $path -Recurse -Force
                Write-Host "Removed $path"
            }
        }
    }

    Write-Host ""
    Write-Host "== Done. The game files are original again. You can delete the BetterSpeech folder."
    exit 0
} catch {
    Write-Host ""
    Write-Host "Uninstall failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "If the game does not start, use Steam > Properties > Installed Files > Verify integrity."
    exit 1
}
