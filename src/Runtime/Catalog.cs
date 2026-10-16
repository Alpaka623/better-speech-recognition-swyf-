using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BetterSpeech;

internal enum Fit
{
    Good,
    Ok,
    Slow,
    TooBig,
}

internal sealed class ModelInfo
{
    public string Id;
    public string Family;
    public string Quantization;
    public string Url;
    public string Repo;
    public long SizeBytes;
    public string Sha256;
    public int EstMemoryMB;
    public int RelativeCompute;
    public List<string> Languages; // null = multilingual
    public string License;
    public bool BundledWithGame;

    /// <summary>Local file name in BetterSpeech\models; upstream names like ggml-model-q5_0.bin are ambiguous.</summary>
    public string FileName => "ggml-" + Id + ".bin";

    public bool Supports(string language) => Languages == null || Languages.Contains(language);
}

/// <summary>catalog\models.json: models plus error rates per language and model family.</summary>
internal sealed class Catalog
{
    public readonly List<ModelInfo> Models = new List<ModelInfo>();
    public readonly List<string> LanguageCodes = new List<string>();
    private readonly Dictionary<string, string> languageNames = new Dictionary<string, string>();
    private readonly Dictionary<string, Dictionary<string, double>> errors = new Dictionary<string, Dictionary<string, double>>();
    private readonly Dictionary<string, string> metrics = new Dictionary<string, string>();
    private readonly List<KeyValuePair<int, double>> thresholds = new List<KeyValuePair<int, double>>();

    public static Catalog Load(string path)
    {
        var root = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(path));
        var catalog = new Catalog();

        foreach (object item in (List<object>)root["models"])
        {
            var m = (Dictionary<string, object>)item;
            catalog.Models.Add(new ModelInfo
            {
                Id = Str(m, "id"),
                Family = Str(m, "family"),
                Quantization = Str(m, "quantization"),
                Url = Str(m, "url"),
                Repo = Str(m, "repo"),
                SizeBytes = (long)Num(m, "sizeBytes"),
                Sha256 = Str(m, "sha256"),
                EstMemoryMB = (int)Num(m, "estMemoryMB"),
                RelativeCompute = (int)Num(m, "relativeCompute"),
                Languages = m.TryGetValue("languages", out object langs) && langs is List<object> list
                    ? list.ConvertAll(x => (string)x)
                    : null,
                License = Str(m, "license"),
                BundledWithGame = m.TryGetValue("bundledWithGame", out object bundled) && bundled is bool b && b,
            });
        }

        foreach (KeyValuePair<string, object> lang in (Dictionary<string, object>)root["quality"])
        {
            var q = (Dictionary<string, object>)lang.Value;
            catalog.LanguageCodes.Add(lang.Key);
            catalog.languageNames[lang.Key] = Str(q, "name");
            catalog.metrics[lang.Key] = Str(q, "metric");
            var perFamily = new Dictionary<string, double>();
            foreach (KeyValuePair<string, object> fam in q)
            {
                if (fam.Value is Dictionary<string, object> f && f.TryGetValue("err", out object err) && err is double d)
                {
                    perFamily[fam.Key] = d;
                }
            }
            catalog.errors[lang.Key] = perFamily;
        }
        catalog.LanguageCodes.Sort((a, b) => string.CompareOrdinal(catalog.LanguageName(a), catalog.LanguageName(b)));

        foreach (KeyValuePair<string, object> t in (Dictionary<string, object>)root["ratingThresholds"])
        {
            catalog.thresholds.Add(new KeyValuePair<int, double>(int.Parse(t.Key), (double)t.Value));
        }
        catalog.thresholds.Sort((a, b) => b.Key.CompareTo(a.Key));
        return catalog;
    }

    private static string Str(Dictionary<string, object> obj, string key) =>
        obj.TryGetValue(key, out object value) && value is string s ? s : "";

    private static double Num(Dictionary<string, object> obj, string key) =>
        obj.TryGetValue(key, out object value) && value is double d ? d : 0;

    public string LanguageName(string code) => languageNames.TryGetValue(code, out string name) ? name : code;

    public string Metric(string code) => metrics.TryGetValue(code, out string metric) ? metric : "WER";

    /// <summary>Error rate in percent, or -1 when unknown.</summary>
    public double ErrorRate(string language, ModelInfo model) =>
        errors.TryGetValue(language, out var perFamily) && perFamily.TryGetValue(model.Family, out double err) ? err : -1;

    /// <summary>0-5 stars from the catalog's thresholds (err &lt;= 5 → 5 stars, ...).</summary>
    public int Stars(double errorRate)
    {
        if (errorRate < 0)
        {
            return 0;
        }
        foreach (KeyValuePair<int, double> t in thresholds)
        {
            if (errorRate <= t.Value)
            {
                return t.Key;
            }
        }
        return 0;
    }
}

/// <summary>What this PC can run; estimates until the in-game self-test exists.</summary>
internal static class Hardware
{
    // The game itself keeps a few GB of VRAM busy (rendering, TTS).
    private const int GameVramMB = 3000;

    public static string GpuName => SystemInfo.graphicsDeviceName;
    public static int VramMB => SystemInfo.graphicsMemorySize;
    public static int RamMB => SystemInfo.systemMemorySize;

    /// <summary>The game forces Whisper onto the CPU for some AMD/Intel GPUs (WindowsSpeechCompatibility).</summary>
    public static bool GpuUsable(bool useGpuSetting) =>
        useGpuSetting && Environment.GetEnvironmentVariable("KOLKATA_WHISPER_CPU_ONLY") != "1";

    /// <summary>
    /// Rough time to recognize one spoken sentence. Based on Large v3 Turbo (compute 12) taking about
    /// 250 ms on the graphics card in game; on the processor about 12x slower.
    /// </summary>
    public static float EstimateSeconds(ModelInfo model, bool gpu) => model.RelativeCompute * (gpu ? 0.02f : 0.25f);

    public static int SpeedDots(float seconds)
    {
        if (seconds <= 0.3f) return 5;
        if (seconds <= 0.6f) return 4;
        if (seconds <= 1.2f) return 3;
        if (seconds <= 2.5f) return 2;
        return 1;
    }

    /// <summary>How likely the model runs on this PC, in words.</summary>
    public static string Likelihood(ModelInfo model, bool gpu)
    {
        float need = model.EstMemoryMB / 1000f;
        switch (Evaluate(model, gpu))
        {
            case Fit.Good:
                return "Runs well on this PC";
            case Fit.Ok:
                return gpu ? "Should run, video memory is tight" : "Should run, a bit slow";
            case Fit.Slow:
                return "Runs, but too slow for calls";
            default:
                return gpu
                    ? $"Unlikely to run: needs {need:0.0} GB, about {Math.Max(0, VramMB - GameVramMB) / 1000f:0.0} GB free"
                    : $"Unlikely to run: needs {need:0.0} GB of RAM";
        }
    }

    public static Fit Evaluate(ModelInfo model, bool gpu)
    {
        if (gpu)
        {
            int free = VramMB - GameVramMB;
            if (model.EstMemoryMB <= free)
            {
                return Fit.Good;
            }
            return model.EstMemoryMB <= VramMB - GameVramMB / 2 ? Fit.Ok : Fit.TooBig;
        }
        if (model.EstMemoryMB > RamMB / 2)
        {
            return Fit.TooBig;
        }
        if (model.RelativeCompute <= 2)
        {
            return Fit.Good;
        }
        return model.RelativeCompute <= 6 ? Fit.Ok : Fit.Slow;
    }
}
