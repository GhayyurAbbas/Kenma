using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kenma.Engines
{
    /// <summary>Uses the user's Claude Code sign-in through the claude CLI. No API key needed.</summary>
    sealed class ClaudeCliEngine : IEngine
    {
        public string Id => EngineCatalog.ClaudeCli;
        public string Name => "Claude Code";
        public string Detail => "";
        public bool IsAi => true;

        public Task<string?> CheckAsync(CancellationToken ct) => Task.FromResult(
            !Connectivity.ClaudeCliInstalled ? "not installed" : !Connectivity.IsOnline ? "offline" : null);

        public async Task<string> EnhanceAsync(string raw, CancellationToken ct)
        {
            // Run from an empty sandbox with tools, MCP servers, slash commands, user settings/hooks and
            // session persistence switched off, and with our own system prompt replacing Claude Code's.
            Directory.CreateDirectory(AppPaths.CliSandboxDir);
            var promptFile = Path.Combine(AppPaths.CliSandboxDir, "system-prompt.txt");
            if (!File.Exists(promptFile) || File.ReadAllText(promptFile) != Enhancer.SystemPrompt)
                File.WriteAllText(promptFile, Enhancer.SystemPrompt);

            var psi = new ProcessStartInfo("cmd.exe",
                "/d /c claude -p --output-format text --system-prompt-file system-prompt.txt --tools \"\" " +
                "--strict-mcp-config --no-session-persistence --disable-slash-commands --setting-sources project")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppPaths.CliSandboxDir
            };

            using var p = Process.Start(psi) ?? throw new EnhanceException("Couldn't start the claude CLI.");
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            try
            {
                await p.StandardInput.WriteAsync(Enhancer.Wrap(raw));
                p.StandardInput.Close();
            }
            catch (IOException) { /* process exited early (e.g. CLI missing); handled below */ }

            try { await p.WaitForExitAsync(ct); }
            catch (OperationCanceledException)
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                throw;
            }

            var output = (await outTask).Trim();
            var err = (await errTask).Trim();
            if (p.ExitCode == 0 && output.Length > 0) return output;

            Log.Warn($"claude CLI exited with {p.ExitCode}: {err}");
            if (p.ExitCode == 9009 || err.Contains("is not recognized", StringComparison.OrdinalIgnoreCase))
                throw new EnhanceException("Claude Code isn't installed.");
            throw new EnhanceException(err.Length > 0 ? err : "The claude CLI returned nothing. Check that Claude Code is signed in.");
        }
    }
}
