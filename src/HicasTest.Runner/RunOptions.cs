namespace HicasTest.Runner;

public sealed class RunOptions
{
    /// <summary>Root of built bridges: &lt;dir&gt;/revit/&lt;year&gt;/… and &lt;dir&gt;/autocad/&lt;year&gt;/… (see build.ps1).</summary>
    public string BridgesDir { get; set; } = DefaultBridgesDir();

    public string OutputDir { get; set; } = MachineConfig.Current.OutputDir ?? Path.Combine(Environment.CurrentDirectory, "out");

    /// <summary>Ledger CSV to append to (docs/accuracy-study.md). Null = no ledger.</summary>
    public string? LedgerPath { get; set; }

    /// <summary>Fresh-process runs per case; 2 detects flaky cases.</summary>
    public int Repeat { get; set; } = 1;

    /// <summary>"normal" or "mutant" (deliberately broken build for the accuracy study).</summary>
    public string Build { get; set; } = "normal";

    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Runs every case on this host year instead of the one in its YAML (same case, other version).</summary>
    public int? HostVersionOverride { get; set; }

    public string RevitBridgeDll(int year) => Path.Combine(BridgesDir, "revit", year.ToString(), "HicasTest.Bridge.Revit.dll");

    public string AutoCadBridgeDll(int year) => Path.Combine(BridgesDir, "autocad", year.ToString(), "HicasTest.Bridge.AutoCAD.dll");

    /// <summary>
    /// HICASTEST_BRIDGES → machine config → "bridges" next to the exe (installed package) →
    /// artifacts/bridges in the current folder (developer checkout).
    /// </summary>
    private static string DefaultBridgesDir()
    {
        var env = Environment.GetEnvironmentVariable("HICASTEST_BRIDGES");
        if (!string.IsNullOrWhiteSpace(env))
            return env;
        if (!string.IsNullOrWhiteSpace(MachineConfig.Current.BridgesDir))
            return MachineConfig.Current.BridgesDir!;

        // Package layout: <root>\hicastest.exe, <root>\bridges\, <root>\mcp\hicastest-mcp.exe.
        foreach (var candidate in new[] { Path.Combine(AppContext.BaseDirectory, "bridges"), Path.Combine(AppContext.BaseDirectory, "..", "bridges") })
        {
            if (Directory.Exists(candidate))
                return Path.GetFullPath(candidate);
        }
        return Path.Combine(Environment.CurrentDirectory, "artifacts", "bridges");
    }
}
