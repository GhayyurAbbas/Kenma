using System;
using System.Collections.Generic;
using System.Linq;

namespace Kenma.Engines
{
    public sealed record EngineInfo(
        string Id, string Name, string Description,
        bool NeedsKey, bool IsLocal, bool HasModels,
        string KeyTarget = "", string EnvVar = "", string DefaultBaseUrl = "", string DefaultModel = "");

    /// <summary>The list of engines Kenma knows about, and how to build each one from settings.</summary>
    static class EngineCatalog
    {
        public const string Auto = "auto", Anthropic = "anthropic", OpenAI = "openai", Gemini = "gemini", OpenRouter = "openrouter",
            ClaudeCli = "claude-cli", Ollama = "ollama", LmStudio = "lmstudio", Template = "template";

        public static readonly IReadOnlyList<EngineInfo> All = new[]
        {
            new EngineInfo(Anthropic, "Anthropic API", "Claude models with your Anthropic API key.",
                NeedsKey: true, IsLocal: false, HasModels: true, KeyTarget: "Kenma:AnthropicApiKey", EnvVar: "ANTHROPIC_API_KEY", DefaultModel: "claude-opus-5-5"),
            new EngineInfo(OpenAI, "OpenAI API", "GPT models with your OpenAI API key.",
                NeedsKey: true, IsLocal: false, HasModels: true, KeyTarget: "Kenma:OpenAIApiKey", EnvVar: "OPENAI_API_KEY", DefaultBaseUrl: "https://api.openai.com/v1"),
            new EngineInfo(Gemini, "Google Gemini API", "Gemini models with your Google AI Studio key.",
                NeedsKey: true, IsLocal: false, HasModels: true, KeyTarget: "Kenma:GeminiApiKey", EnvVar: "GEMINI_API_KEY"),
            new EngineInfo(OpenRouter, "OpenRouter", "Hundreds of models through one OpenRouter key.",
                NeedsKey: true, IsLocal: false, HasModels: true, KeyTarget: "Kenma:OpenRouterApiKey", EnvVar: "OPENROUTER_API_KEY", DefaultBaseUrl: "https://openrouter.ai/api/v1"),
            new EngineInfo(ClaudeCli, "Claude Code", "Uses your Claude Code sign-in through the claude CLI. No key needed; needs internet.",
                NeedsKey: false, IsLocal: false, HasModels: false),
            new EngineInfo(Ollama, "Ollama (local)", "A model running on this PC with Ollama. Works offline.",
                NeedsKey: false, IsLocal: true, HasModels: true, DefaultBaseUrl: "http://localhost:11434/v1"),
            new EngineInfo(LmStudio, "LM Studio (local)", "A model running on this PC with LM Studio's server. Works offline.",
                NeedsKey: false, IsLocal: true, HasModels: true, DefaultBaseUrl: "http://localhost:1234/v1"),
            new EngineInfo(Template, "Offline template", "No AI: structures your prompt and marks the gaps with [PLACEHOLDERS]. Always available.",
                NeedsKey: false, IsLocal: true, HasModels: false),
        };

        public static readonly string[] CloudIds = { Anthropic, OpenAI, Gemini, OpenRouter };

        public static EngineInfo Info(string id) =>
            All.FirstOrDefault(e => e.Id == id) ?? throw new ArgumentException($"Unknown engine '{id}'", nameof(id));

        public static bool IsKnown(string id) => id == Auto || All.Any(e => e.Id == id);

        /// <summary>The stored key from Credential Manager, falling back to the provider's environment variable.</summary>
        public static string GetKey(string id)
        {
            var info = Info(id);
            if (!info.NeedsKey) return "";
            var key = Secrets.Get(info.KeyTarget);
            return key.Length > 0 ? key : (Environment.GetEnvironmentVariable(info.EnvVar) ?? "").Trim();
        }

        public static bool SetKey(string id, string key) => Secrets.Set(Info(id).KeyTarget, key);

        /// <summary>Moves keys saved under a previous app name ("Whetstone:…") to the current Credential Manager names.</summary>
        public static void MigrateLegacyKeys()
        {
            foreach (var info in All.Where(e => e.NeedsKey))
            {
                var suffix = info.KeyTarget[(info.KeyTarget.IndexOf(':') + 1)..];
                foreach (var legacy in AppPaths.LegacyNames)
                {
                    var oldTarget = $"{legacy}:{suffix}";
                    var oldKey = Secrets.Get(oldTarget);
                    if (oldKey.Length == 0) continue;
                    if (Secrets.Get(info.KeyTarget).Length > 0 || Secrets.Set(info.KeyTarget, oldKey))
                    {
                        Secrets.Set(oldTarget, "");
                        Log.Info($"Moved {info.Name} key from {legacy}");
                    }
                }
            }
        }

        /// <param name="keyOverride">An unsaved key to use instead of the stored one (Settings panel Test / Load models).</param>
        public static IEngine Create(string id, Settings s, string? keyOverride = null)
        {
            var info = Info(id);
            var cfg = s.EngineConfig(id);
            var model = cfg.Model.Trim();
            var baseUrl = string.IsNullOrWhiteSpace(cfg.BaseUrl) ? info.DefaultBaseUrl : cfg.BaseUrl.Trim();
            var key = string.IsNullOrWhiteSpace(keyOverride) ? GetKey(id) : keyOverride.Trim();
            return id switch
            {
                Anthropic => new AnthropicEngine(key, model.Length > 0 ? model : info.DefaultModel),
                Gemini => new GeminiEngine(key, model),
                OpenAI or OpenRouter => new OpenAiCompatibleEngine(id, info.Name, baseUrl, key, model, local: false),
                Ollama or LmStudio => new OpenAiCompatibleEngine(id, info.Name, baseUrl, "", model, local: true),
                ClaudeCli => new ClaudeCliEngine(),
                Template => new TemplateEngine(),
                _ => throw new ArgumentException($"Unknown engine '{id}'", nameof(id))
            };
        }
    }
}
