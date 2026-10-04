using System.Diagnostics;
using HicasTest.Protocol;
using HicasTest.Runner.Bridge;
using HicasTest.Runner.Hosts;
using HicasTest.Runner.Model;

namespace HicasTest.Runner.Engine;

/// <summary>Runs one case in a fresh host process per run.</summary>
public sealed class TestRunner(RunOptions options)
{
    public async Task<CaseResult> RunAsync(TestCase testCase, CancellationToken ct)
    {
        if (options.HostVersionOverride is { } year)
            testCase.Host.Version = year;

        var errors = TestCaseLoader.Validate(testCase);
        if (errors.Count > 0)
            return new CaseResult { TestCase = testCase, Build = options.Build, ValidationErrors = errors };

        var host = HostCatalog.Status(testCase.Host.App, testCase.Host.Version, options, testCase.Host.ExePath);
        if (!host.Ready)
        {
            var reason = !host.Installed
                ? $"{host.App} {host.Year} is not installed on this machine ({host.ExePath})."
                : $"Bridge for {host.App} {host.Year} is not built ({host.BridgeDll}). Run: ./build.ps1 -{(host.App == "revit" ? "Revit" : "AutoCAD")} {host.Year}";
            return new CaseResult { TestCase = testCase, Build = options.Build, NotRunReason = reason };
        }

        var result = new CaseResult { TestCase = testCase, Build = options.Build };

        for (var i = 1; i <= Math.Max(1, options.Repeat); i++)
            result.Runs.Add(await RunOnceAsync(testCase, i, ct));
        return result;
    }

    private async Task<CaseRun> RunOnceAsync(TestCase testCase, int runIndex, CancellationToken ct)
    {
        var started = DateTimeOffset.Now;
        var workDir = Path.Combine(options.OutputDir, Safe(testCase.Id), $"{started:yyyyMMdd-HHmmss}-{options.Build}-r{runIndex}");
        Directory.CreateDirectory(workDir);
        var run = new CaseRun { RunIndex = runIndex, StartedAt = started, WorkDir = workDir };
        var watch = Stopwatch.StartNew();

        try
        {
            // Never touch the fixture itself.
            var model = Path.Combine(workDir, Path.GetFileName(testCase.Model));
            File.Copy(testCase.Model, model, overwrite: true);

            using var dialogs = new DialogDriver();
            IHostLauncher launcher = testCase.Host.App.Equals("revit", StringComparison.OrdinalIgnoreCase)
                ? new RevitLauncher()
                : new AutoCadLauncher();

            using var session = await launcher.StartAsync(testCase, options, dialogs, ct);
            run.Bridge = session.Info;
            var bridge = session.Client;

            await bridge.CallAsync<Empty>(Methods.SetDialogRules, new DialogRulesRequest
            {
                Rules = testCase.Dialogs.Select(d => new DialogRule { Match = d.Match, Answer = d.Answer }).ToList(),
            }, ct);
            dialogs.Watch(session.Process.Id, testCase.Dialogs);

            await bridge.CallAsync<DocumentInfo>(Methods.OpenDocument, new OpenDocumentRequest { Path = model }, ct);
            await bridge.CallAsync<Empty>(Methods.StartRecording, null, ct);
            run.Command = await bridge.CallAsync<CommandResult>(Methods.RunCommand, ToRequest(testCase.Run), ct);
            run.Changes = await bridge.CallAsync<ChangeSet>(Methods.StopRecording, null, ct);
            run.Dialogs.AddRange((await bridge.CallAsync<DialogEventList>(Methods.TakeDialogEvents, null, ct)).Events);
            run.Dialogs.AddRange(dialogs.TakeEvents());

            for (var i = 0; i < testCase.Expect.Count; i++)
            {
                run.Assertions.Add(await AssertionEngine.EvaluateAsync(i, testCase.Expect[i], run.Changes, run.Command,
                    q => bridge.CallAsync<QueryResult>(Methods.Query, q, ct)));
            }

            if (run.Command.Status != "completed")
                run.Notes.Add($"Command ended as '{run.Command.Status}': {run.Command.Message}");
            if (run.Dialogs.Any(d => !d.Handled))
                run.Notes.Add("Some dialogs had no matching rule; see the dialog list.");

            if (testCase.Evidence.Image)
            {
                try
                {
                    var image = await bridge.CallAsync<ExportImageResult>(Methods.ExportImage,
                        new ExportImageRequest { Path = Path.Combine(workDir, "view.png") }, ct);
                    run.ImagePath = image.Path;
                }
                catch (BridgeException ex)
                {
                    run.Notes.Add("Image not exported: " + ex.Message);
                }
            }

            run.Verdict = VerdictText.Combine(run.Assertions.Select(a => a.Verdict));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Verdict = Verdict.Error;
            run.Error = ex.GetType().Name + ": " + ex.Message;
        }

        run.Duration = watch.Elapsed;
        return run;
    }

    private static RunCommandRequest ToRequest(RunSpec spec) => new()
    {
        Mode = spec.Mode,
        Command = spec.Command,
        AssemblyPath = spec.Assembly,
        TypeName = spec.Type,
        MethodName = spec.Method,
        Argument = spec.Argument,
        TimeoutSec = spec.TimeoutSec,
    };

    private static string Safe(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
