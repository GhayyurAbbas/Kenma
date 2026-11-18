using System;
using System.IO;

namespace Kenma
{
    static class AppPaths
    {
        public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kenma");
        /// <summary>Log folder; KENMA_LOG_DIR overrides it (the test suite uses this to keep the user's log clean).</summary>
        public static readonly string LogDir = Environment.GetEnvironmentVariable("KENMA_LOG_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kenma", "logs");
        public static readonly string CliSandboxDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kenma", "cli-sandbox");

        /// <summary>Earlier names of the app, newest first.</summary>
        public static readonly string[] LegacyNames = { "Promptstone", "Whetstone", "promptuDX" };

        /// <summary>Writes via a temp file + replace so a crash mid-write never leaves a truncated file.</summary>
        public static void WriteAtomic(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, content);
            File.Move(tmp, path, overwrite: true);
        }

        /// <summary>Moves a file that failed to parse out of the way so it can be inspected instead of silently overwritten.</summary>
        public static void QuarantineCorrupt(string path)
        {
            try
            {
                var dest = Path.Combine(Path.GetDirectoryName(path)!,
                    Path.GetFileNameWithoutExtension(path) + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}" + Path.GetExtension(path));
                File.Move(path, dest);
                Log.Warn($"Corrupt file moved to {dest}");
            }
            catch (Exception ex) { Log.Error("Could not quarantine corrupt file", ex); }
        }

        /// <summary>One-time move of settings and history from the newest folder of a previous app name.</summary>
        public static void MigrateLegacyData()
        {
            if (Directory.Exists(DataDir)) return;
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            foreach (var legacy in LegacyNames)
            {
                var legacyDir = Path.Combine(appData, legacy);
                if (!Directory.Exists(legacyDir)) continue;
                try
                {
                    Directory.CreateDirectory(DataDir);
                    foreach (var name in new[] { "settings.json", "history.json" })
                    {
                        var src = Path.Combine(legacyDir, name);
                        if (File.Exists(src)) File.Copy(src, Path.Combine(DataDir, name));
                    }
                    Directory.Delete(legacyDir, recursive: true);
                    Log.Info($"Migrated data from {legacy}");
                }
                catch (Exception ex) { Log.Error($"Migrating data from {legacy} failed", ex); }
                return;
            }
        }
    }
}
