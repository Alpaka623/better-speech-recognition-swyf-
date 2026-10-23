using System;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace BetterSpeech.Installer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Must be in place before any method that touches BetterSpeech.Patcher or Mono.Cecil is compiled.
        AppDomain.CurrentDomain.AssemblyResolve += Payload.ResolveAssembly;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Run();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run() => Application.Run(new InstallerForm());
}
