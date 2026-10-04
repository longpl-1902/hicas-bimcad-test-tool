using System.Globalization;
using HicasTest.Bridge.Core;
using HicasTest.Protocol;
using HicasTest.Runner.Model;

namespace HicasTest.Runner.Engine;

/// <summary>Compares what the host reported with the case's expectations. No host access except through <paramref name="query"/>.</summary>
public static class AssertionEngine
{
    private const double DefaultTolerance = 1e-9;
    private const int MaxListedFailures = 5;

    public static async Task<AssertionResult> EvaluateAsync(
        int index,
        Expectation expectation,
        ChangeSet changes,
        CommandResult command,
        Func<QueryRequest, Task<QueryResult>> query)
    {
        try
        {
            return expectation.Kind switch
            {
                "added" => CountCheck(index, expectation, Filter(changes.Added, expectation.Category).Count, "added elements"),
                "modified" => CountCheck(index, expectation, Filter(changes.Modified, expectation.Category).Count, "modified elements"),
                "deleted" => CountCheck(index, expectation, changes.Deleted.Count, "deleted elements"),
                "count" => CountCheck(index, expectation, (await query(ToQuery(expectation, changes, readParam: false))).Elements.Count, "matching elements in model"),
                "param" => ParamCheck(index, expectation, await query(ToQuery(expectation, changes, readParam: true))),
                "no-warnings" => Result(index, expectation, changes.Warnings.Count == 0, "no warnings",
                    changes.Warnings.Count == 0 ? "none" : string.Join(" | ", changes.Warnings)),
                "command-status" => StatusCheck(index, expectation, command),
                _ => Result(index, expectation, Verdict.NotRun, "", "unknown kind"),
            };
        }
        catch (Exception ex)
        {
            return Result(index, expectation, Verdict.Error, Describe(expectation), ex.Message);
        }
    }

    private static List<ElementRef> Filter(List<ElementRef> elements, string? category) =>
        string.IsNullOrEmpty(category)
            ? elements
            : elements.Where(e => string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase)).ToList();

    private static AssertionResult CountCheck(int index, Expectation e, int actual, string what)
    {
        bool ok;
        string expected;
        if (e.Count is { } exact)
        {
            ok = actual == exact;
            expected = $"{exact} {what}";
        }
        else
        {
            ok = (e.Min is null || actual >= e.Min) && (e.Max is null || actual <= e.Max);
            expected = $"{Range(e.Min, e.Max)} {what}";
        }

        if (!string.IsNullOrEmpty(e.Category))
            expected += $" ({e.Category})";
        return Result(index, e, ok, expected, actual.ToString(CultureInfo.InvariantCulture));
    }

    private static AssertionResult ParamCheck(int index, Expectation e, QueryResult result)
    {
        var expected = e.Expected is not null
            ? $"{e.Param} = {e.Expected}{Suffix(e.Unit)}" + (e.Tolerance is { } t ? $" ± {t}" : "")
            : $"{e.Param} in {Range(e.Min, e.Max)}{Suffix(e.Unit)}";
        expected += $" on every {e.Category ?? "element"}" + (e.Scope == "changed" ? " changed by the command" : "");

        if (result.Elements.Count == 0)
            return Result(index, e, Verdict.Mismatch, expected, "no element matched the selector");

        var failures = new List<string>();
        foreach (var element in result.Elements)
        {
            var value = element.Values.FirstOrDefault(v => v.Name == e.Param);
            if (value is not { Found: true })
                failures.Add($"#{element.Id}: parameter missing");
            else if (!ValueMatches(value, e))
                failures.Add($"#{element.Id}: {Show(value)}");
        }

        var actual = failures.Count == 0
            ? $"all {result.Elements.Count} match (e.g. {Show(result.Elements[0].Values.First(v => v.Name == e.Param))})"
            : $"{failures.Count}/{result.Elements.Count} differ: " + string.Join("; ", failures.Take(MaxListedFailures))
              + (failures.Count > MaxListedFailures ? " …" : "");
        if (result.Truncated)
            actual += " (result truncated by limit)";
        return Result(index, e, failures.Count == 0, expected, actual);
    }

    private static bool ValueMatches(ParamValue value, Expectation e)
    {
        if (value.Number is { } number)
        {
            if (e.Expected is not null)
            {
                return FilterEvaluator.TryParse(e.Expected, out var target)
                    ? Math.Abs(number - target) <= (e.Tolerance ?? DefaultTolerance)
                    : string.Equals(value.Display, e.Expected, StringComparison.Ordinal);
            }
            return (e.Min is null || number >= e.Min) && (e.Max is null || number <= e.Max);
        }

        return e.Expected is not null && string.Equals(value.Text ?? value.Display, e.Expected, StringComparison.Ordinal);
    }

    private static AssertionResult StatusCheck(int index, Expectation e, CommandResult command)
    {
        var expected = e.Expected ?? "completed";
        return Result(index, e, string.Equals(command.Status, expected, StringComparison.OrdinalIgnoreCase),
            $"command {expected}", $"{command.Status} {command.Message}".Trim());
    }

    private static QueryRequest ToQuery(Expectation e, ChangeSet changes, bool readParam)
    {
        var request = new QueryRequest
        {
            Category = e.Category ?? "*",
            Where = e.Where.Select(w => new Filter { Param = w.Param, Op = w.Op, Value = w.Value, Tolerance = w.Tolerance, Unit = w.Unit }).ToList(),
        };
        if (readParam && e.Param is not null)
            request.Read.Add(new ParamRead { Name = e.Param, Unit = e.Unit });
        if (e.Scope == "changed")
        {
            request.OnlyIds = changes.Added.Concat(changes.Modified).Select(x => x.Id).ToList();
            if (request.OnlyIds.Count == 0)
                request.OnlyIds.Add("<none>"); // empty list means "no restriction" on the wire
        }
        return request;
    }

    private static AssertionResult Result(int index, Expectation e, bool ok, string expected, string actual) =>
        Result(index, e, ok ? Verdict.Match : Verdict.Mismatch, expected, actual);

    private static AssertionResult Result(int index, Expectation e, Verdict verdict, string expected, string actual) => new()
    {
        Index = index,
        Kind = e.Kind,
        Description = Describe(e),
        Verdict = verdict,
        Expected = expected,
        Actual = actual,
        Source = e.Source,
    };

    private static string Describe(Expectation e) => e.Note ?? $"{e.Kind} {e.Category} {e.Param}".Trim();

    private static string Range(double? min, double? max) => $"[{min?.ToString(CultureInfo.InvariantCulture) ?? "-∞"} .. {max?.ToString(CultureInfo.InvariantCulture) ?? "+∞"}]";

    private static string Suffix(string? unit) => string.IsNullOrEmpty(unit) ? "" : " " + unit;

    private static string Show(ParamValue v) =>
        (v.Number?.ToString("G10", CultureInfo.InvariantCulture) ?? v.Text ?? "null") + (v.Number.HasValue && v.Unit is not null ? " " + v.Unit : "")
        + (v.Display is not null && v.Display != v.Text ? $" (shown as '{v.Display}')" : "");
}
