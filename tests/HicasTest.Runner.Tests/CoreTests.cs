using HicasTest.Bridge.Core;
using HicasTest.Protocol;
using HicasTest.Runner.Model;

namespace HicasTest.Runner.Tests;

public class ChangeLogTests
{
    [Fact]
    public void Created_then_deleted_element_disappears()
    {
        var log = new ChangeLog();
        log.Added("1");
        log.Modified("1");
        log.Deleted("1");

        Assert.Empty(log.AddedIds);
        Assert.Empty(log.ModifiedIds);
        Assert.Empty(log.DeletedIds);
    }

    [Fact]
    public void Added_element_is_not_also_reported_as_modified()
    {
        var log = new ChangeLog();
        log.Added("1");
        log.Modified("1");
        log.Modified("2");

        Assert.Equal(new[] { "1" }, log.AddedIds);
        Assert.Equal(new[] { "2" }, log.ModifiedIds);
    }
}

public class FilterEvaluatorTests
{
    [Theory]
    [InlineData("eq", "50", 0.5, true)]
    [InlineData("eq", "51", 0.5, false)]
    [InlineData("gt", "40", null, true)]
    [InlineData("lt", "40", null, false)]
    public void Numbers_compare_numerically(string op, string value, double? tolerance, bool expected)
    {
        var actual = new ParamValue { Name = "D", Found = true, Number = 50.2 };

        Assert.Equal(expected, FilterEvaluator.Matches(actual, new Filter { Param = "D", Op = op, Value = value, Tolerance = tolerance }));
    }

    [Fact]
    public void Missing_parameter_never_matches()
    {
        var missing = new ParamValue { Name = "D", Found = false };

        Assert.False(FilterEvaluator.Matches(missing, new Filter { Param = "D", Op = "eq", Value = "" }));
        Assert.False(FilterEvaluator.Matches(missing, new Filter { Param = "D", Op = "exists" }));
    }
}

public class ValidationTests
{
    [Fact]
    public void Value_checks_without_oracle_source_are_rejected()
    {
        var testCase = new TestCase
        {
            Id = "X",
            Host = new HostSpec { App = "revit", Version = 2024 },
            Model = typeof(ValidationTests).Assembly.Location, // any existing file
            Run = new RunSpec { Mode = "postcommand", Command = "ID_X" },
            Expect = { new Expectation { Kind = "added", Count = 1 } },
        };

        var errors = TestCaseLoader.Validate(testCase);

        Assert.Contains(errors, e => e.Contains("source"));
    }
}

public class JsonInteropTests
{
    [Fact]
    public void Bridge_json_reads_back_in_runner_json()
    {
        var info = new BridgeInfo { Pid = 42, Host = "revit", HostVersion = "2024", PipeName = "hicastest-revit-42" };

        var json = JsonCodec.Serialize(info);
        var back = System.Text.Json.JsonSerializer.Deserialize<BridgeInfo>(json, Bridge.BridgeClient.Json)!;

        Assert.Equal("hicastest-revit-42", back.PipeName);
        Assert.Equal(42, back.Pid);
    }

    [Fact]
    public void Runner_json_reads_back_in_bridge_json()
    {
        var request = new QueryRequest { Category = "OST_Walls", Read = { new ParamRead { Name = "Width", Unit = "mm" } } };

        var json = System.Text.Json.JsonSerializer.Serialize(request, Bridge.BridgeClient.Json);
        var back = JsonCodec.Deserialize<QueryRequest>(json);

        Assert.Equal("OST_Walls", back.Category);
        Assert.Equal("mm", back.Read.Single().Unit);
    }
}
