# Better Speech Recognition - installer.
# The "BetterSpeech" folder belongs in the game folder, next to "Scam With Your Friends.exe".
#   install.bat                         # patches the game; pick a model in game with F8
#   install.ps1 -Model turbo-q5         # also downloads and activates a model ("id" from catalog\models.json)
#   install.ps1 -GameDir "E:\SteamLibrary\steamapps\common\Scam With Your Friends"
param(
    [string]$GameDir = (Split-Path $PSScriptRoot -Parent),
    [string]$Model = "",
    [switch]$SkipModel
)
$ErrorActionPreference = "Stop"
$exe = Join-Path $GameDir "Scam With Your Friends.exe"

try {
    if (-not (Test-Path $exe)) {
        throw "Game not found in '$GameDir'. Put the BetterSpeech folder into the game folder (next to 'Scam With Your Friends.exe') or pass -GameDir."
    }
    if (Get-Process "Scam With Your Friends" -ErrorAction SilentlyContinue) {
        throw "The game is running. Close it first."
    }

    Write-Host ""
    Write-Host "== Patching the game"
    $lib = Join-Path $PSScriptRoot "lib"
    # DLLs from a downloaded zip carry the "from the internet" mark, and .NET refuses to load them (0x80131515).
    Get-ChildItem $lib -Filter *.dll | Unblock-File
    Add-Type -Path (Join-Path $lib "Mono.Cecil.dll")
    Add-Type -Path (Join-Path $lib "Mono.Cecil.Rocks.dll")
    Add-Type -Path (Join-Path $lib "BetterSpeech.Patcher.dll")
    [BetterSpeech.Patcher.GamePatcher]::Install($GameDir, (Join-Path $lib "BetterSpeech.Runtime.dll"))

    if ($Model) {
        $catalog = Get-Content (Join-Path $PSScriptRoot "catalog\models.json") -Raw | ConvertFrom-Json
        $entry = $catalog.models | Where-Object { $_.id -eq $Model }
        if (-not $entry) {
            throw "Unknown model '$Model'. Available: $($catalog.models.id -join ', ')"
        }
        # Local name is derived from the catalog id, because upstream names like ggml-model-q5_0.bin are ambiguous.
        $modelFile = "ggml-$($entry.id).bin"

        $modelDir = Join-Path $PSScriptRoot "models"
        New-Item -ItemType Directory -Force $modelDir | Out-Null
        $modelPath = Join-Path $modelDir $modelFile
        if (-not $SkipModel -and -not (Test-Path $modelPath)) {
            Write-Host ("== Downloading {0} ({1:N0} MB) from {2}" -f $entry.id, ($entry.sizeBytes / 1MB), $entry.repo)
            $tmp = "$modelPath.part"
            curl.exe -L --fail -o $tmp $entry.url
            if ($LASTEXITCODE -ne 0) { throw "Model download failed." }
            $hash = (Get-FileHash $tmp -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($hash -ne $entry.sha256) {
                Remove-Item $tmp
                throw "Checksum mismatch for $($entry.id): got $hash, expected $($entry.sha256)."
            }
            Move-Item $tmp $modelPath -Force
            Write-Host "== Checksum OK"
        }

        # The game fills in the remaining settings with defaults on first start.
        $configPath = Join-Path $PSScriptRoot "BetterSpeech.cfg"
        if (Test-Path $configPath) {
            $lines = @(Get-Content $configPath)
            if ($lines -match '^\s*ModelFile\s*=') {
                $lines = $lines -replace '^\s*ModelFile\s*=.*$', "ModelFile = $modelFile"
            } else {
                $lines += @("", "[Model]", "ModelFile = $modelFile")
            }
            Set-Content $configPath $lines
        } else {
            Set-Content $configPath @("[Model]", "ModelFile = $modelFile")
        }
    }

    Write-Host ""
    Write-Host "== Done."
    if ($Model) { Write-Host "   Active model: $modelFile" }
    Write-Host "   Start the game and press F8 to download and switch models."
    Write-Host "   After a game update, run install.bat again."
    exit 0
} catch {
    Write-Host ""
    Write-Host "Installation failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
