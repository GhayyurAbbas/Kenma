using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kenma.Engines;
using Xunit;

namespace Kenma.Tests
{
    sealed class FakeEngine : IEngine
    {
        readonly Func<CancellationToken, Task<string>> _run;
        readonly string? _notReady;
        public int Calls;

        public FakeEngine(string name, Func<CancellationToken, Task<string>> run, string? notReady = null)
        {
            Name = name; _run = run; _notReady = notReady;
        }

        public string Id => Name;
        public string Name { get; }
        public string Detail => "";
        public bool IsAi => true;
        public Task<string?> CheckAsync(CancellationToken ct) => Task.FromResult(_notReady);
        public Task<string> EnhanceAsync(string raw, CancellationToken ct) { Calls++; return _run(ct); }

        public static FakeEngine Ok(string name, string text = "ok") => new(name, _ => Task.FromResult(text));
        public static FakeEngine Fails(string name, string msg) => new(name, _ => throw new EnhanceException(msg));
        public static FakeEngine Hangs(string name) => new(name, async ct => { await Task.Delay(Timeout.Infinite, ct); return ""; });
    }

    public class EngineChainTests
    {
        static readonly TimeSpan Timeout1s = TimeSpan.FromSeconds(1);

        [Fact]
        public async Task Uses_first_engine_when_it_works()
        {
            var a = FakeEngine.Ok("A", "from A");
            var b = FakeEngine.Ok("B");
            var r = await EngineChain.RunAsync(new[] { a, b }, "p", Timeout1s, CancellationToken.None);
            Assert.Equal("from A", r.Text);
            Assert.Same(a, r.Engine);
            Assert.Empty(r.Skipped);
            Assert.Equal(0, b.Calls);
        }

        [Fact]
        public async Task Skips_engines_that_are_not_ready_without_calling_them()
        {
            var a = new FakeEngine("A", _ => Task.FromResult("never"), notReady: "offline");
            var r = await EngineChain.RunAsync(new[] { a, FakeEngine.Ok("B", "from B") }, "p", Timeout1s, CancellationToken.None);
            Assert.Equal("from B", r.Text);
            Assert.Equal(0, a.Calls);
            Assert.Equal(new[] { "A: offline" }, r.Skipped);
        }

        [Fact]
        public async Task Falls_back_when_an_engine_errors()
        {
            var r = await EngineChain.RunAsync(new[] { FakeEngine.Fails("A", "rate limit"), FakeEngine.Ok("B", "from B") }, "p", Timeout1s, CancellationToken.None);
            Assert.Equal("from B", r.Text);
            Assert.Equal(new[] { "A: rate limit" }, r.Skipped);
        }

        [Fact]
        public async Task Falls_back_when_an_engine_times_out()
        {
            var r = await EngineChain.RunAsync(new[] { FakeEngine.Hangs("A"), FakeEngine.Ok("B", "from B") }, "p", Timeout1s, CancellationToken.None);
            Assert.Equal("from B", r.Text);
            Assert.Contains("no response", r.Skipped.Single());
        }

        [Fact]
        public async Task User_cancel_stops_the_whole_chain()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var b = FakeEngine.Ok("B");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                EngineChain.RunAsync(new[] { FakeEngine.Hangs("A"), b }, "p", TimeSpan.FromSeconds(30), cts.Token));
            Assert.Equal(0, b.Calls);
        }

        [Fact]
        public async Task Reports_every_reason_when_all_engines_fail()
        {
            var ex = await Assert.ThrowsAsync<EnhanceException>(() => EngineChain.RunAsync(
                new[] { FakeEngine.Fails("A", "no key"), FakeEngine.Fails("B", "not running") }, "p", Timeout1s, CancellationToken.None));
            Assert.Contains("A: no key", ex.Message);
            Assert.Contains("B: not running", ex.Message);
        }
    }

    public class EngineOrderTests
    {
        [Fact]
        public void Auto_without_keys_goes_cli_then_local_then_template()
        {
            var order = EngineChain.Order(new Settings(), _ => false);
            Assert.Equal(new[] { "claude-cli", "ollama", "lmstudio", "template" }, order);
        }

        [Fact]
        public void Auto_puts_cloud_engines_with_keys_first()
        {
            var order = EngineChain.Order(new Settings(), id => id is "openai" or "gemini");
            Assert.Equal(new[] { "openai", "gemini", "claude-cli", "ollama", "lmstudio", "template" }, order);
        }

        [Fact]
        public void Explicit_engine_respects_fallback_switches()
        {
            var s = new Settings { PrimaryEngine = "anthropic", FallbackClaudeCli = false, FallbackLocal = true, FallbackTemplate = false };
            Assert.Equal(new[] { "anthropic", "ollama", "lmstudio" }, EngineChain.Order(s, _ => true));
        }

        [Fact]
        public void Explicit_local_engine_is_not_duplicated()
        {
            var s = new Settings { PrimaryEngine = "ollama" };
            Assert.Equal(new[] { "ollama", "claude-cli", "lmstudio", "template" }, EngineChain.Order(s, _ => true));
        }

        [Fact]
        public void Every_catalog_engine_can_be_created()
        {
            foreach (var info in EngineCatalog.All)
                Assert.Equal(info.Id, EngineCatalog.Create(info.Id, new Settings(), keyOverride: "x").Id);
        }
    }

    public class TemplateEngineTests
    {
        [Fact]
        public void Keeps_the_prompt_and_adds_only_missing_parts()
        {
            var t = TemplateEngine.Build("fix the login bug in my python app");
            Assert.Contains("fix the login bug in my python app", t);
            Assert.Contains("<task>", t);
            Assert.DoesNotContain("[LANGUAGE", t);         // python already given
            Assert.Contains("Relevant code", t);
        }

        [Fact]
        public void Coding_without_stack_asks_for_it()
        {
            Assert.Contains("[LANGUAGE / FRAMEWORK / VERSION]", TemplateEngine.Build("fix the login bug in my app"));
        }

        [Fact]
        public void Image_prompts_become_a_descriptive_line()
        {
            var t = TemplateEngine.Build("a logo image of a fox");
            Assert.StartsWith("a logo image of a fox", t);
            Assert.Contains("aspect ratio", t);
            Assert.DoesNotContain("<task>", t);
        }

        [Fact]
        public void Detailed_prompt_gets_no_format_placeholder()
        {
            var t = TemplateEngine.Build("Write a blog post for beginners because customers ask about backups. Use a friendly tone, format it with headings, and avoid jargon.");
            Assert.DoesNotContain("<output_format>", t);
            Assert.DoesNotContain("Constraints:", t);
        }
    }

    /// <summary>Exercises the OpenAI-compatible engine (Ollama / LM Studio / OpenAI / OpenRouter) against a local mock server.</summary>
    public class OpenAiCompatibleEngineTests
    {
        sealed class MockServer : IDisposable
        {
            readonly HttpListener _listener = new();
            public readonly List<string> Bodies = new();
            public string BaseUrl { get; }
            public int ChatStatus = 200;

            public MockServer()
            {
                var port = FreePort();
                BaseUrl = $"http://localhost:{port}/v1";
                _listener.Prefixes.Add($"http://localhost:{port}/");
                _listener.Start();
                _ = Task.Run(Loop);
            }

            static int FreePort()
            {
                var l = new TcpListener(IPAddress.Loopback, 0);
                l.Start();
                int p = ((IPEndPoint)l.LocalEndpoint).Port;
                l.Stop();
                return p;
            }

            async Task Loop()
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _listener.GetContextAsync(); } catch { return; }
                    string reply; int status = 200;
                    if (ctx.Request.Url!.AbsolutePath.EndsWith("/models"))
                        reply = "{\"data\":[{\"id\":\"llama3.2\"},{\"id\":\"qwen2.5\"}]}";
                    else
                    {
                        using var r = new StreamReader(ctx.Request.InputStream);
                        var requestBody = await r.ReadToEndAsync();
                        lock (Bodies) Bodies.Add(requestBody);
                        status = ChatStatus;
                        reply = status == 200
                            ? "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"  Enhanced!  \"}}]}"
                            : "{\"error\":{\"message\":\"bad key\"}}";
                    }
                    var bytes = Encoding.UTF8.GetBytes(reply);
                    ctx.Response.StatusCode = status;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                    ctx.Response.Close();
                }
            }

            public void Dispose() { _listener.Stop(); _listener.Close(); }
        }

        [Fact]
        public async Task Local_engine_is_ready_and_uses_first_model_when_none_set()
        {
            using var server = new MockServer();
            LocalProbe.Invalidate();
            var engine = new OpenAiCompatibleEngine("ollama", "Ollama (local)", server.BaseUrl, "", "", local: true);

            Assert.Null(await engine.CheckAsync(CancellationToken.None));
            Assert.Equal("Enhanced!", await engine.EnhanceAsync("my prompt", CancellationToken.None));

            var body = server.Bodies.Single();
            Assert.Contains("\"model\":\"llama3.2\"", body);
            Assert.Contains("\"role\":\"system\"", body);
            Assert.Contains("my prompt", body);
        }

        [Fact]
        public async Task Lists_models()
        {
            using var server = new MockServer();
            var engine = new OpenAiCompatibleEngine("lmstudio", "LM Studio (local)", server.BaseUrl, "", "", local: true);
            Assert.Equal(new[] { "llama3.2", "qwen2.5" }, await engine.ListModelsAsync(CancellationToken.None));
        }

        [Fact]
        public async Task Maps_auth_errors_to_a_friendly_message()
        {
            using var server = new MockServer { ChatStatus = 401 };
            var engine = new OpenAiCompatibleEngine("openai", "OpenAI API", server.BaseUrl, "sk-wrong", "gpt-x", local: false);
            var ex = await Assert.ThrowsAsync<EnhanceException>(() => engine.EnhanceAsync("p", CancellationToken.None));
            Assert.Contains("rejected the API key", ex.Message);
        }

        [Fact]
        public async Task Local_engine_reports_not_running_when_server_is_down()
        {
            LocalProbe.Invalidate();
            var engine = new OpenAiCompatibleEngine("ollama", "Ollama (local)", "http://localhost:1/v1", "", "", local: true);
            Assert.Equal("not running", await engine.CheckAsync(CancellationToken.None));
        }

        [Fact]
        public async Task Cloud_engine_without_key_is_not_ready()
        {
            var engine = new OpenAiCompatibleEngine("openai", "OpenAI API", "https://example.invalid/v1", "", "gpt-x", local: false);
            Assert.Equal("no API key", await engine.CheckAsync(CancellationToken.None));
        }
    }
}
