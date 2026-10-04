using FlaUI.Core.Definitions;
using HicasTest.Runner.Hosts;

namespace HicasTest.Runner.Tests;

public class HostWindowsTests
{
    [Fact]
    public void Task_dialog_command_button_counts_as_button()
    {
        // Revit "Security - Unsigned Add-In": command buttons are Pane elements of class CCPushButton.
        Assert.True(HostWindows.IsButtonNamed(ControlType.Pane, "CCPushButton", "Load Once", "Load Once"));
    }

    [Fact]
    public void Win32_button_exposed_as_pane_counts_as_button()
    {
        // Revit "Dialog_Revit_DocWarnDialog": OK / Cancel are Win32 buttons reported as Pane.
        Assert.True(HostWindows.IsButtonNamed(ControlType.Pane, "Button", "OK", "OK"));
    }

    [Fact]
    public void Answer_matches_regardless_of_case()
    {
        // AutoCAD rules say "Load once", Revit shows "Load Once".
        Assert.True(HostWindows.IsButtonNamed(ControlType.Button, "Button", "Load Once", "Load once"));
    }

    [Fact]
    public void Other_pane_or_text_with_same_name_is_not_a_button()
    {
        Assert.False(HostWindows.IsButtonNamed(ControlType.Pane, "Element", "Load Once", "Load Once"));
        Assert.False(HostWindows.IsButtonNamed(ControlType.Text, null, "Load Once", "Load Once"));
    }

    [Fact]
    public void Different_label_is_not_matched()
    {
        Assert.False(HostWindows.IsButtonNamed(ControlType.Pane, "CCPushButton", "Always Load", "Load Once"));
    }

    [Theory]
    [InlineData("Warning           --           0 Errors, 67 Warnings", true)]
    [InlineData("Error           --           10 Errors, 2 Warnings", false)]
    [InlineData("Error           --           1 Errors, 0 Warnings", false)]
    public void Model_warnings_dialog_is_answered_only_without_errors(string header, bool answered)
    {
        var rule = Assert.Single(OpenModelDialogs.Ui);
        Assert.Equal(answered, header.Contains(rule.Match, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Owned_window_is_never_the_main_window()
    {
        // During startup the process main window can be the dialog's owner or the dialog itself.
        var handle = new IntPtr(100);
        Assert.False(HostWindows.IsMainWindow(handle, handle, owner: new IntPtr(200), className: "HwndWrapper"));
    }

    [Fact]
    public void Win32_dialog_is_never_the_main_window()
    {
        var handle = new IntPtr(100);
        Assert.False(HostWindows.IsMainWindow(handle, handle, owner: IntPtr.Zero, className: "#32770"));
    }

    [Fact]
    public void Unowned_process_main_window_is_the_main_window()
    {
        var handle = new IntPtr(100);
        Assert.True(HostWindows.IsMainWindow(handle, handle, owner: IntPtr.Zero, className: "Afx:00007FF62A3A0000:40"));
        Assert.False(HostWindows.IsMainWindow(handle, new IntPtr(300), owner: IntPtr.Zero, className: "Afx:00007FF62A3A0000:40"));
    }
}
