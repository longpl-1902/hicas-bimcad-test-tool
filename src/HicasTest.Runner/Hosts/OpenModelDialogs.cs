using HicasTest.Runner.Model;

namespace HicasTest.Runner.Hosts;

/// <summary>
/// Dialogs the host shows while opening a fixture copy, answered the way a tester would so the model opens.
/// Case rules come first and can override them.
/// </summary>
public static class OpenModelDialogs
{
    /// <summary>Answered inside the host by the bridge (Revit DialogBoxShowing: dialog id or message).</summary>
    public static readonly IReadOnlyList<DialogSpec> Bridge = new[]
    {
        // Revit: CAD links / DWF markups of the fixture are missing. Command link 2 = "Ignore and continue opening the project".
        new DialogSpec { Match = "TaskDialog_Unresolved_References", Answer = "commandlink2" },
    };

    /// <summary>Answered from outside by the dialog driver (window title or text, button label).</summary>
    public static readonly IReadOnlyList<DialogSpec> Ui = new[]
    {
        // Revit: warnings stored in the model ("Warning -- 0 Errors, 67 Warnings", Dialog_Revit_DocWarnDialog).
        // Overriding it through the API cancels the open, so OK is pressed from outside, and only when there are
        // no errors (the leading space keeps "10 Errors" from matching).
        new DialogSpec { Match = " 0 Errors, ", Answer = "OK" },
    };

    public static List<DialogSpec> WithBridge(IEnumerable<DialogSpec> caseRules) => caseRules.Concat(Bridge).ToList();

    public static List<DialogSpec> WithUi(IEnumerable<DialogSpec> caseRules) => caseRules.Concat(Ui).ToList();
}
