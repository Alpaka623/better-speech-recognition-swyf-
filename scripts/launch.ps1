# Starts the game and reinstalls the mods first if a game update removed them.
# Set as the game's launch options in Steam (Properties > General > Launch options), with your game path:
#   powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "D:\...\Scam With Your Friends\BetterSpeech\launch.ps1" %command%
# Runs install.ps1 of every mod folder next to this one that was installed before (has backup\state.txt),
# so one launch option covers Better Speech Recognition and Gemini Backend. An already patched game takes
# a second or two. A failed install shows a message; the game then starts without that mod.
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Command)

$gameDir = Split-Path $PSScriptRoot -Parent
$log = Join-Path $PSScriptRoot "launch.log"
Set-Content $log "[$(Get-Date -Format s)] launch.ps1 $($Command -join ' ')"
$failures = @()

foreach ($mod in Get-ChildItem $gameDir -Directory) {
    $installer = Join-Path $mod.FullName "install.ps1"
    if (-not ((Test-Path $installer) -and (Test-Path (Join-Path $mod.FullName "backup\state.txt")))) {
        continue
    }
    # One process per mod: each mod loads its own Mono.Cecil and patcher.
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer 2>&1 | Out-String
    $code = $LASTEXITCODE
    Add-Content $log "== $($mod.Name) (exit $code)`r`n$output"
    if ($code -ne 0) {
        $reason = ($output -split "`r?`n" | Where-Object { $_ -match "failed" } | Select-Object -Last 1)
        $failures += "$($mod.Name): $reason"
    }
}

if ($failures.Count -gt 0) {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show(
        "These mods could not be reinstalled, the game starts without them:`n`n$($failures -join "`n")`n`nDetails: $log",
        "Scam With Your Friends mods", "OK", "Warning") | Out-Null
}

# Steam passes the game's own command line (%command%); without one, start the game directly.
if (-not $Command -or $Command.Count -eq 0) {
    $Command = @(Join-Path $gameDir "Scam With Your Friends.exe")
}
$arguments = @($Command | Select-Object -Skip 1)
# Waits for the game, so Steam keeps showing it as running.
if ($arguments.Count -gt 0) {
    Start-Process -FilePath $Command[0] -ArgumentList $arguments -WorkingDirectory $gameDir -Wait
} else {
    Start-Process -FilePath $Command[0] -WorkingDirectory $gameDir -Wait
}
