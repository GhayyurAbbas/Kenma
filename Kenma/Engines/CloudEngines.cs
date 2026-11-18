using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Kenma.Engines
{
    /// <summary>Anthropic Messages API.</summary>
    sealed class AnthropicEngine : IEngine, IModelSource
    {
        const string Base = "https://api.anthropic.com/v1";
        readonly string _key, _model;

        public AnthropicEngine(string key, string model) { _key = key; _model = model; }

        public string Id => EngineCatalog.Anthropic;
        public string Name => "Anthropic API";
        public string Detail => _model;
        public bool IsAi => true;

        public Task<string?> CheckAsync(CancellationToken ct) => Task.FromResult(
            _key.Length == 0 ? "no API key" : !Connectivity.IsOnline ? "offline" : _model.Length == 0 ? "no model selected" : null);

        System.Net.Http.HttpRequestMessage Auth(System.Net.Http.HttpRequestMessage req)
        {
            req.Headers.Add("x-api-key", _key);
            req.Headers.Add("anthropic-version", "2023-06-01");
            return req;
        }

        public async Task<string> EnhanceAsync(string raw, CancellationToken ct)
        {
            var body = new JsonObject
            {
                ["model"] = _model,
                ["max_tokens"] = 8000,
                ["system"] = Enhancer.SystemPrompt,
                ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = Enhancer.Wrap(raw) })
            };
            using var req = Auth(HttpJson.Post($"{Base}/messages", body));
            var node = await HttpJson.SendAsync(req, Name, false, ct);
            var sb = new StringBuilder();
            foreach (var block in node["content"]?.AsArray() ?? new JsonArray())
                if (block?["type"]?.GetValue<string>() == "text") sb.Append(block["text"]?.GetValue<string>());
            return HttpJson.Required(sb.ToString(), Name);
        }

        public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct)
        {
            using var req = Auth(HttpJson.Get($"{Base}/models?limit=100"));
            var node = await HttpJson.SendAsync(req, Name, false, ct);
            return (node["data"]?.AsArray() ?? new JsonArray()).Select(m => m?["id"]?.GetValue<string>() ?? "").Where(s => s.Length > 0).ToList();
        }
    }

    /// <summary>
    /// Any OpenAI-compatible Chat Completions endpoint: OpenAI, OpenRouter, Ollama, LM Studio.
    /// Local servers need no key and pick their first installed model when none is configured.
    /// </summary>
    sealed class OpenAiCompatibleEngine : IEngine, IModelSource
    {
        readonly string _id, _name, _baseUrl, _key, _model;
        readonly bool _local;

        public OpenAiCompatibleEngine(string id, string name, string baseUrl, string key, string model, bool local)
        {
            _id = id; _name = name; _baseUrl = baseUrl.TrimEnd('/'); _key = key; _model = model; _local = local;
        }

        public string Id => _id;
        public string Name => _name;
        public string Detail => _model.Length > 0 ? _model : (_local && LocalProbe.Cached(_baseUrl) is { Models.Count: > 0 } p ? p.Models[0] : "");
        public bool IsAi => true;

        public async Task<string?> CheckAsync(CancellationToken ct)
        {
            if (_local)
            {
                var probe = await LocalProbe.RunAsync(_baseUrl, ct);
                if (probe.Error != null) return probe.Error;
                return _model.Length == 0 && probe.Models.Count == 0 ? "no models installed" : null;
            }
            if (_key.Length == 0) return "no API key";
            if (!Connectivity.IsOnline) return "offline";
            return _model.Length == 0 ? "no model selected" : null;
        }

        System.Net.Http.HttpRequestMessage Auth(System.Net.Http.HttpRequestMessage req)
        {
            if (_key.Length > 0) req.Headers.Add("Authorization", "Bearer " + _key);
            if (_id == EngineCatalog.OpenRouter) req.Headers.Add("X-Title", "Kenma");
            return req;
        }

        public async Task<string> EnhanceAsync(string raw, CancellationToken ct)
        {
            var model = _model;
            if (model.Length == 0 && _local)
            {
                var probe = await LocalProbe.RunAsync(_baseUrl, ct);
                model = probe.Models.FirstOrDefault() ?? throw new EnhanceException($"{_name} has no models installed.");
            }

            var body = new JsonObject
            {
                ["model"] = model,
                ["stream"] = false,
                ["messages"] = new JsonArray(
                    new JsonObject { ["role"] = "system", ["content"] = Enhancer.SystemPrompt },
                    new JsonObject { ["role"] = "user", ["content"] = Enhancer.Wrap(raw) })
            };
            using var req = Auth(HttpJson.Post($"{_baseUrl}/chat/completions", body));
            var node = await HttpJson.SendAsync(req, _name, _local, ct);
            return HttpJson.Required(node["choices"]?[0]?["message"]?["content"]?.GetValue<string>(), _name);
        }

        public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct)
        {
            using var req = Auth(HttpJson.Get($"{_baseUrl}/models"));
            var node = await HttpJson.SendAsync(req, _name, _local, ct);
            return (node["data"]?.AsArray() ?? new JsonArray()).Select(m => m?["id"]?.GetValue<string>() ?? "")
                .Where(s => s.Length > 0).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>Google Gemini generateContent API.</summary>
    sealed class GeminiEngine : IEngine, IModelSource
    {
        const string Base = "https://generativelanguage.googleapis.com/v1beta";
        readonly string _key, _model;

        public GeminiEngine(string key, string model) { _key = key; _model = model.StartsWith("models/") ? model[7..] : model; }

        public string Id => EngineCatalog.Gemini;
        public string Name => "Google Gemini API";
        public string Detail => _model;
        public bool IsAi => true;

        public Task<string?> CheckAsync(CancellationToken ct) => Task.FromResult(
            _key.Length == 0 ? "no API key" : !Connectivity.IsOnline ? "offline" : _model.Length == 0 ? "no model selected" : null);

        System.Net.Http.HttpRequestMessage Auth(System.Net.Http.HttpRequestMessage req)
        {
            req.Headers.Add("x-goog-api-key", _key); // header, not query string, so the key never lands in URLs or logs
            return req;
        }

        public async Task<string> EnhanceAsync(string raw, CancellationToken ct)
        {
            var body = new JsonObject
            {
                ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = Enhancer.SystemPrompt }) },
                ["contents"] = new JsonArray(new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray(new JsonObject { ["text"] = Enhancer.Wrap(raw) })
                })
            };
            using var req = Auth(HttpJson.Post($"{Base}/models/{Uri.EscapeDataString(_model)}:generateContent", body));
            var node = await HttpJson.SendAsync(req, Name, false, ct);
            var sb = new StringBuilder();
            foreach (var part in node["candidates"]?[0]?["content"]?["parts"]?.AsArray() ?? new JsonArray())
                sb.Append(part?["text"]?.GetValue<string>());
            return HttpJson.Required(sb.ToString(), Name);
        }

        public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct)
        {
            using var req = Auth(HttpJson.Get($"{Base}/models?pageSize=200"));
            var node = await HttpJson.SendAsync(req, Name, false, ct);
            return (node["models"]?.AsArray() ?? new JsonArray())
                .Where(m => m?["supportedGenerationMethods"]?.AsArray().Any(x => x?.GetValue<string>() == "generateContent") == true)
                .Select(m => (m?["name"]?.GetValue<string>() ?? "").Replace("models/", ""))
                .Where(s => s.Length > 0).ToList();
        }
    }

    /// <summary>Caches "is the local server up, and which models does it have" for a few seconds.</summary>
    static class LocalProbe
    {
        public sealed record Result(string? Error, IReadOnlyList<string> Models);

        static readonly Dictionary<string, (DateTime At, Result Result)> Cache = new();
        static readonly TimeSpan Ttl = TimeSpan.FromSeconds(15);

        public static Result? Cached(string baseUrl)
        {
            lock (Cache) return Cache.TryGetValue(baseUrl, out var c) && DateTime.UtcNow - c.At < Ttl ? c.Result : null;
        }

        public static async Task<Result> RunAsync(string baseUrl, CancellationToken ct)
        {
            if (Cached(baseUrl) is { } hit) return hit;
            Result result;
            try
            {
                using var quick = CancellationTokenSource.CreateLinkedTokenSource(ct);
                quick.CancelAfter(TimeSpan.FromSeconds(1.5));
                using var res = await HttpJson.Client.GetAsync($"{baseUrl}/models", quick.Token);
                if (!res.IsSuccessStatusCode) result = new($"server returned HTTP {(int)res.StatusCode}", Array.Empty<string>());
                else
                {
                    var node = JsonNode.Parse(await res.Content.ReadAsStringAsync(quick.Token));
                    var models = (node?["data"]?.AsArray() ?? new JsonArray()).Select(m => m?["id"]?.GetValue<string>() ?? "").Where(s => s.Length > 0).ToList();
                    result = new(null, models);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { result = new("not running", Array.Empty<string>()); }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Text.Json.JsonException) { result = new("not running", Array.Empty<string>()); }
            lock (Cache) Cache[baseUrl] = (DateTime.UtcNow, result);
            return result;
        }

        public static void Invalidate() { lock (Cache) Cache.Clear(); }
    }
}
