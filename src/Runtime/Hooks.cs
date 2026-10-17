using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using Whisper;
using Whisper.Native;

namespace BetterSpeech;

/// <summary>
/// Entry points the patcher injects into the game. Every method must swallow its own exceptions:
/// an exception here would break the game's speech recognition instead of just the mod.
/// </summary>
public static class Hooks
{
    public const string Version = "0.7.0";

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo ModelPathField = typeof(WhisperManager).GetField("modelPath", Private);
    private static readonly FieldInfo InStreamingAssetsField = typeof(WhisperManager).GetField("isModelPathInStreamingAssets", Private);
    private static readonly FieldInfo UseGpuField = typeof(WhisperManager).GetField("useGpu", Private);
    private static readonly FieldInfo FlashAttentionField = typeof(WhisperManager).GetField("flashAttention", Private);
    private static readonly FieldInfo StrategyField = typeof(WhisperManager).GetField("strategy", Private);

    private static readonly Regex SoundTag = new Regex(@"\*[^*]*\*|\[[^\]]*\]|\([^)]*\)");
    private static readonly Regex LeftoverPunctuation = new Regex(@"^[\s.,!?…;:]*$");
    private static readonly Regex Whitespace = new Regex(@"\s+");
    private static readonly Regex NonWord = new Regex(@"[^\w\s]");
    private static readonly Regex OnlySoundDescription = new Regex(@"^\s*\*[^*]+\*[\s.,!?…;:]*$");

    private static ModConfig config;
    private static float lastConfigureTime = -1f;

    /// <summary>Model file the game actually loaded this session (null = game default).</summary>
    internal static string ActiveModelPath { get; private set; }
    internal static bool ActiveGpu { get; private set; }
    /// <summary>Time from configuring the recognizer to the final transcript, in ms; -1 = none yet.</summary>
    internal static float LastLatencyMs { get; private set; } = -1f;

    internal static string ModDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "BetterSpeech"));

    internal static ModConfig Config
    {
        get
        {
            if (config == null)
            {
                var loaded = new ModConfig(Path.Combine(ModDirectory, "BetterSpeech.cfg"));
                loaded.Load();
                config = loaded;
            }
            return config;
        }
    }

    /// <summary>Injected at the start of WindowsSpeechCompatibility.Initialize (runs once after assemblies load).</summary>
    public static void Init()
    {
        try
        {
            Log($"Better Speech Recognition {Version} loaded. Mod directory: {ModDirectory}");
            Log($"Model={Config.ModelFile} gpu={Config.UseGpu} flashAttn={Config.FlashAttention} " +
                $"beam={Config.BeamSearch} filterSoundTags={Config.FilterSoundTags} enabled={Config.Enabled}");
            if (Config.NativeLog)
            {
                NativeLog.Enable();
                Log("whisper.cpp log forwarding enabled.");
            }
            // Too early for GameObjects here (AfterAssembliesLoaded); the menu is created with the first scene.
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => MenuBehaviour.EnsureCreated();
        }
        catch (Exception ex)
        {
            LogError("Init failed", ex);
        }
    }

    /// <summary>The model a WhisperManager was set up with by the game, before the mod changed it.</summary>
    private sealed class GameModel
    {
        public string Path;
        public bool InStreamingAssets;
        public bool EnglishOnly => Path != null && Path.EndsWith(".en.bin", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly Dictionary<WhisperManager, GameModel> GameModels = new Dictionary<WhisperManager, GameModel>();
    private static readonly Dictionary<WhisperManager, string> LoadedPaths = new Dictionary<WhisperManager, string>();
    private static readonly Dictionary<WhisperManager, bool> LoadedGpu = new Dictionary<WhisperManager, bool>();

    /// <summary>Injected at the start of WhisperManager.InitModel, before the weights are loaded.</summary>
    public static void BeforeInitModel(WhisperManager manager)
    {
        try
        {
            if (manager == null || manager.IsLoaded || manager.IsLoading)
            {
                return;
            }

            if (!GameModels.TryGetValue(manager, out GameModel game))
            {
                game = new GameModel
                {
                    Path = (string)ModelPathField.GetValue(manager),
                    InStreamingAssets = (bool)InStreamingAssetsField.GetValue(manager),
                };
                GameModels[manager] = game;
                Log($"Game is loading Whisper model '{game.Path}' (gpu={UseGpuField.GetValue(manager)}, " +
                    $"flashAttn={FlashAttentionField.GetValue(manager)}, strategy={StrategyField.GetValue(manager)}).");
            }
            // A reload (model switch from the menu) starts from the game's own setup again.
            ModelPathField.SetValue(manager, game.Path);
            InStreamingAssetsField.SetValue(manager, game.InStreamingAssets);
            LoadedPaths[manager] = null;
            if (!Config.Enabled)
            {
                return;
            }

            LoadedGpu[manager] = Config.UseGpu;
            UseGpuField.SetValue(manager, Config.UseGpu);
            FlashAttentionField.SetValue(manager, Config.FlashAttention);
            StrategyField.SetValue(manager, Config.BeamSearch
                ? WhisperSamplingStrategy.WHISPER_SAMPLING_BEAM_SEARCH
                : WhisperSamplingStrategy.WHISPER_SAMPLING_GREEDY);
            if (!game.EnglishOnly)
            {
                ActiveGpu = Config.UseGpu;
                ActiveModelPath = null;
            }

            string replacement = Replacement(game, true);
            if (replacement == null)
            {
                return;
            }
            ModelPathField.SetValue(manager, replacement);
            InStreamingAssetsField.SetValue(manager, false);
            LoadedPaths[manager] = replacement;
            if (!game.EnglishOnly)
            {
                ActiveModelPath = replacement;
            }
            Log($"Replaced '{game.Path}' with '{replacement}' (gpu={Config.UseGpu}, " +
                $"flashAttn={Config.FlashAttention}, beam={Config.BeamSearch}).");
        }
        catch (Exception ex)
        {
            LogError("BeforeInitModel failed, the game's model stays active", ex);
        }
    }

    /// <summary>The mod's model file for this manager, or null to keep the game's model.</summary>
    private static string Replacement(GameModel game, bool log)
    {
        if (!Config.Enabled || game.EnglishOnly && !Config.ReplaceEnglishModel)
        {
            return null;
        }
        string file = Config.ModelFile.Trim();
        if (file.Length == 0)
        {
            return null;
        }
        string replacement = Path.IsPathRooted(file) ? file : Path.Combine(ModDirectory, "models", file);
        if (!File.Exists(replacement))
        {
            if (log)
            {
                LogWarning($"Model '{replacement}' not found, keeping the game's model '{game.Path}'.");
            }
            return null;
        }
        return replacement;
    }

    /// <summary>Loaded managers whose model or GPU setting no longer matches the config.</summary>
    internal static List<WhisperManager> ManagersNeedingReload()
    {
        var result = new List<WhisperManager>();
        foreach (KeyValuePair<WhisperManager, GameModel> entry in GameModels)
        {
            WhisperManager manager = entry.Key;
            if (manager == null || !manager.IsLoaded)
            {
                continue;
            }
            LoadedPaths.TryGetValue(manager, out string loaded);
            bool gpu = LoadedGpu.TryGetValue(manager, out bool g) ? g : (bool)UseGpuField.GetValue(manager);
            string wanted = Replacement(entry.Value, false);
            if (!string.Equals(wanted, loaded, StringComparison.OrdinalIgnoreCase) || Config.Enabled && gpu != Config.UseGpu)
            {
                result.Add(manager);
            }
        }
        return result;
    }

    /// <summary>The manager the game uses for this call language (the English one only for "en").</summary>
    internal static WhisperManager ManagerFor(string languageCode)
    {
        WhisperManager main = null, english = null;
        foreach (KeyValuePair<WhisperManager, GameModel> entry in GameModels)
        {
            if (entry.Key == null)
            {
                continue;
            }
            if (entry.Value.EnglishOnly)
            {
                english = entry.Key;
            }
            else
            {
                main = entry.Key;
            }
        }
        return languageCode == "en" && english != null ? english : main;
    }

    /// <summary>
    /// Injected before every return of WhisperVcPipeline.ConfigureRecognizer, which resets initialPrompt
    /// to "" before each inference.
    /// </summary>
    public static void AfterConfigureRecognizer(WhisperManager recognizer, string languageCode)
    {
        try
        {
            lastConfigureTime = Time.realtimeSinceStartup;
            if (recognizer == null || !Config.Enabled)
            {
                return;
            }
            string prompt = Config.PromptFor(languageCode);
            if (!string.IsNullOrWhiteSpace(prompt))
            {
                recognizer.initialPrompt = prompt.Trim();
            }
        }
        catch (Exception ex)
        {
            LogError("AfterConfigureRecognizer failed", ex);
        }
    }

    /// <summary>
    /// Injected at the start of SpeechTranscriptPostprocessor.ProcessTranscript(text, confidence, languageCode);
    /// its result replaces the text. The game only drops [BLANK_AUDIO] and [NO SPEECH]; an empty result makes
    /// it discard the transcript.
    /// </summary>
    public static string FilterTranscript(string text, float confidence)
    {
        try
        {
            if (lastConfigureTime >= 0f)
            {
                LastLatencyMs = (Time.realtimeSinceStartup - lastConfigureTime) * 1000f;
            }
            string result = ApplyFilters(text, confidence, out string reason);
            if (reason != null)
            {
                Log(reason);
            }
            return result;
        }
        catch (Exception ex)
        {
            LogError("FilterTranscript failed", ex);
            return text;
        }
    }

    /// <summary>The mod's transcript filters; reason describes what was changed (null = unchanged).</summary>
    internal static string ApplyFilters(string text, float confidence, out string reason)
    {
        reason = null;
        if (string.IsNullOrEmpty(text) || !Config.Enabled)
        {
            return text;
        }
        if (Config.DropHallucinations && confidence < Config.HallucinationMaxConfidence && IsHallucination(text))
        {
            reason = $"Dropped likely hallucination \"{text}\" (confidence {confidence:0.00} < {Config.HallucinationMaxConfidence:0.00})";
            return string.Empty;
        }
        if (!Config.FilterSoundTags)
        {
            return text;
        }
        string cleaned = Whitespace.Replace(SoundTag.Replace(text, " "), " ").Trim();
        if (LeftoverPunctuation.IsMatch(cleaned))
        {
            cleaned = string.Empty;
        }
        if (cleaned != text.Trim())
        {
            reason = $"Filtered sound tags: \"{text}\" -> \"{cleaned}\"";
            return cleaned;
        }
        return text;
    }

    /// <summary>Whisper invents these from silence because of its subtitle training data ("Vielen Dank.", "Untertitel im Auftrag des ZDF").</summary>
    private static bool IsHallucination(string text)
    {
        // A transcript that is nothing but one sound description, e.g. "*Kritte, die Musik*."
        if (OnlySoundDescription.IsMatch(text))
        {
            return true;
        }
        string normalized = Whitespace.Replace(NonWord.Replace(text.ToLowerInvariant(), " "), " ").Trim();
        if (normalized.Length == 0)
        {
            return false;
        }
        foreach (string raw in Config.HallucinationPhrases.Split('|'))
        {
            string phrase = Whitespace.Replace(NonWord.Replace(raw.ToLowerInvariant().Replace("*", ""), " "), " ").Trim();
            if (phrase.Length == 0)
            {
                continue;
            }
            bool prefix = raw.Trim().EndsWith("*");
            if (prefix ? normalized.StartsWith(phrase, StringComparison.Ordinal) : normalized == phrase)
            {
                return true;
            }
        }
        return false;
    }

    internal static void Log(string message) => Debug.Log("[BetterSpeech] " + message);

    internal static void LogWarning(string message) => Debug.LogWarning("[BetterSpeech] " + message);

    internal static void LogError(string message, Exception ex) => Debug.LogError($"[BetterSpeech] {message}: {ex}");
}
