using System;
using System.Collections.Generic;
using System.IO;
using Kolkata.Speech;
using UnityEngine;

namespace BetterSpeech;

/// <summary>
/// In-game menu (F8): status chips, language, the model list for that language (sorted by recommendation,
/// with download / use / delete), a microphone test and the main switches. Only the model list scrolls.
/// Model and GPU changes are applied right away, or once the current call is over (ModelSwitcher).
/// </summary>
internal sealed class MenuBehaviour : MonoBehaviour
{
    private const KeyCode ToggleKey = KeyCode.F8;
    private const float ReferenceHeight = 1080f;
    private const float WindowWidth = 820f;
    private const float QualityColumn = 165f;
    private const float SpeedColumn = 110f;
    private const float ActionColumn = 170f;
    private const float MinListHeight = 160f;

    private static MenuBehaviour instance;

    private Theme theme;
    private bool open;
    private Vector2 listScroll;
    // Sizes are measured during Repaint and applied only in the next Layout event, so every event of a frame
    // sees the same layout (IMGUI calls OnGUI several times per frame; changing sizes in between makes it jump).
    private float fixedHeight = 300f;       // menu height without the list, measured
    private float listContentHeight = 300f; // height of everything inside the list, measured
    private float listViewHeight = 300f;    // applied list height
    private float panelHeight = 600f;       // applied panel height
    private bool languageListOpen;
    private bool advancedOpen;
    private Catalog catalog;
    private string catalogError;
    private string language;
    private ModelDownload download;
    private string notice = "";
    private CursorLockMode previousLock;
    private readonly MicTest micTest = new MicTest();

    public static void EnsureCreated()
    {
        if (instance != null)
        {
            return;
        }
        try
        {
            var go = new GameObject("BetterSpeechMenu");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<MenuBehaviour>();
            InputBlocker.Install();
            Debug.Log("[BetterSpeech] Menu ready, press F8 to open it.");
        }
        catch (Exception ex)
        {
            Debug.LogError("[BetterSpeech] Could not create the menu: " + ex);
        }
    }

    private static string ModelDirectory => Path.Combine(Hooks.ModDirectory, "models");

    private void OnDestroy()
    {
        InputBlocker.Blocking = false;
        theme?.Destroy();
    }

    private void LoadCatalog()
    {
        try
        {
            catalog = Catalog.Load(Path.Combine(Hooks.ModDirectory, "catalog", "models.json"));
            catalogError = null;
        }
        catch (Exception ex)
        {
            catalog = null;
            catalogError = "Could not read catalog\\models.json: " + ex.Message;
            Debug.LogError("[BetterSpeech] " + catalogError);
        }
    }

    private static string CallLanguageCode()
    {
        try
        {
            return CallLanguage.Current?.Code ?? "en";
        }
        catch
        {
            return "en";
        }
    }

    private void SetOpen(bool value)
    {
        if (open == value)
        {
            return;
        }
        open = value;
        InputBlocker.Blocking = value;
        if (open)
        {
            if (catalog == null)
            {
                LoadCatalog();
            }
            if (language == null || catalog != null && !catalog.LanguageCodes.Contains(language))
            {
                language = CallLanguageCode();
            }
            previousLock = Cursor.lockState;
        }
        else
        {
            languageListOpen = false;
            micTest.Cancel();
            // Cursor.visible has no getter in the game's stripped UnityEngine; a locked cursor is a hidden one.
            Cursor.lockState = previousLock;
            Cursor.visible = previousLock != CursorLockMode.Locked;
        }
    }

    private void Update()
    {
        micTest.Update();
        if (ModelSwitcher.Pending && ModelSwitcher.CanSwitchNow(micTest.Busy))
        {
            // If the new model fails to load, the one running now comes back.
            string fallback = Hooks.ActiveModelPath != null ? Path.GetFileName(Hooks.ActiveModelPath) : "";
            StartCoroutine(ModelSwitcher.Run(fallback, error =>
                notice = error ?? (open ? $"Now using {SelectedName(Hooks.Config)}." : "")));
        }
    }

    /// <summary>Applies the config's model and GPU setting now, or after the call.</summary>
    private void RequestSwitch()
    {
        ModelSwitcher.Pending = true;
        notice = ModelSwitcher.InCall ? $"{SelectedName(Hooks.Config)} is loaded when the call is over." : "";
    }

    private static string SelectedName(ModConfig config)
    {
        string file = config.ModelFile.Trim();
        return file.Length == 0 ? "Base (game default)" : DisplayName(Path.GetFileNameWithoutExtension(file).Replace("ggml-", ""));
    }

    private void LateUpdate()
    {
        if (open)
        {
            // The game re-locks the cursor every frame while playing.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void OnGUI()
    {
        Event e = Event.current;
        if (e.type == EventType.MouseUp && micTest.Status == MicTest.State.Listening)
        {
            micTest.End();
        }
        if (e.type == EventType.KeyDown && (e.keyCode == ToggleKey || open && e.keyCode == KeyCode.Escape))
        {
            SetOpen(e.keyCode == ToggleKey && !open);
            e.Use();
        }
        if (!open)
        {
            return;
        }

        if (theme == null)
        {
            theme = new Theme();
        }
        Font previousFont = GUI.skin.font;
        if (theme.Font != null)
        {
            GUI.skin.font = theme.Font;
        }

        float scale = Mathf.Max(0.75f, Screen.height / ReferenceHeight);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
        float screenHeight = Screen.height / scale;
        if (e.type == EventType.Layout)
        {
            // The list gets whatever height the rest of the menu leaves; only it scrolls.
            float maxHeight = screenHeight - 60f;
            listViewHeight = Mathf.Round(Mathf.Max(MinListHeight, Mathf.Min(listContentHeight, maxHeight - fixedHeight)));
            panelHeight = Mathf.Round(Mathf.Min(fixedHeight + listViewHeight, maxHeight));
        }
        var area = new Rect(Mathf.Round(Mathf.Max(0f, (Screen.width / scale - WindowWidth) / 2f)),
            Mathf.Round(Mathf.Max(20f, (screenHeight - panelHeight) / 2f)), WindowWidth, panelHeight);

        // GUILayout.Window cannot be themed with the stripped IMGUI (no style overload, no style getters),
        // so the menu is a centered layout area with its own background.
        GUILayout.BeginArea(area, GUIContent.none, theme.Panel);
        try
        {
            DrawContent();
            Rect end = GUILayoutUtility.GetRect(0f, 0f, GUIStyle.none);
            if (e.type == EventType.Repaint)
            {
                // + bottom padding of the panel; minus the list, whose height is chosen separately.
                fixedHeight = end.yMax + theme.Panel.padding.bottom - listViewHeight;
            }
        }
        finally
        {
            GUILayout.EndArea();
            GUI.skin.font = previousFont;
        }
    }

    private void DrawContent()
    {
        ModConfig config = Hooks.Config;

        GUILayout.BeginHorizontal();
        GUILayout.Label("Better Speech Recognition", theme.Title, GUILayout.ExpandWidth(false));
        GUILayout.Space(8);
        GUILayout.Label("v" + Hooks.Version, theme.Version, GUILayout.ExpandWidth(false));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Close", theme.LinkButton, GUILayout.ExpandWidth(false)))
        {
            SetOpen(false);
        }
        GUILayout.EndHorizontal();
        GUILayout.Space(12);

        DrawStatus(config);

        if (catalog == null)
        {
            GUILayout.Space(12);
            GUILayout.Label(catalogError ?? "Catalog not loaded.", theme.Label);
            if (GUILayout.Button("Reload catalog", theme.SecondaryButton, GUILayout.ExpandWidth(false), GUILayout.Height(Theme.ButtonHeight)))
            {
                LoadCatalog();
            }
        }
        else if (languageListOpen)
        {
            DrawLanguageList();
        }
        else
        {
            DrawModels(config);
        }

        DrawMicTest();
        DrawSettings(config);

        if (notice.Length > 0)
        {
            GUILayout.Space(10);
            GUILayout.Label(notice, theme.Label);
        }
        GUILayout.Space(10);
        GUILayout.Label("Model changes apply right away, or after the call if you are in one.", theme.Muted);
    }

    private void DrawStatus(ModConfig config)
    {
        string running = Hooks.ActiveModelPath != null
            ? DisplayName(Path.GetFileNameWithoutExtension(Hooks.ActiveModelPath).Replace("ggml-", ""))
            : "Base (game default)";
        string device = Hooks.ActiveGpu ? "graphics card" : "processor";
        if (ModelSwitcher.Switching)
        {
            running = "Loading " + SelectedName(config);
        }
        string latency = Hooks.LastLatencyMs >= 0 ? $"Last sentence in a call: {Hooks.LastLatencyMs:0} ms" : "Speak in a call to measure speed";
        string languageName = catalog != null ? catalog.LanguageName(language ?? CallLanguageCode()) : CallLanguageCode();

        GUILayout.BeginHorizontal();
        GUILayout.Label($"{running} · {device}", theme.ChipStyle, GUILayout.ExpandWidth(false));
        GUILayout.Label(latency, theme.ChipStyle, GUILayout.ExpandWidth(false));
        if (catalog != null && GUILayout.Button(languageListOpen ? "Back to models" : $"Language: {languageName}", theme.ChipButton, GUILayout.ExpandWidth(false)))
        {
            languageListOpen = !languageListOpen;
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    private void DrawLanguageList()
    {
        GUILayout.Label($"Choose your language. Models are rated for it.", theme.Section);
        string call = CallLanguageCode();
        List<string> codes = GameLanguages();
        if (codes.Remove(call))
        {
            codes.Insert(0, call);
        }
        BeginList();
        foreach (string code in codes)
        {
            string label = catalog.LanguageName(code) + (code == call ? "   game setting" : "");
            if (GUILayout.Button(label, code == language ? theme.ListItemSelected : theme.ListItem, GUILayout.Height(30)))
            {
                language = code;
                languageListOpen = false;
                listScroll = Vector2.zero;
            }
        }
        EndList();
    }

    /// <summary>The game's call languages (kolkata-speech-languages.json) that the catalog has data for.</summary>
    private List<string> GameLanguages()
    {
        var codes = new List<string>();
        try
        {
            foreach (CallLanguageOption option in CallLanguage.Options)
            {
                if (catalog.LanguageCodes.Contains(option.Code))
                {
                    codes.Add(option.Code);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterSpeech] Could not read the game's call languages: " + ex.Message);
        }
        if (codes.Count == 0)
        {
            codes.AddRange(catalog.LanguageCodes);
        }
        codes.Sort((a, b) => string.CompareOrdinal(catalog.LanguageName(a), catalog.LanguageName(b)));
        return codes;
    }

    private void DrawModels(ModConfig config)
    {
        bool gpu = Hardware.GpuUsable(config.UseGpu);
        List<ModelInfo> models = catalog.Models.FindAll(m => m.Supports(language));
        ModelInfo best = Recommend(models, gpu);
        models.Sort((a, b) => Compare(a, b, best, gpu));

        GUILayout.Label($"Models for {catalog.LanguageName(language)} · {Hardware.GpuName}, {Hardware.VramMB / 1024f:0} GB video memory", theme.Section);
        // Column headings, aligned with the tiles' columns (tile padding 14 on both sides).
        GUILayout.BeginHorizontal();
        GUILayout.Space(14);
        GUILayout.FlexibleSpace();
        GUILayout.Label("Quality", theme.Heading, GUILayout.Width(QualityColumn));
        GUILayout.Label("Speed", theme.Heading, GUILayout.Width(SpeedColumn));
        GUILayout.Space(ActionColumn + 14);
        GUILayout.EndHorizontal();
        BeginList();
        foreach (ModelInfo model in models)
        {
            DrawModel(config, model, model == best, gpu);
        }
        EndList();
    }

    private float listTop;

    private void BeginList()
    {
        listTop = GUILayoutUtility.GetRect(0f, 0f, GUIStyle.none).y;
        listScroll = GUILayout.BeginScrollView(listScroll, false, false, GUIStyle.none, GUIStyle.none, GUIStyle.none,
            GUILayout.Height(listViewHeight));
    }

    private void EndList()
    {
        Rect end = GUILayoutUtility.GetRect(0f, 0f, GUIStyle.none);
        if (Event.current.type == EventType.Repaint)
        {
            listContentHeight = end.yMax;
        }
        GUILayout.EndScrollView();

        // Thin scroll indicator; the stripped scrollbar styles cannot be themed. Scroll with the mouse wheel.
        if (listContentHeight > listViewHeight + 1f)
        {
            // In the panel's right padding, next to the tiles.
            float x = WindowWidth - 15f;
            Rounded(new Rect(x, listTop, 3f, listViewHeight), Theme.Chip, 1.5f);
            float thumb = Mathf.Max(24f, listViewHeight * listViewHeight / listContentHeight);
            float travel = listViewHeight - thumb;
            float offset = travel * Mathf.Clamp01(listScroll.y / (listContentHeight - listViewHeight));
            Rounded(new Rect(x, listTop + offset, 3f, thumb), Theme.TextMuted, 1.5f);
        }
    }

    /// <summary>Recommended first, then what runs well on this PC, then by quality, then by size.</summary>
    private int Compare(ModelInfo a, ModelInfo b, ModelInfo best, bool gpu)
    {
        if (a == best || b == best)
        {
            return a == best ? (b == best ? 0 : -1) : 1;
        }
        int fit = ((int)Hardware.Evaluate(a, gpu)).CompareTo((int)Hardware.Evaluate(b, gpu));
        if (fit != 0)
        {
            return fit;
        }
        double ea = catalog.ErrorRate(language, a);
        double eb = catalog.ErrorRate(language, b);
        if (ea < 0 != eb < 0)
        {
            return ea < 0 ? 1 : -1;
        }
        int quality = ea.CompareTo(eb);
        return quality != 0 ? quality : a.SizeBytes.CompareTo(b.SizeBytes);
    }

    private static bool IsSelected(ModConfig config, ModelInfo model) => model.BundledWithGame
        ? config.ModelFile.Trim().Length == 0
        : string.Equals(config.ModelFile.Trim(), model.FileName, StringComparison.OrdinalIgnoreCase);

    private static bool IsInstalled(ModelInfo model) =>
        model.BundledWithGame || File.Exists(Path.Combine(ModelDirectory, model.FileName));

    private bool IsDownloading(ModelInfo model) => download != null && download.Model == model && !download.Finished;

    private void DrawModel(ModConfig config, ModelInfo model, bool recommended, bool gpu)
    {
        double err = catalog.ErrorRate(language, model);
        Fit fit = Hardware.Evaluate(model, gpu);
        bool selected = IsSelected(config, model);
        bool installed = IsInstalled(model);
        bool downloading = IsDownloading(model);

        GUILayout.BeginVertical(selected ? theme.TileSelectedStyle : theme.TileStyle);
        GUILayout.BeginHorizontal();

        GUILayout.BeginVertical();
        GUILayout.Label(DisplayName(model.Id), theme.Name);
        var details = new List<string>();
        if (recommended)
        {
            details.Add("Recommended");
        }
        details.Add(model.BundledWithGame ? "built in" : $"{model.SizeBytes / 1e6:0} MB");
        details.Add($"needs {model.EstMemoryMB / 1000f:0.0} GB");
        GUILayout.Label(string.Join(" · ", details.ToArray()), theme.Muted);
        GUILayout.Label(Hardware.Likelihood(model, gpu), fit == Fit.TooBig || fit == Fit.Slow ? theme.MutedWarning : theme.Muted);
        GUILayout.EndVertical();

        // Quality: share of words (or characters, for some languages) the model gets wrong.
        GUILayout.BeginVertical(GUILayout.Width(QualityColumn));
        GUILayout.Space(16);
        GUILayout.BeginHorizontal();
        Dots(catalog.Stars(err));
        GUILayout.Space(8);
        string unit = catalog.Metric(language) == "CER" ? "char errors" : "word errors";
        GUILayout.Label(err >= 0 ? $"{err:0.#}% {unit}" : "unknown", theme.Percent, GUILayout.ExpandWidth(false));
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();

        // Speed: estimated time to recognize one sentence on this PC.
        float seconds = Hardware.EstimateSeconds(model, gpu);
        GUILayout.BeginVertical(GUILayout.Width(SpeedColumn));
        GUILayout.Space(16);
        GUILayout.BeginHorizontal();
        Dots(Hardware.SpeedDots(seconds));
        GUILayout.Space(8);
        GUILayout.Label(seconds < 1f ? $"~{seconds * 1000f:0} ms" : $"~{seconds:0.#} s", theme.Percent, GUILayout.ExpandWidth(false));
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();

        GUILayout.BeginVertical(GUILayout.Width(ActionColumn));
        GUILayout.Space(12);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (downloading)
        {
            if (GUILayout.Button("Cancel", theme.SecondaryButton, GUILayout.Height(Theme.ButtonHeight)))
            {
                download.Cancel();
            }
        }
        else if (!installed)
        {
            bool busy = download != null && !download.Finished;
            if (!busy && GUILayout.Button("Download", theme.PrimaryButton, GUILayout.Height(Theme.ButtonHeight)))
            {
                StartDownload(model);
            }
        }
        else
        {
            if (!model.BundledWithGame && GUILayout.Button("Delete", theme.DangerLink, GUILayout.Height(Theme.ButtonHeight)))
            {
                DeleteModel(config, model, selected);
            }
            GUILayout.Space(10);
            if (selected)
            {
                string state = ModelSwitcher.Switching ? "Loading..." : ModelSwitcher.Pending ? "After the call" : "In use";
                GUILayout.Label(state, theme.Status, GUILayout.ExpandWidth(false), GUILayout.Height(Theme.ButtonHeight));
            }
            else if (GUILayout.Button("Use", theme.SecondaryButton, GUILayout.Height(Theme.ButtonHeight)))
            {
                config.Set("Model", "ModelFile", model.BundledWithGame ? "" : model.FileName);
                RequestSwitch();
            }
        }
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();

        GUILayout.EndHorizontal();

        if (downloading)
        {
            GUILayout.Space(8);
            ProgressBar(download.Progress);
            GUILayout.Label(download.Status == ModelDownload.State.Verifying
                ? "Checking the file..."
                : $"{download.Progress * model.SizeBytes / 1e6:0} of {model.SizeBytes / 1e6:0} MB", theme.Muted);
        }
        GUILayout.EndVertical();
    }

    /// <summary>Five quality dots; drawn as rounded rects because the font may not have a ● glyph.</summary>
    private void Dots(int stars)
    {
        Rect row = GUILayoutUtility.GetRect(5 * 8 + 4 * 4, 14, GUIStyle.none, GUILayout.Width(5 * 8 + 4 * 4), GUILayout.Height(14));
        for (int i = 0; i < 5; i++)
        {
            Rounded(new Rect(row.x + i * 12, row.y + 3, 8, 8), i < stars ? Theme.Text : Theme.Dim, 4f);
        }
    }

    private static string DisplayName(string id)
    {
        switch (id)
        {
            case "base": return "Base (game default)";
            case "tiny": return "Tiny";
            case "small": return "Small";
            case "small-q5": return "Small, compact";
            case "medium": return "Medium";
            case "medium-q5": return "Medium, compact";
            case "turbo": return "Large v3 Turbo";
            case "turbo-q8": return "Large v3 Turbo, near-full size";
            case "turbo-q5": return "Large v3 Turbo, compact";
            case "large-v3": return "Large v3";
            case "large-v3-q5": return "Large v3, compact";
            case "turbo-german": return "German Turbo";
            case "turbo-german-q5": return "German Turbo, compact";
            case "turbo-chinese": return "Chinese Turbo";
            case "french-distil-q5": return "French Large v3, compact";
            case "swedish-large-q5": return "Swedish Large, compact";
            case "swedish-medium-q5": return "Swedish Medium, compact";
            default: return id;
        }
    }

    private void DrawMicTest()
    {
        GUILayout.Label("Microphone test", theme.Section);
        GUILayout.BeginHorizontal();
        bool listening = micTest.Status == MicTest.State.Listening;
        bool inCall = ModelSwitcher.InCall;
        // Drawn by hand: IMGUI buttons fire on release, the test records while the button is held.
        Rect button = GUILayoutUtility.GetRect(140, Theme.ButtonHeight, GUIStyle.none,
            GUILayout.Width(140), GUILayout.Height(Theme.ButtonHeight));
        GUI.Label(button, new GUIContent(listening ? "Listening..." : "Hold to talk"), listening ? theme.PrimaryButton : theme.SecondaryButton);
        Event e = Event.current;
        if (!inCall && e.type == EventType.MouseDown && e.button == 0 && button.Contains(e.mousePosition))
        {
            micTest.Begin(ModelSwitcher.Switching);
            e.Use();
        }
        GUILayout.Space(14);
        switch (micTest.Status)
        {
            case MicTest.State.Listening:
                Rect bar = GUILayoutUtility.GetRect(160, Theme.ButtonHeight, GUIStyle.none,
                    GUILayout.Width(160), GUILayout.Height(Theme.ButtonHeight));
                bar = new Rect(bar.x, bar.y + bar.height / 2f - 2f, bar.width, 4f);
                Rounded(bar, Theme.Dim, 2f);
                Rounded(new Rect(bar.x, bar.y, Mathf.Max(4f, bar.width * micTest.Level), bar.height), Theme.Text, 2f);
                GUILayout.Space(12);
                GUILayout.Label($"{micTest.Seconds:0.0} s", theme.MutedMiddle, GUILayout.Height(Theme.ButtonHeight));
                break;
            case MicTest.State.Recognizing:
                GUILayout.Label("Recognizing...", theme.MutedMiddle, GUILayout.Height(Theme.ButtonHeight));
                break;
            case MicTest.State.Failed:
                GUILayout.Label(micTest.Info, theme.MutedMiddle, GUILayout.Height(Theme.ButtonHeight));
                break;
            default:
                GUILayout.Label(inCall ? "Not available during a call." : "Hold the button and say a sentence, like in a call.",
                    theme.MutedMiddle, GUILayout.Height(Theme.ButtonHeight));
                break;
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        if (micTest.Status == MicTest.State.Done)
        {
            GUILayout.Space(8);
            GUILayout.Label(micTest.Text.Length > 0 ? micTest.Text : "Nothing was recognized.", theme.Result);
            GUILayout.Label(micTest.Info, theme.Muted);
            if (micTest.FilterNote.Length > 0)
            {
                GUILayout.Label(micTest.FilterNote, theme.Muted);
            }
        }
    }

    private void DrawSettings(ModConfig config)
    {
        GUILayout.Space(12);
        Switch(config, "Transcript", "DropHallucinations", config.DropHallucinations,
            "Ignore phrases Whisper invents during silence");
        Switch(config, "Transcript", "FilterSoundTags", config.FilterSoundTags,
            "Remove sound descriptions like *music* from sentences");
        if (advancedOpen)
        {
            if (Switch(config, "Inference", "UseGpu", config.UseGpu,
                "Use the graphics card (only turn off if the game crashes or recognition fails)"))
            {
                RequestSwitch();
            }
            if (Switch(config, "Model", "ReplaceEnglishModel", config.ReplaceEnglishModel,
                "Also use the selected model for English calls"))
            {
                RequestSwitch();
            }
            Switch(config, "Debug", "NativeLog", config.NativeLog,
                "Write whisper.cpp's own log into Player.log, for troubleshooting");
        }
        GUILayout.Space(2);
        if (GUILayout.Button(advancedOpen ? "Fewer settings" : "More settings", theme.LinkButton, GUILayout.ExpandWidth(false)))
        {
            advancedOpen = !advancedOpen;
        }
    }

    /// <summary>Small switch drawn with rounded GUI.DrawTexture; the label is a button, so the whole row is clickable.</summary>
    private bool Switch(ModConfig config, string section, string key, bool current, string label)
    {
        GUILayout.BeginHorizontal();
        Rect track = GUILayoutUtility.GetRect(30, 20, GUIStyle.none, GUILayout.Width(30), GUILayout.Height(20));
        track.y += 2;
        track.height = 16;
        Rounded(track, current ? Theme.Text : Theme.Dim, 8f);
        Rounded(new Rect(current ? track.xMax - 14 : track.x + 2, track.y + 2, 12, 12), current ? Theme.Background : Theme.TextMuted, 6f);
        bool clicked = GUI.Button(track, GUIContent.none, GUIStyle.none);
        GUILayout.Space(10);
        clicked |= GUILayout.Button(label, theme.RowButton);
        GUILayout.EndHorizontal();
        if (clicked)
        {
            config.SetBool(section, key, !current);
        }
        return clicked;
    }

    private void Rounded(Rect rect, Color color, float radius)
    {
        GUI.DrawTexture(rect, theme.Solid, ScaleMode.StretchToFill, true, 0f, color, 0f, radius);
    }

    private void ProgressBar(float progress)
    {
        Rect bar = GUILayoutUtility.GetRect(100, 4, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(4));
        Rounded(bar, Theme.Dim, 2f);
        if (progress > 0.005f)
        {
            Rounded(new Rect(bar.x, bar.y, Mathf.Max(4f, bar.width * Mathf.Clamp01(progress)), bar.height), Theme.Text, 2f);
        }
    }

    private void StartDownload(ModelInfo model)
    {
        download = new ModelDownload(model, ModelDirectory);
        notice = "";
        StartCoroutine(RunDownload(download));
    }

    private System.Collections.IEnumerator RunDownload(ModelDownload job)
    {
        yield return job.Run();
        if (job.Status == ModelDownload.State.Done)
        {
            notice = $"{DisplayName(job.Model.Id)} is ready. Click Use to switch to it.";
        }
        else if (job.Status == ModelDownload.State.Failed)
        {
            notice = $"{DisplayName(job.Model.Id)}: {job.Error}";
        }
        else if (job.Status == ModelDownload.State.Cancelled)
        {
            notice = "Download cancelled.";
        }
    }

    /// <summary>
    /// Any downloaded model can be deleted. The game reads the model into memory, so even the running one
    /// can go; deleting the selected one switches back to the game's default.
    /// </summary>
    private void DeleteModel(ModConfig config, ModelInfo model, bool selected)
    {
        try
        {
            File.Delete(Path.Combine(ModelDirectory, model.FileName));
            if (selected)
            {
                config.Set("Model", "ModelFile", "");
                RequestSwitch();
                if (notice.Length == 0)
                {
                    notice = $"{DisplayName(model.Id)} deleted, the game's own model is used again.";
                }
            }
            else
            {
                notice = $"{DisplayName(model.Id)} deleted.";
            }
        }
        catch (Exception ex)
        {
            notice = $"Could not delete {DisplayName(model.Id)}: {ex.Message}";
        }
    }

    /// <summary>Best quality among models that run well; ties go to the smaller download.</summary>
    private ModelInfo Recommend(List<ModelInfo> models, bool gpu)
    {
        ModelInfo best = null;
        double bestErr = 1e9; // double.MaxValue is stripped
        foreach (ModelInfo model in models)
        {
            double err = catalog.ErrorRate(language, model);
            if (err < 0 || Hardware.Evaluate(model, gpu) != Fit.Good)
            {
                continue;
            }
            if (err < bestErr - 0.05 || Math.Abs(err - bestErr) <= 0.05 && best != null && model.SizeBytes < best.SizeBytes)
            {
                best = model;
                bestErr = err;
            }
        }
        return best;
    }
}
