namespace HicasTest.Runner.Hosts;

public sealed record HostStatus(string App, int Year, string Runtime, string ExePath, bool Installed, string BridgeDll, bool BridgeBuilt)
{
    public bool Ready => Installed && BridgeBuilt;
}

/// <summary>Supported host years. Keep in sync with build\HostVersions.props and build.ps1.</summary>
public static class HostCatalog
{
    public static readonly int[] RevitYears = { 2021, 2022, 2023, 2024, 2025, 2026, 2027 };
    public static readonly int[] AutoCadYears = { 2021, 2022, 2023, 2024, 2025, 2026, 2027 };

    public static bool IsSupported(string app, int year) => YearsOf(app).Contains(year);

    public static IReadOnlyList<int> YearsOf(string app) => Normalize(app) switch
    {
        "revit" => RevitYears,
        "autocad" => AutoCadYears,
        _ => Array.Empty<int>(),
    };

    public static string Runtime(int year) => year <= 2024 ? ".NET Framework 4.8" : year <= 2026 ? ".NET 8" : ".NET 10";

    /// <summary>Machine config (config.json) first, then the default Autodesk install folder.</summary>
    public static string DefaultExe(string app, int year) =>
        MachineConfig.Current.ExeFor(app, year) ?? (Normalize(app) == "revit"
            ? $@"C:\Program Files\Autodesk\Revit {year}\Revit.exe"
            : $@"C:\Program Files\Autodesk\AutoCAD {year}\acad.exe");

    public static HostStatus Status(string app, int year, RunOptions options, string? exeOverride = null)
    {
        var exe = exeOverride ?? DefaultExe(app, year);
        var bridge = Normalize(app) == "revit" ? options.RevitBridgeDll(year) : options.AutoCadBridgeDll(year);
        return new HostStatus(Normalize(app), year, Runtime(year), exe, File.Exists(exe), bridge, File.Exists(bridge));
    }

    public static IEnumerable<HostStatus> All(RunOptions options) =>
        RevitYears.Select(y => Status("revit", y, options))
            .Concat(AutoCadYears.Select(y => Status("autocad", y, options)));

    private static string Normalize(string app) => app.Trim().ToLowerInvariant();
}
