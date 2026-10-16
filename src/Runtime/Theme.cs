using System.Collections.Generic;
using UnityEngine;

namespace BetterSpeech;

/// <summary>
/// Dark theme for the IMGUI menu: status chips, model tiles, a white primary button. Textures are
/// generated at runtime, so the mod ships no assets.
/// </summary>
internal sealed class Theme
{
    public static readonly Color Background = Hex(0x161616);
    public static readonly Color Chip = Hex(0x202020);
    public static readonly Color Tile = Hex(0x1D1D1D);
    public static readonly Color TileSelected = Hex(0x232323);
    public static readonly Color Outline = Hex(0x555555);
    public static readonly Color Text = Hex(0xEEEEEE);
    public static readonly Color TextMuted = Hex(0x8C8C8C);
    public static readonly Color Dim = Hex(0x3A3A3A);
    public static readonly Color Danger = Hex(0xE8786F);

    /// <summary>
    /// GUIStyle.font, richText, clipping and fixedHeight have no setters in the game's stripped IMGUI module,
    /// so the font is applied to the whole skin and heights come from GUILayout options.
    /// </summary>
    public readonly Font Font;
    public const float ButtonHeight = 28f;

    private readonly List<Texture2D> textures = new List<Texture2D>();

    public readonly Texture2D Solid;
    public readonly GUIStyle Panel, ChipStyle, TileStyle, TileSelectedStyle;
    public readonly GUIStyle Title, Label, Name, Muted, Section, Version, Percent, Status, MutedMiddle, Result, Heading, MutedWarning;
    public readonly GUIStyle PrimaryButton, SecondaryButton, LinkButton, DangerLink, ChipButton, ListItem, ListItemSelected, RowButton;

    public Theme()
    {
        Font = FindGameFont();
        Solid = Rounded(Color.white, 2, null);

        Panel = Box(Background, Hex(0x2C2C2C), 12, new RectOffset(24, 24, 20, 20));
        ChipStyle = Box(Chip, null, 6, new RectOffset(10, 10, 6, 7));
        ChipStyle.fontSize = 13;
        ChipStyle.normal.textColor = Text;
        ChipStyle.margin = new RectOffset(0, 8, 0, 0);
        TileStyle = Box(Tile, null, 8, new RectOffset(14, 14, 10, 10));
        TileStyle.margin = new RectOffset(0, 0, 0, 6);
        TileSelectedStyle = Box(TileSelected, Outline, 8, new RectOffset(14, 14, 10, 10));
        TileSelectedStyle.margin = TileStyle.margin;

        Title = Text_(18, Text);
        Title.fontStyle = FontStyle.Bold;
        Label = Text_(14, Text);
        Name = Text_(14, Text);
        Muted = Text_(12, TextMuted);
        Section = Text_(12, TextMuted);
        Section.margin = new RectOffset(0, 0, 14, 6);
        Version = Text_(12, TextMuted);
        Version.padding = new RectOffset(0, 0, 7, 0); // sits on the title's baseline
        Percent = Text_(12, Hex(0xBDBDBD));
        Status = Text_(13, TextMuted);
        Status.alignment = TextAnchor.MiddleCenter;
        Heading = Text_(12, TextMuted);
        Heading.fontStyle = FontStyle.Bold;
        MutedWarning = Text_(12, Danger);
        MutedMiddle = Text_(13, TextMuted);
        MutedMiddle.alignment = TextAnchor.MiddleLeft;
        Result = Text_(15, Text);
        Result.wordWrap = true;
        Result.margin = new RectOffset(0, 0, 0, 4);

        PrimaryButton = Button(Text, Color.white, Background, null, 13);
        SecondaryButton = Button(Hex(0x2B2B2B), Hex(0x353535), Text, null, 13);
        ChipButton = Button(Chip, Hex(0x2A2A2A), Text, null, 13);
        ChipButton.margin = new RectOffset(0, 8, 0, 0);
        ChipButton.padding = new RectOffset(10, 10, 6, 7);
        LinkButton = Button(new Color(0, 0, 0, 0), new Color(0, 0, 0, 0), TextMuted, null, 13);
        LinkButton.hover.textColor = Text;
        LinkButton.padding = new RectOffset(0, 0, 4, 4);
        LinkButton.margin = new RectOffset(0, 0, 2, 2);
        DangerLink = Button(new Color(0, 0, 0, 0), new Color(0, 0, 0, 0), TextMuted, null, 13);
        DangerLink.hover.textColor = Danger;
        DangerLink.padding = new RectOffset(6, 0, 4, 4);
        ListItem = Button(new Color(0, 0, 0, 0), Chip, Text, null, 14);
        ListItem.alignment = TextAnchor.MiddleLeft;
        ListItemSelected = Button(Chip, Chip, Text, null, 14);
        ListItemSelected.alignment = TextAnchor.MiddleLeft;
        RowButton = new GUIStyle(Label) { padding = new RectOffset(0, 0, 2, 2) };
    }

    public void Destroy()
    {
        foreach (Texture2D texture in textures)
        {
            Object.Destroy(texture);
        }
        textures.Clear();
    }

    /// <summary>Game font if one is loaded (the game ships Liberation Sans and Roboto), else Unity's default.</summary>
    private static Font FindGameFont()
    {
        Font fallback = null;
        foreach (Font font in Resources.FindObjectsOfTypeAll<Font>())
        {
            string name = font.name.ToLowerInvariant();
            if (name.Contains("liberation") && name.Contains("sans") && !name.Contains("bold"))
            {
                return font;
            }
            if (fallback == null && name.Contains("roboto") && !name.Contains("bold") && !name.Contains("condensed"))
            {
                fallback = font;
            }
        }
        return fallback;
    }

    private static GUIStyle Text_(int size, Color color)
    {
        return new GUIStyle
        {
            fontSize = size,
            normal = { textColor = color },
            margin = new RectOffset(0, 0, 1, 1),
            padding = new RectOffset(0, 0, 1, 1),
        };
    }

    private GUIStyle Box(Color fill, Color? border, int radius, RectOffset padding)
    {
        return new GUIStyle
        {
            normal = { background = Rounded(fill, radius, border) },
            border = new RectOffset(radius, radius, radius, radius),
            padding = padding,
        };
    }

    private GUIStyle Button(Color fill, Color hover, Color text, Color? border, int fontSize)
    {
        return new GUIStyle
        {
            fontSize = fontSize,
            alignment = TextAnchor.MiddleCenter,
            normal = { background = Rounded(fill, 5, border), textColor = text },
            hover = { background = Rounded(hover, 5, border), textColor = text },
            active = { background = Rounded(hover, 5, border), textColor = text },
            border = new RectOffset(5, 5, 5, 5),
            padding = new RectOffset(12, 12, 5, 6),
            margin = new RectOffset(6, 0, 0, 0),
        };
    }

    /// <summary>Rounded rectangle for 9-slicing, with optional 1 px border; anti-aliased corners.</summary>
    private Texture2D Rounded(Color fill, int radius, Color? border)
    {
        int size = radius * 2 + 2;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float dx = x + 0.5f - cx;
                float dy = y + 0.5f - cy;
                float distance = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                Color c = border.HasValue && distance > -1.5f ? border.Value : fill;
                c.a *= Mathf.Clamp01(0.5f - distance);
                pixels[y * size + x] = c;
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();
        textures.Add(texture);
        return texture;
    }

    private static Color Hex(int rgb) =>
        new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
}
