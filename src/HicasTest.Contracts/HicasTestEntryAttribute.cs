using System;

namespace HicasTest.Contracts
{
    /// <summary>
    /// Marks a public static method of the add-in's test assembly as a test entry: one gate of a feature that
    /// HicasTest (MCP tools list_entries / call_entry) can call. Signature: (host context, string requestJson) → string resultJson;
    /// the host context is UIApplication (Revit) or Document (AutoCAD), matched by type.
    /// Main gate: "feature.action". Step gates: "feature.action.validate", ".plan", ".apply".
    /// See docs/test-entries.md in the HicasTest repository.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class HicasTestEntryAttribute : Attribute
    {
        public HicasTestEntryAttribute(string name)
        {
            Name = name;
        }

        public string Name { get; private set; }

        public string Description { get; set; }

        /// <summary>Path of the contract file (request, result, prompt ids), relative to the add-in's repository.</summary>
        public string Contract { get; set; }

        /// <summary>The entry never writes to the model (validate, plan).</summary>
        public bool ReadOnly { get; set; }
    }
}
