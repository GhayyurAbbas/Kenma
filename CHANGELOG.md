# Changelog

## 1.0.0

### Changed
- Renamed from Whetstone to **Kenma** (研磨, Japanese for "polishing"), because an existing Mac prompt-optimizer app already uses the Whetstone name. Settings, history, saved API keys and the startup entry migrate automatically.

### Added
- **Engines:** Anthropic API, OpenAI API, Google Gemini API, OpenRouter, Claude Code, Ollama, LM Studio, and an offline no-AI template.
- **Automatic mode and fallback chain.** Uses the first engine that's ready; a failing or slow engine falls back to the next. Results always say which engine answered and why earlier ones were skipped.
- **Offline support.** Cloud engines are skipped instantly when the PC is offline; local models and the template keep working.
- **Status indicator** under the title showing the engine that will be used right now.
- **Settings:** per-engine API key, model picker with *Load models*, server URL for local engines, and a *Test* button.
- Error cards offer **Retry** and **Use offline template**.
- History entries record which engine produced them.
- New logo, the **tag gem**: prompt-tag brackets `< >` closing into a diamond around a polished gem (structure + polish). Drawn in your Windows accent color; mono and color SVGs in `assets/`.

## 0.9.0

### Changed
- Renamed from promptuDX to **Whetstone**; settings and history migrate automatically.
- Windows 11 Fluent design that follows light/dark mode and the accent color.

### Fixed / hardened
- API keys moved from `settings.json` to Windows Credential Manager.
- Requests can be stopped, and time out instead of hanging.
- Configurable show/hide shortcut, with a clear message when another app already uses it.
- Window returns to the primary screen if its saved monitor is gone.
- Crash log and global error handling; the tray app keeps running after unexpected errors.
- Prompt history can be turned off and has a retention period.
- Claude Code runs isolated, and account details are kept out of results.
- Settings and history are written atomically; corrupt files are set aside instead of crashing.
