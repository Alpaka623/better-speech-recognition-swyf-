using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace BetterSpeech.Patcher;

/// <summary>
/// Injects one call into BetterSpeech.Runtime per hook point. Originals are backed up to
/// BetterSpeech\backup together with their SHA256 and the SHA256 of the patched file, so a game update
/// (file hash is neither) is detected and simply patched again.
/// </summary>
public static class GamePatcher
{
    private const string RuntimeAssembly = "BetterSpeech.Runtime";
    private const string HooksType = "BetterSpeech.Hooks";

    /// <summary>Bump whenever the injected IL changes, so already patched files get re-patched from the backup.</summary>
    private const int PatchVersion = 2;

    private sealed class PatchState
    {
        public string Original;
        public string Patched;
        public int Version;
    }

    private sealed class HookPoint
    {
        public string Assembly;
        public string Type;
        public string Method;
        public int ParameterCount;
        public string Hook;
        public Action<MethodDefinition, MethodReference> Inject;
    }

    private static readonly HookPoint[] HookPoints =
    {
        new HookPoint
        {
            Assembly = "com.whisper.unity.dll", Type = "Whisper.WhisperManager", Method = "InitModel", ParameterCount = 0,
            Hook = "BeforeInitModel", Inject = (m, hook) => InsertAtStart(m, OpCodes.Ldarg_0, hook)
        },
        new HookPoint
        {
            Assembly = "ScriptsAssDef.dll", Type = "WhisperVcPipeline", Method = "ConfigureRecognizer", ParameterCount = 2,
            Hook = "AfterConfigureRecognizer", Inject = InsertBeforeReturns
        },
        new HookPoint
        {
            Assembly = "ScriptsAssDef.dll", Type = "WindowsSpeechCompatibility", Method = "Initialize", ParameterCount = 0,
            Hook = "Init", Inject = (m, hook) => InsertAtStart(m, null, hook)
        },
        new HookPoint
        {
            Assembly = "MetaVosk.dll", Type = "SpeechTranscriptPostprocessor", Method = "ProcessTranscript", ParameterCount = 3,
            Hook = "FilterTranscript", Inject = ReplaceFirstArgument
        },
    };

    public static void Install(string gameDir, string runtimeDll)
    {
        string managed = ManagedDir(gameDir);
        string backupDir = BackupDir(gameDir);
        Directory.CreateDirectory(backupDir);
        Dictionary<string, PatchState> state = LoadState(backupDir);

        // The runtime has to be in place before any patched method can run.
        File.Copy(runtimeDll, Path.Combine(managed, RuntimeAssembly + ".dll"), true);
        Console.WriteLine($"Copied {RuntimeAssembly}.dll");

        ModuleDefinition runtime = ModuleDefinition.ReadModule(runtimeDll);
        TypeDefinition hooks = runtime.GetType(HooksType) ?? throw new InvalidOperationException("Hooks type missing in " + runtimeDll);

        foreach (IGrouping<string, HookPoint> group in HookPoints.GroupBy(h => h.Assembly))
        {
            string file = Path.Combine(managed, group.Key);
            string hash = Sha256(file);
            string pdb = Path.ChangeExtension(file, ".pdb");
            byte[] originalBytes;
            string originalHash;
            bool known = state.TryGetValue(group.Key, out PatchState entry);
            if (known && entry.Patched == hash && entry.Version == PatchVersion)
            {
                Console.WriteLine($"{group.Key}: already patched");
                continue;
            }
            if (known && entry.Patched == hash)
            {
                // Patched by an older mod version: start again from the backed-up original.
                Console.WriteLine($"{group.Key}: patched by an older version (patch v{entry.Version}), updating to v{PatchVersion}");
                originalBytes = File.ReadAllBytes(Path.Combine(backupDir, group.Key));
                originalHash = entry.Original;
            }
            else
            {
                if (known)
                {
                    Console.WriteLine($"{group.Key}: changed since the last install (game update?), patching the new version");
                }
                originalBytes = File.ReadAllBytes(file);
                originalHash = hash;
                File.WriteAllBytes(Path.Combine(backupDir, group.Key), originalBytes);
                if (File.Exists(pdb))
                {
                    File.Copy(pdb, Path.Combine(backupDir, Path.GetFileName(pdb)), true);
                }
            }

            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(managed);
            using (var module = ModuleDefinition.ReadModule(new MemoryStream(originalBytes),
                       new ReaderParameters { AssemblyResolver = resolver }))
            {
                foreach (HookPoint point in group)
                {
                    MethodDefinition target = FindMethod(module, point);
                    if (target.Body.Instructions.Any(i => i.Operand is MethodReference r && r.DeclaringType.FullName == HooksType))
                    {
                        throw new InvalidOperationException($"{point.Type}.{point.Method} already contains a hook, but the file is not recorded as patched. Verify the game files in Steam and run install again.");
                    }
                    MethodDefinition hookDef = hooks.Methods.Single(m => m.Name == point.Hook);
                    target.Body.SimplifyMacros();
                    point.Inject(target, module.ImportReference(hookDef));
                    target.Body.OptimizeMacros();
                    Console.WriteLine($"{group.Key}: hooked {point.Type}.{point.Method} -> Hooks.{point.Hook}");
                }
                // The shipped .pdb no longer matches the patched IL; Mono ignores a mismatched one, but a
                // stale file would only confuse stack traces, so it is removed (and restored on uninstall).
                module.Write(file);
            }
            if (File.Exists(pdb))
            {
                File.Delete(pdb);
            }
            state[group.Key] = new PatchState { Original = originalHash, Patched = Sha256(file), Version = PatchVersion };
        }

        SaveState(backupDir, state);
        Console.WriteLine("Patched.");
    }

    public static void Uninstall(string gameDir)
    {
        string managed = ManagedDir(gameDir);
        string backupDir = BackupDir(gameDir);
        Dictionary<string, PatchState> state = LoadState(backupDir);

        foreach (KeyValuePair<string, PatchState> entry in state)
        {
            string file = Path.Combine(managed, entry.Key);
            string backup = Path.Combine(backupDir, entry.Key);
            string current = File.Exists(file) ? Sha256(file) : null;
            if (current == entry.Value.Patched && File.Exists(backup))
            {
                File.Copy(backup, file, true);
                string pdbBackup = Path.Combine(backupDir, Path.ChangeExtension(entry.Key, ".pdb"));
                if (File.Exists(pdbBackup))
                {
                    File.Copy(pdbBackup, Path.ChangeExtension(file, ".pdb"), true);
                }
                Console.WriteLine($"{entry.Key}: restored");
            }
            else
            {
                Console.WriteLine($"{entry.Key}: not patched (game update?), left as is");
            }
        }

        // Only after every patched file is restored, nothing references the runtime any more.
        string runtime = Path.Combine(managed, RuntimeAssembly + ".dll");
        if (File.Exists(runtime))
        {
            File.Delete(runtime);
            Console.WriteLine($"Removed {RuntimeAssembly}.dll");
        }
        if (Directory.Exists(backupDir))
        {
            Directory.Delete(backupDir, true);
        }
        Console.WriteLine("Unpatched.");
    }

    /// <summary>Returns "patched", "not patched" or "outdated" (patched before, game files changed since).</summary>
    public static string Status(string gameDir)
    {
        Dictionary<string, PatchState> state = LoadState(BackupDir(gameDir));
        if (state.Count == 0)
        {
            return "not patched";
        }
        bool allPatched = state.All(e => File.Exists(Path.Combine(ManagedDir(gameDir), e.Key)) &&
                                         Sha256(Path.Combine(ManagedDir(gameDir), e.Key)) == e.Value.Patched);
        return allPatched ? "patched" : "outdated";
    }

    private static void InsertAtStart(MethodDefinition method, OpCode? loadArgument, MethodReference hook)
    {
        ILProcessor il = method.Body.GetILProcessor();
        Instruction first = method.Body.Instructions[0];
        if (loadArgument.HasValue)
        {
            il.InsertBefore(first, il.Create(loadArgument.Value));
        }
        il.InsertBefore(first, il.Create(OpCodes.Call, hook));
    }

    /// <summary>static void M(a, b): every "ret" becomes "ldarg.0; ldarg.1; call hook; ret", keeping branch targets.</summary>
    private static void InsertBeforeReturns(MethodDefinition method, MethodReference hook)
    {
        ILProcessor il = method.Body.GetILProcessor();
        foreach (Instruction ret in method.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList())
        {
            // Reuse the ret instruction object as the first new instruction, so branches to it still land here.
            ret.OpCode = OpCodes.Ldarg_0;
            ret.Operand = null;
            Instruction second = il.Create(OpCodes.Ldarg_1);
            Instruction call = il.Create(OpCodes.Call, hook);
            Instruction newRet = il.Create(OpCodes.Ret);
            il.InsertAfter(ret, second);
            il.InsertAfter(second, call);
            il.InsertAfter(call, newRet);
        }
    }

    /// <summary>static R M(string text, float confidence, ...): text = hook(text, confidence) at the start.</summary>
    private static void ReplaceFirstArgument(MethodDefinition method, MethodReference hook)
    {
        ILProcessor il = method.Body.GetILProcessor();
        Instruction first = method.Body.Instructions[0];
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(first, il.Create(OpCodes.Call, hook));
        il.InsertBefore(first, il.Create(OpCodes.Starg, method.Parameters[0]));
    }

    private static MethodDefinition FindMethod(ModuleDefinition module, HookPoint point)
    {
        TypeDefinition type = module.GetType(point.Type)
            ?? throw new InvalidOperationException($"Type {point.Type} not found in {point.Assembly}. The game changed; this mod version does not support it yet.");
        return type.Methods.SingleOrDefault(m => m.Name == point.Method && m.Parameters.Count == point.ParameterCount && m.HasBody)
            ?? throw new InvalidOperationException($"Method {point.Type}.{point.Method} not found in {point.Assembly}. The game changed; this mod version does not support it yet.");
    }

    private static string ManagedDir(string gameDir)
    {
        string managed = Path.Combine(gameDir, "Scam With Your Friends_Data", "Managed");
        if (!Directory.Exists(managed))
        {
            throw new DirectoryNotFoundException("Game not found: " + managed);
        }
        return managed;
    }

    private static string BackupDir(string gameDir) => Path.Combine(gameDir, "BetterSpeech", "backup");

    private static Dictionary<string, PatchState> LoadState(string backupDir)
    {
        var state = new Dictionary<string, PatchState>(StringComparer.OrdinalIgnoreCase);
        string path = Path.Combine(backupDir, "state.txt");
        if (File.Exists(path))
        {
            foreach (string line in File.ReadAllLines(path))
            {
                string[] parts = line.Split('|');
                // name|original sha|patched sha[|patch version]; files from 0.2.0 have no version (= 1)
                if (parts.Length >= 3)
                {
                    state[parts[0]] = new PatchState
                    {
                        Original = parts[1],
                        Patched = parts[2],
                        Version = parts.Length >= 4 && int.TryParse(parts[3], out int v) ? v : 1,
                    };
                }
            }
        }
        return state;
    }

    private static void SaveState(string backupDir, Dictionary<string, PatchState> state)
    {
        File.WriteAllLines(Path.Combine(backupDir, "state.txt"),
            state.Select(e => $"{e.Key}|{e.Value.Original}|{e.Value.Patched}|{e.Value.Version}"));
    }

    private static string Sha256(string file)
    {
        using (SHA256 sha = SHA256.Create())
        using (FileStream stream = File.OpenRead(file))
        {
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
