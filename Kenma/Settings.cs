using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kenma.Engines;

namespace Kenma
{
    public class EngineSettings
    {
        public string Model { get; set; } = "";
        /// <summary>Empty means the engine's default URL.</summary>
        public string BaseUrl { get; set; } = "";
    }

    public class Settings
    {
        /// <summary>An engine id from <see cref="EngineCatalog"/>, or "auto" to use the first one that's ready.</summary>
        public string PrimaryEngine { get; set; } = EngineCatalog.Auto;
        public Dictionary<string, EngineSettings> Engines { get; set; } = new();
        public bool FallbackClaudeCli { get; set; } = true;
        public bool FallbackLocal { get; set; } = true;
        public bool FallbackTemplate { get; set; } = true;

        public bool Topmost { get; set; } = true;
        public bool StartWithWindows { get; set; } = true;
        public bool AutoCopy { get; set; } = true;
        public string Hotkey { get; set; } = Kenma.Hotkey.Default;
        public bool SaveHistory { get; set; } = true;
        /// <summary>Days to keep history; 0 keeps it forever.</summary>
        public int HistoryDays { get; set; } = 90;
        /// <summary>Per-engine timeout before falling back to the next engine.</summary>
        public int TimeoutSeconds { get; set; } = 180;
        public double Left { get; set; } = double.NaN;
        public double Top { get; set; } = double.NaN;
        public double Width { get; set; } = 400;
        public double Height { get; set; } = 580;
        public bool TrayHintShown { get; set; }

        /// <summary>Plain-text key written by versions before 0.9; read once, moved to Credential Manager, then dropped.</summary>
        [JsonPropertyName("ApiKey")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LegacyApiKey { get; set; }

        /// <summary>Single Anthropic model setting from 0.9; moved into <see cref="Engines"/>.</summary>
        [JsonPropertyName("Model")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LegacyModel { get; set; }

        public EngineSettings EngineConfig(string id)
        {
            if (!Engines.TryGetValue(id, out var cfg)) Engines[id] = cfg = new EngineSettings();
            return cfg;
        }

        static readonly string FilePath = Path.Combine(AppPaths.DataDir, "settings.json");

        static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        public static Settings Load()
        {
            Settings s;
            try
            {
                s = File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Options) ?? new Settings()
                    : new Settings();
            }
            catch (Exception ex)
            {
                Log.Error("settings.json could not be read; using defaults", ex);
                AppPaths.QuarantineCorrupt(FilePath);
                s = new Settings();
            }

            bool migrated = false;
            if (!string.IsNullOrWhiteSpace(s.LegacyApiKey))
            {
                if (EngineCatalog.SetKey(EngineCatalog.Anthropic, s.LegacyApiKey)) Log.Info("Moved API key from settings.json to Credential Manager");
                migrated = true;
            }
            if (!string.IsNullOrWhiteSpace(s.LegacyModel))
            {
                var cfg = s.EngineConfig(EngineCatalog.Anthropic);
                if (cfg.Model.Length == 0) cfg.Model = s.LegacyModel.Trim();
                migrated = true;
            }
            s.LegacyApiKey = null;
            s.LegacyModel = null;
            s.Normalize();
            if (migrated) s.Save();
            return s;
        }

        void Normalize()
        {
            Engines ??= new();
            if (!EngineCatalog.IsKnown(PrimaryEngine)) PrimaryEngine = EngineCatalog.Auto;
            if (!Kenma.Hotkey.TryParse(Hotkey, out _, out _)) Hotkey = Kenma.Hotkey.Default;
            if (HistoryDays < 0) HistoryDays = 90;
            TimeoutSeconds = Math.Clamp(TimeoutSeconds, 15, 900);
            Width = double.IsFinite(Width) && Width >= 340 ? Width : 400;
            Height = double.IsFinite(Height) && Height >= 420 ? Height : 580;
        }

        public void Save()
        {
            try { AppPaths.WriteAtomic(FilePath, JsonSerializer.Serialize(this, Options)); }
            catch (Exception ex) { Log.Error("Saving settings failed", ex); }
        }
    }
}
