using System;
using Microsoft.Win32;

namespace Kenma
{
    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "Kenma";

        public static void Set(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
                foreach (var legacy in AppPaths.LegacyNames) key.DeleteValue(legacy, throwOnMissingValue: false);
                if (enabled) key.SetValue(Name, $"\"{Environment.ProcessPath}\"");
                else key.DeleteValue(Name, throwOnMissingValue: false);
            }
            catch (Exception ex) { Log.Error("Updating the startup entry failed", ex); }
        }
    }
}
