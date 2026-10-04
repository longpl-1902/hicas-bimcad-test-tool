using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace HicasTest.Runner.Model;

public static class TestCaseLoader
{
    private static readonly string[] Kinds = { "added", "modified", "deleted", "count", "param", "no-warnings", "command-status" };
    private static readonly string[] KindsNeedingSource = { "added", "modified", "deleted", "count", "param" };

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    /// <summary>Loads one YAML file or every *.yaml / *.yml under a folder.</summary>
    public static IReadOnlyList<TestCase> LoadAll(string path)
    {
        if (File.Exists(path))
            return new[] { Load(path) };
        if (!Directory.Exists(path))
            throw new FileNotFoundException("No test case file or folder at " + path);

        return Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(Load)
            .ToList();
    }

    public static TestCase Load(string file)
    {
        var raw = File.ReadAllText(file);
        var testCase = Yaml.Deserialize<TestCase>(raw) ?? new TestCase();
        testCase.FilePath = Path.GetFullPath(file);
        testCase.RawYaml = raw;

        var baseDir = Path.GetDirectoryName(testCase.FilePath)!;
        testCase.Model = Resolve(baseDir, testCase.Model)!;
        if (testCase.Addin != null)
        {
            testCase.Addin.Manifest = Resolve(baseDir, testCase.Addin.Manifest);
            testCase.Addin.Assembly = Resolve(baseDir, testCase.Addin.Assembly);
        }
        testCase.Run.Assembly = Resolve(baseDir, testCase.Run.Assembly);
        return testCase;
    }

    /// <summary>Returns problems that make the case unrunnable or untrustworthy. Empty = valid.</summary>
    public static IReadOnlyList<string> Validate(TestCase testCase)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(testCase.Id))
            errors.Add("id is required.");

        var app = testCase.Host.App.ToLowerInvariant();
        if (app != "revit" && app != "autocad")
            errors.Add("host.app must be 'revit' or 'autocad'.");
        else if (!Hosts.HostCatalog.IsSupported(app, testCase.Host.Version))
            errors.Add($"host.version {testCase.Host.Version} is not supported for {app}. Supported: {string.Join(", ", Hosts.HostCatalog.YearsOf(app))}.");

        if (string.IsNullOrWhiteSpace(testCase.Model) || !File.Exists(testCase.Model))
            errors.Add($"model not found: '{testCase.Model}'.");

        var mode = testCase.Run.Mode.ToLowerInvariant();
        if (mode == "invoke")
        {
            if (string.IsNullOrEmpty(testCase.Run.Assembly) || string.IsNullOrEmpty(testCase.Run.Type) || string.IsNullOrEmpty(testCase.Run.Method))
                errors.Add("run.mode invoke needs run.assembly, run.type and run.method.");
        }
        else if (app == "revit" && mode != "postcommand")
            errors.Add("Revit run.mode must be postcommand or invoke.");
        else if (app == "autocad" && mode != "commandline")
            errors.Add("AutoCAD run.mode must be commandline or invoke.");
        else if (string.IsNullOrWhiteSpace(testCase.Run.Command))
            errors.Add("run.command is required.");

        if (testCase.Expect.Count == 0)
            errors.Add("expect needs at least one expectation.");

        for (var i = 0; i < testCase.Expect.Count; i++)
        {
            var e = testCase.Expect[i];
            var where = $"expect[{i}] ({e.Kind})";
            if (!Kinds.Contains(e.Kind))
                errors.Add($"{where}: unknown kind. Use one of {string.Join(", ", Kinds)}.");
            if (KindsNeedingSource.Contains(e.Kind) && string.IsNullOrWhiteSpace(e.Source))
                errors.Add($"{where}: 'source' is required — expected values must cite an independent oracle.");
            if (e.Kind is "added" or "modified" or "deleted" or "count" && e.Count is null && e.Min is null && e.Max is null)
                errors.Add($"{where}: set count, or min/max.");
            if (e.Kind == "deleted" && !string.IsNullOrEmpty(e.Category))
                errors.Add($"{where}: deleted elements have no category; remove 'category'.");
            if (e.Kind == "param" && (string.IsNullOrWhiteSpace(e.Param) || (e.Expected is null && e.Min is null && e.Max is null)))
                errors.Add($"{where}: needs 'param' and 'equals' or min/max.");
            if (e.Scope is not ("all" or "changed"))
                errors.Add($"{where}: scope must be 'all' or 'changed'.");
        }

        return errors;
    }

    private static string? Resolve(string baseDir, string? path) =>
        string.IsNullOrWhiteSpace(path) ? path : Path.GetFullPath(Path.Combine(baseDir, path));
}
