using HicasTest.Bridge.Core;
using HicasTest.Protocol;
using HicasTest.Runner.Bridge;
using HicasTest.Runner.Hosts;
using HicasTest.Runner.Model;

namespace HicasTest.Runner.Engine;

/// <summary>
/// run.mode entries: calls the add-in's test entries in order in one host session (docs/test-entries.md), records what
/// each call changed, and checks the result, the prompts and the model.
/// </summary>
internal static class EntriesFlow
{
    private static readonly TimeSpan HostTimeout = TimeSpan.FromMinutes(5); // list, recording, queries: the host is idle then
    private const int MaxResultInReport = 2000;

    public static async Task RunAsync(TestCase testCase, BridgeClient bridge, DialogDriver dialogs, UiDriver ui,
        CaseRun run, string workDir, CancellationToken ct)
    {
        var assembly = testCase.Run.Assembly!;
        var list = await bridge.CallAsync<EntryListResult>(Methods.EntriesList, new EntryListRequest { AssemblyPath = assembly }, ct, HostTimeout);
        if (list.Warnings.Count > 0)
            throw new InvalidOperationException("The entries would not test the build under test. " + string.Join(" | ", list.Warnings)
                                                + " Fix: disable the other manifest, or point it at this build's folder.");
        var known = new HashSet<string>(list.Entries.Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
        var missing = testCase.Calls.Select(c => c.Entry).Where(n => !known.Contains(n)).Distinct().ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException($"Entry not found in {assembly}: {string.Join(", ", missing)}. Entries: "
                                                + (known.Count == 0 ? "(none)" : string.Join(", ", known.Order(StringComparer.OrdinalIgnoreCase).Take(25))));

        var log = new ChangeLog();
        var refs = new Dictionary<string, ElementRef>();
        var warnings = new List<string>();
        var completed = new CommandResult { Status = "completed", Message = "test entries" };
        var next = 0;

        try
        {
            for (var i = 0; i < testCase.Calls.Count; i++)
            {
                var call = testCase.Calls[i];
                var id = string.IsNullOrWhiteSpace(call.Id) ? $"call-{i + 1}" : call.Id!;
                var request = new EntryCallRequest
                {
                    AssemblyPath = assembly,
                    Name = call.Entry,
                    Argument = ArgumentText(call),
                    Answers = call.Answers.Select(a => new PromptAnswerSpec { Id = a.Id, Option = a.Option }).ToList(),
                };

                await bridge.CallAsync<Empty>(Methods.StartRecording, null, ct, HostTimeout);
                var timeout = TimeSpan.FromSeconds(call.TimeoutSec ?? testCase.Run.TimeoutSec);
                EntryCallResult result;
                result = await WatchedEntryCall.CallAsync(bridge, dialogs, ui, request, id, timeout, workDir,
                    (events, picture) =>
                    {
                        run.Dialogs.AddRange(events);
                        run.ImagePath = picture;
                    }, ct);
                var changes = await bridge.CallAsync<ChangeSet>(Methods.StopRecording, null, ct, HostTimeout);
                Merge(changes, log, refs, warnings);

                run.Calls.Add(new CallLog(id, call.Entry, result, changes, MaxResultInReport));
                run.Assertions.Add(EntryAssertions.Status(next++, id, result));
                if (result.Status != "ok")
                {
                    run.Notes.Add($"Call '{id}' failed; the remaining calls were not run.");
                    break; // later calls build on this one
                }

                foreach (var check in call.ExpectResult)
                    run.Assertions.Add(EntryAssertions.Result(next++, id, check, result.Result));
                foreach (var check in call.ExpectPrompts)
                    run.Assertions.Add(EntryAssertions.Prompt(next++, id, check, result.Prompts));
                foreach (var expectation in call.Expect)
                {
                    var checkResult = await AssertionEngine.EvaluateAsync(next++, expectation, changes, completed,
                        q => bridge.CallAsync<QueryResult>(Methods.Query, q, ct, HostTimeout));
                    run.Assertions.Add(WithPrefix(checkResult, id));
                }
                foreach (var unused in result.Prompts.Where(p => p.Unused))
                    run.Notes.Add($"Call '{id}': answer for '{unused.Id}' was never used (that prompt was not raised).");
            }
        }
        finally
        {
            run.Changes = ToChangeSet(log, refs, warnings);
            run.Command = completed;
            run.Command.DurationMs = run.Calls.Sum(c => c.DurationMs);
        }

        for (var i = 0; i < testCase.Expect.Count; i++)
        {
            run.Assertions.Add(await AssertionEngine.EvaluateAsync(next++, testCase.Expect[i], run.Changes!, completed,
                q => bridge.CallAsync<QueryResult>(Methods.Query, q, ct, HostTimeout)));
        }

        run.Dialogs.AddRange((await bridge.CallAsync<DialogEventList>(Methods.TakeDialogEvents, null, ct, HostTimeout)).Events);
        run.Dialogs.AddRange(dialogs.TakeEvents());
    }

    internal static string ArgumentText(CallSpec call) =>
        call.Argument?.ToJsonString()
        ?? (call.ArgumentFilePath != null ? File.ReadAllText(call.ArgumentFilePath) : "{}");

    internal static void Merge(ChangeSet changes, ChangeLog log, Dictionary<string, ElementRef> refs, List<string> warnings)
    {
        foreach (var element in changes.Added)
        {
            log.Added(element.Id);
            refs[element.Id] = element;
        }
        foreach (var element in changes.Modified)
        {
            log.Modified(element.Id);
            refs.TryAdd(element.Id, element);
        }
        foreach (var element in changes.Deleted)
            log.Deleted(element.Id);
        warnings.AddRange(changes.Warnings);
    }

    internal static ChangeSet ToChangeSet(ChangeLog log, Dictionary<string, ElementRef> refs, List<string> warnings)
    {
        ElementRef Of(string id) => refs.TryGetValue(id, out var known) ? known : new ElementRef { Id = id };
        return new ChangeSet
        {
            Added = log.AddedIds.Select(Of).ToList(),
            Modified = log.ModifiedIds.Select(Of).ToList(),
            Deleted = log.DeletedIds.Select(Of).ToList(),
            Warnings = warnings.ToList(),
        };
    }

    private static AssertionResult WithPrefix(AssertionResult r, string callId) => new()
    {
        Index = r.Index,
        Kind = r.Kind,
        Description = $"call {callId}: {r.Description}",
        Verdict = r.Verdict,
        Expected = r.Expected,
        Actual = r.Actual,
        Source = r.Source,
    };
}
