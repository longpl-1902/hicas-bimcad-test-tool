using System.Text.Json.Nodes;
using YamlDotNet.Serialization;

namespace HicasTest.Runner.Model;

/// <summary>One call of a test entry in a case with run.mode entries. Calls run in order in the same host session.</summary>
public sealed class CallSpec
{
    /// <summary>Name used in the report; defaults to call-N.</summary>
    public string? Id { get; set; }

    /// <summary>Entry name from the add-in's contract, e.g. keyplan.create or keyplan.create.plan.</summary>
    public string Entry { get; set; } = "";

    /// <summary>The feature's request. Plain scalars keep their type (100 is a number, "100" a string).</summary>
    public JsonNode? Argument { get; set; }

    /// <summary>JSON file with the request, relative to the case file. Alternative to Argument.</summary>
    public string? ArgumentFile { get; set; }

    /// <summary>Scripted answers to the feature's prompts. A prompt without an answer takes its default.</summary>
    public List<AnswerSpec> Answers { get; set; } = new();

    /// <summary>Checks on the JSON the entry returned.</summary>
    public List<ResultCheck> ExpectResult { get; set; } = new();

    /// <summary>Checks on the prompts the feature raised during this call.</summary>
    public List<PromptCheck> ExpectPrompts { get; set; } = new();

    /// <summary>Model checks on what this call changed (same kinds as the case-level expect).</summary>
    public List<Expectation> Expect { get; set; } = new();

    /// <summary>Longest wait for this call; default run.timeoutSec.</summary>
    public int? TimeoutSec { get; set; }

    [YamlIgnore] public string? ArgumentFilePath { get; set; }
}

public sealed class AnswerSpec
{
    public string Id { get; set; } = "";
    public string Option { get; set; } = "";
}

/// <summary>A check on one value of the result JSON. Exactly one of equals / min+max / exists / count / contains.</summary>
public sealed class ResultCheck
{
    /// <summary>Dotted path with array indexes: status, items[0].name, view.scale.</summary>
    public string Path { get; set; } = "";

    [YamlMember(Alias = "equals")] public string? Expected { get; set; }
    public double? Tolerance { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public bool? Exists { get; set; }

    /// <summary>Length of the array (or number of properties of the object) at the path.</summary>
    public int? Count { get; set; }

    public string? Contains { get; set; }

    /// <summary>Independent source of the expected value.</summary>
    public string? Source { get; set; }

    public string? Note { get; set; }
}

public sealed class PromptCheck
{
    public string Id { get; set; } = "";

    /// <summary>true (default): the prompt must be raised; false: it must not.</summary>
    public bool Raised { get; set; } = true;

    public string? Severity { get; set; }

    /// <summary>The answer the prompt was given (scripted or default).</summary>
    public string? Answer { get; set; }

    public string? Source { get; set; }
}
