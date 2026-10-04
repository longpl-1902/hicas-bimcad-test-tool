using System.Text;
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

    private readonly UIA3Automation _automation = new();

    /// <summary>Captures the host's main window including any dialog on top of it (screen copy of its bounds).</summary>
    public string Screenshot(string path)
    {
        using var app = FlaUI.Core.Application.Attach(pid);
        var main = app.GetMainWindow(_automation, TimeSpan.FromSeconds(10))
                   ?? throw new InvalidOperationException("Host main window not found.");
        main.SetForeground();
        Thread.Sleep(300); // let the window repaint after coming to front
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Capture.Element(main).ToFile(path);
        return path;
    }

    /// <summary>Lists windows and their interactive controls so Claude can choose what to click.</summary>
    public string Describe(string? windowFilter, string? textFilter, int max = 150)
    {
        var sb = new StringBuilder();
        var count = 0;
        foreach (var window in Windows(windowFilter))
        {
            sb.AppendLine($"[window] \"{window.Title}\"");
            var interactive = new FlaUI.Core.Conditions.OrCondition(Interactive.Select(t => (FlaUI.Core.Conditions.ConditionBase)window.ConditionFactory.ByControlType(t)).ToArray());
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
        if (element.Patterns.Invoke.IsSupported)
            element.Patterns.Invoke.Pattern.Invoke();
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

    private AutomationElement Find(string? windowFilter, string target)
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

    private IEnumerable<Window> Windows(string? filter)
    {
        using var app = FlaUI.Core.Application.Attach(pid);
        // Dialogs first: they are usually what the next step needs.
        return app.GetAllTopLevelWindows(_automation)
            .Where(w => filter == null || Contains(Safe(() => w.Title), filter))
            .OrderBy(w => Safe(() => w.Properties.NativeWindowHandle.ValueOrDefault) == app.MainWindowHandle ? 1 : 0)
            .ToList();
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
