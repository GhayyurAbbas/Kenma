using System;
using System.IO;
using System.Text.Json;

namespace PromptuDX
{
    public class Settings
    {
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = "claude-opus-5-5";
        public bool Topmost { get; set; } = true;
        public bool StartWithWindows { get; set; } = true;
        public bool AutoCopy { get; set; } = true;
        public double Left { get; set; } = double.NaN;
        public double Top { get; set; } = double.NaN;
        public double Width { get; set; } = 400;
        public double Height { get; set; } = 560;
        public bool TrayHintShown { get; set; }

        static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "promptuDX");
        static readonly string FilePath = Path.Combine(Dir, "settings.json");

        static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Options) ?? new Settings();
            }
            catch { }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
            }
            catch { }
        }

        public string EffectiveApiKey =>
            !string.IsNullOrWhiteSpace(ApiKey) ? ApiKey.Trim() : (Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "");
    }
}
