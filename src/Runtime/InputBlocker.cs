using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace BetterSpeech;

/// <summary>
/// Keeps mouse and keyboard input away from the game while the menu is open. The game (and its UI Toolkit
/// UI) reads input through the Input System, while IMGUI gets its events from the engine directly, so
/// marking Input System events as handled blocks the game without affecting the menu.
/// InputSystem.DisableDevice is stripped from the game, hence the event filter.
/// </summary>
internal static class InputBlocker
{
    private static bool installed;

    public static bool Blocking { get; set; }

    public static void Install()
    {
        if (installed)
        {
            return;
        }
        try
        {
            InputSystem.onEvent += OnEvent;
            installed = true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterSpeech] Could not block game input while the menu is open: " + ex.Message);
        }
    }

    private static void OnEvent(InputEventPtr eventPtr, InputDevice device)
    {
        if (Blocking && (device is Pointer || device is Keyboard))
        {
            eventPtr.handled = true;
        }
    }
}
