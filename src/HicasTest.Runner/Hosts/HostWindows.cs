using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace HicasTest.Runner.Hosts;

/// <summary>A visible top-level window of the host process.</summary>
/// <param name="IsMainWindow">The host's own main window (ribbon, views), not a dialog.</param>
internal sealed record HostWindow(Window Window, bool IsMainWindow);

/// <summary>
/// Finds every visible top-level window of a process, including owned dialogs. UI Automation nests an owned
/// window under its owner (Revit's "Security - Unsigned Add-In" sits under the main window), so a desktop
/// children scan misses it; Win32 EnumWindows lists them all.
/// </summary>
internal static class HostWindows
{
    private const uint GwOwner = 4;
    private const string Win32DialogClass = "#32770";
    private const string Win32ButtonClass = "Button";
    private const uint WmCommand = 0x0111;

    public static List<HostWindow> List(int pid, UIA3Automation automation)
    {
        var mainHandle = MainWindowHandle(pid);
        var result = new List<HostWindow>();
        foreach (var handle in VisibleTopLevelWindows(pid))
        {
            Window window;
            try
            {
                window = automation.FromHandle(handle).AsWindow();
            }
            catch (Exception ex) when (!IsTimeout(ex))
            {
                continue; // closed between enumeration and lookup; a busy host (timeout) is left to the caller's retry
            }
            result.Add(new HostWindow(window, IsMainWindow(handle, mainHandle, GetOwner(handle), ClassName(handle))));
        }
        return result;
    }

    /// <summary>
    /// The process "main window" is the first unowned visible window, which during startup can be a splash or an
    /// empty host frame. An owned window or a Win32 dialog is never the main window, whatever the process says.
    /// </summary>
    internal static bool IsMainWindow(IntPtr handle, IntPtr mainHandle, IntPtr owner, string className) =>
        handle == mainHandle && owner == IntPtr.Zero && className != Win32DialogClass;

    /// <summary>
    /// True when the element is the button labelled <paramref name="answer"/>. Task dialog command buttons
    /// (class CCPushButton) are exposed as Pane, not Button; labels differ in case between hosts ("Load Once" /
    /// "Load once").
    /// </summary>
    internal static bool IsButtonNamed(ControlType type, string? className, string? name, string answer) =>
        string.Equals((name ?? "").Trim(), answer.Trim(), StringComparison.OrdinalIgnoreCase)
        && (type is ControlType.Button or ControlType.SplitButton || IsButtonClass(className));

    /// <summary>
    /// Task dialog command buttons (CCPushButton) and, in Revit's Win32 dialogs, plain Win32 buttons are exposed
    /// as Pane with no control pattern.
    /// </summary>
    internal static bool IsButtonClass(string? className) => className is "CCPushButton" or Win32ButtonClass;

    internal static bool IsButtonElement(AutomationElement element) => IsButtonClass(Safe(() => element.ClassName));

    /// <summary>Presses a button without moving the mouse when the control allows it.</summary>
    public static void Press(AutomationElement element)
    {
        // A Win32 button that exposes no pattern: notify its dialog as a click would (BM_CLICK needs focus).
        var handle = Safe(() => element.Properties.NativeWindowHandle.ValueOrDefault);
        if (handle != IntPtr.Zero && Safe(() => element.ClassName) == Win32ButtonClass)
        {
            var command = (IntPtr)(GetDlgCtrlID(handle) & 0xFFFF); // high word BN_CLICKED = 0
            PostMessage(GetParent(handle), WmCommand, command, handle);
        }
        else if (element.Patterns.Invoke.IsSupported)
            element.Patterns.Invoke.Pattern.Invoke();
        else if (element.Patterns.LegacyIAccessible.IsSupported)
            element.Patterns.LegacyIAccessible.Pattern.DoDefaultAction();
        else
            element.Click(moveMouse: true);
    }

    private const int CorETimeout = unchecked((int)0x80131505);

    /// <summary>UI Automation call timed out: the host's UI thread is busy, not gone.</summary>
    internal static bool IsTimeout(Exception ex) => ex is TimeoutException || ex.HResult == CorETimeout;

    /// <summary>The main window is disabled while a modal dialog of the process is open (Win32 only, no UI Automation).</summary>
    public static bool MainWindowDisabled(int pid)
    {
        var main = MainWindowHandle(pid);
        return main != IntPtr.Zero && !IsWindowEnabled(main);
    }

    private static IntPtr MainWindowHandle(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return process.MainWindowHandle;
        }
        catch (ArgumentException)
        {
            return IntPtr.Zero; // exited
        }
    }

    private static List<IntPtr> VisibleTopLevelWindows(int pid)
    {
        var handles = new List<IntPtr>();
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var owner);
            if (owner == pid && IsWindowVisible(handle))
                handles.Add(handle);
            return true;
        }, IntPtr.Zero);
        return handles;
    }

    private static IntPtr GetOwner(IntPtr handle) => GetWindow(handle, GwOwner);

    private static string ClassName(IntPtr handle)
    {
        var buffer = new char[256];
        var length = GetClassName(handle, buffer, buffer.Length);
        return new string(buffer, 0, Math.Max(length, 0));
    }

    private static T? Safe<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return default;
        }
    }

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out int processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr handle, uint command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr handle, char[] buffer, int maxCount);

    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
}
