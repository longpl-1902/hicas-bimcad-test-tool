using HicasTest.Runner.Engine;
using HicasTest.Runner.Model;
using HicasTest.Runner.Reporting;

namespace HicasTest.Runner;

/// <summary>Entry point shared by the CLI and the MCP server: load → run → report → ledger.</summary>
public static class TestSuite
{
    public static async Task<IReadOnlyList<CaseResult>> RunAsync(string path, RunOptions options, Action<string>? log, CancellationToken ct)
    {
        var cases = TestCaseLoader.LoadAll(path);
        var runner = new TestRunner(options);
        var results = new List<CaseResult>();

        foreach (var testCase in cases)
        {
            log?.Invoke($"▶ {testCase.Id} ({testCase.Host.App} {testCase.Host.Version}, build {options.Build}, x{options.Repeat})");
            var result = await runner.RunAsync(testCase, ct);
            var report = ReportWriter.Write(result, options.OutputDir);
            if (options.LedgerPath != null)
                Ledger.Append(options.LedgerPath, result);
            log?.Invoke($"  {VerdictText.Of(result.Verdict)} · consistent: {result.RunsConsistent} · {report}");
            results.Add(result);
        }

        return results;
    }
}
