using HicasTest.Runner;
using HicasTest.Runner.Bridge;
using HicasTest.Runner.Engine;
using HicasTest.Runner.Hosts;
using HicasTest.Runner.Model;

const string Usage = """
    hicastest run <case.yaml|folder> [--host-version <year>] [--out <dir>] [--ledger <csv>] [--repeat <n>]
                                     [--build normal|mutant] [--bridges <dir>]
    hicastest validate <case.yaml|folder>
    hicastest hosts [--bridges <dir>]      supported Revit/AutoCAD years: installed? bridge built?
    hicastest bridges                      running bridges

    --host-version runs the cases on that year instead of host.version in the YAML.

    Exit codes: 0 all cases MATCH · 1 any MISMATCH / NOT-RUN / ERROR · 2 usage or invalid case.
    Tool verdicts are machine evidence, never a Pass (docs/accuracy-study.md).
    """;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine(Usage);
    return args.Length == 0 ? 2 : 0;
}

try
{
    switch (args[0])
    {
        case "run":
            return await Run(args.Skip(1).ToArray());
        case "validate":
            return Validate(args.Skip(1).ToArray());
        case "hosts":
            return Hosts(args.Skip(1).ToArray());
        case "bridges":
            foreach (var b in BridgeDiscovery.List())
                Console.WriteLine($"{b.Pid}\t{b.Host} {b.HostVersion}\t{b.PipeName}\tbridge {b.BridgeVersion}");
            return 0;
        default:
            Console.Error.WriteLine($"Unknown command '{args[0]}'.\n\n{Usage}");
            return 2;
    }
}
catch (Exception ex) when (ex is ArgumentException or FileNotFoundException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

static async Task<int> Run(string[] a)
{
    if (a.Length == 0)
        throw new ArgumentException("run needs a case file or folder.");

    var options = new RunOptions();
    for (var i = 1; i < a.Length; i++)
    {
        var value = i + 1 < a.Length ? a[i + 1] : throw new ArgumentException($"{a[i]} needs a value.");
        switch (a[i])
        {
            case "--out": options.OutputDir = Path.GetFullPath(value); break;
            case "--ledger": options.LedgerPath = Path.GetFullPath(value); break;
            case "--repeat": options.Repeat = int.Parse(value); break;
            case "--build":
                options.Build = value is "normal" or "mutant" ? value : throw new ArgumentException("--build must be normal or mutant.");
                break;
            case "--bridges": options.BridgesDir = Path.GetFullPath(value); break;
            case "--host-version": options.HostVersionOverride = int.Parse(value); break;
            default: throw new ArgumentException($"Unknown option {a[i]}.");
        }
        i++;
    }

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    var results = await TestSuite.RunAsync(a[0], options, Console.WriteLine, cts.Token);
    if (results.Any(r => r.ValidationErrors.Count > 0))
    {
        foreach (var r in results.Where(r => r.ValidationErrors.Count > 0))
            Console.Error.WriteLine($"{r.TestCase.Id}: {string.Join(" | ", r.ValidationErrors)}");
        return 2;
    }
    return results.All(r => r.Verdict == Verdict.Match) ? 0 : 1;
}

static int Hosts(string[] a)
{
    var options = new RunOptions();
    if (a.Length == 2 && a[0] == "--bridges")
        options.BridgesDir = Path.GetFullPath(a[1]);

    Console.WriteLine($"{"host",-8} {"year",-5} {"runtime",-19} {"installed",-10} {"bridge",-7} ready");
    foreach (var h in HostCatalog.All(options))
        Console.WriteLine($"{h.App,-8} {h.Year,-5} {h.Runtime,-19} {(h.Installed ? "yes" : "-"),-10} {(h.BridgeBuilt ? "built" : "-"),-7} {(h.Ready ? "yes" : "")}");
    return 0;
}

static int Validate(string[] a)
{
    if (a.Length == 0)
        throw new ArgumentException("validate needs a case file or folder.");

    var invalid = 0;
    foreach (var testCase in TestCaseLoader.LoadAll(a[0]))
    {
        var errors = TestCaseLoader.Validate(testCase);
        Console.WriteLine(errors.Count == 0 ? $"ok      {testCase.Id}" : $"invalid {testCase.Id}");
        foreach (var e in errors)
            Console.WriteLine($"        - {e}");
        invalid += errors.Count > 0 ? 1 : 0;
    }
    return invalid == 0 ? 0 : 2;
}
