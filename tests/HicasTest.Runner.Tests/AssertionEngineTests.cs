using HicasTest.Protocol;
using HicasTest.Runner.Engine;
using HicasTest.Runner.Model;

namespace HicasTest.Runner.Tests;

public class AssertionEngineTests
{
    private static readonly CommandResult Completed = new() { Status = "completed" };

    private static ChangeSet Changes(params (string id, string category)[] added) => new()
    {
        Added = added.Select(a => new ElementRef { Id = a.id, Category = a.category }).ToList(),
    };

    private static Func<QueryRequest, Task<QueryResult>> Returns(params ElementSnapshot[] elements) =>
        _ => Task.FromResult(new QueryResult { Elements = elements.ToList() });

    private static ElementSnapshot Pipe(string id, double diameterMm) => new()
    {
        Id = id,
        Category = "OST_PipeCurves",
        Values = { new ParamValue { Name = "Diameter", Found = true, Number = diameterMm, Unit = "mm" } },
    };

    [Fact]
    public async Task Added_count_matches_only_the_given_category()
    {
        var expectation = new Expectation { Kind = "added", Category = "OST_Views", Count = 1, Source = "AC-02" };
        var changes = Changes(("1", "OST_Views"), ("2", "OST_Walls"));

        var result = await AssertionEngine.EvaluateAsync(0, expectation, changes, Completed, Returns());

        Assert.Equal(Verdict.Match, result.Verdict);
    }

    [Fact]
    public async Task Added_count_reports_mismatch_with_actual_value()
    {
        var expectation = new Expectation { Kind = "added", Category = "OST_Views", Count = 2, Source = "AC-02" };

        var result = await AssertionEngine.EvaluateAsync(0, expectation, Changes(("1", "OST_Views")), Completed, Returns());

        Assert.Equal(Verdict.Mismatch, result.Verdict);
        Assert.Equal("1", result.Actual);
    }

    [Theory]
    [InlineData(50.4, Verdict.Match)]
    [InlineData(50.6, Verdict.Mismatch)]
    public async Task Param_uses_tolerance(double actualMm, Verdict expected)
    {
        var expectation = new Expectation { Kind = "param", Category = "OST_PipeCurves", Param = "Diameter", Unit = "mm", Expected = "50", Tolerance = 0.5, Source = "comment #3" };

        var result = await AssertionEngine.EvaluateAsync(0, expectation, new ChangeSet(), Completed, Returns(Pipe("7", actualMm)));

        Assert.Equal(expected, result.Verdict);
    }

    [Fact]
    public async Task Param_with_no_selected_element_is_a_mismatch_not_a_match()
    {
        var expectation = new Expectation { Kind = "param", Category = "OST_PipeCurves", Param = "Diameter", Expected = "50", Source = "comment #3" };

        var result = await AssertionEngine.EvaluateAsync(0, expectation, new ChangeSet(), Completed, Returns());

        Assert.Equal(Verdict.Mismatch, result.Verdict);
    }

    [Fact]
    public async Task Changed_scope_restricts_query_to_changed_ids()
    {
        QueryRequest? sent = null;
        var expectation = new Expectation { Kind = "param", Category = "OST_PipeCurves", Param = "Diameter", Expected = "50", Scope = "changed", Source = "x" };

        await AssertionEngine.EvaluateAsync(0, expectation, Changes(("7", "OST_PipeCurves")), Completed, q =>
        {
            sent = q;
            return Task.FromResult(new QueryResult());
        });

        Assert.Equal(new[] { "7" }, sent!.OnlyIds);
    }

    [Fact]
    public void Any_mismatch_makes_the_case_a_mismatch()
    {
        Assert.Equal(Verdict.Mismatch, VerdictText.Combine(new[] { Verdict.Match, Verdict.Error, Verdict.Mismatch }));
        Assert.Equal(Verdict.NotRun, VerdictText.Combine(Array.Empty<Verdict>()));
    }
}
