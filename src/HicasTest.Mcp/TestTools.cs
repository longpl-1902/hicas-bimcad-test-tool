using System.ComponentModel;
using System.Text;
using HicasTest.Protocol;
using HicasTest.Runner;
using HicasTest.Runner.Bridge;
using HicasTest.Runner.Engine;
using HicasTest.Runner.Hosts;
using HicasTest.Runner.Model;
using ModelContextProtocol.Server;

namespace HicasTest.Mcp;

[McpServerToolType]
public static class TestTools
{
    [McpServerTool(Name = "validate_test_case"), Description("Validate a level-B test case YAML (or every YAML in a folder) without starting Revit/AutoCAD.")]
    public static string ValidateTestCase([Description("Path to a .yaml file or a folder.")] string path)
    {
        var sb = new StringBuilder();
        foreach (var testCase in TestCaseLoader.LoadAll(path))
        {
            var errors = TestCaseLoader.Validate(testCase);
            sb.AppendLine(errors.Count == 0 ? $"ok {testCase.Id}" : $"invalid {testCase.Id}: {string.Join(" | ", errors)}");
        }
        return sb.ToString();
    }

    [McpServerTool(Name = "run_test_case"), Description(
        "Run level-B test case(s) in a fresh Revit/AutoCAD process and write report + ledger row. " +
        "Verdicts are MATCH / MISMATCH / NOT-RUN / ERROR: machine evidence only, never a Pass. " +
        "The case stays 'Chờ xác nhận — có bằng chứng máy' until a human confirms. Only use test/golden fixtures.")]
    public static async Task<string> RunTestCase(
        [Description("Path to a .yaml file or a folder.")] string path,
        [Description("Output folder for reports, e.g. .harness/features/<id>/evidence/T<n>/host.")] string outputDir,
        [Description("Ledger CSV to append to, e.g. .harness/features/<id>/b-auto-ledger.csv. Empty = none.")] string? ledger = null,
        [Description("Fresh-process runs per case; 2 detects flaky cases.")] int repeat = 2,
        [Description("normal, or mutant for a deliberately broken build.")] string build = "normal",
        [Description("Bridges folder; default HICASTEST_BRIDGES or ./artifacts/bridges.")] string? bridgesDir = null,
        [Description("Run on this Revit/AutoCAD year instead of host.version in the YAML (see list_hosts). 0 = use the YAML.")] int hostVersion = 0,
        CancellationToken ct = default)
    {
        var options = new RunOptions
        {
            HostVersionOverride = hostVersion > 0 ? hostVersion : null,
            OutputDir = Path.GetFullPath(outputDir),
            LedgerPath = string.IsNullOrWhiteSpace(ledger) ? null : Path.GetFullPath(ledger),
            Repeat = Math.Clamp(repeat, 1, 5),
            Build = build == "mutant" ? "mutant" : "normal",
        };
        if (!string.IsNullOrWhiteSpace(bridgesDir))
            options.BridgesDir = Path.GetFullPath(bridgesDir);

        var log = new StringBuilder();
        var results = await TestSuite.RunAsync(path, options, line => log.AppendLine(line), ct);
        log.AppendLine();
        foreach (var r in results)
            log.AppendLine($"{r.TestCase.Id}: {VerdictText.Of(r.Verdict)} (consistent: {r.RunsConsistent}) report: {r.ReportPath}"
                           + (r.ValidationErrors.Count > 0 ? " invalid: " + string.Join(" | ", r.ValidationErrors) : ""));
        return log.ToString();
    }

    [McpServerTool(Name = "list_hosts"), Description("Supported Revit/AutoCAD years with runtime, whether each is installed on this machine and whether its bridge is built.")]
    public static string ListHosts([Description("Bridges folder; default HICASTEST_BRIDGES or ./artifacts/bridges.")] string? bridgesDir = null)
    {
        var options = new RunOptions();
        if (!string.IsNullOrWhiteSpace(bridgesDir))
            options.BridgesDir = Path.GetFullPath(bridgesDir);
        return string.Join("\n", HostCatalog.All(options).Select(h =>
            $"{h.App} {h.Year} ({h.Runtime}): installed={(h.Installed ? "yes" : "no")}, bridge={(h.BridgeBuilt ? "built" : "missing")}{(h.Ready ? " → ready" : "")}"));
    }

    [McpServerTool(Name = "list_bridges"), Description("List running Revit/AutoCAD processes that have the HicasTest bridge loaded.")]
    public static string ListBridges()
    {
        var bridges = BridgeDiscovery.List();
        return bridges.Count == 0
            ? "No bridge running."
            : string.Join("\n", bridges.Select(b => $"pid {b.Pid}: {b.Host} {b.HostVersion} (pipe {b.PipeName})"));
    }

    [McpServerTool(Name = "query_elements"), Description(
        "Read-only: list elements of a category with parameter values from a running bridge (for writing or debugging test cases). " +
        "Revit category = BuiltInCategory name (OST_Walls); AutoCAD = DXF name (LINE) or *.")]
    public static async Task<string> QueryElements(
        int pid,
        string category,
        [Description("Comma-separated parameter names to read.")] string parameters = "",
        [Description("Unit for numeric Revit values (mm, m, ft, deg…). Empty = internal.")] string? unit = null,
        int limit = 50,
        CancellationToken ct = default)
    {
        var info = BridgeDiscovery.List().FirstOrDefault(b => b.Pid == pid)
                   ?? throw new ArgumentException($"No bridge for pid {pid}. Use list_bridges.");
        using var client = await BridgeClient.ConnectAsync(info.PipeName, TimeSpan.FromSeconds(10), ct);

        var request = new QueryRequest { Category = category, Limit = limit };
        foreach (var name in parameters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            request.Read.Add(new ParamRead { Name = name, Unit = string.IsNullOrWhiteSpace(unit) ? null : unit });

        var result = await client.CallAsync<QueryResult>(Methods.Query, request, ct);
        var sb = new StringBuilder($"{result.Elements.Count} element(s){(result.Truncated ? " (truncated)" : "")}\n");
        foreach (var e in result.Elements)
            sb.AppendLine($"{e.Id} {e.Category} '{e.Name}': " + string.Join(", ",
                e.Values.Select(v => $"{v.Name}={(v.Found ? v.Number?.ToString() ?? v.Text : "<missing>")}{(v.Display != null ? $" [{v.Display}]" : "")}")));
        return sb.ToString();
    }
}
