using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kenma
{
    public record CheckResult(int Score, string Kind, List<string> Good, List<string> Missing)
    {
        public bool HasContext { get; init; }
        public bool HasFormat { get; init; }
        public bool HasAudience { get; init; }
        public bool HasConstraints { get; init; }
        public bool HasTech { get; init; }
        public bool HasStyle { get; init; }
        public bool HasAspect { get; init; }
    }

    /// <summary>Instant, offline heuristic review of a raw prompt before it is sent for enhancement.</summary>
    static class PreCheck
    {
        static readonly string[] CodeWords = { "code", "function", "bug", "api", "script", "app", "website", "class", "error", "build", "implement", "deploy", "debug", "component", "endpoint", "database", "refactor", "test", "compile", "frontend", "backend" };
        static readonly string[] ImageWords = { "image", "picture", "photo", "logo", "illustration", "render", "midjourney", "dall-e", "art", "wallpaper", "drawing", "poster", "icon", "thumbnail", "portrait" };
        static readonly string[] WritingWords = { "write", "email", "essay", "blog", "article", "post", "letter", "story", "caption", "copy", "rewrite", "proposal", "report", "script", "speech", "bio" };
        static readonly string[] AnalysisWords = { "analyze", "analyse", "compare", "research", "summarize", "summarise", "explain", "evaluate", "review", "assess", "pros and cons" };

        static readonly string[] ContextWords = { "because", "so that", "in order to", "goal", "purpose", "for my", "for our", "i'm", "i am", "we are", "we're", "my team", "context", "background", "currently", "i have", "we have", "i want to", "i need" };
        static readonly string[] FormatWords = { "format", "table", "json", "list", "bullet", "bullets", "markdown", "steps", "outline", "paragraph", "paragraphs", "words", "sentences", "csv", "code block", "structure", "sections", "headings", "length", "short", "detailed", "summary" };
        static readonly string[] AudienceWords = { "audience", "beginner", "beginners", "expert", "experts", "readers", "users", "customers", "clients", "tone", "formal", "casual", "friendly", "professional", "kids", "children", "students", "team", "manager", "stakeholders", "non-technical" };
        static readonly string[] ConstraintWords = { "must", "should", "don't", "do not", "avoid", "only", "without", "limit", "max", "maximum", "minimum", "at least", "under", "within", "no more than", "requirements", "constraint", "deadline", "budget", "version" };
        static readonly string[] VagueWords = { "something", "stuff", "things", "nice", "good", "better", "etc", "whatever", "kind of", "sort of", "somehow", "some", "cool", "awesome" };

        static readonly string[] TechWords = { "python", "javascript", "typescript", "c#", "java", "go", "golang", "rust", "react", "angular", "vue", "node", ".net", "dotnet", "sql", "php", "kotlin", "swift", "html", "css", "flutter", "django", "next.js", "nextjs", "wpf", "spring", "laravel", "c++", "ruby", "rails", "powershell", "bash" };
        static readonly string[] StyleWords = { "realistic", "photorealistic", "anime", "cartoon", "watercolor", "3d", "cinematic", "minimalist", "pixel", "oil painting", "sketch", "flat", "vector", "studio ghibli", "cyberpunk", "lighting" };
        static readonly string[] AspectWords = { "16:9", "1:1", "9:16", "4:3", "3:2", "portrait", "landscape", "square", "vertical", "horizontal", "resolution", "4k" };

        static bool Has(string text, IEnumerable<string> words) =>
            words.Any(w => Regex.IsMatch(text, @"(?<![\w])" + Regex.Escape(w) + @"(?![\w])", RegexOptions.IgnoreCase));

        static string DetectKind(string t)
        {
            int Count(string[] ws) => ws.Count(w => Regex.IsMatch(t, @"(?<![\w])" + Regex.Escape(w) + @"(?![\w])", RegexOptions.IgnoreCase));
            var scores = new (string kind, int n)[]
            {
                ("Coding", Count(CodeWords) + Count(TechWords)),
                ("Image", Count(ImageWords) + Count(StyleWords)),
                ("Writing", Count(WritingWords)),
                ("Analysis", Count(AnalysisWords))
            };
            var best = scores.OrderByDescending(s => s.n).First();
            return best.n > 0 ? best.kind : "General";
        }

        public static CheckResult Analyze(string raw)
        {
            var t = raw.Trim();
            var good = new List<string>();
            var missing = new List<string>();
            int score = 0;
            int words = Regex.Matches(t, @"\S+").Count;
            var kind = DetectKind(t);

            if (words >= 15) { score += 20; good.Add("Enough detail to work with"); }
            else if (words >= 7) { score += 10; missing.Add("Quite short: add a bit more detail"); }
            else missing.Add("Very short: the model has to guess most of it");

            if (Has(t, ContextWords)) { score += 20; good.Add("Explains context / why"); }
            else missing.Add("No context: say why you need it or what it's for");

            if (Has(t, FormatWords)) { score += 20; good.Add("Defines the output shape"); }
            else missing.Add("No output format (list, table, length, sections…)");

            if (Has(t, AudienceWords)) { score += 15; good.Add("Mentions audience or tone"); }
            else missing.Add("No audience or tone specified");

            if (Has(t, ConstraintWords)) { score += 15; good.Add("Sets constraints"); }
            else missing.Add("No constraints (must / avoid / limits)");

            if (!Has(t, VagueWords)) score += 10;
            else missing.Add("Contains vague words (\"something\", \"good\", \"stuff\"…)");

            if (kind == "Coding" && !Has(t, TechWords)) missing.Add("Which language / framework / version?");
            if (kind == "Image")
            {
                if (!Has(t, StyleWords)) missing.Add("No visual style or lighting");
                if (!Has(t, AspectWords)) missing.Add("No aspect ratio / orientation");
            }

            return new CheckResult(Math.Clamp(score, 0, 100), kind, good, missing)
            {
                HasContext = Has(t, ContextWords),
                HasFormat = Has(t, FormatWords),
                HasAudience = Has(t, AudienceWords),
                HasConstraints = Has(t, ConstraintWords),
                HasTech = Has(t, TechWords),
                HasStyle = Has(t, StyleWords),
                HasAspect = Has(t, AspectWords)
            };
        }
    }
}
