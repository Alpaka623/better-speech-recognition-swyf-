using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace BetterSpeech.Installer;

/// <summary>The mod package (dist\BetterSpeech) embedded by build.ps1 -Exe as "payload.zip".</summary>
internal static class Payload
{
    private const string ResourceName = "payload.zip";
    private static readonly Assembly Self = typeof(Payload).Assembly;
    private static readonly Dictionary<string, Assembly> Loaded = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

    public static bool Available => Self.GetManifestResourceInfo(ResourceName) != null;

    public static string Version => Self.GetName().Version.ToString(3);

    private static ZipArchive Open()
    {
        Stream stream = Self.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("This setup was built without the mod files. Build it with build.ps1 -Exe.");
        return new ZipArchive(stream, ZipArchiveMode.Read);
    }

    /// <summary>Zip tools disagree on "/" and "\"; entries are compared with "/".</summary>
    private static string Normalize(string entryName) => entryName.Replace('\\', '/');

    /// <summary>Serves BetterSpeech.Patcher, Mono.Cecil and Mono.Cecil.Rocks from the payload's lib folder.</summary>
    public static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name;
        lock (Loaded)
        {
            if (Loaded.TryGetValue(name, out Assembly loaded))
            {
                return loaded;
            }
            if (!Available)
            {
                return null;
            }
            string wanted = "lib/" + name + ".dll";
            using (ZipArchive zip = Open())
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (!string.Equals(Normalize(entry.FullName), wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    using (Stream input = entry.Open())
                    using (var buffer = new MemoryStream())
                    {
                        input.CopyTo(buffer);
                        loaded = Assembly.Load(buffer.ToArray());
                    }
                    Loaded[name] = loaded;
                    return loaded;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Writes every payload file into modDirectory, replacing older copies. Files the mod creates later
    /// (models, settings, backups) are not in the payload and stay untouched.
    /// </summary>
    public static void ExtractTo(string modDirectory, Action<string> log)
    {
        string root = Path.GetFullPath(modDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        int count = 0;
        using (ZipArchive zip = Open())
        {
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                string name = Normalize(entry.FullName);
                if (name.EndsWith("/", StringComparison.Ordinal))
                {
                    continue;
                }
                string target = Path.GetFullPath(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Unexpected file path in the setup: " + entry.FullName);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (Stream input = entry.Open())
                using (FileStream output = File.Create(target))
                {
                    input.CopyTo(output);
                }
                count++;
            }
        }
        log($"Copied {count} files to {modDirectory}");
    }
}
