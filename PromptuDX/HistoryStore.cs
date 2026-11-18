using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PromptuDX
{
    public class HistoryEntry
    {
        public DateTime Time { get; set; }
        public string Raw { get; set; } = "";
        public string Enhanced { get; set; } = "";
    }

    static class HistoryStore
    {
        const int MaxItems = 300;
        static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "promptuDX", "history.json");

        public static List<HistoryEntry> Items { get; private set; } = new();

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    Items = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(FilePath)) ?? new();
            }
            catch { Items = new(); }
        }

        public static void Add(string raw, string enhanced)
        {
            Items.Insert(0, new HistoryEntry { Time = DateTime.Now, Raw = raw, Enhanced = enhanced });
            if (Items.Count > MaxItems) Items.RemoveRange(MaxItems, Items.Count - MaxItems);
            Save();
        }

        public static void Remove(HistoryEntry e) { Items.Remove(e); Save(); }

        public static void Clear() { Items.Clear(); Save(); }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Items, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
