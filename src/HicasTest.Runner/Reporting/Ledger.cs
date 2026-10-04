using System.Globalization;
using System.Text;
using HicasTest.Runner.Engine;

namespace HicasTest.Runner.Reporting;

/// <summary>
/// Accuracy-study ledger (docs/accuracy-study.md). The tool appends one row per case;
/// the human fills human_verdict, disagreement_cause, human_minutes, reviewer, notes.
/// </summary>
public static class Ledger
{
    public static readonly string[] Columns =
    {
        "timestamp", "ticket", "case", "check_kinds", "build", "tool_verdict", "runs_consistent", "tool_seconds", "report",
        "human_verdict", "disagreement_cause", "human_minutes", "reviewer", "notes",
    };

    public static void Append(string path, CaseResult result)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (dir != null)
            Directory.CreateDirectory(dir);

        var row = new[]
        {
            DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
            result.TestCase.Ticket ?? "",
            result.TestCase.Case ?? result.TestCase.Id,
            string.Join(";", result.TestCase.Expect.Select(e => e.Kind).Distinct()),
            result.Build,
            VerdictText.Of(result.Verdict),
            result.RunsConsistent,
            result.Runs.Sum(r => r.Duration.TotalSeconds).ToString("0.0", CultureInfo.InvariantCulture),
            result.ReportPath ?? "",
            "", "", "", "", "",
        };

        var sb = new StringBuilder();
        if (!File.Exists(path))
            sb.AppendLine(string.Join(",", Columns));
        sb.AppendLine(string.Join(",", row.Select(Escape)));
        File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    internal static string Escape(string value) =>
        value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\"";
}
