using HicasTest.Bridge.Core;
using HicasTest.Contracts;
using HicasTest.Protocol;
using HicasTest.Runner.Engine;
using HicasTest.Runner.Model;

namespace HicasTest.Runner.Tests;

public sealed class FakeHost
{
}

/// <summary>Entries as an add-in's test assembly would define them.</summary>
public static class DemoEntries
{
    [HicasTestEntry("demo.make", Description = "Make a thing", Contract = "docs/specs/test-entries/demo.make.yaml")]
    public static string Make(FakeHost host, string requestJson)
    {
        var answer = TestContext.Ask("demo.duplicate-name", "confirm", "Name exists", new[] { "rename", "cancel" }, "rename");
        return "{\"status\":\"" + answer + "\",\"request\":" + requestJson + "}";
    }

    [HicasTestEntry("demo.make.plan", ReadOnly = true)]
    public static string Plan(FakeHost host, string requestJson) => "{\"count\":3}";

    [HicasTestEntry("demo.fail")]
    public static string Fail(FakeHost host, string requestJson) => throw new InvalidOperationException("boom");

    public static string NotAnEntry(FakeHost host, string requestJson) => "{}";
}

public class TestContextTests
{
    [Fact]
    public void Scripted_answer_wins_and_unanswered_prompt_uses_the_default()
    {
        TestContext.Begin(new[] { "a" }, new[] { "cancel" });
        var scripted = TestContext.Ask("a", "confirm", "m", new[] { "rename", "cancel" }, "rename");
        var defaulted = TestContext.Ask("b", "warning", "n", new[] { "ok" }, "ok");
        var records = TestContext.End();

        Assert.Equal("cancel", scripted);
        Assert.Equal("ok", defaulted);
        Assert.Equal(2, records.Length);
        Assert.EndsWith("cancel" + TestContext.FieldSeparator + "0", records[0]);
        Assert.EndsWith("ok" + TestContext.FieldSeparator + "1", records[1]);
    }

    [Fact]
    public void Answer_that_is_not_an_option_is_an_error_not_a_silent_default()
    {
        TestContext.Begin(new[] { "a" }, new[] { "maybe" });
        try
        {
            var ex = Assert.Throws<ArgumentException>(() => TestContext.Ask("a", "confirm", "m", new[] { "rename", "cancel" }, "rename"));
            Assert.Contains("rename, cancel", ex.Message);
        }
        finally
        {
            TestContext.End();
        }
    }

    [Fact]
    public void Answer_for_a_prompt_that_never_came_is_reported()
    {
        TestContext.Begin(new[] { "ghost" }, new[] { "cancel" });
        var records = TestContext.End();

        Assert.Single(records);
        Assert.StartsWith("unused-answer", records[0]);
    }

    [Fact]
    public void Outside_a_test_call_the_default_is_returned_and_nothing_is_recorded()
    {
        Assert.False(TestContext.IsActive);
        Assert.Equal("rename", TestContext.Ask("a", "confirm", "m", new[] { "rename", "cancel" }, "rename"));
        Assert.Empty(TestContext.End());
    }
}

public class EntryInvokerTests
{
    private static readonly string Assembly = typeof(DemoEntries).Assembly.Location;

    [Fact]
    public void Lists_marked_methods_only_and_tells_main_from_step()
    {
        var list = EntryInvoker.List(Assembly);

        var names = list.Entries.Select(e => e.Name).ToList();
        Assert.Contains("demo.make", names);
        Assert.DoesNotContain("NotAnEntry", names);
        Assert.Equal("main", list.Entries.Single(e => e.Name == "demo.make").Kind);
        var plan = list.Entries.Single(e => e.Name == "demo.make.plan");
        Assert.Equal("step", plan.Kind);
        Assert.True(plan.ReadOnly);
        Assert.Equal("docs/specs/test-entries/demo.make.yaml", list.Entries.Single(e => e.Name == "demo.make").Contract);
    }

    [Fact]
    public void Call_returns_the_result_and_the_prompt_with_its_scripted_answer()
    {
        var result = EntryInvoker.Call(new EntryCallRequest
        {
            AssemblyPath = Assembly,
            Name = "DEMO.make",
            Argument = "{\"level\":1}",
            Answers = new List<PromptAnswerSpec> { new() { Id = "demo.duplicate-name", Option = "cancel" } },
        }, new FakeHost());

        Assert.Equal("ok", result.Status);
        Assert.Equal("{\"status\":\"cancel\",\"request\":{\"level\":1}}", result.Result);
        var prompt = Assert.Single(result.Prompts);
        Assert.Equal(("demo.duplicate-name", "confirm", "cancel", false), (prompt.Id, prompt.Severity, prompt.Answer, prompt.Unanswered));
        Assert.Equal(new[] { "rename", "cancel" }, prompt.Options);
        Assert.False(TestContext.IsActive); // the call context never leaks into the next call
    }

    [Fact]
    public void Failing_entry_is_reported_not_thrown_and_the_context_is_closed()
    {
        var result = EntryInvoker.Call(new EntryCallRequest { AssemblyPath = Assembly, Name = "demo.fail", Argument = "{}" }, new FakeHost());

        Assert.Equal("failed", result.Status);
        Assert.Contains("InvalidOperationException: boom", result.Error);
        Assert.False(TestContext.IsActive);
    }

    [Fact]
    public void Unknown_entry_lists_the_names_that_exist()
    {
        var result = EntryInvoker.Call(new EntryCallRequest { AssemblyPath = Assembly, Name = "demo.nope" }, new FakeHost());

        Assert.Equal("failed", result.Status);
        Assert.Contains("demo.make", result.Error);
    }

    [Fact]
    public void Null_answers_from_the_wire_are_treated_as_none()
    {
        var result = EntryInvoker.Call(new EntryCallRequest { AssemblyPath = Assembly, Name = "demo.make.plan", Argument = "{}", Answers = null! }, new FakeHost());

        Assert.Equal("ok", result.Status);
        Assert.Equal("{\"count\":3}", result.Result);
    }
}

public class EntryAssertionTests
{
    private static AssertionResult Check(string json, ResultCheck check) => EntryAssertions.Result(0, "c1", check, json);

    [Theory]
    [InlineData("{\"status\":\"ok\"}", "status", "ok", true)]
    [InlineData("{\"status\":\"ok\"}", "STATUS", "ok", true)]
    [InlineData("{\"status\":\"ok\"}", "status", "cancelled", false)]
    [InlineData("{\"view\":{\"scale\":100}}", "view.scale", "100", true)]
    [InlineData("{\"items\":[{\"n\":\"a\"},{\"n\":\"b\"}]}", "items[1].n", "b", true)]
    [InlineData("{\"ok\":true}", "ok", "true", true)]
    [InlineData("{\"missing\":1}", "status", "ok", false)]
    public void Equals_reads_a_dotted_path_with_indexes(string json, string path, string expected, bool match) =>
        Assert.Equal(match ? Verdict.Match : Verdict.Mismatch, Check(json, new ResultCheck { Path = path, Expected = expected, Source = "s" }).Verdict);

    [Fact]
    public void Numbers_use_tolerance()
    {
        var json = "{\"length\":111.8039}";

        Assert.Equal(Verdict.Match, Check(json, new ResultCheck { Path = "length", Expected = "111.8034", Tolerance = 0.001, Source = "s" }).Verdict);
        Assert.Equal(Verdict.Mismatch, Check(json, new ResultCheck { Path = "length", Expected = "111.8034", Tolerance = 0.0001, Source = "s" }).Verdict);
        Assert.Equal(Verdict.Match, Check(json, new ResultCheck { Path = "length", Min = 111, Max = 112, Source = "s" }).Verdict);
    }

    [Fact]
    public void Count_exists_and_contains()
    {
        var json = "{\"views\":[1,2,3],\"name\":\"KP - Level 1\"}";

        Assert.Equal(Verdict.Match, Check(json, new ResultCheck { Path = "views", Count = 3, Source = "s" }).Verdict);
        Assert.Equal(Verdict.Match, Check(json, new ResultCheck { Path = "gone", Exists = false, Source = "s" }).Verdict);
        Assert.Equal(Verdict.Match, Check(json, new ResultCheck { Path = "name", Contains = "Level 1", Source = "s" }).Verdict);
    }

    [Fact]
    public void Result_that_is_not_json_is_a_mismatch_not_a_crash()
    {
        Assert.Equal(Verdict.Mismatch, Check("not json", new ResultCheck { Path = "a", Expected = "b", Source = "s" }).Verdict);
    }

    [Fact]
    public void Prompt_checks_cover_raised_not_raised_severity_and_answer()
    {
        var prompts = new List<PromptRecord>
        {
            new() { Id = "demo.duplicate-name", Severity = "confirm", Answer = "cancel", Options = { "rename", "cancel" } },
            new() { Id = "ghost", Answer = "x", Unused = true },
        };

        Assert.Equal(Verdict.Match, EntryAssertions.Prompt(0, "c", new PromptCheck { Id = "demo.duplicate-name", Severity = "confirm", Answer = "cancel", Source = "s" }, prompts).Verdict);
        Assert.Equal(Verdict.Mismatch, EntryAssertions.Prompt(0, "c", new PromptCheck { Id = "demo.duplicate-name", Answer = "rename", Source = "s" }, prompts).Verdict);
        Assert.Equal(Verdict.Match, EntryAssertions.Prompt(0, "c", new PromptCheck { Id = "other", Raised = false, Source = "s" }, prompts).Verdict);
        Assert.Equal(Verdict.Mismatch, EntryAssertions.Prompt(0, "c", new PromptCheck { Id = "demo.duplicate-name", Raised = false, Source = "s" }, prompts).Verdict);
        Assert.Equal(Verdict.Mismatch, EntryAssertions.Prompt(0, "c", new PromptCheck { Id = "ghost", Source = "s" }, prompts).Verdict); // an unused answer is not a raised prompt
    }

    [Fact]
    public void Failed_call_is_a_mismatch_with_the_error()
    {
        var result = EntryAssertions.Status(0, "c1", new EntryCallResult { Status = "failed", Error = "InvalidOperationException: boom" });

        Assert.Equal(Verdict.Mismatch, result.Verdict);
        Assert.Contains("boom", result.Actual);
    }
}

public class EntriesCaseTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("hicastest-entries-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private TestCase Load(string body)
    {
        File.WriteAllText(Path.Combine(_dir, "model.rvt"), "");
        File.WriteAllText(Path.Combine(_dir, "request.json"), "{\"level\":\"Level 1\"}");
        var file = Path.Combine(_dir, "case.yaml");
        File.WriteAllText(file, "id: T-1\nhost: { app: revit, version: 2024 }\nmodel: model.rvt\n" + body);
        return TestCaseLoader.Load(file);
    }

    [Fact]
    public void Entries_case_loads_with_typed_arguments_and_validates()
    {
        var testCase = Load("""
            run: { mode: entries, assembly: bin/My.Testing.dll }
            calls:
              - id: make
                entry: keyplan.create
                argument: { level: "Level 1", scale: 100, dry: false, ratio: 0.5, note: "100", tags: [a, 2] }
                answers: [ { id: keyplan.duplicate-name, option: cancel } ]
                expectResult:
                  - { path: status, equals: cancelled, source: "ticket #1 AC-5" }
                expectPrompts:
                  - { id: keyplan.duplicate-name, severity: confirm, source: "ticket #1 AC-5" }
                expect:
                  - { kind: added, count: 0, source: "ticket #1 AC-5" }
              - entry: keyplan.create.plan
                argumentFile: request.json
            """);

        Assert.Empty(TestCaseLoader.Validate(testCase));
        Assert.Equal("{\"level\":\"Level 1\",\"scale\":100,\"dry\":false,\"ratio\":0.5,\"note\":\"100\",\"tags\":[\"a\",2]}",
            EntriesFlow.ArgumentText(testCase.Calls[0]));
        Assert.Equal("{\"level\":\"Level 1\"}", EntriesFlow.ArgumentText(testCase.Calls[1]));
        Assert.EndsWith("bin" + Path.DirectorySeparatorChar + "My.Testing.dll", testCase.Run.Assembly);
        Assert.Equal("keyplan.duplicate-name", testCase.Calls[0].Answers[0].Id);
    }

    [Fact]
    public void Missing_sources_wrong_operators_and_bad_entries_are_rejected()
    {
        var testCase = Load("""
            run: { mode: entries }
            calls:
              - entry: a
                argument: { x: 1 }
                argumentFile: request.json
                answers: [ { id: p } ]
                expectResult:
                  - { path: status, equals: ok }
                  - { path: n, equals: 1, min: 0, source: s }
              - id: x
              - id: x
                entry: b
                expectPrompts:
                  - { id: q, raised: false, answer: cancel, source: s }
            """);

        var errors = TestCaseLoader.Validate(testCase);

        Assert.Contains(errors, e => e.Contains("run.assembly"));
        Assert.Contains(errors, e => e.Contains("not both"));
        Assert.Contains(errors, e => e.Contains("every answer needs id and option"));
        Assert.Contains(errors, e => e.Contains("expectResult[0]") && e.Contains("'source' is required"));
        Assert.Contains(errors, e => e.Contains("expectResult[1]") && e.Contains("exactly one of"));
        Assert.Contains(errors, e => e.Contains("entry is required"));
        Assert.Contains(errors, e => e.Contains("id is used twice"));
        Assert.Contains(errors, e => e.Contains("only apply to a prompt that is raised"));
    }

    [Fact]
    public void Calls_need_entries_mode_and_some_check_somewhere()
    {
        var wrongMode = Load("""
            run: { mode: postcommand, command: X }
            calls:
              - entry: a
            expect:
              - { kind: no-warnings }
            """);
        var noChecks = Load("""
            run: { mode: entries, assembly: a.dll }
            calls:
              - entry: a
            """);

        Assert.Contains(TestCaseLoader.Validate(wrongMode), e => e.Contains("calls only work with run.mode entries"));
        Assert.Contains(TestCaseLoader.Validate(noChecks), e => e.Contains("at least one expectation"));
    }

    [Fact]
    public void Changes_of_several_calls_merge_like_one_recording()
    {
        var log = new ChangeLog();
        var refs = new Dictionary<string, ElementRef>();
        var warnings = new List<string>();

        EntriesFlow.Merge(new ChangeSet { Added = { new ElementRef { Id = "1", Category = "OST_Views" }, new ElementRef { Id = "2", Category = "OST_Views" } }, Warnings = { "w1" } }, log, refs, warnings);
        EntriesFlow.Merge(new ChangeSet { Deleted = { new ElementRef { Id = "2" } }, Modified = { new ElementRef { Id = "1" }, new ElementRef { Id = "9", Category = "OST_Walls" } } }, log, refs, warnings);
        var merged = EntriesFlow.ToChangeSet(log, refs, warnings);

        Assert.Equal(new[] { "1" }, merged.Added.Select(a => a.Id)); // 2 was created and deleted: gone
        Assert.Equal("OST_Views", merged.Added[0].Category);
        Assert.Equal(new[] { "9" }, merged.Modified.Select(a => a.Id)); // 1 is added, not modified
        Assert.Empty(merged.Deleted);
        Assert.Equal(new[] { "w1" }, merged.Warnings);
    }
}

public class ForeignAssemblyTests
{
    private static List<string> Find(string[] referenced, (string name, string path)[] loaded, string testDir, params string[] shipped) =>
        EntryInvoker.FindForeign(referenced, loaded.Select(l => new KeyValuePair<string, string>(l.name, l.path)), testDir, n => shipped.Contains(n));

    [Fact]
    public void Add_in_assembly_loaded_from_another_build_is_reported()
    {
        var problems = Find(new[] { "HaweeDraw.Actions", "HicasTest.Contracts" },
            new[] { ("HaweeDraw.Actions", @"D:\main\Outcome\HaweeDraw.Actions.dll"), ("HicasTest.Contracts", @"D:\lane\Outcome\HicasTest.Contracts.dll") },
            @"D:\lane\Outcome", "HaweeDraw.Actions", "HicasTest.Contracts");

        var problem = Assert.Single(problems);
        Assert.Contains(@"HaweeDraw.Actions is loaded from D:\main\Outcome", problem);
        Assert.Contains(@"not from the build under test (D:\lane\Outcome)", problem);
    }

    [Fact]
    public void Same_folder_is_fine_whatever_the_slash_or_case()
    {
        Assert.Empty(Find(new[] { "HaweeDraw.Actions" }, new[] { ("HaweeDraw.Actions", @"d:\LANE\outcome\HaweeDraw.Actions.dll") },
            @"D:\lane\Outcome\", "HaweeDraw.Actions"));
    }

    [Fact]
    public void Host_assemblies_and_assemblies_the_build_does_not_ship_are_not_judged()
    {
        Assert.Empty(Find(new[] { "RevitAPI", "System.Text.Json", "Newtonsoft.Json" },
            new[] { ("RevitAPI", @"C:\Program Files\Autodesk\Revit 2024\RevitAPI.dll"), ("System.Text.Json", @"C:\Windows\x.dll"), ("Newtonsoft.Json", @"C:\other\Newtonsoft.Json.dll") },
            @"D:\lane\Outcome", "RevitAPI", "System.Text.Json")); // RevitAPI shipped by mistake, still the host's; Newtonsoft is not shipped here
    }
}
