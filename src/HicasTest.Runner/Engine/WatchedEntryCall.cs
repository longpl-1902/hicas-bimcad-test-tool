using HicasTest.Protocol;
using HicasTest.Runner.Bridge;
using HicasTest.Runner.Hosts;

namespace HicasTest.Runner.Engine;

/// <summary>An entry call opened a window nobody can answer; the run stops instead of waiting on a hung host.</summary>
public sealed class UnexpectedDialogException(string message) : Exception(message);

/// <summary>
/// Calls a test entry while watching the host from outside: a test entry must not show UI, and a modal window would
/// freeze the host's main thread (and the bridge with it), so a window that stays up is reported and the call abandoned.
/// </summary>
public static class WatchedEntryCall
{
    private static readonly TimeSpan WatchInterval = TimeSpan.FromMilliseconds(300);

    /// <param name="onUnexpected">Gets the dialog events and the screenshot path (or null) before the exception is thrown.</param>
    public static async Task<EntryCallResult> CallAsync(BridgeClient bridge, DialogDriver dialogs, UiDriver ui,
        EntryCallRequest request, string label, TimeSpan timeout, string evidenceDir,
        Action<IReadOnlyList<DialogEvent>, string?>? onUnexpected, CancellationToken ct)
    {
        dialogs.ClearUnexpected();
        dialogs.Strict = true;
        try
        {
            var task = bridge.CallAsync<EntryCallResult>(Methods.EntriesCall, request, ct, timeout);
            while (!task.IsCompleted)
            {
                if (dialogs.Unexpected is { } window)
                {
                    _ = task.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted); // the host is killed with the session
                    var picture = await TryScreenshotAsync(ui, Path.Combine(evidenceDir, "unexpected-dialog.png"));
                    onUnexpected?.Invoke(dialogs.TakeEvents(), picture);
                    throw new UnexpectedDialogException(
                        $"Unexpected dialog {window} while calling entry '{request.Name}' ({label}). A test entry must not show UI: " +
                        "warnings and questions go through the feature's prompt interface." + (picture == null ? "" : $" Screenshot: {picture}"));
                }
                await Task.WhenAny(task, Task.Delay(WatchInterval, ct));
            }
            return await task;
        }
        finally
        {
            dialogs.Strict = false;
        }
    }

    private static async Task<string?> TryScreenshotAsync(UiDriver ui, string path)
    {
        try
        {
            return await Task.Run(() => ui.Screenshot(path)).WaitAsync(TimeSpan.FromSeconds(20));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null; // the host may be too stuck even for UI Automation
        }
    }
}
