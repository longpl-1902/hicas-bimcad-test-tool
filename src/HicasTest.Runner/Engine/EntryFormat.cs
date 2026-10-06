using System.Text;
using System.Text.Json;
using HicasTest.Protocol;

namespace HicasTest.Runner.Engine;

/// <summary>
/// Compact text for an agent: one line per entry, and a call result that never floods the conversation. A long result
/// is cut and the whole JSON saved to a file the agent can read on demand.
/// </summary>
public static class EntryFormat
{
    public const int MaxInline = 3000;

    public static string List(EntryListResult list) =>
        string.Concat(list.Warnings.Select(w => "WARNING wrong build? " + w + "\n")) +
        (list.Entries.Count == 0
            ? "No entries (no public static method with [HicasTestEntry] in that assembly)."
            : string.Join("\n", list.Entries.Select(e =>
                $"{e.Name} [{e.Kind}{(e.ReadOnly ? ", read-only" : "")}]" +
                (string.IsNullOrWhiteSpace(e.Description) ? "" : " " + e.Description) +
                (string.IsNullOrWhiteSpace(e.Contract) ? "" : $" (contract: {e.Contract})"))));

    /// <param name="savePath">Where the full result JSON goes when it is cut; null = never saved.</param>
    public static string Call(string name, EntryCallResult call, ChangeSet? changes, string? savePath)
    {
        var sb = new StringBuilder($"{name}: {call.Status} in {call.DurationMs} ms");
        if (call.Error != null)
            sb.Append("\nerror: ").Append(call.Error);
        foreach (var warning in call.Warnings)
            sb.Append("\nWARNING wrong build? ").Append(warning);

        foreach (var p in call.Prompts)
        {
            sb.Append('\n').Append(p.Unused
                ? $"scripted answer {p.Id} = {p.Answer} was never used (that prompt was not raised)"
                : $"prompt {p.Id} ({p.Severity}) [{string.Join(" | ", p.Options)}] -> {p.Answer}{(p.Unanswered ? " (default, not scripted)" : "")}: {p.Message}");
        }

        if (changes != null)
        {
            sb.Append($"\nchanges: {changes.Added.Count} added, {changes.Modified.Count} modified, {changes.Deleted.Count} deleted");
            foreach (var group in changes.Added.GroupBy(a => a.Category ?? "?"))
                sb.Append($"; added {group.Key} x{group.Count()}");
            foreach (var warning in changes.Warnings)
                sb.Append("\nhost warning: ").Append(warning);
        }

        if (!string.IsNullOrEmpty(call.Result))
        {
            var compact = Compact(call.Result);
            if (compact.Length <= MaxInline)
                sb.Append("\nresult: ").Append(compact);
            else
            {
                sb.Append("\nresult (cut, ").Append(compact.Length).Append(" chars): ").Append(compact[..MaxInline]).Append('…');
                if (savePath != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
                    File.WriteAllText(savePath, call.Result, new UTF8Encoding(false));
                    sb.Append("\nfull result: ").Append(savePath);
                }
            }
        }
        return sb.ToString();
    }

    private static string Compact(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }
        catch (JsonException)
        {
            return json; // not JSON: show as is
        }
    }
}
