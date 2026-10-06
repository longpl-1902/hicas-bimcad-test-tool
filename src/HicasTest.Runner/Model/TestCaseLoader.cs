using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace HicasTest.Runner.Model;

public static class TestCaseLoader
{
    private static readonly string[] Kinds = { "added", "modified", "deleted", "count", "param", "no-warnings", "command-status" };
    private static readonly string[] KindsNeedingSource = { "added", "modified", "deleted", "count", "param" };

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new JsonNodeYamlConverter())
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
        foreach (var call in testCase.Calls)
            call.ArgumentFilePath = Resolve(baseDir, call.ArgumentFile);
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
        else if (mode == "entries")
        {
            if (string.IsNullOrEmpty(testCase.Run.Assembly))
                errors.Add("run.mode entries needs run.assembly (the add-in's test assembly, <Addin>.Testing.dll).");
            if (testCase.Calls.Count == 0)
                errors.Add("run.mode entries needs calls.");
        }
        else if (app == "revit" && mode != "postcommand")
            errors.Add("Revit run.mode must be postcommand, invoke or entries.");
        else if (app == "autocad" && mode != "commandline")
            errors.Add("AutoCAD run.mode must be commandline, invoke or entries.");
        else if (string.IsNullOrWhiteSpace(testCase.Run.Command))
            errors.Add("run.command is required.");

        if (mode != "entries" && testCase.Calls.Count > 0)
            errors.Add("calls only work with run.mode entries.");

        var anyCheck = testCase.Expect.Count > 0
                       || testCase.Calls.Any(c => c.Expect.Count + c.ExpectResult.Count + c.ExpectPrompts.Count > 0);
        if (!anyCheck)
            errors.Add("expect needs at least one expectation (for entries: in expect or in a call).");

        ValidateExpectations(testCase.Expect, "expect", errors);
        ValidateCalls(testCase.Calls, errors);
        return errors;
    }

    private static void ValidateExpectations(IReadOnlyList<Expectation> expectations, string prefix, List<string> errors)
    {
        for (var i = 0; i < expectations.Count; i++)
        {
            var e = expectations[i];
            var where = $"{prefix}[{i}] ({e.Kind})";
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
    }

    private static void ValidateCalls(IReadOnlyList<CallSpec> calls, List<string> errors)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < calls.Count; i++)
        {
            var c = calls[i];
            var where = $"calls[{i}] ({(string.IsNullOrWhiteSpace(c.Id) ? c.Entry : c.Id)})";
            if (!string.IsNullOrWhiteSpace(c.Id) && !ids.Add(c.Id))
                errors.Add($"{where}: id is used twice.");
            if (string.IsNullOrWhiteSpace(c.Entry))
                errors.Add($"{where}: entry is required.");
            if (c.Argument is not null && c.ArgumentFile is not null)
                errors.Add($"{where}: use argument or argumentFile, not both.");
            if (c.ArgumentFilePath is not null && !File.Exists(c.ArgumentFilePath))
                errors.Add($"{where}: argumentFile not found: '{c.ArgumentFilePath}'.");
            if (c.TimeoutSec is <= 0)
                errors.Add($"{where}: timeoutSec must be positive.");
            foreach (var a in c.Answers.Where(a => string.IsNullOrWhiteSpace(a.Id) || string.IsNullOrWhiteSpace(a.Option)))
                errors.Add($"{where}: every answer needs id and option (got '{a.Id}' / '{a.Option}').");

            for (var r = 0; r < c.ExpectResult.Count; r++)
            {
                var check = c.ExpectResult[r];
                var at = $"{where} expectResult[{r}] ({check.Path})";
                if (string.IsNullOrWhiteSpace(check.Path))
                    errors.Add($"{at}: path is required.");
                var operators = (check.Expected is not null ? 1 : 0) + (check.Min is not null || check.Max is not null ? 1 : 0)
                                + (check.Exists is not null ? 1 : 0) + (check.Count is not null ? 1 : 0) + (check.Contains is not null ? 1 : 0);
                if (operators != 1)
                    errors.Add($"{at}: set exactly one of equals, min/max, exists, count, contains.");
                if (string.IsNullOrWhiteSpace(check.Source))
                    errors.Add($"{at}: 'source' is required — expected values must cite an independent oracle.");
            }

            for (var p = 0; p < c.ExpectPrompts.Count; p++)
            {
                var check = c.ExpectPrompts[p];
                var at = $"{where} expectPrompts[{p}] ({check.Id})";
                if (string.IsNullOrWhiteSpace(check.Id))
                    errors.Add($"{at}: id is required.");
                if (!check.Raised && (check.Severity is not null || check.Answer is not null))
                    errors.Add($"{at}: severity and answer only apply to a prompt that is raised.");
                if (string.IsNullOrWhiteSpace(check.Source))
                    errors.Add($"{at}: 'source' is required — expected prompts must cite an independent oracle.");
            }

            ValidateExpectations(c.Expect, where + " expect", errors);
        }
    }

    private static string? Resolve(string baseDir, string? path) =>
        string.IsNullOrWhiteSpace(path) ? path : Path.GetFullPath(Path.Combine(baseDir, path));
}
