using System;
using Microsoft.Win32;

namespace PromptuDX
{
    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "promptuDX";

        public static void Set(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
                if (enabled) key.SetValue(Name, $"\"{Environment.ProcessPath}\"");
                else key.DeleteValue(Name, throwOnMissingValue: false);
            }
            catch { }
        }
    }
}
