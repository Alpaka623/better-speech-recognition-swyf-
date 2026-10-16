using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using Whisper;

namespace BetterSpeech;

/// <summary>
/// Swaps the loaded Whisper model without restarting the game. WhisperManager loads its model once, so the
/// switch frees the native context of the old model (first, so both never sit in video memory together)
/// and calls InitModel again, where Hooks.BeforeInitModel applies the new config. Only done outside calls
/// and while the game is not recognizing anything, because a running inference uses the native context.
/// </summary>
internal static class ModelSwitcher
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo WrapperField = typeof(WhisperManager).GetField("_whisper", Private);
    private static readonly FieldInfo ContextField = typeof(WhisperWrapper).GetField("_whisperCtx", Private);
    private static readonly FieldInfo LockField = typeof(WhisperWrapper).GetField("_lock", Private);
    private static readonly FieldInfo InferenceField = typeof(WhisperVcPipeline).GetField("inferenceInProgress", Private);

    [DllImport("libwhisper")]
    private static extern void whisper_free(IntPtr ctx);

    /// <summary>A model or GPU change waits to be applied (right away, or once the call is over).</summary>
    public static bool Pending { get; set; }

    public static bool Switching { get; private set; }

    /// <summary>The game has a call (ringing or talking); its id is 0 otherwise.</summary>
    public static bool InCall
    {
        get
        {
            try
            {
                return ReviewCaptureClock.CurrentCallId != 0;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>The game's recognizer is running an inference right now.</summary>
    public static bool GameRecognizing
    {
        get
        {
            try
            {
                PlayerVoiceSingleton voice = PlayerVoiceSingleton.Instance;
                if (voice == null || InferenceField == null)
                {
                    return false;
                }
                WhisperVcPipeline pipeline = voice.GetComponent<WhisperVcPipeline>();
                return pipeline != null && (bool)InferenceField.GetValue(pipeline);
            }
            catch
            {
                return false;
            }
        }
    }

    public static bool CanSwitchNow(bool micTestBusy) => !Switching && !micTestBusy && !InCall && !GameRecognizing;

    /// <summary>
    /// Reloads every loaded model that no longer matches the config. If a model fails to load, the config
    /// goes back to fallbackModelFile and that one is loaded instead; done gets an error text or null.
    /// </summary>
    public static IEnumerator Run(string fallbackModelFile, Action<string> done)
    {
        Switching = true;
        Pending = false;
        string error = null;
        bool retried = false;
        try
        {
            List<WhisperManager> managers = Hooks.ManagersNeedingReload();
            for (int i = 0; i < managers.Count; i++)
            {
                WhisperManager manager = managers[i];
                if (manager == null)
                {
                    continue;
                }
                float started = Time.realtimeSinceStartup;
                if (!Unload(manager))
                {
                    error = "The model could not be unloaded.";
                    break;
                }
                Task load = manager.InitModel();
                while (!load.IsCompleted)
                {
                    yield return null;
                }
                if (manager.IsLoaded)
                {
                    Hooks.Log($"Switched model in {(Time.realtimeSinceStartup - started) * 1000f:0} ms.");
                    continue;
                }
                if (retried || fallbackModelFile == null)
                {
                    error = "The model could not be loaded.";
                    break;
                }
                // Back to the model that worked, and reload the managers that still need it.
                Hooks.LogWarning("The new model could not be loaded, going back to the previous one.");
                Hooks.Config.Set("Model", "ModelFile", fallbackModelFile);
                error = "The model could not be loaded, the previous one is used again.";
                retried = true;
                managers = Hooks.ManagersNeedingReload();
                if (!managers.Contains(manager) && !manager.IsLoaded)
                {
                    managers.Insert(0, manager);
                }
                i = -1;
            }
        }
        finally
        {
            Switching = false;
        }
        done?.Invoke(error);
    }

    /// <summary>Frees the native context under the wrapper's own lock, so no inference is cut off.</summary>
    private static bool Unload(WhisperManager manager)
    {
        try
        {
            object wrapper = WrapperField.GetValue(manager);
            if (wrapper == null)
            {
                return true;
            }
            lock (LockField.GetValue(wrapper))
            {
                WrapperField.SetValue(manager, null);
                var context = (IntPtr)ContextField.GetValue(wrapper);
                if (context != IntPtr.Zero)
                {
                    // Zeroed first, so the wrapper's finalizer does not free it a second time.
                    ContextField.SetValue(wrapper, IntPtr.Zero);
                    whisper_free(context);
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            Hooks.LogError("Could not unload the Whisper model", ex);
            return false;
        }
    }
}
