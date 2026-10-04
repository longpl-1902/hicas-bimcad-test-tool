using System.Text;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace HicasTest.Runner.Hosts;

/// <summary>
/// Explicit UI actions on one host process for QA sessions: screenshot, list controls, click, type.
/// Only windows of that process are touched. Unlike <see cref="DialogDriver"/>, the main window is allowed
/// (ribbon tabs and buttons are clicked like a user would).
/// </summary>
public sealed class UiDriver(int pid) : IDisposable
{
    private static readonly ControlType[] Interactive =
    {
        ControlType.Button, ControlType.Edit, ControlType.ComboBox, ControlType.CheckBox, ControlType.RadioButton,
        ControlType.ListItem, ControlType.TabItem, ControlType.MenuItem, ControlType.TreeItem, ControlType.DataItem,
        ControlType.SplitButton, ControlType.Hyperlink,
    };

    // Revit answers UI Automation slowly: one descendant search of its main window (~500 elements) takes 15-30 s,
    // longer than the default transaction timeout. Allow a full search instead of failing it.
    private readonly UIA3Automation _automation = new()
    {
        TransactionTimeout = TimeSpan.FromSeconds(90),
        ConnectionTimeout = TimeSpan.FromSeconds(10),
    };

    /// <summary>Captures the host's main window including any dialog on top of it (screen copy of its bounds).</summary>
    public string Screenshot(string path) => WhileBusy(() =>
    {
        using var app = FlaUI.Core.Application.Attach(pid);
        var main = app.GetMainWindow(_automation, TimeSpan.FromSeconds(10))
                   ?? throw new InvalidOperationException("Host main window not found.");
        main.SetForeground();
        Thread.Sleep(300); // let the window repaint after coming to front
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Capture.Element(main).ToFile(path);
        return path;
    });

    /// <summary>Lists windows and their interactive controls so Claude can choose what to click.</summary>
    public string Describe(string? windowFilter, string? textFilter, int max = 150) =>
        WhileBusy(() => DescribeOnce(windowFilter, textFilter, max));

    private string DescribeOnce(string? windowFilter, string? textFilter, int max)
    {
        var sb = new StringBuilder();
        var count = 0;
        // Fetch the properties with the search instead of one slow cross-process call per property.
        var cache = new CacheRequest { TreeScope = TreeScope.Element };
        cache.Add(_automation.PropertyLibrary.Element.Name);
        cache.Add(_automation.PropertyLibrary.Element.AutomationId);
        cache.Add(_automation.PropertyLibrary.Element.ControlType);
        cache.Add(_automation.PropertyLibrary.Element.IsEnabled);
        foreach (var window in Windows(windowFilter))
        {
            sb.AppendLine($"[window] \"{window.Title}\"");
            // Task dialog and Revit Win32 dialog buttons are Pane elements of class CCPushButton / Button.
            var interactive = new FlaUI.Core.Conditions.OrCondition(Interactive
                .Select(t => (FlaUI.Core.Conditions.ConditionBase)window.ConditionFactory.ByControlType(t))
                .Append(window.ConditionFactory.ByClassName("CCPushButton"))
                .Append(window.ConditionFactory.ByClassName("Button"))
                .ToArray());
            using var caching = cache.Activate();
            foreach (var element in window.FindAllDescendants(interactive))
            {
                var name = Safe(() => element.Name);
                var id = Safe(() => element.AutomationId);
                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(id))
                    continue;
                if (textFilter != null && !Contains(name, textFilter) && !Contains(id, textFilter))
                    continue;
                if (++count > max)
                {
                    sb.AppendLine($"  … more than {max} controls; narrow with window/text filters");
                    return sb.ToString();
                }
                var enabled = Safe(() => element.IsEnabled ? "" : " (disabled)");
                sb.AppendLine($"  {element.ControlType,-12} name=\"{name}\"{(string.IsNullOrEmpty(id) ? "" : $" id=\"{id}\"")}{enabled}");
            }
        }
        return count == 0 && sb.Length == 0 ? "No window matched." : sb.ToString();
    }

    /// <summary>Clicks the first control whose name or automation id equals <paramref name="target"/> (case-insensitive).</summary>
    public string Click(string? windowFilter, string target)
    {
        var element = Find(windowFilter, target);
        if (element.Patterns.Invoke.IsSupported || HostWindows.IsButtonElement(element))
            HostWindows.Press(element);
        else if (element.Patterns.Toggle.IsSupported)
            element.Patterns.Toggle.Pattern.Toggle();
        else if (element.Patterns.SelectionItem.IsSupported)
            element.Patterns.SelectionItem.Pattern.Select();
        else if (element.Patterns.ExpandCollapse.IsSupported)
            element.Patterns.ExpandCollapse.Pattern.Expand();
        else
            element.Click(moveMouse: true);
        return $"clicked {element.ControlType} \"{Safe(() => element.Name)}\"";
    }

    public string Type(string? windowFilter, string target, string text)
    {
        var element = Find(windowFilter, target);
        if (element.Patterns.Value.IsSupported)
            element.Patterns.Value.Pattern.SetValue(text);
        else
            element.AsTextBox().Enter(text);
        return $"typed into \"{Safe(() => element.Name)}\"";
    }

    // Only the lookup is retried: pressing twice could run the action twice.
    private AutomationElement Find(string? windowFilter, string target) => WhileBusy(() => FindOnce(windowFilter, target));

    private AutomationElement FindOnce(string? windowFilter, string target)
    {
        foreach (var window in Windows(windowFilter))
        {
            var match = window.FindFirstDescendant(cf => cf.ByName(target).Or(cf.ByAutomationId(target)))
                        ?? window.FindAllDescendants().FirstOrDefault(e => string.Equals(Safe(() => e.Name), target, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match;
        }
        throw new InvalidOperationException($"No control \"{target}\" in {(windowFilter == null ? "any window" : $"window \"{windowFilter}\"")}. Use the ui list tool first.");
    }

    private IEnumerable<Window> Windows(string? filter) =>
        // Dialogs first: they are usually what the next step needs.
        HostWindows.List(pid, _automation)
            .Where(w => filter == null || Contains(Safe(() => w.Window.Title), filter))
            .OrderBy(w => w.IsMainWindow ? 1 : 0)
            .Select(w => w.Window)
            .ToList();

    /// <summary>
    /// The host's UI thread can be busy for a while (e.g. Revit regenerating right after opening a large model);
    /// UI Automation calls then time out. One retry after a pause; the long transaction timeout covers the rest.
    /// </summary>
    private static T WhileBusy<T>(Func<T> action)
    {
        const int attempts = 2;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (Exception ex) when (attempt < attempts && HostWindows.IsTimeout(ex))
            {
                Thread.Sleep(TimeSpan.FromSeconds(3));
            }
        }
    }

    private static bool Contains(string? text, string part) => (text ?? "").Contains(part, StringComparison.OrdinalIgnoreCase);

    private static T? Safe<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return default; // element vanished or property unsupported; skip it
        }
    }

    public void Dispose() => _automation.Dispose();
}
