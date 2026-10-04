using System.Diagnostics;
using System.Text;
using HicasTest.Protocol;
using HicasTest.Runner.Bridge;
using HicasTest.Runner.Hosts;
using HicasTest.Runner.Model;

namespace HicasTest.Runner.Sessions;

public sealed class SessionStep
{
    public int Index { get; init; }
    public DateTimeOffset At { get; init; } = DateTimeOffset.Now;
    public string Action { get; init; } = "";
    public string Detail { get; set; } = "";
    public string? Screenshot { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Interactive, step-by-step session for QA: Claude opens a host on a fixture copy, runs commands, clicks
/// through UI, takes screenshots, and gets a report. Every action is logged as a step with evidence.
/// The session produces evidence, never a verdict; QA decides.
/// </summary>
public sealed class QaSession : IAsyncDisposable
{
    private readonly HostSession _host;
    private readonly DialogDriver _dialogs;
    private readonly UiDriver _ui;
    private readonly List<SessionStep> _steps = new();
    private Task<string>? _pendingCommand;

    private QaSession(string id, string title, string dir, TestCase spec, HostSession host, DialogDriver dialogs)
    {
        Id = id;
        Title = title;
        Directory = dir;
        Spec = spec;
        _host = host;
        _dialogs = dialogs;
        _ui = new UiDriver(host.Process.Id);
    }

    public string Id { get; }
    public string Title { get; }
    public string Directory { get; }
    public TestCase Spec { get; }
    public int Pid => _host.Process.Id;
    public IReadOnlyList<SessionStep> Steps => _steps;

    public static async Task<QaSession> StartAsync(string title, TestCase spec, RunOptions options, CancellationToken ct)
    {
        var status = HostCatalog.Status(spec.Host.App, spec.Host.Version, options, spec.Host.ExePath);
        if (!status.Ready)
            throw new InvalidOperationException($"{status.App} {status.Year} cannot run here: installed={status.Installed}, bridge built={status.BridgeBuilt}.");

        var id = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..4]}";
        var dir = Path.Combine(options.OutputDir, "sessions", id);
        System.IO.Directory.CreateDirectory(dir);

        var model = Path.Combine(dir, Path.GetFileName(spec.Model));
        File.Copy(spec.Model, model, overwrite: true); // never the fixture itself

        var dialogs = new DialogDriver();
        IHostLauncher launcher = spec.Host.App.Equals("revit", StringComparison.OrdinalIgnoreCase) ? new RevitLauncher() : new AutoCadLauncher();
        HostSession host;
        try
        {
            host = await launcher.StartAsync(spec, options, dialogs, ct);
        }
        catch
        {
            dialogs.Dispose();
            throw;
        }
        var session = new QaSession(id, title, dir, spec, host, dialogs);

        await session.StepAsync("open model", async step =>
        {
            var doc = await host.Client.CallAsync<DocumentInfo>(Methods.OpenDocument, new OpenDocumentRequest { Path = model }, ct);
            step.Detail = $"{spec.Host.App} {spec.Host.Version}, opened copy of {Path.GetFileName(spec.Model)} ({doc.Title})";
        }, screenshot: true);
        return session;
    }

    /// <summary>
    /// Runs a command with change recording. With <paramref name="wait"/> = false the call returns as soon as the
    /// command started (screenshot after a short delay shows its dialog); drive the UI, then call
    /// <see cref="FinishCommandAsync"/> to collect the result. The bridge pipe is busy until then.
    /// </summary>
    public async Task<SessionStep> RunCommandAsync(RunCommandRequest request, IEnumerable<DialogSpec> dialogRules, bool wait, CancellationToken ct)
    {
        var label = $"run {request.Mode} {request.Command ?? request.MethodName}";
        if (_pendingCommand is { IsCompleted: false })
            return await StepAsync(label, _ => throw new InvalidOperationException("A command is still running; call finish first."), screenshot: false);

        var rules = dialogRules.ToList();
        await _host.Client.CallAsync<Empty>(Methods.SetDialogRules, new DialogRulesRequest
        {
            Rules = rules.Select(d => new DialogRule { Match = d.Match, Answer = d.Answer }).ToList(),
        }, ct);
        _dialogs.Watch(Pid, rules);
        await _host.Client.CallAsync<Empty>(Methods.StartRecording, null, ct);
        _pendingCommand = CollectCommandAsync(request, CancellationToken.None);

        if (wait)
            return await StepAsync(label, async step => step.Detail = await _pendingCommand, screenshot: true);

        return await StepAsync(label + " (started)", step =>
        {
            step.Detail = "Command started without waiting. Use the UI tools, then finish the command to record its result.";
            return Task.CompletedTask;
        }, screenshot: true, settle: TimeSpan.FromSeconds(3));
    }

    public Task<SessionStep> FinishCommandAsync(TimeSpan timeout, CancellationToken ct) =>
        StepAsync("finish command", async step =>
        {
            var pending = _pendingCommand ?? throw new InvalidOperationException("No command was started.");
            if (await Task.WhenAny(pending, Task.Delay(timeout, ct)) != pending)
                throw new TimeoutException($"Command still running after {timeout.TotalSeconds:0}s (a dialog may be waiting).");
            step.Detail = await pending;
        }, screenshot: true);

    private async Task<string> CollectCommandAsync(RunCommandRequest request, CancellationToken ct)
    {
        var result = await _host.Client.CallAsync<CommandResult>(Methods.RunCommand, request, ct);
        var changes = await _host.Client.CallAsync<ChangeSet>(Methods.StopRecording, null, ct);
        var dialogEvents = (await _host.Client.CallAsync<DialogEventList>(Methods.TakeDialogEvents, null, ct)).Events
            .Concat(_dialogs.TakeEvents()).ToList();

        var sb = new StringBuilder($"command {result.Status} in {result.DurationMs} ms. {result.Message}\n");
        sb.AppendLine($"changes: {changes.Added.Count} added, {changes.Modified.Count} modified, {changes.Deleted.Count} deleted, {changes.Warnings.Count} warnings");
        foreach (var g in changes.Added.GroupBy(a => a.Category ?? "?"))
            sb.AppendLine($"  added {g.Key}: {g.Count()}");
        foreach (var w in changes.Warnings)
            sb.AppendLine($"  warning: {w}");
        foreach (var d in dialogEvents)
            sb.AppendLine($"  dialog [{d.Source}] {d.DialogId} {d.Message} → {(d.Handled ? d.Answer : "not answered")}");
        return sb.ToString().TrimEnd();
    }

    public Task<SessionStep> ClickAsync(string? window, string target, CancellationToken ct) =>
        StepAsync($"click \"{target}\"", step =>
        {
            step.Detail = _ui.Click(window, target);
            return Task.CompletedTask;
        }, screenshot: true, settle: TimeSpan.FromSeconds(1));

    public Task<SessionStep> TypeAsync(string? window, string target, string text, CancellationToken ct) =>
        StepAsync($"type into \"{target}\"", step =>
        {
            step.Detail = _ui.Type(window, target, text) + $": \"{text}\"";
            return Task.CompletedTask;
        }, screenshot: true);

    public Task<SessionStep> ScreenshotAsync(string label, CancellationToken ct) =>
        StepAsync("screenshot: " + label, _ => Task.CompletedTask, screenshot: true);

    /// <summary>QA's own observation, recorded in the report as-is.</summary>
    public Task<SessionStep> NoteAsync(string text) =>
        StepAsync("note", step =>
        {
            step.Detail = text;
            return Task.CompletedTask;
        }, screenshot: false);

    public string DescribeUi(string? window, string? text) => _ui.Describe(window, text);

    public Task<QueryResult> QueryAsync(QueryRequest request, CancellationToken ct) =>
        _pendingCommand is { IsCompleted: false }
            ? throw new InvalidOperationException("A command is still running; finish it before querying the model.")
            : _host.Client.CallAsync<QueryResult>(Methods.Query, request, ct);

    public string WriteReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# QA session — {Title}");
        sb.AppendLine();
        sb.AppendLine($"- Session: `{Id}` · {Spec.Host.App} {Spec.Host.Version} · model: `{Spec.Model}`");
        if (Spec.Addin != null)
            sb.AppendLine($"- Build under test: `{Spec.Addin.Manifest ?? Spec.Addin.Assembly}`");
        sb.AppendLine("- Evidence recorded by the tool. Pass/Fail is QA's decision.");
        sb.AppendLine();
        foreach (var s in _steps)
        {
            sb.AppendLine($"## Step {s.Index} — {s.Action}  ({s.At:HH:mm:ss})");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(s.Detail))
                sb.AppendLine("```text\n" + s.Detail + "\n```");
            if (s.Error != null)
                sb.AppendLine($"**Error:** {s.Error}");
            if (s.Screenshot != null)
                sb.AppendLine($"\n![step {s.Index}]({Path.GetFileName(s.Screenshot)})");
            sb.AppendLine();
        }

        var path = Path.Combine(Directory, "report.md");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return path;
    }

    // Steps may overlap (UI clicks while a command waits on its dialog), so only numbering is serialized.
    private async Task<SessionStep> StepAsync(string action, Func<SessionStep, Task> work, bool screenshot, TimeSpan? settle = null)
    {
        SessionStep step;
        lock (_steps)
        {
            step = new SessionStep { Index = _steps.Count + 1, Action = action };
            _steps.Add(step);
        }

        try
        {
            await work(step);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            step.Error = ex.Message;
        }

        if (screenshot)
        {
            if (settle is { } wait)
                await Task.Delay(wait);
            try
            {
                step.Screenshot = _ui.Screenshot(Path.Combine(Directory, $"step-{step.Index:00}.png"));
            }
            catch (Exception ex)
            {
                step.Error = (step.Error == null ? "" : step.Error + " | ") + "screenshot failed: " + ex.Message;
            }
        }
        return step;
    }

    public ValueTask DisposeAsync()
    {
        _ui.Dispose();
        _host.Dispose();
        _dialogs.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Open sessions of this process (MCP server lifetime).</summary>
public static class SessionRegistry
{
    private static readonly Dictionary<string, QaSession> Open = new();

    public static void Add(QaSession session)
    {
        lock (Open) Open[session.Id] = session;
    }

    public static QaSession Get(string id)
    {
        lock (Open)
            return Open.TryGetValue(id, out var s) ? s : throw new ArgumentException($"No open session '{id}'. Use qa_session_list.");
    }

    public static QaSession? Remove(string id)
    {
        lock (Open)
            return Open.Remove(id, out var s) ? s : null;
    }

    public static IReadOnlyList<QaSession> All()
    {
        lock (Open) return Open.Values.ToList();
    }
}
