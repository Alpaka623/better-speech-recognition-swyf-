using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using Whisper;

namespace BetterSpeech;

/// <summary>
/// Game 0.1.95 and later no longer load models through WhisperManager.InitModel. WhisperVcPipeline keeps one
/// WhisperModel (an internal class in ScriptsAssDef) per WhisperManager in its private "models" dictionary and
/// loads it with WhisperModel.LoadAsync(manager), where Hooks.BeforeInitModel is injected. The WhisperManager
/// is only the settings holder there. Everything here reaches the pipeline through reflection; on older game
/// versions Active is false and the mod works on the WhisperManager as before.
/// </summary>
internal static class GamePipeline
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo ModelsField = typeof(WhisperVcPipeline).GetField("models", Private);
    private static readonly FieldInfo InitializationsField = typeof(WhisperVcPipeline).GetField("initializations", Private);
    private static readonly FieldInfo SwitchingField = typeof(WhisperVcPipeline).GetField("switchingWhisper", Private);
    private static readonly MethodInfo ApplyPreference = typeof(WhisperVcPipeline).GetMethod("ApplyRecognitionPreference", Private);
    private static readonly MethodInfo GetText = typeof(WhisperVcPipeline).Assembly.GetType("WhisperModel")
        ?.GetMethod("GetTextAsync", new[] { typeof(float[]), typeof(WhisperManager) });

    /// <summary>The game loads its models through WhisperModel.LoadAsync (0.1.95 and later).</summary>
    public static bool Active => ModelsField != null && InitializationsField != null && SwitchingField != null
                                 && ApplyPreference != null && GetText != null;

    private static WhisperVcPipeline Pipeline
    {
        get
        {
            PlayerVoiceSingleton voice = PlayerVoiceSingleton.Instance;
            return voice != null ? voice.GetComponent<WhisperVcPipeline>() : null;
        }
    }

    private static bool Contains(FieldInfo dictionary, WhisperManager manager)
    {
        WhisperVcPipeline pipeline = Pipeline;
        return pipeline != null && manager != null && dictionary.GetValue(pipeline) is IDictionary entries && entries.Contains(manager);
    }

    /// <summary>The pipeline has a loaded model for this manager.</summary>
    public static bool HasModel(WhisperManager manager) => Contains(ModelsField, manager);

    /// <summary>The pipeline is loading a model for this manager right now.</summary>
    public static bool Loading(WhisperManager manager) => Contains(InitializationsField, manager);

    /// <summary>The pipeline is disposing its models for a switch.</summary>
    public static bool Switching
    {
        get
        {
            WhisperVcPipeline pipeline = Pipeline;
            return pipeline != null && (bool)SwitchingField.GetValue(pipeline);
        }
    }

    /// <summary>
    /// Runs the game's own model switch: it disposes every loaded model, puts its own model path back and
    /// reloads the model for the current call language, through BeforeInitModel. False while the game's
    /// speech pipeline does not exist yet.
    /// </summary>
    public static bool Reload()
    {
        WhisperVcPipeline pipeline = Pipeline;
        if (pipeline == null)
        {
            return false;
        }
        ApplyPreference.Invoke(pipeline, null);
        return true;
    }

    /// <summary>Recognizes 16 kHz mono samples with the pipeline's model for this manager; null when none is loaded.</summary>
    public static Task<WhisperResult> GetTextAsync(WhisperManager manager, float[] samples)
    {
        WhisperVcPipeline pipeline = Pipeline;
        if (pipeline == null || manager == null || !(ModelsField.GetValue(pipeline) is IDictionary models) || !models.Contains(manager))
        {
            return null;
        }
        return (Task<WhisperResult>)GetText.Invoke(models[manager], new object[] { samples, manager });
    }
}
