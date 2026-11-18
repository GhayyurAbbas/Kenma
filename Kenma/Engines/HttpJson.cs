using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Kenma.Engines
{
    /// <summary>Shared HTTP plumbing: one client, JSON in/out, and consistent user-facing error messages.</summary>
    static class HttpJson
    {
        // Timeouts come from the caller's CancellationToken so cancel and timeout behave the same way.
        public static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };

        public static HttpRequestMessage Post(string url, JsonNode body) => new(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };

        public static HttpRequestMessage Get(string url) => new(HttpMethod.Get, url);

        public static async Task<JsonNode> SendAsync(HttpRequestMessage req, string provider, bool isLocal, CancellationToken ct)
        {
            HttpResponseMessage res;
            try { res = await Client.SendAsync(req, ct); }
            catch (HttpRequestException ex)
            {
                Log.Warn($"{provider} unreachable: {ex.Message}");
                throw new EnhanceException(isLocal
                    ? $"Couldn't connect to {provider}. Make sure it's running."
                    : $"Couldn't reach {provider}. Check your internet connection.");
            }

            using (res)
            {
                var text = await res.Content.ReadAsStringAsync(ct);
                JsonNode? node = null;
                try { node = JsonNode.Parse(text); } catch (JsonException) { }

                if (!res.IsSuccessStatusCode)
                {
                    var detail = ErrorMessage(node);
                    int code = (int)res.StatusCode;
                    Log.Warn($"{provider} returned HTTP {code}: {detail}");
                    throw new EnhanceException(code switch
                    {
                        401 or 403 => $"{provider} rejected the API key. Check it in Settings.",
                        404 => $"{provider} couldn't find that model. Pick another in Settings." + (detail != null ? $" ({detail})" : ""),
                        429 => $"{provider} rate limit or quota reached. Try again shortly.",
                        >= 500 => $"{provider} is having trouble right now. Try again shortly.",
                        _ => detail != null ? $"{provider}: {detail}" : $"{provider} returned HTTP {code}."
                    });
                }

                return node ?? throw new EnhanceException($"{provider} sent a response Kenma couldn't read.");
            }
        }

        static string? ErrorMessage(JsonNode? node)
        {
            try
            {
                var err = node?["error"];
                if (err is JsonValue v) return v.GetValue<string>();   // Ollama: { "error": "..." }
                return err?["message"]?.GetValue<string>();           // OpenAI / Anthropic / Gemini
            }
            catch { return null; }
        }

        public static string Required(string? text, string provider)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text)) throw new EnhanceException($"{provider} returned an empty response. Try again.");
            return text;
        }
    }
}
