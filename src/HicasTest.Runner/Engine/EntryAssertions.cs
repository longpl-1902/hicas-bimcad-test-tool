using System.Globalization;
using System.Text.Json;
using HicasTest.Protocol;
using HicasTest.Runner.Model;

namespace HicasTest.Runner.Engine;

/// <summary>Checks on what a test entry call returned: status, result JSON, prompts. Host-free.</summary>
public static class EntryAssertions
{
    private const double DefaultTolerance = 1e-9;
    private const int MaxShown = 160;

    public static AssertionResult Status(int index, string callId, EntryCallResult call) => Make(
        index, "entry-status", $"call {callId} runs", call.Status == "ok" ? Verdict.Match : Verdict.Mismatch,
        "entry returns without error",
        call.Status == "ok" ? $"ok in {call.DurationMs} ms" : Shorten(call.Error ?? "failed"),
        null);

    public static AssertionResult Result(int index, string callId, ResultCheck check, string? resultJson)
    {
        var description = check.Note ?? $"call {callId}: result {check.Path}";
        var expected = Describe(check);
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(resultJson) ? "null" : resultJson);
            var found = JsonPath.TryGet(document.RootElement, check.Path, out var value);

            if (check.Exists is { } wanted)
                return Make(index, "entry-result", description, found == wanted ? Verdict.Match : Verdict.Mismatch, expected, found ? "exists" : "missing", check.Source);
            if (!found)
                return Make(index, "entry-result", description, Verdict.Mismatch, expected, "missing", check.Source);

            if (check.Count is { } count)
            {
                var actual = value.ValueKind switch
                {
                    JsonValueKind.Array => value.GetArrayLength(),
                    JsonValueKind.Object => value.EnumerateObject().Count(),
                    _ => -1,
                };
                return Make(index, "entry-result", description, actual == count ? Verdict.Match : Verdict.Mismatch, expected,
                    actual < 0 ? $"not an array or object ({value.ValueKind})" : actual.ToString(CultureInfo.InvariantCulture), check.Source);
            }

            if (check.Contains is { } part)
            {
                var text = Display(value);
                return Make(index, "entry-result", description, text.Contains(part, StringComparison.Ordinal) ? Verdict.Match : Verdict.Mismatch,
                    expected, Shorten(text), check.Source);
            }

            if (check.Min is not null || check.Max is not null)
            {
                if (value.ValueKind != JsonValueKind.Number)
                    return Make(index, "entry-result", description, Verdict.Mismatch, expected, $"not a number: {Shorten(Display(value))}", check.Source);
                var number = value.GetDouble();
                var ok = (check.Min is null || number >= check.Min) && (check.Max is null || number <= check.Max);
                return Make(index, "entry-result", description, ok ? Verdict.Match : Verdict.Mismatch, expected, Display(value), check.Source);
            }

            return Make(index, "entry-result", description, Equal(value, check) ? Verdict.Match : Verdict.Mismatch, expected, Shorten(Display(value)), check.Source);
        }
        catch (JsonException ex)
        {
            return Make(index, "entry-result", description, Verdict.Mismatch, expected, "result is not JSON: " + Shorten(ex.Message), check.Source);
        }
    }

    public static AssertionResult Prompt(int index, string callId, PromptCheck check, IReadOnlyList<PromptRecord> prompts)
    {
        var description = $"call {callId}: prompt {check.Id}";
        var raised = prompts.FirstOrDefault(p => !p.Unused && string.Equals(p.Id, check.Id, StringComparison.OrdinalIgnoreCase));
        var expected = check.Raised
            ? $"{check.Id} raised" + (check.Severity is null ? "" : $" ({check.Severity})") + (check.Answer is null ? "" : $", answered {check.Answer}")
            : $"{check.Id} not raised";

        if (!check.Raised)
            return Make(index, "entry-prompt", description, raised is null ? Verdict.Match : Verdict.Mismatch, expected,
                raised is null ? "not raised" : $"raised: {Shorten(raised.Message ?? "")}", check.Source);
        if (raised is null)
            return Make(index, "entry-prompt", description, Verdict.Mismatch, expected,
                "not raised. Raised: " + (prompts.Count(p => !p.Unused) == 0 ? "none" : string.Join(", ", prompts.Where(p => !p.Unused).Select(p => p.Id))), check.Source);

        var problems = new List<string>();
        if (check.Severity is not null && !string.Equals(raised.Severity, check.Severity, StringComparison.OrdinalIgnoreCase))
            problems.Add($"severity {raised.Severity}");
        if (check.Answer is not null && !string.Equals(raised.Answer, check.Answer, StringComparison.OrdinalIgnoreCase))
            problems.Add($"answered {raised.Answer}");
        var actual = problems.Count == 0 ? $"raised ({raised.Severity}), answered {raised.Answer}{(raised.Unanswered ? " (default)" : "")}" : string.Join(", ", problems);
        return Make(index, "entry-prompt", description, problems.Count == 0 ? Verdict.Match : Verdict.Mismatch, expected, actual, check.Source);
    }

    private static bool Equal(JsonElement value, ResultCheck check)
    {
        var expected = check.Expected ?? "";
        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                return double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var target)
                    ? Math.Abs(value.GetDouble() - target) <= (check.Tolerance ?? DefaultTolerance)
                    : false;
            case JsonValueKind.True:
            case JsonValueKind.False:
                return string.Equals(expected, value.GetBoolean() ? "true" : "false", StringComparison.OrdinalIgnoreCase);
            case JsonValueKind.Null:
                return expected is "null" or "";
            case JsonValueKind.String:
                return string.Equals(value.GetString(), expected, StringComparison.Ordinal);
            default:
                return string.Equals(value.GetRawText(), expected, StringComparison.Ordinal);
        }
    }

    private static string Describe(ResultCheck c)
    {
        var path = c.Path;
        if (c.Exists is { } e) return $"{path} {(e ? "exists" : "is missing")}";
        if (c.Count is { } n) return $"{path} has {n} items";
        if (c.Contains is { } s) return $"{path} contains \"{s}\"";
        if (c.Min is not null || c.Max is not null)
            return $"{path} in [{c.Min?.ToString(CultureInfo.InvariantCulture) ?? "-∞"} .. {c.Max?.ToString(CultureInfo.InvariantCulture) ?? "+∞"}]";
        return $"{path} = {c.Expected}" + (c.Tolerance is { } t ? $" ± {t.ToString(CultureInfo.InvariantCulture)}" : "");
    }

    private static string Display(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText();

    private static string Shorten(string text)
    {
        text = text.Replace("\r", " ").Replace("\n", " ");
        return text.Length <= MaxShown ? text : text[..MaxShown] + "…";
    }

    private static AssertionResult Make(int index, string kind, string description, Verdict verdict, string expected, string actual, string? source) => new()
    {
        Index = index,
        Kind = kind,
        Description = description,
        Verdict = verdict,
        Expected = expected,
        Actual = actual,
        Source = source,
    };
}

/// <summary>Reads a value from JSON by a path such as status, view.scale or items[0].name.</summary>
public static class JsonPath
{
    public static bool TryGet(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        foreach (var segment in path.Split('.'))
        {
            var name = segment;
            var indexes = new List<int>();
            var bracket = segment.IndexOf('[');
            if (bracket >= 0)
            {
                name = segment[..bracket];
                foreach (var part in segment[bracket..].Split(new[] { '[', ']' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                        return false;
                    indexes.Add(index);
                }
            }

            if (name.Length > 0)
            {
                if (value.ValueKind != JsonValueKind.Object || !TryGetProperty(value, name, out value))
                    return false;
            }
            foreach (var index in indexes)
            {
                if (value.ValueKind != JsonValueKind.Array || index >= value.GetArrayLength())
                    return false;
                value = value[index];
            }
        }
        return true;
    }

    // The feature's JSON may differ in letter case from the contract text (camelCase vs PascalCase).
    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
            return true;
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}
