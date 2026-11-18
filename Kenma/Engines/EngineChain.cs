using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Kenma.Engines
{
    /// <param name="Skipped">Engines tried or skipped before <paramref name="Engine"/>, each as "Name: reason".</param>
    public sealed record EnhanceResult(string Text, IEngine Engine, IReadOnlyList<string> Skipped);

    /// <summary>
    /// Decides which engines to try, in what order, and walks the list until one succeeds.
    /// Never falls back silently: the result says which engine answered and why earlier ones didn't.
    /// </summary>
    static class EngineChain
    {
        public static List<string> Order(Settings s) => Order(s, id => EngineCatalog.GetKey(id).Length > 0);

        public static List<string> Order(Settings s, Func<string, bool> hasKey)
        {
            var ids = new List<string>();
            if (s.PrimaryEngine == EngineCatalog.Auto)
            {
                ids.AddRange(EngineCatalog.CloudIds.Where(hasKey));
                ids.AddRange(new[] { EngineCatalog.ClaudeCli, EngineCatalog.Ollama, EngineCatalog.LmStudio, EngineCatalog.Template });
            }
            else
            {
                ids.Add(s.PrimaryEngine);
                if (s.FallbackClaudeCli) ids.Add(EngineCatalog.ClaudeCli);
                if (s.FallbackLocal) ids.AddRange(new[] { EngineCatalog.Ollama, EngineCatalog.LmStudio });
                if (s.FallbackTemplate) ids.Add(EngineCatalog.Template);
            }
            return ids.Distinct().ToList();
        }

        public static Task<EnhanceResult> EnhanceAsync(string raw, Settings s, CancellationToken ct) =>
            RunAsync(Order(s).Select(id => EngineCatalog.Create(id, s)), raw, TimeSpan.FromSeconds(s.TimeoutSeconds), ct);

        /// <summary>
        /// Tries each engine in turn. An engine is skipped when its readiness check fails, and abandoned (moving on to
        /// the next) when it errors or exceeds <paramref name="perEngineTimeout"/>. Only the user's own cancel stops the walk.
        /// </summary>
        public static async Task<EnhanceResult> RunAsync(IEnumerable<IEngine> engines, string raw, TimeSpan perEngineTimeout, CancellationToken ct)
        {
            var skipped = new List<string>();
            foreach (var engine in engines)
            {
                ct.ThrowIfCancellationRequested();
                var notReady = await engine.CheckAsync(ct);
                if (notReady != null) { skipped.Add($"{engine.Name}: {notReady}"); continue; }

                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attempt.CancelAfter(perEngineTimeout);
                try
                {
                    var text = await engine.EnhanceAsync(raw, attempt.Token);
                    if (skipped.Count > 0) Log.Info($"Used {engine.Name} after: {string.Join("; ", skipped)}");
                    return new EnhanceResult(text, engine, skipped);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    skipped.Add($"{engine.Name}: no response after {perEngineTimeout.TotalSeconds:0} s");
                }
                catch (EnhanceException ex)
                {
                    skipped.Add($"{engine.Name}: {ex.Message}");
                }
            }

            throw new EnhanceException(skipped.Count == 0
                ? "No engine is configured. Choose one in Settings."
                : "No engine could enhance this prompt:\n• " + string.Join("\n• ", skipped));
        }

        /// <summary>The engine a request would use right now (for the status indicator), plus why earlier ones were skipped.</summary>
        public static async Task<(IEngine? Engine, List<string> Skipped)> FirstReadyAsync(Settings s, CancellationToken ct)
        {
            var skipped = new List<string>();
            foreach (var id in Order(s))
            {
                var engine = EngineCatalog.Create(id, s);
                var notReady = await engine.CheckAsync(ct);
                if (notReady == null) return (engine, skipped);
                skipped.Add($"{engine.Name}: {notReady}");
            }
            return (null, skipped);
        }
    }
}
