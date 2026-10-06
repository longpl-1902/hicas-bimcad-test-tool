using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Autodesk.Revit.DB;

namespace HicasTest.SampleEntries
{
    // The feature "create a level", split as the convention asks: Validate (pure) -> Plan (pure, given a snapshot)
    // -> Apply (the only step that writes). Execute is the use case the ribbon command would call.

    [DataContract]
    public sealed class LevelRequest
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "elevationMm")] public double ElevationMm { get; set; }
    }

    [DataContract]
    public sealed class PromptRequest
    {
        [DataMember(Name = "id")] public string Id { get; set; }
        [DataMember(Name = "severity")] public string Severity { get; set; }
        [DataMember(Name = "message")] public string Message { get; set; }
        [DataMember(Name = "options")] public List<string> Options { get; set; } = new List<string>();
        [DataMember(Name = "default")] public string Default { get; set; }
    }

    /// <summary>The only way the use case warns or asks. Production: a dialog. Tests: HicasTest.Contracts.TestContext.</summary>
    public interface IUserPrompt
    {
        string Ask(PromptRequest prompt);
    }

    [DataContract]
    public sealed class LevelValidation
    {
        [DataMember(Name = "errors")] public List<string> Errors { get; set; } = new List<string>();
        [DataMember(Name = "prompts")] public List<PromptRequest> Prompts { get; set; } = new List<PromptRequest>();
    }

    [DataContract]
    public sealed class LevelPlan
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "elevationMm")] public double ElevationMm { get; set; }
        [DataMember(Name = "renamed")] public bool Renamed { get; set; }
    }

    [DataContract]
    public sealed class LevelResult
    {
        /// <summary>created | renamed | cancelled | invalid</summary>
        [DataMember(Name = "status")] public string Status { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "elevationMm")] public double ElevationMm { get; set; }
        [DataMember(Name = "levelId")] public string LevelId { get; set; }
        [DataMember(Name = "errors")] public List<string> Errors { get; set; } = new List<string>();
    }

    public static class LevelFeature
    {
        public const string DuplicateNamePrompt = "sample.level.duplicate-name";
        private const double MaxElevationMm = 200000;

        /// <summary>Pure: what is wrong with the request, and which prompts the flow would raise.</summary>
        public static LevelValidation Validate(LevelRequest request, ICollection<string> existingNames)
        {
            var result = new LevelValidation();
            if (string.IsNullOrWhiteSpace(request.Name))
                result.Errors.Add("name is required");
            if (Math.Abs(request.ElevationMm) > MaxElevationMm)
                result.Errors.Add("elevationMm must be within +-" + MaxElevationMm);
            if (result.Errors.Count == 0 && existingNames.Contains(request.Name, StringComparer.OrdinalIgnoreCase))
                result.Prompts.Add(DuplicatePrompt(request.Name));
            return result;
        }

        /// <summary>Pure: what Apply would create. A duplicate name is kept as asked or renamed to the next free "name (n)".</summary>
        public static LevelPlan Plan(LevelRequest request, ICollection<string> existingNames, bool renameDuplicate)
        {
            var name = request.Name.Trim();
            var renamed = false;
            if (renameDuplicate && existingNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                var n = 2;
                while (existingNames.Contains(name + " (" + n + ")", StringComparer.OrdinalIgnoreCase))
                    n++;
                name = name + " (" + n + ")";
                renamed = true;
            }
            return new LevelPlan { Name = name, ElevationMm = request.ElevationMm, Renamed = renamed };
        }

        /// <summary>The only step that writes: one named transaction.</summary>
        public static LevelResult Apply(Document document, LevelPlan plan)
        {
            using (var transaction = new Transaction(document, "Sample: create level"))
            {
                transaction.Start();
                var level = Level.Create(document, plan.ElevationMm / 304.8);
                level.Name = plan.Name;
                transaction.Commit();
                return new LevelResult
                {
                    Status = plan.Renamed ? "renamed" : "created",
                    Name = level.Name,
                    ElevationMm = plan.ElevationMm,
                    LevelId = level.Id.IntegerValue.ToString(),
                };
            }
        }

        /// <summary>The use case: Validate -> (prompts) -> Plan -> Apply.</summary>
        public static LevelResult Execute(Document document, LevelRequest request, IUserPrompt prompt)
        {
            var names = Names(document);
            var validation = Validate(request, names);
            if (validation.Errors.Count > 0)
                return new LevelResult { Status = "invalid", Errors = validation.Errors };

            var rename = false;
            foreach (var raised in validation.Prompts)
            {
                var answer = prompt.Ask(raised);
                if (raised.Id == DuplicateNamePrompt)
                {
                    if (string.Equals(answer, "cancel", StringComparison.OrdinalIgnoreCase))
                        return new LevelResult { Status = "cancelled", Name = request.Name };
                    rename = true;
                }
            }
            return Apply(document, Plan(request, names, rename));
        }

        /// <summary>Host read (snapshot) used by the entries.</summary>
        public static List<string> Names(Document document) =>
            new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>().Select(l => l.Name).ToList();

        private static PromptRequest DuplicatePrompt(string name) => new PromptRequest
        {
            Id = DuplicateNamePrompt,
            Severity = "confirm",
            Message = "A level named '" + name + "' exists.",
            Options = new List<string> { "rename", "cancel" },
            Default = "rename",
        };
    }

    internal static class Json
    {
        public static T Parse<T>(string json) where T : class, new()
        {
            if (string.IsNullOrWhiteSpace(json))
                return new T();
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream) ?? new T();
        }

        public static string Write<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
