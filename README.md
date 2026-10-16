# Better Speech Recognition for Scam With Your Friends

Scam With Your Friends turns what you say in calls into text with a small Whisper model (`ggml-base.bin`). It often gets words or whole sentences wrong, especially outside English. This mod lets you swap it for a larger or language-specific Whisper model, right in the game.

- **In-game menu (F8):** see every model for your language, rated for quality and speed and checked against your PC. Download, switch and delete models without leaving the game.
- **Switching without a restart:** a new model loads right away, or after the current call if you are in one.
- **Microphone test:** hold a button, say a sentence and see what the model heard and how long it took.
- **Language-specific models** where they clearly beat the standard ones: German, Chinese, French and Swedish.
- **Fewer made-up sentences:** Whisper sometimes invents phrases during silence ("Thanks for watching", "Vielen Dank."). The mod drops them when the model itself is unsure.
- Runs on the graphics card, with a start prompt per language that helps with scam-call vocabulary.

## Install

1. Extract the `BetterSpeech` folder into the game folder, next to `Scam With Your Friends.exe`.
   (Steam: right-click the game → Manage → Browse local files.)
2. Close the game and run `BetterSpeech\install.bat`. Choose your language; the matching model is downloaded and checked.
3. Start the game and press **F8** to open the menu.

After a game update, run `install.bat` again. The game's own files are replaced by the update, so the mod is gone until you do.

**Uninstall:** run `BetterSpeech\uninstall.bat` with the game closed. It restores the original game files. `uninstall.ps1 -Purge` also deletes the downloaded models and the settings. If anything goes wrong: Steam → Properties → Installed Files → Verify integrity of game files.

## The menu

| Part | What it shows |
|---|---|
| Status | The model in use, whether it runs on the graphics card, and how long the last sentence in a call took. |
| Language | Models are rated for the language you pick. Only the game's call languages are listed. |
| Model list | Sorted by recommendation. **Quality**: five dots and the share of words the model gets wrong (lower is better). **Speed**: five dots and the estimated time per sentence on your PC. Below each name: how likely the model runs on your PC. |
| Microphone test | Hold "Hold to talk", speak, release. Shows the text, the audio length, the recognition time and whether the mod's filters would change it in a call. |
| Settings | Ignore made-up phrases, remove sound descriptions like `*music*`, graphics card on or off, English model, debug log. |

Ratings are estimates. Quality comes from published benchmarks (see `catalog/models.json` for the sources). Speed and memory needs are estimated from model size and adjusted to your graphics card's memory.

## Models

All models are downloaded from their authors' Hugging Face pages and checked with SHA256. Nothing is bundled with the mod.

| Model | Languages | Source | License |
|---|---|---|---|
| Tiny, Small, Medium, Large v3, Large v3 Turbo (several sizes) | all | [ggerganov/whisper.cpp](https://huggingface.co/ggerganov/whisper.cpp) (OpenAI Whisper) | MIT |
| German Turbo | German | [primeline](https://huggingface.co/primeline/whisper-large-v3-turbo-german), converted by [cstr](https://huggingface.co/cstr/whisper-large-v3-turbo-german-ggml) | Apache-2.0 |
| Chinese Turbo | Chinese | [BELLE-2](https://huggingface.co/BELLE-2/Belle-whisper-large-v3-turbo-zh-ggml) | Apache-2.0 |
| French Large v3 | French | [bofenghuang](https://huggingface.co/bofenghuang/whisper-large-v3-french-distil-dec16) | MIT |
| Swedish Large, Swedish Medium | Swedish | [KBLab](https://huggingface.co/KBLab/kb-whisper-large) (National Library of Sweden) | Apache-2.0 |

A language-specific model is only listed when its authors publish whisper.cpp files themselves, under an open license, with a benchmark against the OpenAI model it is based on.

## Settings file

`BetterSpeech\BetterSpeech.cfg`. Everything important is in the menu; all options appear after the first game start.

- `[Model] ModelFile`: file in `models\`, empty = the game's own model. `ReplaceEnglishModel`: also use it for English calls (the game has a separate English model).
- `[Inference] UseGpu`, `BeamSearch`, `FlashAttention` (off by default: with the game's Vulkan build it made recognition about 60 times slower in testing).
- `[Prompt] German / English / Other`: start prompt per language.
- `[Transcript] DropHallucinations`, `HallucinationMaxConfidence` (default 0.72), `HallucinationPhrases`, `FilterSoundTags`.
- `[Debug] NativeLog`: write whisper.cpp's own log (backend, GPU) into Player.log.

To check that the mod works, look for lines with `[BetterSpeech]` in
`%USERPROFILE%\AppData\LocalLow\Jatater Worldwide\Scam With Your Friends\Player.log`.

## How it works

The game is built with Unity's managed code stripping: `mscorlib.dll` and the other libraries are trimmed, and the game's own DLLs were trimmed to match. That rules out the usual tools:

- BepInEx fails on the trimmed `mscorlib` (`Module.GetPEKind` is missing).
- Full libraries no longer match the trimmed game DLLs (`TypeLoadException` in Mirror, UniTask and Newtonsoft; the game hangs).
- Harmony needs `MethodBase.GetMethodBody`, which is missing too.

So the mod patches four methods in the game's DLLs once, on disk, with a backup, instead of changing them at runtime. The installer is a PowerShell script (no `.exe`) that uses Mono.Cecil.

| Game method | Injected call |
|---|---|
| `Whisper.WhisperManager.InitModel` (start) | `Hooks.BeforeInitModel(this)`: model path, GPU, flash attention, sampling |
| `WhisperVcPipeline.ConfigureRecognizer` (before every `ret`) | `Hooks.AfterConfigureRecognizer(recognizer, code)`: start prompt (the game clears it before each recognition) |
| `WindowsSpeechCompatibility.Initialize` (start) | `Hooks.Init()`: load the settings, create the menu |
| `SpeechTranscriptPostprocessor.ProcessTranscript` (start) | `text = Hooks.FilterTranscript(text, confidence)` |

The patcher stores the checksum of each file before and after patching, plus the patch version, in `BetterSpeech\backup\state.txt`. From that it recognizes files it already patched, files patched by an older version of the mod (it re-patches the backed-up original) and game updates (it patches the new version). If it cannot find a method, it stops before writing anything.

Everything else runs from `BetterSpeech.Runtime.dll`, which is compiled against the game's trimmed libraries (`NoStdLib`), so any API the game lacks is a compile error instead of a crash in game. Model switching frees the old model's native context and loads the new one through the game's own `InitModel`. The microphone test reads along from the game's microphone recording instead of opening the microphone itself, because Unity allows only one recording at a time.

## Building

Requirements: .NET SDK, Windows PowerShell 5.1, and the game installed (the runtime compiles against its DLLs).

```powershell
$game = "X:\SteamLibrary\steamapps\common\Scam With Your Friends"
.\build.ps1 -GameDir $game           # builds dist\BetterSpeech
.\build.ps1 -GameDir $game -Deploy   # also copies it into the game folder (keeps models, settings, backups)
.\build.ps1 -GameDir $game -Zip      # also creates dist\BetterSpeech-<version>.zip
```

Then run `BetterSpeech\install.bat` in the game folder. Run the scripts with Windows PowerShell (`powershell.exe`).

| Path | Content |
|---|---|
| `src/Runtime` | The mod: hooks, menu, model download and switching, microphone test, settings. |
| `src/Patcher` | The Mono.Cecil patcher used by the install scripts. |
| `scripts` | `install` / `uninstall` (`.bat` starts the `.ps1`). |
| `catalog` | `models.json` (models, sizes, hashes, error rates per language) and `gen.py`, which generates it from the benchmark data next to it. |

## License

The mod is released under the [MIT License](LICENSE). Mono.Cecil is MIT. The models keep their own licenses (see the table above).

This mod is not affiliated with the developers of Scam With Your Friends.
