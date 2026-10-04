using System.Collections.Concurrent;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using HicasTest.Protocol;
using HicasTest.Runner.Model;

namespace HicasTest.Runner.Hosts;

/// <summary>
/// Answers dialogs of the host process from outside via UI Automation (WPF/WinForms/Win32 windows).
/// Revit TaskDialogs are answered inside the host by the bridge; this covers everything else.
/// </summary>
public sealed class DialogDriver : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly UIA3Automation _automation = new();
    private readonly ConcurrentQueue<DialogEvent> _events = new();
    private readonly CancellationTokenSource _cts = new();
    private volatile DialogSpec[] _rules = Array.Empty<DialogSpec>();
    private Task? _loop;

    /// <summary>Starts (or retargets) watching. Later calls replace the rule set.</summary>
    public void Watch(int pid, IEnumerable<DialogSpec> rules)
    {
        _rules = rules.ToArray();
        _loop ??= Task.Run(() => LoopAsync(pid, _cts.Token));
    }

    public IReadOnlyList<DialogEvent> TakeEvents()
    {
        var list = new List<DialogEvent>();
        while (_events.TryDequeue(out var e))
            list.Add(e);
        return list;
    }

    private async Task LoopAsync(int pid, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                foreach (var host in HostWindows.List(pid, _automation))
                {
                    if (host.IsMainWindow)
                        continue; // never click inside the main window
                    TryAnswer(host.Window);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Windows appear and close while we scan; the next poll sees a consistent state.
            }

            try
            {
                await Task.Delay(PollInterval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void TryAnswer(Window window)
    {
        var title = window.Title ?? string.Empty;
        foreach (var rule in _rules)
        {
            if (title.IndexOf(rule.Match, StringComparison.OrdinalIgnoreCase) < 0 && !ContainsText(window, rule.Match))
                continue;

            var button = window.FindAllDescendants().FirstOrDefault(e =>
                HostWindows.IsButtonNamed(Safe(() => e.ControlType), Safe(() => e.ClassName), Safe(() => e.Name), rule.Answer));
            var handled = false;
            if (button != null)
            {
                HostWindows.Press(button);
                handled = true;
            }
            _events.Enqueue(new DialogEvent
            {
                Source = "flaui",
                DialogId = title,
                Message = rule.Match,
                Answer = rule.Answer,
                Handled = handled,
            });
            return;
        }
    }

    // Any element, not only Text: Revit's Win32 dialogs expose their labels as Pane/Edit.
    private static bool ContainsText(Window window, string text) =>
        window.FindAllDescendants()
            .Any(t => (Safe(() => t.Name) ?? string.Empty).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);

    private static T? Safe<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return default; // element vanished or property unsupported
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // loop faulted while shutting down; nothing left to clean
        }
        _automation.Dispose();
        _cts.Dispose();
    }
}
