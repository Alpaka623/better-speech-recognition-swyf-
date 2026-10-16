using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace BetterSpeech;

/// <summary>
/// whisper.cpp / ggml write diagnostics (selected backend, GPU device, timings) to stderr, which Unity
/// never shows. whisper_log_set routes them (and ggml's, it forwards the callback) into Player.log.
/// </summary>
internal static class NativeLog
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void LogCallback(int level, IntPtr text, IntPtr userData);

    [DllImport("libwhisper", CallingConvention = CallingConvention.Cdecl)]
    private static extern void whisper_log_set(LogCallback callback, IntPtr userData);

    [DllImport("libwhisper", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr whisper_print_system_info();

    // Kept in a static field so the GC never collects the delegate native code still calls.
    private static LogCallback callback;
    private static readonly StringBuilder Pending = new StringBuilder();
    private static readonly object Gate = new object();

    public static void Enable()
    {
        callback = OnLog;
        whisper_log_set(callback, IntPtr.Zero);
        Debug.Log("[BetterSpeech/whisper] system info: " + Marshal.PtrToStringAnsi(whisper_print_system_info()));
    }

    private static void OnLog(int level, IntPtr text, IntPtr userData)
    {
        try
        {
            string message = Marshal.PtrToStringAnsi(text);
            if (string.IsNullOrEmpty(message))
            {
                return;
            }
            // ggml sends partial lines (e.g. progress dots); only complete lines are logged.
            lock (Gate)
            {
                Pending.Append(message);
                string all = Pending.ToString();
                int newline = all.LastIndexOf('\n');
                if (newline < 0)
                {
                    return;
                }
                Pending.Length = 0;
                Pending.Append(all.Substring(newline + 1));
                foreach (string line in all.Substring(0, newline).Split('\n'))
                {
                    if (line.Trim().Length > 0)
                    {
                        Debug.Log("[BetterSpeech/whisper] " + line.TrimEnd());
                    }
                }
            }
        }
        catch
        {
            // Never let an exception unwind into native code.
        }
    }
}
