using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HicasTest.Runner.Engine;

namespace HicasTest.Runner.Reporting;

/// <summary>Writes result.json and a human-readable report.md per case.</summary>
public static class ReportWriter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Write(CaseResult result, string outputDir)
    {
        var dir = Path.Combine(outputDir, string.Concat(result.TestCase.Id.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)));
        Directory.CreateDirectory(dir);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

        var json = Path.Combine(dir, $"result-{stamp}-{result.Build}.json");
        File.WriteAllText(json, JsonSerializer.Serialize(new
        {
            result.TestCase.Id,
            result.TestCase.Ticket,
            result.TestCase.Case,
            result.Build,
            Verdict = VerdictText.Of(result.Verdict),
            result.RunsConsistent,
            result.ValidationErrors,
            result.Runs,
        }, Json));

        var markdown = Path.Combine(dir, $"report-{stamp}-{result.Build}.md");
        File.WriteAllText(markdown, Markdown(result), new UTF8Encoding(false));
        result.ReportPath = markdown;
        return markdown;
    }

    private static string Markdown(CaseResult r)
    {
        var tc = r.TestCase;
        var sb = new StringBuilder();
        sb.AppendLine($"# {tc.Id} — tool verdict: {VerdictText.Of(r.Verdict)}");
        sb.AppendLine();
        sb.AppendLine("> Machine evidence only. This is **not** a Pass: the case stays `Chờ xác nhận — có bằng chứng máy`");
        sb.AppendLine("> until a human confirms it. During the accuracy study, run the manual script *before* reading this report.");
        sb.AppendLine();
        sb.AppendLine($"- Ticket / case: {tc.Ticket} / {tc.Case}");
        sb.AppendLine($"- Translated from: {tc.Source}");
        sb.AppendLine($"- Host: {tc.Host.App} {tc.Host.Version} · model: `{tc.Model}`");
        sb.AppendLine($"- Build: {r.Build} · runs: {r.Runs.Count} · consistent: {r.RunsConsistent}");
        sb.AppendLine();

        if (r.NotRunReason != null)
        {
            sb.AppendLine("## Not run on this machine");
            sb.AppendLine($"- {r.NotRunReason}");
            sb.AppendLine();
        }

        if (r.ValidationErrors.Count > 0)
        {
            sb.AppendLine("## Not run — invalid test case");
            foreach (var e in r.ValidationErrors)
                sb.AppendLine($"- {e}");
            sb.AppendLine();
        }

        foreach (var run in r.Runs)
        {
            sb.AppendLine($"## Run {run.RunIndex} — {VerdictText.Of(run.Verdict)} ({run.Duration.TotalSeconds:0.0}s)");
            sb.AppendLine();
            if (run.Error != null)
                sb.AppendLine($"**Error:** {run.Error}\n");
            if (run.Command != null)
                sb.AppendLine($"Command: `{run.Command.Status}` in {run.Command.DurationMs} ms. {run.Command.Message}\n");

            if (run.Assertions.Count > 0)
            {
                sb.AppendLine("| # | Check | Verdict | Expected | Actual | Source |");
                sb.AppendLine("|---|---|---|---|---|---|");
                foreach (var a in run.Assertions)
                    sb.AppendLine($"| {a.Index} | {Cell(a.Description)} | {VerdictText.Of(a.Verdict)} | {Cell(a.Expected)} | {Cell(a.Actual)} | {Cell(a.Source)} |");
                sb.AppendLine();
            }

            if (run.Calls.Count > 0)
            {
                sb.AppendLine("Calls:");
                foreach (var call in run.Calls)
                {
                    sb.AppendLine($"- **{call.Id}** `{call.Entry}` — {call.Status} in {call.DurationMs} ms; changes: {call.Added} added, {call.Modified} modified, {call.Deleted} deleted, {call.Warnings.Count} warnings");
                    if (call.Error != null)
                        sb.AppendLine($"  - error: {call.Error}");
                    foreach (var p in call.Prompts)
                        sb.AppendLine(p.Unused
                            ? $"  - scripted answer `{p.Id}` = {p.Answer} was never used"
                            : $"  - prompt `{p.Id}` ({p.Severity}) [{string.Join(" | ", p.Options)}] → {p.Answer}{(p.Unanswered ? " (default)" : "")}: {p.Message}");
                    foreach (var warning in call.Warnings)
                        sb.AppendLine($"  - host warning: {warning}");
                    if (!string.IsNullOrEmpty(call.Result))
                        sb.AppendLine($"  - result: `{call.Result.Replace("`", "'")}`");
                }
                sb.AppendLine();
            }

            if (run.Changes != null)
            {
                sb.AppendLine($"Changes: {run.Changes.Added.Count} added, {run.Changes.Modified.Count} modified, {run.Changes.Deleted.Count} deleted, {run.Changes.Warnings.Count} warnings.");
                foreach (var group in run.Changes.Added.GroupBy(a => a.Category ?? "?"))
                    sb.AppendLine($"- added {group.Key}: {group.Count()} (ids {string.Join(", ", group.Take(10).Select(x => x.Id))}{(group.Count() > 10 ? ", …" : "")})");
                foreach (var w in run.Changes.Warnings)
                    sb.AppendLine($"- warning: {w}");
                sb.AppendLine();
            }

            if (run.Dialogs.Count > 0)
            {
                sb.AppendLine("Dialogs:");
                foreach (var d in run.Dialogs)
                    sb.AppendLine($"- [{d.Source}] {d.DialogId} {d.Message} → {(d.Handled ? d.Answer : "NOT ANSWERED")}");
                sb.AppendLine();
            }

            foreach (var note in run.Notes)
                sb.AppendLine($"- note: {note}");
            if (run.ImagePath != null)
                sb.AppendLine($"\n![view]({new Uri(run.ImagePath).AbsoluteUri})");
            sb.AppendLine($"\nWork folder: `{run.WorkDir}`\n");
        }

        sb.AppendLine("## Test case as run (check the translation)");
        sb.AppendLine();
        sb.AppendLine("```yaml");
        sb.AppendLine(tc.RawYaml.TrimEnd());
        sb.AppendLine("```");
        return sb.ToString();
    }

    private static string Cell(string? text) => (text ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
