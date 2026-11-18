using System;
using System.IO;

namespace Kenma
{
    /// <summary>
    /// Minimal file logger. Never log prompt text or secrets here: only events and error details.
    /// </summary>
    static class Log
    {
        const long MaxBytes = 1_000_000;
        static readonly object Gate = new();
        public static readonly string FilePath = Path.Combine(AppPaths.LogDir, "kenma.log");

        public static void Info(string message) => Write("INFO", message, null);
        public static void Warn(string message) => Write("WARN", message, null);
        public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

        static void Write(string level, string message, Exception? ex)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(AppPaths.LogDir);
                    var info = new FileInfo(FilePath);
                    if (info.Exists && info.Length > MaxBytes)
                        File.Move(FilePath, Path.Combine(AppPaths.LogDir, "kenma.1.log"), overwrite: true);

                    var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                    if (ex != null) line += Environment.NewLine + ex;
                    File.AppendAllText(FilePath, line + Environment.NewLine);
                }
            }
            catch { /* logging must never crash the app */ }
        }
    }
}
