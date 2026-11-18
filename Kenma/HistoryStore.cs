using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Kenma
{
    public class HistoryEntry
    {
        public DateTime Time { get; set; }
        public string Raw { get; set; } = "";
        public string Enhanced { get; set; } = "";
        /// <summary>Name of the engine that produced <see cref="Enhanced"/> (empty for entries from before 1.0).</summary>
        public string Engine { get; set; } = "";
    }

    static class HistoryStore
    {
        const int MaxItems = 300;
        static readonly string FilePath = Path.Combine(AppPaths.DataDir, "history.json");

        public static List<HistoryEntry> Items { get; private set; } = new();

        public static void Load(int retentionDays)
        {
            try
            {
                Items = File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(FilePath)) ?? new()
                    : new();
            }
            catch (Exception ex)
            {
                Log.Error("history.json could not be read; starting empty", ex);
                AppPaths.QuarantineCorrupt(FilePath);
                Items = new();
            }
            if (Prune(retentionDays)) Save();
        }

        public static void Add(string raw, string enhanced, string engine, int retentionDays)
        {
            Items.Insert(0, new HistoryEntry { Time = DateTime.Now, Raw = raw, Enhanced = enhanced, Engine = engine });
            Prune(retentionDays);
            Save();
        }

        /// <summary>Applies the item cap and the retention window. Returns true if anything was removed.</summary>
        public static bool Prune(int retentionDays)
        {
            int before = Items.Count;
            if (retentionDays > 0)
            {
                var cutoff = DateTime.Now.AddDays(-retentionDays);
                Items.RemoveAll(e => e.Time < cutoff);
            }
            if (Items.Count > MaxItems) Items.RemoveRange(MaxItems, Items.Count - MaxItems);
            return Items.Count != before;
        }

        public static void Remove(HistoryEntry e) { Items.Remove(e); Save(); }

        public static void Clear() { Items.Clear(); Save(); }

        public static void Save()
        {
            try { AppPaths.WriteAtomic(FilePath, JsonSerializer.Serialize(Items, new JsonSerializerOptions { WriteIndented = true })); }
            catch (Exception ex) { Log.Error("Saving history failed", ex); }
        }
    }
}
