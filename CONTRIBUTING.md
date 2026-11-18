# Contributing to Kenma

Thanks for helping! Bug reports, engine integrations and prompt-quality improvements are all welcome.

## Getting started

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows.
2. `dotnet test Kenma.slnx` must pass before and after your change.
3. Run the app with `dotnet run --project Kenma`.

Live tests against the real `claude` CLI are opt-in: set `KENMA_INTEGRATION=1` (requires Claude Code installed and signed in).

## Project layout

| Path | What's there |
|---|---|
| `Kenma/Enhancer.cs` | The prompt-engineering system prompt. Changes here change every result, so include before/after examples in your PR. |
| `Kenma/Engines/` | One class per engine, `EngineChain` (order and fallback), `EngineCatalog` (the list shown in Settings). |
| `Kenma/PreCheck.cs` | The offline heuristic pre-check, also used by the offline template. |
| `Kenma/MainWindow.*` | The UI. Colors come from the WPF Fluent theme resources; don't hard-code colors. |
| `Kenma/IconFactory.cs` | The logo is drawn in code. `Kenma.exe --icon-preview out.png` renders every size for review. |
| `Kenma.Tests/` | xUnit tests. Engines are tested against a local mock server; no network is needed. |

## Adding an engine

1. Implement `IEngine` (and `IModelSource` if it can list models) in `Kenma/Engines/`.
   If the service is OpenAI-compatible, you probably only need a new entry for `OpenAiCompatibleEngine`.
2. Add an `EngineInfo` to `EngineCatalog.All` and a case in `EngineCatalog.Create`.
3. `CheckAsync` must be fast (no slow network calls) and return a short reason when not ready.
4. Throw `EnhanceException` with a message a user can act on. Never include keys in messages or logs.
5. Add tests.

## Rules of the road

- **Privacy first.** Never log prompt text or keys. Never send a prompt anywhere except the engine the user chose.
- **No silent fallbacks.** If a different engine answered, the UI must say so.
- Match the surrounding code style. Keep PRs focused.

## Releasing (maintainers)

Update `<Version>` in `Kenma/Kenma.csproj` and `CHANGELOG.md`, then push a tag such as `v1.0.0`.
The release workflow tests, builds self-contained x64 and arm64 zips, and publishes a GitHub release with SHA-256 checksums.
Then update the hashes in `packaging/`. Code signing is set up through SignPath; see [docs/SIGNING.md](docs/SIGNING.md).
