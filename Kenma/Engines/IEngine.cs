using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kenma.Engines
{
    /// <summary>Something that can turn a raw prompt into an enhanced one.</summary>
    public interface IEngine
    {
        string Id { get; }

        /// <summary>User-facing name, e.g. "Ollama (local)".</summary>
        string Name { get; }

        /// <summary>Model or other detail shown next to the name, or "" if none.</summary>
        string Detail { get; }

        bool IsAi { get; }

        /// <summary>Quick readiness check. Returns null when ready, otherwise a short reason ("no API key", "offline").</summary>
        Task<string?> CheckAsync(CancellationToken ct);

        /// <summary>Enhances the prompt. Throws <see cref="EnhanceException"/> with a user-safe message on failure.</summary>
        Task<string> EnhanceAsync(string raw, CancellationToken ct);
    }

    /// <summary>Engines that can list the models they offer (for the Settings model picker).</summary>
    public interface IModelSource
    {
        Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct);
    }
}
