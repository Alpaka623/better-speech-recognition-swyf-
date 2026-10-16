# Better Speech Recognition - installer.
# The "BetterSpeech" folder belongs in the game folder, next to "Scam With Your Friends.exe".
#   install.bat                         # asks for your language and picks a model
#   install.ps1 -Model turbo-q5         # any "id" from catalog\models.json
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

    $catalog = Get-Content (Join-Path $PSScriptRoot "catalog\models.json") -Raw | ConvertFrom-Json
    if (-not $Model) {
        Write-Host ""
        Write-Host "Which language do you speak in calls?"
        Write-Host "  1) German   -> turbo-german-q5 (574 MB, trained on German)"
        Write-Host "  2) Other    -> turbo-q5        (574 MB, all languages)"
        Write-Host "  3) Weak PC / no GPU -> small-q5 (190 MB)"
        $choice = Read-Host "Choose 1, 2 or 3"
        switch ($choice) {
            "1" { $Model = "turbo-german-q5" }
            "3" { $Model = "small-q5" }
            default { $Model = "turbo-q5" }
        }
    }
    $entry = $catalog.models | Where-Object { $_.id -eq $Model }
    if (-not $entry) {
        throw "Unknown model '$Model'. Available: $($catalog.models.id -join ', ')"
    }
    # Local name is derived from the catalog id, because upstream names like ggml-model-q5_0.bin are ambiguous.
    $modelFile = "ggml-$($entry.id).bin"

    Write-Host ""
    Write-Host "== Patching the game"
    $lib = Join-Path $PSScriptRoot "lib"
    Add-Type -Path (Join-Path $lib "Mono.Cecil.dll")
    Add-Type -Path (Join-Path $lib "Mono.Cecil.Rocks.dll")
    Add-Type -Path (Join-Path $lib "BetterSpeech.Patcher.dll")
    [BetterSpeech.Patcher.GamePatcher]::Install($GameDir, (Join-Path $lib "BetterSpeech.Runtime.dll"))

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

    Write-Host ""
    Write-Host "== Done. Active model: $modelFile"
    Write-Host "   Settings: $configPath (all options appear after the first game start)"
    Write-Host "   After a game update, run install.bat again."
    exit 0
} catch {
    Write-Host ""
    Write-Host "Installation failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
