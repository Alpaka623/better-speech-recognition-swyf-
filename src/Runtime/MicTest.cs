using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MetaVoiceChat.Core;
using UnityEngine;
using Whisper;

namespace BetterSpeech;

/// <summary>
/// Push-to-talk test in the menu: records while the button is held and runs the game's own Whisper model on
/// it, with the call language and the mod's prompt and filters. Unity allows only one microphone recording,
/// and the game's voice chat owns it, so the test reads along from the game's looping microphone clip
/// instead of starting its own.
/// </summary>
internal sealed class MicTest
{
    public enum State { Idle, Listening, Recognizing, Done, Failed }

    private const float MaxSeconds = 20f;     // the game's own utterance limit
    private const float MinSeconds = 0.3f;
    private const float WhisperMinSeconds = 1.1f; // the game pads shorter audio to this

    private readonly List<float> samples = new List<float>();
    private AudioClip clip;
    private string device;
    private int frequency;
    private int lastPosition;

    private Task<WhisperResult> task;
    private WhisperManager manager;
    private string previousLanguage;
    private string previousPrompt;
    private string languageCode;
    private float recognizeStarted;

    public State Status { get; private set; }
    public bool Busy => Status == State.Listening || Status == State.Recognizing;
    public float Level { get; private set; }
    public float Seconds => frequency > 0 ? samples.Count / (float)frequency : 0f;

    /// <summary>What Whisper heard (empty = nothing).</summary>
    public string Text { get; private set; } = "";
    /// <summary>Audio length, time and model, or the error when Failed.</summary>
    public string Info { get; private set; } = "";
    /// <summary>What the mod's filters would do with it in a call; empty if it passes unchanged.</summary>
    public string FilterNote { get; private set; } = "";

    public void Begin(bool switching)
    {
        if (Busy)
        {
            return;
        }
        if (ModelSwitcher.InCall)
        {
            Fail("Not available during a call.");
            return;
        }
        if (switching)
        {
            Fail("Wait until the model has loaded.");
            return;
        }
        try
        {
            PlayerVoiceSingleton voice = PlayerVoiceSingleton.Instance;
            MicVcInput input = voice != null ? voice.AudioInput : null;
            if (input == null || !input.IsRecording || input.AudioClip == null || string.IsNullOrEmpty(input.ActiveDevice))
            {
                Fail("The game is not using a microphone right now. Check the input device in the game's audio settings.");
                return;
            }
            clip = input.AudioClip;
            device = input.ActiveDevice;
            frequency = clip.frequency;
            lastPosition = Microphone.GetPosition(device);
            samples.Clear();
            Level = 0f;
            Text = Info = FilterNote = "";
            Status = State.Listening;
        }
        catch (Exception ex)
        {
            Fail("Could not read the microphone: " + ex.Message);
        }
    }

    public void End()
    {
        if (Status != State.Listening)
        {
            return;
        }
        Read();
        if (Seconds < MinSeconds)
        {
            Fail("Hold the button while you speak.");
            return;
        }
        try
        {
            languageCode = CurrentLanguage();
            manager = Hooks.ManagerFor(languageCode);
            if (manager == null)
            {
                Fail("Speech recognition has not started yet. Try again in a moment.");
                return;
            }
            if (!manager.IsLoaded && !manager.IsLoading)
            {
                _ = manager.InitModel();
            }
            float[] audio = samples.ToArray();
            int minimum = Mathf.CeilToInt(WhisperMinSeconds * frequency);
            if (audio.Length < minimum)
            {
                Array.Resize(ref audio, minimum);
            }
            // Same setup the game applies before each recognition in a call; restored when done.
            previousLanguage = manager.language;
            previousPrompt = manager.initialPrompt;
            manager.language = languageCode;
            manager.translateToEnglish = false;
            string prompt = Hooks.Config.Enabled ? Hooks.Config.PromptFor(languageCode) : "";
            manager.initialPrompt = string.IsNullOrWhiteSpace(prompt) ? string.Empty : prompt.Trim();
            recognizeStarted = Time.realtimeSinceStartup;
            task = manager.GetTextAsync(audio, frequency, 1);
            Status = State.Recognizing;
        }
        catch (Exception ex)
        {
            Restore();
            Fail("Recognition failed: " + ex.Message);
        }
    }

    public void Cancel()
    {
        if (Status == State.Listening)
        {
            Status = State.Idle;
            samples.Clear();
        }
    }

    public void Update()
    {
        if (Status == State.Listening)
        {
            if (ModelSwitcher.InCall)
            {
                Fail("Stopped because a call started.");
                return;
            }
            Read();
            if (Seconds >= MaxSeconds)
            {
                End();
            }
        }
        else if (Status == State.Recognizing && task != null && task.IsCompleted)
        {
            Finish();
        }
    }

    private void Read()
    {
        if (clip == null)
        {
            Fail("The microphone stopped.");
            return;
        }
        int position = Microphone.GetPosition(device);
        int count = position - lastPosition;
        if (count < 0)
        {
            count += clip.samples; // the game's clip loops every few seconds
        }
        if (count <= 0)
        {
            return;
        }
        var chunk = new float[count];
        // GetData wraps around the end of a looping clip.
        if (!clip.GetData(chunk, lastPosition))
        {
            return;
        }
        lastPosition = position;
        double sum = 0;
        for (int i = 0; i < count; i++)
        {
            sum += chunk[i] * chunk[i];
        }
        samples.AddRange(chunk);
        // Speech sits around 0.01-0.1 RMS; scaled so normal speaking fills most of the bar.
        Level = Mathf.Clamp01(Mathf.Sqrt((float)(sum / count)) * 8f);
    }

    private void Finish()
    {
        float ms = (Time.realtimeSinceStartup - recognizeStarted) * 1000f;
        Restore();
        if (task.IsFaulted || task.IsCanceled)
        {
            Fail("Recognition failed" + (task.Exception != null ? ": " + task.Exception.GetBaseException().Message : "."));
            return;
        }
        WhisperResult result = task.Result;
        task = null;
        if (result == null)
        {
            Fail("Recognition failed. The model may not be loaded.");
            return;
        }
        string raw = (result.Result ?? "").Trim();
        float confidence = Confidence(result);
        string model = Hooks.ActiveModelPath != null ? Path.GetFileNameWithoutExtension(Hooks.ActiveModelPath) : "game model";
        Text = raw;
        Info = $"{Seconds:0.0} s of audio · recognized in {ms:0} ms · {confidence * 100f:0}% sure";
        string filtered = Hooks.ApplyFilters(raw, confidence, out string reason);
        if (raw.Length > 0 && filtered.Length == 0)
        {
            FilterNote = "In a call this would be ignored as a likely made-up phrase.";
        }
        else if (filtered != raw)
        {
            FilterNote = $"In a call this is sent as: {filtered}";
        }
        Hooks.Log($"Mic test ({languageCode}, {model}): \"{raw}\" in {ms:0} ms, {Seconds:0.0} s audio, confidence {confidence:0.00}");
        Status = State.Done;
    }

    private void Restore()
    {
        if (manager != null)
        {
            manager.language = previousLanguage;
            manager.initialPrompt = previousPrompt;
        }
    }

    private void Fail(string message)
    {
        Status = State.Failed;
        Info = message;
        Text = FilterNote = "";
        samples.Clear();
        task = null;
    }

    /// <summary>Average token probability, as the game computes it for its hallucination checks.</summary>
    private static float Confidence(WhisperResult result)
    {
        float sum = 0f;
        int count = 0;
        foreach (WhisperSegment segment in result.Segments)
        {
            if (segment.Tokens == null)
            {
                continue;
            }
            foreach (WhisperTokenData token in segment.Tokens)
            {
                if (token != null && !token.IsSpecial && !string.IsNullOrWhiteSpace(token.Text))
                {
                    sum += token.Prob;
                    count++;
                }
            }
        }
        return count > 0 ? sum / count : 0f;
    }

    private static string CurrentLanguage()
    {
        try
        {
            return Kolkata.Speech.CallLanguage.Current?.Code ?? "en";
        }
        catch
        {
            return "en";
        }
    }
}
