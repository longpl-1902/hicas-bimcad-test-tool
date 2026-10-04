using HicasTest.Protocol;
using HicasTest.Runner.Model;

namespace HicasTest.Runner.Engine;

/// <summary>
/// Tool verdicts. Deliberately not the plugin's statuses: the tool never says "Pass"
/// (docs/accuracy-study.md). A human still confirms every level-B case.
/// </summary>
public enum Verdict
{
    Match,
    Mismatch,
    NotRun,
    Error,
}

public static class VerdictText
{
    public static string Of(Verdict verdict) => verdict switch
    {
        Verdict.Match => "MATCH",
        Verdict.Mismatch => "MISMATCH",
        Verdict.NotRun => "NOT-RUN",
        _ => "ERROR",
    };

    /// <summary>Case verdict from its assertions: any mismatch wins, then error, then not-run.</summary>
    public static Verdict Combine(IEnumerable<Verdict> verdicts)
    {
        var list = verdicts.ToList();
        if (list.Count == 0) return Verdict.NotRun;
        if (list.Contains(Verdict.Mismatch)) return Verdict.Mismatch;
        if (list.Contains(Verdict.Error)) return Verdict.Error;
        if (list.Contains(Verdict.NotRun)) return Verdict.NotRun;
        return Verdict.Match;
    }
}

public sealed class AssertionResult
{
    public int Index { get; init; }
    public string Kind { get; init; } = "";
    public string Description { get; init; } = "";
    public Verdict Verdict { get; init; }
    public string Expected { get; init; } = "";
    public string Actual { get; init; } = "";
    public string? Source { get; init; }
}

public sealed class CaseRun
{
    public int RunIndex { get; init; }
    public Verdict Verdict { get; set; }
    public string? Error { get; set; }
    public string WorkDir { get; init; } = "";
    public DateTimeOffset StartedAt { get; init; }
    public TimeSpan Duration { get; set; }
    public BridgeInfo? Bridge { get; set; }
    public CommandResult? Command { get; set; }
    public ChangeSet? Changes { get; set; }
    public List<DialogEvent> Dialogs { get; } = new();
    public List<AssertionResult> Assertions { get; } = new();
    public string? ImagePath { get; set; }
    public List<string> Notes { get; } = new();
}

public sealed class CaseResult
{
    public required TestCase TestCase { get; init; }
    public required string Build { get; init; }
    public List<CaseRun> Runs { get; } = new();
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();

    /// <summary>Why the case could not start on this machine (host year not installed, bridge not built).</summary>
    public string? NotRunReason { get; init; }

    public Verdict Verdict => ValidationErrors.Count > 0 || NotRunReason != null
        ? Verdict.NotRun
        : VerdictText.Combine(Runs.Select(r => r.Verdict));

    /// <summary>"single", "yes" or "no" — whether repeated runs agreed.</summary>
    public string RunsConsistent => Runs.Count < 2 ? "single" : Runs.Select(r => r.Verdict).Distinct().Count() == 1 ? "yes" : "no";

    public string? ReportPath { get; set; }
}
