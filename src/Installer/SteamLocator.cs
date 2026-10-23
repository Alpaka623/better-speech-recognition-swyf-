using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BetterSpeech.Installer;

/// <summary>Finds the game in every Steam library listed in steamapps\libraryfolders.vdf.</summary>
internal static class SteamLocator
{
    public const string GameExe = "Scam With Your Friends.exe";
    private const string GameFolder = "Scam With Your Friends";
    private static readonly Regex LibraryPath = new Regex("\"path\"\\s+\"([^\"]+)\"");

    public static bool IsGameDir(string dir)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir.Trim(), GameExe));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static string FindGameDir()
    {
        // Started from "<game>\BetterSpeech" or the game folder itself.
        string here = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        foreach (string dir in new[] { here, Path.GetDirectoryName(here) })
        {
            if (IsGameDir(dir))
            {
                return dir;
            }
        }

        var libraries = new List<string>();
        foreach (string root in SteamRoots())
        {
            libraries.Add(root);
            string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
            {
                continue;
            }
            foreach (Match match in LibraryPath.Matches(File.ReadAllText(vdf)))
            {
                libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
            }
        }
        foreach (string library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string dir = Path.Combine(library, "steamapps", "common", GameFolder);
            if (IsGameDir(dir))
            {
                return Path.GetFullPath(dir);
            }
        }
        return null;
    }

    private static IEnumerable<string> SteamRoots()
    {
        var roots = new List<string>();
        Read(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath", roots);
        Read(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", roots);
        Read(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath", roots);
        return roots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void Read(RegistryKey hive, string path, string name, List<string> roots)
    {
        try
        {
            using (RegistryKey key = hive.OpenSubKey(path))
            {
                if (key?.GetValue(name) is string value && value.Length > 0)
                {
                    roots.Add(value.Replace('/', '\\'));
                }
            }
        }
        catch (Exception)
        {
            // No Steam entry or no access: the user can still pick the folder.
        }
    }
}
