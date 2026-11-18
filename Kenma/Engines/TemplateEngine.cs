using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kenma.Engines
{
    /// <summary>
    /// Offline, no-AI fallback. Uses the local pre-check to wrap the prompt in a structure suited to its type,
    /// adding [PLACEHOLDERS] only for the parts the pre-check found missing. Always available, instant.
    /// </summary>
    sealed class TemplateEngine : IEngine
    {
        public string Id => EngineCatalog.Template;
        public string Name => "Offline template";
        public string Detail => "";
        public bool IsAi => false;

        public Task<string?> CheckAsync(CancellationToken ct) => Task.FromResult<string?>(null);

        public Task<string> EnhanceAsync(string raw, CancellationToken ct) => Task.FromResult(Build(raw));

        public static string Build(string raw)
        {
            var text = raw.Trim();
            var r = PreCheck.Analyze(text);

            if (r.Kind == "Image")
            {
                var parts = new List<string> { text.TrimEnd('.') };
                if (!r.HasStyle) parts.Add("[STYLE, e.g. photorealistic, watercolor, 3D render]");
                parts.Add("[LIGHTING]");
                parts.Add("[COMPOSITION / CAMERA ANGLE]");
                if (!r.HasAspect) parts.Add("aspect ratio [16:9]");
                return string.Join(", ", parts) + "\n\nAvoid: [ANYTHING TO EXCLUDE]";
            }

            var sb = new StringBuilder();
            Section(sb, "task", new[] { text });

            var context = new List<string>();
            if (!r.HasContext) context.Add("Purpose: [WHY YOU NEED THIS AND WHAT IT WILL BE USED FOR]");
            if (r.Kind == "Coding")
            {
                if (!r.HasTech) context.Add("Tech stack: [LANGUAGE / FRAMEWORK / VERSION]");
                context.Add("Relevant code or error output: [PASTE HERE]");
            }
            else if (!r.HasAudience) context.Add("Audience and tone: [WHO IT'S FOR AND HOW IT SHOULD SOUND]");
            Section(sb, "context", context, bullets: true);

            var requirements = r.Kind switch
            {
                "Coding" => new List<string>
                {
                    "Handle errors and edge cases explicitly.",
                    "Change only what this task needs; keep existing behaviour elsewhere.",
                    "If anything is ambiguous, ask before assuming."
                },
                "Writing" => new List<string> { "Be clear and concise; avoid filler and jargon." },
                "Analysis" => new List<string>
                {
                    "State your assumptions and the evidence behind each conclusion.",
                    "Point out uncertainty or missing data."
                },
                _ => new List<string> { "If anything is unclear, ask before assuming." }
            };
            if (r.Kind == "Writing" && !r.HasFormat) requirements.Add("Length: [TARGET LENGTH]");
            if (!r.HasConstraints) requirements.Add("Constraints: [MUST-HAVES AND THINGS TO AVOID]");
            Section(sb, "requirements", requirements, bullets: true);

            if (!r.HasFormat)
            {
                Section(sb, "output_format", new[]
                {
                    r.Kind switch
                    {
                        "Coding" => "Complete code in fenced blocks, followed by a short explanation of what changed and why.",
                        "Writing" => "[FORMAT, e.g. email, bullet list, sections with headings]",
                        "Analysis" => "A short summary first, then the details as [TABLE / BULLETS / SECTIONS].",
                        _ => "[THE SHAPE OF THE ANSWER: list, table, steps, length]"
                    }
                });
            }
            return sb.ToString().TrimEnd();
        }

        static void Section(StringBuilder sb, string tag, IReadOnlyList<string> lines, bool bullets = false)
        {
            if (lines.Count == 0) return;
            sb.Append('<').Append(tag).AppendLine(">");
            foreach (var line in lines) sb.AppendLine(bullets ? "- " + line : line);
            sb.Append("</").Append(tag).AppendLine(">").AppendLine();
        }
    }
}
