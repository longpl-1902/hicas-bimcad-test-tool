using System.Collections.Generic;
using System.Runtime.Serialization;

namespace HicasTest.Protocol
{
    // Member names are camelCase so the runner's System.Text.Json (camelCase policy) and the
    // bridge's DataContractJsonSerializer agree. Avoid dictionaries and enums on the wire.

    [DataContract]
    public sealed class BridgeInfo
    {
        [DataMember(Name = "pid")] public int Pid { get; set; }
        [DataMember(Name = "host")] public string Host { get; set; }               // "revit" | "autocad"
        [DataMember(Name = "hostVersion")] public string HostVersion { get; set; }
        [DataMember(Name = "pipeName")] public string PipeName { get; set; }
        [DataMember(Name = "bridgeVersion")] public string BridgeVersion { get; set; }
        [DataMember(Name = "protocolVersion")] public string ProtocolVersion { get; set; }
    }

    [DataContract]
    public sealed class OpenDocumentRequest
    {
        [DataMember(Name = "path")] public string Path { get; set; }
    }

    [DataContract]
    public sealed class DocumentInfo
    {
        [DataMember(Name = "title")] public string Title { get; set; }
        [DataMember(Name = "path")] public string Path { get; set; }
    }

    [DataContract]
    public sealed class CloseDocumentRequest
    {
        [DataMember(Name = "save")] public bool Save { get; set; }
    }

    [DataContract]
    public sealed class RunCommandRequest
    {
        /// <summary>"postcommand" (Revit), "commandline" (AutoCAD) or "invoke" (both).</summary>
        [DataMember(Name = "mode")] public string Mode { get; set; }
        [DataMember(Name = "command")] public string Command { get; set; }
        [DataMember(Name = "assemblyPath")] public string AssemblyPath { get; set; }
        [DataMember(Name = "typeName")] public string TypeName { get; set; }
        [DataMember(Name = "methodName")] public string MethodName { get; set; }
        [DataMember(Name = "argument")] public string Argument { get; set; }
        [DataMember(Name = "timeoutSec")] public int TimeoutSec { get; set; }
    }

    [DataContract]
    public sealed class CommandResult
    {
        /// <summary>"completed" | "cancelled" | "failed" | "timeout".</summary>
        [DataMember(Name = "status")] public string Status { get; set; }
        [DataMember(Name = "durationMs")] public long DurationMs { get; set; }
        [DataMember(Name = "message")] public string Message { get; set; }
    }

    [DataContract]
    public sealed class ElementRef
    {
        /// <summary>Revit ElementId value or AutoCAD handle (hex).</summary>
        [DataMember(Name = "id")] public string Id { get; set; }
        [DataMember(Name = "category")] public string Category { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; }
    }

    [DataContract]
    public sealed class ChangeSet
    {
        [DataMember(Name = "added")] public List<ElementRef> Added { get; set; } = new List<ElementRef>();
        [DataMember(Name = "modified")] public List<ElementRef> Modified { get; set; } = new List<ElementRef>();
        [DataMember(Name = "deleted")] public List<ElementRef> Deleted { get; set; } = new List<ElementRef>();
        [DataMember(Name = "warnings")] public List<string> Warnings { get; set; } = new List<string>();
    }

    [DataContract]
    public sealed class Filter
    {
        [DataMember(Name = "param")] public string Param { get; set; }
        /// <summary>eq | ne | contains | gt | ge | lt | le | exists.</summary>
        [DataMember(Name = "op")] public string Op { get; set; }
        [DataMember(Name = "value")] public string Value { get; set; }
        [DataMember(Name = "tolerance")] public double? Tolerance { get; set; }
        [DataMember(Name = "unit")] public string Unit { get; set; }
    }

    [DataContract]
    public sealed class ParamRead
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "unit")] public string Unit { get; set; }
    }

    [DataContract]
    public sealed class QueryRequest
    {
        /// <summary>Revit BuiltInCategory name, AutoCAD DXF name, or "*".</summary>
        [DataMember(Name = "category")] public string Category { get; set; }
        [DataMember(Name = "where")] public List<Filter> Where { get; set; } = new List<Filter>();
        [DataMember(Name = "read")] public List<ParamRead> Read { get; set; } = new List<ParamRead>();
        /// <summary>Restrict to these ids (e.g. the elements a command changed). Empty = no restriction.</summary>
        [DataMember(Name = "onlyIds")] public List<string> OnlyIds { get; set; } = new List<string>();
        [DataMember(Name = "limit")] public int Limit { get; set; }
    }

    [DataContract]
    public sealed class ParamValue
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "found")] public bool Found { get; set; }
        [DataMember(Name = "number")] public double? Number { get; set; }
        [DataMember(Name = "text")] public string Text { get; set; }
        /// <summary>Value as the host displays it (Revit AsValueString).</summary>
        [DataMember(Name = "display")] public string Display { get; set; }
        [DataMember(Name = "unit")] public string Unit { get; set; }
    }

    [DataContract]
    public sealed class ElementSnapshot
    {
        [DataMember(Name = "id")] public string Id { get; set; }
        [DataMember(Name = "category")] public string Category { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "values")] public List<ParamValue> Values { get; set; } = new List<ParamValue>();
    }

    [DataContract]
    public sealed class QueryResult
    {
        [DataMember(Name = "elements")] public List<ElementSnapshot> Elements { get; set; } = new List<ElementSnapshot>();
        [DataMember(Name = "truncated")] public bool Truncated { get; set; }
    }

    [DataContract]
    public sealed class ExportImageRequest
    {
        [DataMember(Name = "path")] public string Path { get; set; }
    }

    [DataContract]
    public sealed class ExportImageResult
    {
        [DataMember(Name = "path")] public string Path { get; set; }
    }

    [DataContract]
    public sealed class DialogRule
    {
        /// <summary>Case-insensitive "contains" on dialog id, title or message. "*" matches all.</summary>
        [DataMember(Name = "match")] public string Match { get; set; }
        [DataMember(Name = "answer")] public string Answer { get; set; }
    }

    [DataContract]
    public sealed class DialogRulesRequest
    {
        [DataMember(Name = "rules")] public List<DialogRule> Rules { get; set; } = new List<DialogRule>();
    }

    [DataContract]
    public sealed class DialogEvent
    {
        [DataMember(Name = "source")] public string Source { get; set; }     // "bridge" | "flaui"
        [DataMember(Name = "dialogId")] public string DialogId { get; set; }
        [DataMember(Name = "message")] public string Message { get; set; }
        [DataMember(Name = "answer")] public string Answer { get; set; }
        [DataMember(Name = "handled")] public bool Handled { get; set; }
    }

    [DataContract]
    public sealed class DialogEventList
    {
        [DataMember(Name = "events")] public List<DialogEvent> Events { get; set; } = new List<DialogEvent>();
    }
}
