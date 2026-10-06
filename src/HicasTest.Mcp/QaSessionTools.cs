using System.ComponentModel;
using System.Text;
using HicasTest.Protocol;
using HicasTest.Runner;
using HicasTest.Runner.Model;
using HicasTest.Runner.Sessions;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace HicasTest.Mcp;

/// <summary>
/// Interactive QA sessions: Claude drives Revit/AutoCAD step by step on a fixture copy, with a screenshot and a
/// change summary per step, then writes a report. Evidence only â€” QA decides Pass/Fail.
/// </summary>
[McpServerToolType]
public static class QaSessionTools
{
    [McpServerTool(Name = "qa_session_start"), Description(
        "Start Revit or AutoCAD (chosen year) with the build under test, open a COPY of a test/golden model, screenshot it. " +
        "Returns a session id for the other qa_session_* tools. Never use customer models.")]
    public static async Task<string> Start(
        [Description("What QA is testing, used as the report title.")] string title,
        [Description("revit or autocad")] string app,
        [Description("Host year, e.g. 2024 (see list_hosts).")] int version,
        [Description("Path to the test .rvt/.dwg fixture.")] string model,
        [Description("Revit: .addin manifest of the build under test.")] string? addinManifest = null,
        [Description("AutoCAD: dll to NETLOAD. Revit: dll when no manifest (with fullClassName + addinId).")] string? addinAssembly = null,
        string? fullClassName = null,
        string? addinId = null,
        [Description("Where sessions/<id>/ (screenshots + report.md) are written. Default: config outputDir or ./out.")] string? outputDir = null,
        [Description("The add-in's test assembly (<Addin>.Testing.dll) for the entry tools.")] string? testAssembly = null,
        CancellationToken ct = default)
    {
        var spec = new TestCase
        {
            Id = "qa-session",
            Host = new HostSpec { App = app.Trim().ToLowerInvariant(), Version = version },
            Model = Path.GetFullPath(model),
            Run = { Assembly = string.IsNullOrWhiteSpace(testAssembly) ? null : Path.GetFullPath(testAssembly) },
            Addin = addinManifest == null && addinAssembly == null ? null : new AddinSpec
            {
                Manifest = addinManifest == null ? null : Path.GetFullPath(addinManifest),
                Assembly = addinAssembly == null ? null : Path.GetFullPath(addinAssembly),
                FullClassName = fullClassName,
                AddinId = addinId,
            },
        };
        if (!File.Exists(spec.Model))
            throw new FileNotFoundException("Model not found: " + spec.Model);

        var options = new RunOptions();
        if (!string.IsNullOrWhiteSpace(outputDir))
            options.OutputDir = Path.GetFullPath(outputDir);

        QaSession session;
        try
        {
            session = await QaSession.StartAsync(title, spec, options, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not McpException)
        {
            // The SDK hides the message of other exceptions; startup failures need it (timeout, missing bridge...).
            throw new McpException($"{app} {version} did not start: {ex.Message}", ex);
        }
        SessionRegistry.Add(session);
        return $"session {session.Id} (pid {session.Pid})\n" + Describe(session.Steps[^1]);
    }

    [McpServerTool(Name = "qa_session_run_command"), Description(
        "Run a command in the session with change recording. Revit: mode=postcommand + command id " +
        "(CustomCtrl_%CustomCtrl_%Tab%Panel%Button or a PostableCommand name). AutoCAD: mode=commandline + command line " +
        "(extra tokens answer prompts). Either: mode=invoke + assembly/type/method. If the command opens a dialog that QA " +
        "wants to fill step by step, pass wait=false, drive it with qa_session_ui/click/type, then qa_session_finish_command.")]
    public static async Task<string> RunCommand(
        string sessionId,
        string mode,
        string? command = null,
        string? assembly = null,
        string? type = null,
        string? method = null,
        string? argument = null,
        [Description("Dialog rules 'match=answer;match=answer', e.g. 'Confirm=Yes;Options=OK'.")] string? dialogs = null,
        int timeoutSec = 300,
        [Description("false = return right after the command starts (to click through its dialogs).")] bool wait = true,
        CancellationToken ct = default)
    => await ToolErrors.Surface(async () =>
    {
        var session = SessionRegistry.Get(sessionId);
        var step = await session.RunCommandAsync(new RunCommandRequest
        {
            Mode = mode,
            Command = command,
            AssemblyPath = assembly == null ? null : Path.GetFullPath(assembly),
            TypeName = type,
            MethodName = method,
            Argument = argument,
            TimeoutSec = timeoutSec,
        }, ParseRules(dialogs), wait, ct);
        return Describe(step);
    });

    [McpServerTool(Name = "qa_session_finish_command"), Description(
        "Wait for a command started with wait=false to finish and record its changes, dialogs and a screenshot.")]
    public static Task<string> FinishCommand(string sessionId, int timeoutSec = 120, CancellationToken ct = default) =>
        ToolErrors.Surface(async () => Describe(await SessionRegistry.Get(sessionId).FinishCommandAsync(TimeSpan.FromSeconds(timeoutSec), ct)));

    [McpServerTool(Name = "qa_session_ui"), Description(
        "List windows of the host and their clickable controls (name / automation id). Use before clicking. " +
        "Filter by window title and/or control text to keep it short (the main window has many ribbon controls).")]
    public static string Ui(string sessionId, string? window = null, string? text = null) =>
        ToolErrors.Surface(() => SessionRegistry.Get(sessionId).DescribeUi(window, text));

    [McpServerTool(Name = "qa_session_click"), Description(
        "Click a control by exact name or automation id (ribbon tab/button, dialog button, list itemâ€¦), then screenshot.")]
    public static async Task<string> Click(string sessionId, string target, [Description("Window title filter.")] string? window = null, CancellationToken ct = default) =>
        Describe(await SessionRegistry.Get(sessionId).ClickAsync(window, target, ct));

    [McpServerTool(Name = "qa_session_type"), Description("Type text into an input control (by name or automation id), then screenshot.")]
    public static async Task<string> Type(string sessionId, string target, string text, string? window = null, CancellationToken ct = default) =>
        Describe(await SessionRegistry.Get(sessionId).TypeAsync(window, target, text, ct));

    [McpServerTool(Name = "qa_session_screenshot"), Description("Screenshot the host window (with any dialog on top) as a labelled step.")]
    public static async Task<string> Screenshot(string sessionId, string label, CancellationToken ct = default) =>
        Describe(await SessionRegistry.Get(sessionId).ScreenshotAsync(label, ct));

    [McpServerTool(Name = "qa_session_note"), Description("Record QA's observation as a step in the report (no screenshot).")]
    public static async Task<string> Note(string sessionId, string text) =>
        Describe(await SessionRegistry.Get(sessionId).NoteAsync(text));

    [McpServerTool(Name = "qa_session_query"), Description(
        "Read-only: elements of a category with parameter values in the session's model (verify what a step produced).")]
    public static async Task<string> Query(string sessionId, string category, [Description("Comma-separated parameter names.")] string parameters = "",
        string? unit = null, int limit = 50, CancellationToken ct = default)
    => await ToolErrors.Surface(async () =>
    {
        var request = new QueryRequest { Category = category, Limit = limit };
        foreach (var name in parameters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            request.Read.Add(new ParamRead { Name = name, Unit = string.IsNullOrWhiteSpace(unit) ? null : unit });
        var result = await SessionRegistry.Get(sessionId).QueryAsync(request, ct);

        var sb = new StringBuilder($"{result.Elements.Count} element(s){(result.Truncated ? " (truncated)" : "")}\n");
        foreach (var e in result.Elements)
            sb.AppendLine($"{e.Id} {e.Category} '{e.Name}': " + string.Join(", ",
                e.Values.Select(v => $"{v.Name}={(v.Found ? v.Number?.ToString() ?? v.Text : "<missing>")}{(v.Display != null ? $" [{v.Display}]" : "")}")));
        return sb.ToString();
    });

    [McpServerTool(Name = "qa_session_end"), Description("Close the host (model copy is discarded) and write report.md with every step and screenshot. Returns the report path.")]
    public static Task<string> End(string sessionId) => ToolErrors.Surface(async () =>
    {
        var session = SessionRegistry.Remove(sessionId) ?? throw new ArgumentException($"No open session '{sessionId}'.");
        var report = session.WriteReport();
        await session.DisposeAsync();
        return $"report: {report} ({session.Steps.Count} steps)";
    });

    [McpServerTool(Name = "qa_session_list"), Description("List open QA sessions.")]
    public static string List() =>
        SessionRegistry.All() is { Count: > 0 } all
            ? string.Join("\n", all.Select(s => $"{s.Id}: {s.Title} â€” {s.Spec.Host.App} {s.Spec.Host.Version}, pid {s.Pid}, {s.Steps.Count} steps"))
            : "No open session.";

    [McpServerTool(Name = "qa_session_list_entries"), Description("List the add-in's test entries (name, main/step, read-only, contract file).")]
    public static Task<string> ListEntries(string sessionId, string? assembly = null, CancellationToken ct = default) =>
        ToolErrors.Surface(async () => Describe(await SessionRegistry.Get(sessionId).ListEntriesAsync(assembly, ct)));

    [McpServerTool(Name = "qa_session_call_entry"), Description(
        "Call a test entry with recording: result JSON, prompts raised, model changes. 'argument' = the feature's request JSON; " +
        "'answers' = 'promptId=option;promptId=option' (unanswered prompts take their default). Step entries (.validate/.plan) do not write.")]
    public static Task<string> CallEntry(string sessionId, string name, string argument = "{}", string? answers = null,
        string? assembly = null, CancellationToken ct = default) =>
        ToolErrors.Surface(async () => Describe(await SessionRegistry.Get(sessionId).CallEntryAsync(assembly, name, argument,
            ParseRules(answers).Select(r => new PromptAnswerSpec { Id = r.Match, Option = r.Answer }), ct)));

    private static IEnumerable<DialogSpec> ParseRules(string? rules) =>
        (rules ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(r => r.Split('=', 2))
            .Where(p => p.Length == 2)
            .Select(p => new DialogSpec { Match = p[0].Trim(), Answer = p[1].Trim() });

    private static string Describe(SessionStep step) =>
        $"step {step.Index}: {step.Action}\n{step.Detail}" +
        (step.Error != null ? $"\nERROR: {step.Error}" : "") +
        (step.Screenshot != null ? $"\nscreenshot: {step.Screenshot}" : "");
}
