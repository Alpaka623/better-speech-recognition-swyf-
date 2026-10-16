using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BetterSpeech;

/// <summary>
/// Settings in BetterSpeech\BetterSpeech.cfg (INI style). Missing keys get their default and the file is
/// rewritten with all keys and comments, so users always see every option.
/// </summary>
internal sealed class ModConfig
{
    private sealed class Entry
    {
        public string Section;
        public string Key;
        public string Value;
        public string Description;
    }

    private readonly List<Entry> entries = new List<Entry>();
    private readonly string path;

    public bool Enabled => GetBool("General", "Enabled");
    public string ModelFile => Get("Model", "ModelFile");
    public bool ReplaceEnglishModel => GetBool("Model", "ReplaceEnglishModel");
    public bool UseGpu => GetBool("Inference", "UseGpu");
    public bool FlashAttention => GetBool("Inference", "FlashAttention");
    public bool BeamSearch => GetBool("Inference", "BeamSearch");
    public bool FilterSoundTags => GetBool("Transcript", "FilterSoundTags");
    public bool NativeLog => GetBool("Debug", "NativeLog");
    public bool DropHallucinations => GetBool("Transcript", "DropHallucinations");
    public string HallucinationPhrases => Get("Transcript", "HallucinationPhrases");

    public float HallucinationMaxConfidence =>
        float.TryParse(Get("Transcript", "HallucinationMaxConfidence"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float value) ? value : 0.8f;

    public ModConfig(string path)
    {
        this.path = path;
        Define("General", "Enabled", "true",
            "Turns the mod on or off. When off, the game uses its own model and settings.");
        Define("Model", "ModelFile", "ggml-turbo-q5.bin",
            "Whisper model (ggml format) used instead of the game's ggml-base.bin. A file name is looked up in " +
            "BetterSpeech\\models; an absolute path is used as is. If the file does not exist, the game's model stays active.");
        Define("Model", "ReplaceEnglishModel", "false",
            "Also use this model when the call language is English (the game has a separate ggml-base.en.bin for that).");
        Define("Inference", "UseGpu", "true",
            "Run Whisper on the graphics card (Vulkan). Turn off if the game crashes or recognition fails.");
        // Measured on an RTX 4070 Ti SUPER with turbo-german-q5: ~9 s per utterance with flash attention,
        // ~0.14 s without. The game's Vulkan build apparently runs it on the CPU.
        Define("Inference", "FlashAttention", "false",
            "Flash attention. Makes recognition about 60x slower with the game's Vulkan build; leave off.");
        Define("Inference", "BeamSearch", "false",
            "Beam search instead of greedy decoding. Slightly more accurate, noticeably slower.");
        Define("Prompt", "German",
            "Hallo, hier ist der Kundendienst. Es gibt ein Problem mit Ihrem Konto. " +
            "Bitte nennen Sie mir Ihre Kreditkartennummer, die PIN und den Code der Geschenkkarte. " +
            "Überweisung, Rückerstattung, Bankkonto, Fernwartung, Virus, Computer, Polizei, Finanzamt.",
            "Initial prompt for German calls. Whisper adopts its spelling, punctuation and vocabulary. " +
            "Keep it short (a few sentences); leave empty to disable.");
        Define("Prompt", "English", "",
            "Initial prompt for English calls. Empty = game default (no prompt).");
        Define("Prompt", "Other", "",
            "Initial prompt for all other call languages. Empty = no prompt.");
        Define("Transcript", "FilterSoundTags", "false",
            "Remove sound descriptions Whisper invents from noise, such as *Klingel*, [Music] or (laughs), " +
            "before the caller gets the transcript. A transcript that is only such a tag is dropped.");
        Define("Transcript", "DropHallucinations", "true",
            "Drop phrases Whisper typically invents from silence or noise (see HallucinationPhrases) and transcripts " +
            "that are only one *sound description*, but only when the confidence is below HallucinationMaxConfidence.");
        // First test round (German, turbo-german-q5): hallucinations 0.64-0.69, real speech 0.77-0.93.
        Define("Transcript", "HallucinationMaxConfidence", "0.72",
            "Confidence (0-1) below which a hallucination is dropped. Observed: invented phrases 0.64-0.69, real speech 0.77 and up.");
        Define("Transcript", "HallucinationPhrases",
            "vielen dank|danke|danke schön|dankeschön|tschüss|bis zum nächsten mal|untertitel*|copyright*|" +
            "thank you|thanks for watching|thank you for watching|you|bye",
            "Phrases separated by |, compared without case and punctuation. A trailing * matches any continuation.");
        Define("Debug", "NativeLog", "false",
            "Write whisper.cpp's own log (GPU backend, device, load details) into Player.log. For troubleshooting.");
    }

    public string PromptFor(string languageCode)
    {
        switch (languageCode)
        {
            case "de":
                return Get("Prompt", "German");
            case "en":
                return Get("Prompt", "English");
            default:
                return Get("Prompt", "Other");
        }
    }

    public void Load()
    {
        if (File.Exists(path))
        {
            string section = "";
            // File.ReadAllLines is stripped from the game's mscorlib
            foreach (string raw in File.ReadAllText(path).Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                {
                    continue;
                }
                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }
                Entry entry = Find(section, line.Substring(0, eq).Trim());
                if (entry != null)
                {
                    entry.Value = line.Substring(eq + 1).Trim();
                }
            }
        }
        Save();
    }

    public void Set(string section, string key, string value)
    {
        Entry entry = Find(section, key);
        if (entry != null && entry.Value != value)
        {
            entry.Value = value;
            Save();
        }
    }

    public void SetBool(string section, string key, bool value) => Set(section, key, value ? "true" : "false");

    private void Save()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Better Speech Recognition settings. Restart the game after changing them.");
        string section = null;
        foreach (Entry entry in entries)
        {
            if (entry.Section != section)
            {
                section = entry.Section;
                sb.AppendLine();
                sb.Append('[').Append(section).AppendLine("]");
            }
            sb.AppendLine();
            sb.Append("# ").AppendLine(entry.Description);
            sb.Append(entry.Key).Append(" = ").AppendLine(entry.Value);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, sb.ToString());
    }

    private void Define(string section, string key, string value, string description)
    {
        entries.Add(new Entry { Section = section, Key = key, Value = value, Description = description });
    }

    private Entry Find(string section, string key)
    {
        foreach (Entry entry in entries)
        {
            if (string.Equals(entry.Section, section, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }
        return null;
    }

    private string Get(string section, string key) => Find(section, key)?.Value ?? "";

    private bool GetBool(string section, string key) =>
        string.Equals(Get(section, key), "true", StringComparison.OrdinalIgnoreCase);
}
