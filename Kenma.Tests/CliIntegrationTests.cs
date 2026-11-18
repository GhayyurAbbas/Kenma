using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kenma.Engines;
using Xunit;

namespace Kenma.Tests
{
    /// <summary>
    /// Calls the real claude CLI. Opt-in: set KENMA_INTEGRATION=1 (requires Claude Code installed and signed in).
    /// </summary>
    public class CliIntegrationTests
    {
        static bool Enabled => Environment.GetEnvironmentVariable("KENMA_INTEGRATION") == "1";

        [Fact]
        public async Task Cancel_stops_the_cli_quickly()
        {
            if (!Enabled) return;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var sw = Stopwatch.StartNew();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new ClaudeCliEngine().EnhanceAsync("write a detailed project plan for a mobile banking app", cts.Token));
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(8), $"took {sw.Elapsed}");
        }

        [Fact]
        public async Task Does_not_inject_account_details()
        {
            if (!Enabled) return;
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var result = await new ClaudeCliEngine().EnhanceAsync("write an email to my boss asking for 2 days leave, sign it with my name", cts.Token);
            Assert.DoesNotMatch(new Regex(@"[\w.+-]+@[\w-]+\.[\w.]+"), result); // no email address leaked
            Assert.Contains("[", result);                                       // name left as a placeholder
        }
    }
}
