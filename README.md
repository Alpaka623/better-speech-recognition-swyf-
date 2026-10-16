# Better Speech Recognition – Scam With Your Friends

Ersetzt das kleine Whisper-Modell des Spiels (`ggml-base.bin`) durch ein größeres, steuert GPU-Einstellungen und gibt Whisper einen Start-Prompt pro Sprache. Optional werden erfundene Geräusch-Tags wie `*Klingel*` herausgefiltert.

## Warum kein BepInEx

Das Spiel ist mit Managed Code Stripping gebaut: `mscorlib.dll` und Co. sind gekürzt, und Unity hat die Spiel-DLLs passend dazu mitgekürzt.

- BepInEx scheitert an der gekürzten `mscorlib` (`Module.GetPEKind` fehlt).
- Ungekürzte Bibliotheken passen nicht mehr zu den gekürzten Spiel-DLLs (`TypeLoadException` in Mirror, UniTask und Newtonsoft; das Spiel hängt).
- Harmony braucht `MethodBase.GetMethodBody`, das ebenfalls fehlt.

Deshalb patcht die Mod die Spiel-DLLs einmalig auf der Festplatte (mit Sicherung), statt sie zur Laufzeit zu verändern.

## Aufbau

| Teil | Wo | Was |
|---|---|---|
| `BetterSpeech.Runtime.dll` | `src/Runtime` → `Managed\` | Mod-Logik (`Hooks`, `ModConfig`). Kompiliert gegen die **gekürzten** Bibliotheken des Spiels (`NoStdLib`), damit nur vorhandene APIs genutzt werden. |
| `BetterSpeech.Patcher.dll` | `src/Patcher` | Mono.Cecil-Patcher. Läuft in Windows PowerShell 5.1, keine `.exe`. |
| `install.bat` / `uninstall.bat` | `scripts/` | Starten die `.ps1`-Skripte. |
| `catalog/models.json` | | Modell-Liste (Größe, SHA256, Fehlerrate pro Sprache, Lizenz). |

Eingefügte Aufrufe (je einer pro Stelle):

| Spielmethode | Aufruf |
|---|---|
| `Whisper.WhisperManager.InitModel` (Anfang) | `Hooks.BeforeInitModel(this)`: Modellpfad, GPU, Flash Attention, Sampling |
| `WhisperVcPipeline.ConfigureRecognizer` (vor jedem `ret`) | `Hooks.AfterConfigureRecognizer(recognizer, code)`: Start-Prompt (das Spiel leert ihn vor jeder Erkennung) |
| `WindowsSpeechCompatibility.Initialize` (Anfang) | `Hooks.Init()`: Config laden, später Menü |
| `SpeechTranscriptPostprocessor.ProcessTranscript` (Anfang) | `text = Hooks.FilterTranscript(text, confidence)` |

Der Patcher speichert in `BetterSpeech\backup\state.txt` pro Datei die Prüfsumme vor und nach dem Patchen sowie die Patch-Version (`PatchVersion` in `GamePatcher.cs`, bei jeder Änderung am eingefügten IL erhöhen). Daran erkennt er „schon gepatcht“, ältere Patch-Versionen (dann wird das gesicherte Original neu gepatcht) und Spiel-Updates (dann wird die neue Version gepatcht). Findet er eine Methode nicht, bricht er ab, bevor er etwas schreibt.

## Entwickeln

```powershell
.\build.ps1           # baut dist\BetterSpeech (das Nexus-Paket)
.\build.ps1 -Deploy   # kopiert es in den Spielordner (Modelle, Config, Backups bleiben)
.\build.ps1 -Zip      # zusätzlich dist\BetterSpeech-<version>.zip
```

Danach `BetterSpeech\install.bat` im Spielordner ausführen. Skripte immer mit Windows PowerShell (`powershell.exe`) starten; die PowerShell 7 aus dem Microsoft Store sieht wegen der App-Isolation manche Ordner unter `AppData\Roaming` nicht.

## Für Nutzer

1. Den Ordner `BetterSpeech` in den Spielordner entpacken (neben `Scam With Your Friends.exe`).
2. `BetterSpeech\install.bat` ausführen und die Sprache wählen. Das Modell wird geladen und per SHA256 geprüft.
3. Nach jedem Spiel-Update `install.bat` erneut ausführen.

Entfernen: `uninstall.bat` stellt die Originaldateien wieder her. `uninstall.ps1 -Purge` löscht zusätzlich Modelle und Config. Notfalls: Steam → Eigenschaften → Installierte Dateien → Dateien auf Fehler überprüfen.

## Konfiguration

`BetterSpeech\BetterSpeech.cfg` (alle Optionen erscheinen nach dem ersten Spielstart):

- `[Model] ModelFile`: Datei in `models\`, z. B. `ggml-turbo-german-q5.bin`, `ggml-turbo-q5.bin`, `ggml-small-q5.bin`
- `[Model] ReplaceEnglishModel`: auch für Englisch das neue Modell nutzen
- `[Inference] UseGpu / BeamSearch`
- `[Inference] FlashAttention`: Standard **aus**. Gemessen auf einer RTX 4070 Ti SUPER mit turbo-german-q5: ~9 s pro Satz mit, ~0,14 s ohne.
- `[Prompt] German / English / Other`
- `[Transcript] FilterSoundTags`: `*…*`, `[…]`, `(…)` entfernen (Standard: aus)
- `[Transcript] DropHallucinations / HallucinationMaxConfidence / HallucinationPhrases`: verwirft Sätze, die Whisper bei Stille erfindet („Vielen Dank.“, „Untertitel im Auftrag des ZDF“), aber nur unter der Sicherheits-Schwelle (Standard 0,8)
- `[Debug] NativeLog`: whisper.cpp-eigenes Log (Backend, GPU) ins Player.log schreiben

Prüfen, ob es wirkt: `%USERPROFILE%\AppData\LocalLow\Jatater Worldwide\Scam With Your Friends\Player.log`, Zeilen mit `[BetterSpeech]` und `[SpeechTiming] provider=Whisper ... model=<neuer Pfad>`.

## Lizenzen

Mod: MIT. Mono.Cecil: MIT. Whisper-Modelle von OpenAI: MIT. turbo-german (primeline): Apache-2.0.
