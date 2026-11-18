using System;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Xunit;

namespace Kenma.Tests
{
    public class HotkeyTests
    {
        [Theory]
        [InlineData("Ctrl+Alt+P", ModifierKeys.Control | ModifierKeys.Alt, Key.P)]
        [InlineData("ctrl + shift + F9", ModifierKeys.Control | ModifierKeys.Shift, Key.F9)]
        [InlineData("Win+Space", ModifierKeys.Windows, Key.Space)]
        public void Parses_valid_shortcuts(string text, ModifierKeys mods, Key key)
        {
            Assert.True(Hotkey.TryParse(text, out var m, out var k));
            Assert.Equal(mods, m);
            Assert.Equal(key, k);
        }

        [Theory]
        [InlineData("")]
        [InlineData("P")]            // no modifier
        [InlineData("Ctrl+Alt")]     // no key
        [InlineData("Ctrl+P+Q")]     // two keys
        [InlineData("Ctrl+Nope")]    // unknown key
        public void Rejects_invalid_shortcuts(string text) => Assert.False(Hotkey.TryParse(text, out _, out _));

        [Fact]
        public void Format_round_trips()
        {
            var text = Hotkey.Format(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, Key.K);
            Assert.Equal("Ctrl+Alt+Shift+K", text);
            Assert.True(Hotkey.TryParse(text, out var m, out var k));
            Assert.Equal(Key.K, k);
            Assert.Equal(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, m);
        }
    }

    public class PreCheckTests
    {
        [Fact]
        public void Short_vague_prompt_scores_low_and_lists_gaps()
        {
            var r = PreCheck.Analyze("make something cool");
            Assert.True(r.Score < 40, $"score was {r.Score}");
            Assert.Contains(r.Missing, m => m.Contains("format", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(r.Missing, m => m.Contains("vague", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Detailed_prompt_scores_high()
        {
            var r = PreCheck.Analyze("Write a blog post for beginners because our customers keep asking how to set up backups. " +
                                     "Use a friendly tone, keep it under 800 words, format it with headings and a bullet list of steps, and avoid jargon.");
            Assert.True(r.Score >= 70, $"score was {r.Score}");
            Assert.Equal("Writing", r.Kind);
        }

        [Fact]
        public void Coding_prompt_without_stack_asks_for_language()
        {
            var r = PreCheck.Analyze("fix the login bug in my app, the api returns an error");
            Assert.Equal("Coding", r.Kind);
            Assert.Contains(r.Missing, m => m.Contains("language", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Image_prompt_asks_for_style_and_aspect_ratio()
        {
            var r = PreCheck.Analyze("a logo image of a fox");
            Assert.Equal("Image", r.Kind);
            Assert.Contains(r.Missing, m => m.Contains("aspect", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Score_is_always_within_bounds()
        {
            foreach (var p in new[] { "", " ", "x", new string('a', 5000), "must avoid only format table json audience because" })
            {
                var r = PreCheck.Analyze(p);
                Assert.InRange(r.Score, 0, 100);
            }
        }
    }

    public class HistoryPruneTests
    {
        static void Seed(params int[] ageDays)
        {
            HistoryStore.Items.Clear();
            foreach (var d in ageDays)
                HistoryStore.Items.Add(new HistoryEntry { Time = DateTime.Now.AddDays(-d), Raw = "r", Enhanced = "e" });
        }

        [Fact]
        public void Removes_entries_older_than_retention()
        {
            Seed(1, 10, 40, 100);
            Assert.True(HistoryStore.Prune(30));
            Assert.Equal(2, HistoryStore.Items.Count);
        }

        [Fact]
        public void Zero_retention_keeps_everything()
        {
            Seed(1, 400, 4000);
            Assert.False(HistoryStore.Prune(0));
            Assert.Equal(3, HistoryStore.Items.Count);
        }

        [Fact]
        public void Caps_item_count()
        {
            Seed(Enumerable.Repeat(0, 350).ToArray());
            HistoryStore.Prune(0);
            Assert.Equal(300, HistoryStore.Items.Count);
        }
    }

    public class SecretsTests
    {
        [Fact]
        public void Stores_reads_and_deletes_in_credential_manager()
        {
            var target = "Kenma:Test:" + Guid.NewGuid();
            try
            {
                Assert.Equal("", Secrets.Get(target));
                Assert.True(Secrets.Set(target, "  sk-test-123  "));
                Assert.Equal("sk-test-123", Secrets.Get(target));
                Assert.True(Secrets.Set(target, ""));
                Assert.Equal("", Secrets.Get(target));
            }
            finally { Secrets.Set(target, ""); }
        }
    }

    public class AtomicWriteTests
    {
        [Fact]
        public void Writes_and_overwrites_without_leaving_temp_files()
        {
            var dir = Path.Combine(Path.GetTempPath(), "kenma-tests-" + Guid.NewGuid());
            var file = Path.Combine(dir, "a.json");
            try
            {
                AppPaths.WriteAtomic(file, "one");
                AppPaths.WriteAtomic(file, "two");
                Assert.Equal("two", File.ReadAllText(file));
                Assert.Single(Directory.GetFiles(dir));
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }
    }
}
