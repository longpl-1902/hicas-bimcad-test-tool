using System.Text.Json;

namespace HicasTest.Runner;

/// <summary>
/// Per-machine settings, so the tool works on PCs where hosts are not in the default folders.
/// Looked up in order: %HICASTEST_CONFIG%, %LOCALAPPDATA%\HicasTest\config.json, hicastest.json next to the exe.
/// Every field is optional.
/// </summary>
/// <example>
/// {
///   "bridgesDir": "C:/Tools/HicasTest/bridges",
///   "outputDir": "D:/qa-evidence",
///   "revitLanguage": "ENU",
///   "revit":   { "2024": "D:/Autodesk/Revit 2024/Revit.exe" },
///   "autocad": { "2025": "D:/Autodesk/AutoCAD 2025/acad.exe" }
/// }
/// </example>
public sealed class MachineConfig
{
    public string? BridgesDir { get; set; }
    public string? OutputDir { get; set; }

    /// <summary>Revit UI language passed as /language. Empty = Revit's installed default.</summary>
    public string? RevitLanguage { get; set; } = "ENU";

    public Dictionary<string, string> Revit { get; set; } = new();
    public Dictionary<string, string> Autocad { get; set; } = new();

    public string? SourcePath { get; private set; }

    private static readonly Lazy<MachineConfig> Instance = new(Load);

    public static MachineConfig Current => Instance.Value;

    public static IEnumerable<string> Candidates()
    {
        var env = Environment.GetEnvironmentVariable("HICASTEST_CONFIG");
        if (!string.IsNullOrWhiteSpace(env))
            yield return env;
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HicasTest", "config.json");
        yield return Path.Combine(AppContext.BaseDirectory, "hicastest.json");
    }

    public string? ExeFor(string app, int year)
    {
        var map = app.Equals("revit", StringComparison.OrdinalIgnoreCase) ? Revit : Autocad;
        return map.TryGetValue(year.ToString(), out var path) && !string.IsNullOrWhiteSpace(path) ? path : null;
    }

    private static MachineConfig Load()
    {
        var file = Candidates().FirstOrDefault(File.Exists);
        if (file == null)
            return new MachineConfig();

        var config = JsonSerializer.Deserialize<MachineConfig>(File.ReadAllText(file), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        }) ?? new MachineConfig();
        config.SourcePath = file;
        return config;
    }
}
