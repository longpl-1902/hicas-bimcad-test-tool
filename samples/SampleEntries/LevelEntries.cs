using System;
using System.Linq;
using Autodesk.Revit.UI;
using HicasTest.Contracts;

namespace HicasTest.SampleEntries
{
    /// <summary>The test implementation of IUserPrompt: prompts are data (docs/test-entries.md).</summary>
    internal sealed class TestPrompt : IUserPrompt
    {
        public string Ask(PromptRequest prompt) =>
            TestContext.Ask(prompt.Id, prompt.Severity, prompt.Message, prompt.Options.ToArray(), prompt.Default);
    }

    /// <summary>Main gate = the use case the ribbon command calls. Step gates = its small steps.</summary>
    public static class LevelEntries
    {
        private const string Contract = "samples/SampleEntries/sample.level.create.md";

        [HicasTestEntry("sample.level.create", Description = "Create a level (prompt sample.level.duplicate-name when the name exists)", Contract = Contract)]
        public static string Create(UIApplication app, string requestJson)
        {
            var result = LevelFeature.Execute(app.ActiveUIDocument.Document, Json.Parse<LevelRequest>(requestJson), new TestPrompt());
            return Json.Write(result);
        }

        [HicasTestEntry("sample.level.create.validate", Description = "Errors and prompts the flow would raise, nothing written", Contract = Contract, ReadOnly = true)]
        public static string Validate(UIApplication app, string requestJson)
        {
            var document = app.ActiveUIDocument.Document;
            return Json.Write(LevelFeature.Validate(Json.Parse<LevelRequest>(requestJson), LevelFeature.Names(document)));
        }

        [HicasTestEntry("sample.level.create.plan", Description = "What would be created; renameDuplicate is assumed true", Contract = Contract, ReadOnly = true)]
        public static string Plan(UIApplication app, string requestJson)
        {
            var document = app.ActiveUIDocument.Document;
            return Json.Write(LevelFeature.Plan(Json.Parse<LevelRequest>(requestJson), LevelFeature.Names(document), renameDuplicate: true));
        }

        [HicasTestEntry("sample.level.create.apply", Description = "Apply a plan (the plan JSON of .plan)", Contract = Contract)]
        public static string Apply(UIApplication app, string planJson) =>
            Json.Write(LevelFeature.Apply(app.ActiveUIDocument.Document, Json.Parse<LevelPlan>(planJson)));

        // Negative test of the tool, not a feature: a modal dialog freezes the host, HicasTest must stop the case instead of waiting.
        [HicasTestEntry("sample.test.blocking-dialog", Description = "Shows a modal dialog (tests the unexpected-dialog guard only)")]
        public static string BlockingDialog(UIApplication app, string requestJson)
        {
            TaskDialog.Show("HicasTest sample", "A test entry must not show this dialog.");
            return "{}";
        }

        [HicasTestEntry("sample.level.names", Description = "Names of the existing levels (to choose test data)", ReadOnly = true)]
        public static string Names(UIApplication app) =>
            "{\"names\":[" + string.Join(",", LevelFeature.Names(app.ActiveUIDocument.Document).Select(n => "\"" + n.Replace("\"", "\\\"") + "\"")) + "]}";
    }
}
