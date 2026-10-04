using System.Runtime.Serialization;

namespace HicasTest.Protocol
{
    /// <summary>One request line on the bridge pipe. <see cref="Payload"/> is the JSON of the method's request DTO.</summary>
    [DataContract]
    public sealed class RpcRequest
    {
        [DataMember(Name = "id")] public int Id { get; set; }
        [DataMember(Name = "method")] public string Method { get; set; }
        [DataMember(Name = "payload")] public string Payload { get; set; }
    }

    /// <summary>One response line on the bridge pipe.</summary>
    [DataContract]
    public sealed class RpcResponse
    {
        [DataMember(Name = "id")] public int Id { get; set; }
        [DataMember(Name = "ok")] public bool Ok { get; set; }
        [DataMember(Name = "payload")] public string Payload { get; set; }
        [DataMember(Name = "error")] public string Error { get; set; }
    }

    /// <summary>Payload for methods that take or return nothing.</summary>
    [DataContract]
    public sealed class Empty
    {
    }

    public static class Methods
    {
        public const string ProtocolVersion = "1";

        public const string Info = "bridge.info";
        public const string OpenDocument = "doc.open";
        public const string CloseDocument = "doc.close";
        public const string StartRecording = "recorder.start";
        public const string StopRecording = "recorder.stop";
        public const string RunCommand = "command.run";
        public const string Query = "elements.query";
        public const string ExportImage = "view.exportImage";
        public const string SetDialogRules = "dialogs.setRules";
        public const string TakeDialogEvents = "dialogs.take";
    }
}
