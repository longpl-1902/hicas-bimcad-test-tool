using YamlDotNet.Serialization;

namespace HicasTest.Runner.Model;

/// <summary>One level-B case. Format: docs/test-case-format.md.</summary>
public sealed class TestCase
{
    public string Id { get; set; } = "";
    public string? Ticket { get; set; }
    public string? Case { get; set; }
    public string Level { get; set; } = "B";

    /// <summary>Reserved for option 2 ("B-auto"). The runner ignores it today.</summary>
    public bool AutoPassEligible { get; set; }

    /// <summary>The manual script this case was translated from, e.g. qa-handover.md#AC-02.</summary>
    public string? Source { get; set; }

    public HostSpec Host { get; set; } = new();
    public AddinSpec? Addin { get; set; }
    public string Model { get; set; } = "";
    public RunSpec Run { get; set; } = new();
    public List<DialogSpec> Dialogs { get; set; } = new();

    /// <summary>run.mode entries: test-entry calls, in order, in one host session (docs/test-entries.md).</summary>
    public List<CallSpec> Calls { get; set; } = new();

    public List<Expectation> Expect { get; set; } = new();
    public EvidenceSpec Evidence { get; set; } = new();

    [YamlIgnore] public string FilePath { get; set; } = "";
    [YamlIgnore] public string RawYaml { get; set; } = "";
}

public sealed class HostSpec
{
    /// <summary>"revit" or "autocad".</summary>
    public string App { get; set; } = "";
    public int Version { get; set; }
    public string? ExePath { get; set; }
}

public sealed class AddinSpec
{
    /// <summary>Revit: existing .addin manifest of the build under test.</summary>
    public string? Manifest { get; set; }

    /// <summary>Revit: dll for a generated manifest. AutoCAD: dll to NETLOAD.</summary>
    public string? Assembly { get; set; }

    public string? FullClassName { get; set; }
    public string? AddinId { get; set; }
}

public sealed class RunSpec
{
    /// <summary>"postcommand" (Revit), "commandline" (AutoCAD), "invoke" or "entries" (assembly = the add-in test assembly).</summary>
    public string Mode { get; set; } = "";
    public string? Command { get; set; }
    public string? Assembly { get; set; }
    public string? Type { get; set; }
    public string? Method { get; set; }
    public string? Argument { get; set; }
    public int TimeoutSec { get; set; } = 300;
}

public sealed class DialogSpec
{
    public string Match { get; set; } = "";
    public string Answer { get; set; } = "";
}

public sealed class Expectation
{
    /// <summary>added | modified | deleted | count | param | no-warnings | command-status.</summary>
    public string Kind { get; set; } = "";
    public string? Category { get; set; }
    public List<FilterSpec> Where { get; set; } = new();
    public string? Param { get; set; }

    [YamlMember(Alias = "equals")] public string? Expected { get; set; }
    public double? Tolerance { get; set; }
    public string? Unit { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public int? Count { get; set; }

    /// <summary>"all" (default) or "changed" = only elements the command added or modified.</summary>
    public string Scope { get; set; } = "all";

    /// <summary>Independent source of the expected value (contract column "Kết quả đúng (kèm nguồn)").</summary>
    public string? Source { get; set; }

    public string? Note { get; set; }
}

public sealed class FilterSpec
{
    public string Param { get; set; } = "";
    public string Op { get; set; } = "eq";
    public string? Value { get; set; }
    public double? Tolerance { get; set; }
    public string? Unit { get; set; }
}

public sealed class EvidenceSpec
{
    /// <summary>Export the active view at the end. Default: yes, except for run.mode entries (headless).</summary>
    public bool? Image { get; set; }
}
