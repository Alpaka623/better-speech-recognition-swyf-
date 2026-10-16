using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BetterSpeech;

/// <summary>
/// Minimal JSON reader for catalog\models.json. JsonUtility cannot read dictionaries or mixed types,
/// and the game's Newtonsoft is stripped. Objects become Dictionary&lt;string, object&gt;, arrays
/// List&lt;object&gt;, numbers double.
/// </summary>
internal static class MiniJson
{
    public static object Parse(string json)
    {
        int i = 0;
        object value = ReadValue(json, ref i);
        SkipWhitespace(json, ref i);
        if (i != json.Length)
        {
            throw new FormatException("Unexpected data after JSON value at " + i);
        }
        return value;
    }

    private static object ReadValue(string s, ref int i)
    {
        SkipWhitespace(s, ref i);
        if (i >= s.Length)
        {
            throw new FormatException("Unexpected end of JSON");
        }
        char c = s[i];
        if (c == '{')
        {
            return ReadObject(s, ref i);
        }
        if (c == '[')
        {
            return ReadArray(s, ref i);
        }
        if (c == '"')
        {
            return ReadString(s, ref i);
        }
        if (Match(s, ref i, "true"))
        {
            return true;
        }
        if (Match(s, ref i, "false"))
        {
            return false;
        }
        if (Match(s, ref i, "null"))
        {
            return null;
        }
        return ReadNumber(s, ref i);
    }

    private static Dictionary<string, object> ReadObject(string s, ref int i)
    {
        var result = new Dictionary<string, object>();
        i++;
        SkipWhitespace(s, ref i);
        if (s[i] == '}')
        {
            i++;
            return result;
        }
        while (true)
        {
            SkipWhitespace(s, ref i);
            string key = ReadString(s, ref i);
            SkipWhitespace(s, ref i);
            Expect(s, ref i, ':');
            result[key] = ReadValue(s, ref i);
            SkipWhitespace(s, ref i);
            if (s[i] == ',')
            {
                i++;
                continue;
            }
            Expect(s, ref i, '}');
            return result;
        }
    }

    private static List<object> ReadArray(string s, ref int i)
    {
        var result = new List<object>();
        i++;
        SkipWhitespace(s, ref i);
        if (s[i] == ']')
        {
            i++;
            return result;
        }
        while (true)
        {
            result.Add(ReadValue(s, ref i));
            SkipWhitespace(s, ref i);
            if (s[i] == ',')
            {
                i++;
                continue;
            }
            Expect(s, ref i, ']');
            return result;
        }
    }

    private static string ReadString(string s, ref int i)
    {
        Expect(s, ref i, '"');
        var sb = new StringBuilder();
        while (s[i] != '"')
        {
            char c = s[i++];
            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }
            char e = s[i++];
            switch (e)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'u':
                    sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                default: sb.Append(e); break;
            }
        }
        i++;
        return sb.ToString();
    }

    private static double ReadNumber(string s, ref int i)
    {
        int start = i;
        while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0)
        {
            i++;
        }
        if (start == i)
        {
            throw new FormatException("Unexpected character '" + s[i] + "' at " + i);
        }
        return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static bool Match(string s, ref int i, string word)
    {
        if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
        {
            return false;
        }
        i += word.Length;
        return true;
    }

    private static void Expect(string s, ref int i, char c)
    {
        if (i >= s.Length || s[i] != c)
        {
            throw new FormatException("Expected '" + c + "' at " + i);
        }
        i++;
    }

    private static void SkipWhitespace(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i]))
        {
            i++;
        }
    }
}
