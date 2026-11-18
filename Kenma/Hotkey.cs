using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace Kenma
{
    /// <summary>Parses, formats and registers a system-wide hotkey such as "Ctrl+Alt+P".</summary>
    static class Hotkey
    {
        public const string Default = "Ctrl+Alt+P";

        const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
        const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public static bool TryParse(string? text, out ModifierKeys mods, out Key key)
        {
            mods = ModifierKeys.None;
            key = Key.None;
            if (string.IsNullOrWhiteSpace(text)) return false;
            foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                switch (raw.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= ModifierKeys.Control; break;
                    case "alt": mods |= ModifierKeys.Alt; break;
                    case "shift": mods |= ModifierKeys.Shift; break;
                    case "win": case "windows": mods |= ModifierKeys.Windows; break;
                    default:
                        if (key != Key.None || !Enum.TryParse(raw, true, out key)) return false;
                        break;
                }
            }
            return key != Key.None && mods != ModifierKeys.None;
        }

        public static string Format(ModifierKeys mods, Key key)
        {
            var parts = new List<string>();
            if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            parts.Add(key.ToString());
            return string.Join("+", parts);
        }

        public static bool IsModifierKey(Key k) => k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

        /// <summary>Registers the hotkey; returns null on success or a user-facing error message.</summary>
        public static string? Register(IntPtr hwnd, int id, string text)
        {
            UnregisterHotKey(hwnd, id);
            if (!TryParse(text, out var mods, out var key))
                return $"\"{text}\" isn't a valid shortcut. Set one in Settings.";

            uint m = MOD_NOREPEAT;
            if (mods.HasFlag(ModifierKeys.Control)) m |= MOD_CONTROL;
            if (mods.HasFlag(ModifierKeys.Alt)) m |= MOD_ALT;
            if (mods.HasFlag(ModifierKeys.Shift)) m |= MOD_SHIFT;
            if (mods.HasFlag(ModifierKeys.Windows)) m |= MOD_WIN;

            if (RegisterHotKey(hwnd, id, m, (uint)KeyInterop.VirtualKeyFromKey(key))) return null;

            int err = Marshal.GetLastWin32Error();
            Log.Warn($"RegisterHotKey({text}) failed, Win32 error {err}");
            return err == ERROR_HOTKEY_ALREADY_REGISTERED
                ? $"The shortcut {text} is already used by another app. Pick a different one in Settings."
                : $"Couldn't register the shortcut {text} (error {err}).";
        }

        public static void Unregister(IntPtr hwnd, int id) => UnregisterHotKey(hwnd, id);
    }
}
