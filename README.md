<div align="center">

<img src="assets/logo.svg" width="96" alt="Kenma logo">

# Kenma

**Sharpen any prompt.**

A tiny Windows tray app that turns rough prompts into clear, well-structured ones, like having a professional prompt engineer one shortcut away.

[![CI](https://github.com/GhayyurAbbas/Kenma/actions/workflows/ci.yml/badge.svg)](https://github.com/GhayyurAbbas/Kenma/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/GhayyurAbbas/Kenma)](https://github.com/GhayyurAbbas/Kenma/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4)

[Download](https://github.com/GhayyurAbbas/Kenma/releases/latest) · [Features](#features) · [Engines](#engines) · [Privacy](#privacy) · [Build](#build-from-source) · [Contributing](CONTRIBUTING.md)

</div>

---

## What is Kenma?

*Kenma* (研磨) is Japanese for **polishing, honing**. You paste a quick, vague prompt; Kenma rewrites it with a clear task, context, constraints and output format, and asks the questions that actually matter instead of guessing.

It lives in the system tray, opens with a global shortcut (**Ctrl+Alt+P** by default), and gets out of the way when you're done.

<!-- Screenshot: add docs/screenshot.png and uncomment
<p align="center"><img src="docs/screenshot.png" width="420" alt="Kenma window"></p>
-->

## Features

| | |
|---|---|
| **Prompt engineering built in** | A tuned system prompt that adds structure without inventing facts. Coding prompts get a scope fence, open questions and a "stop and report first" step. |
| **Many engines, one shortcut** | Anthropic, OpenAI, Gemini, OpenRouter, Claude Code, Ollama, LM Studio, or no AI at all. |
| **Automatic fallback** | *Auto* mode uses the first engine that's ready. If one fails or is slow, the next takes over, and the result says which engine answered and why. |
| **Works offline** | Cloud engines are skipped instantly when you're offline; local models and the offline template keep working. |
| **Instant pre-check** | A local clarity score as you type, flagging missing context, format, audience and constraints. |
| **History** | Searchable history of past prompts, with a retention period, or turn it off entirely. |
| **Native Windows 11 look** | Fluent design that follows light/dark mode and your accent color, including the tray icon. |
| **Stays out of the way** | Tray app, starts with Windows, configurable hotkey, always-on-top pin, auto-copy result. |

## Install

### Download

1. Grab the latest `Kenma-<version>-win-x64.zip` (or `win-arm64`) from [Releases](https://github.com/GhayyurAbbas/Kenma/releases/latest).
2. Unzip anywhere and run `Kenma.exe`. No installer and no .NET install needed.
3. Look for the <code>&lt;◆&gt;</code> icon in the tray, or press **Ctrl+Alt+P**.

### Package managers (coming soon)

```powershell
# winget
winget install GhayyurAbbas.Kenma

# Scoop
scoop install https://raw.githubusercontent.com/GhayyurAbbas/Kenma/main/packaging/scoop/kenma.json
```

> **Note:** Until releases are code-signed, Windows SmartScreen may warn on first launch. Choose **More info → Run anyway**, or build from source.

## Quick start

1. Open Kenma with **Ctrl+Alt+P** or by clicking the tray icon.
2. Type or paste a prompt, then press **Enter** (**Shift+Enter** for a new line).
3. Copy the enhanced prompt (copied automatically if *Auto-copy* is on) and paste it wherever you need it.

No API key? It still works: Kenma uses Claude Code if it's installed, a local model if one is running, or the offline template.

## Engines

Choose one in **Settings → Engine**, or leave it on **Auto**.

| Engine | Needs | Offline | Notes |
|---|---|:---:|---|
| Anthropic API | API key | | Claude models |
| OpenAI API | API key | | GPT models |
| Google Gemini API | API key | | Google AI Studio key |
| OpenRouter | API key | | Hundreds of models with one key |
| Claude Code | `claude` CLI, signed in | | Uses your existing Claude sign-in, no key |
| Ollama | Ollama running | ✅ | Default `http://localhost:11434/v1` |
| LM Studio | LM Studio server running | ✅ | Default `http://localhost:1234/v1` |
| Offline template | Nothing | ✅ | No AI: structures your prompt and marks gaps with `[PLACEHOLDERS]` |

API keys can also come from the usual environment variables (`ANTHROPIC_API_KEY`, `OPENAI_API_KEY`, `GEMINI_API_KEY`, `OPENROUTER_API_KEY`). Use **Load models** to pick a model and **Test** to check the connection.

**Auto mode** tries engines in order (cloud engines with a key, then Claude Code, then local models, then the template), skipping any that aren't ready. Each fallback step can be turned off in Settings.

## Privacy

- Your prompt is sent **only** to the engine you chose (or the one Auto picked, which is shown on every result).
- API keys are stored in **Windows Credential Manager**, never in files or logs.
- Prompt text is **never logged**.
- History stays on your PC in `%APPDATA%\Kenma\history.json`. You can turn it off or set how long it's kept.
- Claude Code runs in an isolated sandbox with no tools, no MCP servers and no session persistence.
- No telemetry.

## Files and settings

| What | Where |
|---|---|
| Settings and history | `%APPDATA%\Kenma\` |
| Logs | `%LOCALAPPDATA%\Kenma\logs\` (tray menu → *Open log folder*) |
| API keys | Windows Credential Manager, entries named `Kenma:*` |
| Start with Windows | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` |

To uninstall, untick *Start with Windows*, exit from the tray, delete the app folder and, if you like, the folders above.

## Build from source

Requires Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
git clone https://github.com/GhayyurAbbas/Kenma.git
cd Kenma
dotnet test Kenma.slnx                 # run the test suite
dotnet run --project Kenma             # run the app

# single-file, self-contained build
dotnet publish Kenma -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist
```

Live tests against the real `claude` CLI are opt-in: set `KENMA_INTEGRATION=1`.

### Project layout

```
Kenma/              WPF app
  Engines/          One class per engine, plus the fallback chain and catalog
  Enhancer.cs       The prompt-engineering system prompt
  PreCheck.cs       Offline clarity check (also drives the offline template)
  IconFactory.cs    The logo, drawn in code
Kenma.Tests/        xUnit tests (engines tested against a local mock server)
assets/             Logo SVGs and PNG
packaging/          winget and Scoop manifests
docs/               Signing and other docs
```

## Contributing

Bug reports, new engines and prompt-quality improvements are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) first. Changes to the system prompt should include before/after examples.

## License

[MIT](LICENSE) © Kenma contributors
