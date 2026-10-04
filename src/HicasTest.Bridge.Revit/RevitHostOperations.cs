using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using HicasTest.Bridge.Core;
using HicasTest.Protocol;

namespace HicasTest.Bridge.Revit
{
    /// <summary>
    /// Revit side of the protocol. Members run on Revit's main thread (dispatcher or Revit events), so no locks.
    /// Builds for Revit 2021–2027; year differences live in ElementIds and build\HostVersions.props.
    /// </summary>
    public sealed class RevitHostOperations : IHostOperations
    {
        private const int DefaultQueryLimit = 500;

        // PostCommand runs after the current API context returns; we treat the command as finished once
        // Revit has been idle this many times after posting. Verify in the first spike (docs/architecture.md).
        private const int IdleTicksAfterPost = 2;

        private readonly RevitDispatcher _dispatcher;
        private readonly ChangeLog _changes = new ChangeLog();
        private readonly List<string> _warnings = new List<string>();
        private readonly List<DialogEvent> _dialogEvents = new List<DialogEvent>();
        private List<DialogRule> _rules = new List<DialogRule>();
        private bool _recording;

        private TaskCompletionSource<CommandResult> _pendingCommand;
        private Stopwatch _commandWatch;
        private int _idleTicks;

        public RevitHostOperations(RevitDispatcher dispatcher, string revitVersion, int pid)
        {
            _dispatcher = dispatcher;
            Info = new BridgeInfo
            {
                Pid = pid,
                Host = "revit",
                HostVersion = revitVersion,
                PipeName = "hicastest-revit-" + pid,
                BridgeVersion = typeof(RevitHostOperations).Assembly.GetName().Version.ToString(),
                ProtocolVersion = Methods.ProtocolVersion,
            };
        }

        public BridgeInfo Info { get; }

        private UIApplication App => _dispatcher.Current ?? throw new InvalidOperationException("No Revit API context yet.");

        private Document ActiveDocument => App.ActiveUIDocument?.Document ?? throw new InvalidOperationException("No active document.");

        public DocumentInfo OpenDocument(OpenDocumentRequest request)
        {
            var uiDocument = App.OpenAndActivateDocument(request.Path);
            return new DocumentInfo { Title = uiDocument.Document.Title, Path = uiDocument.Document.PathName };
        }

        public void CloseDocument(CloseDocumentRequest request)
        {
            // The API cannot close the active document. The runner works on a copy and ends the Revit process instead.
        }

        public void StartRecording()
        {
            _changes.Clear();
            _warnings.Clear();
            _recording = true;
        }

        public ChangeSet StopRecording()
        {
            _recording = false;
            var document = ActiveDocument;
            return new ChangeSet
            {
                Added = _changes.AddedIds.Select(id => Describe(document, id)).ToList(),
                Modified = _changes.ModifiedIds.Select(id => Describe(document, id)).ToList(),
                Deleted = _changes.DeletedIds.Select(id => new ElementRef { Id = id }).ToList(),
                Warnings = _warnings.ToList(),
            };
        }

        public Task<CommandResult> RunCommandAsync(RunCommandRequest request)
        {
            switch ((request.Mode ?? string.Empty).ToLowerInvariant())
            {
                case "postcommand":
                    return PostCommand(request.Command);
                case "invoke":
                    return Task.FromResult(Invoke(request));
                default:
                    return Task.FromResult(Failed("Unsupported mode '" + request.Mode + "' for Revit. Use postcommand or invoke."));
            }
        }

        public QueryResult Query(QueryRequest request)
        {
            var document = ActiveDocument;
            var collector = new FilteredElementCollector(document).WhereElementIsNotElementType();
            if (!string.IsNullOrEmpty(request.Category) && request.Category != "*")
                collector = collector.OfCategory(RevitMaps.Category(request.Category));

            IEnumerable<Element> elements = collector;
            if (request.OnlyIds != null && request.OnlyIds.Count > 0)
            {
                var only = new HashSet<string>(request.OnlyIds);
                elements = elements.Where(e => only.Contains(ElementIds.Key(e.Id)));
            }

            var limit = request.Limit > 0 ? request.Limit : DefaultQueryLimit;
            var reads = (request.Read ?? new List<ParamRead>())
                .Concat((request.Where ?? new List<Filter>()).Select(f => new ParamRead { Name = f.Param, Unit = f.Unit }))
                .GroupBy(r => r.Name + "|" + r.Unit).Select(g => g.First()).ToList();

            var result = new QueryResult();
            foreach (var element in elements)
            {
                var values = reads.Select(r => ReadParameter(element, r.Name, r.Unit)).ToList();
                if (!(request.Where ?? new List<Filter>()).All(f => FilterEvaluator.Matches(values.First(v => v.Name == f.Param && v.Unit == UnitLabel(f.Unit)), f)))
                    continue;

                if (result.Elements.Count == limit)
                {
                    result.Truncated = true;
                    break;
                }

                result.Elements.Add(new ElementSnapshot
                {
                    Id = ElementIds.Key(element.Id),
                    Category = RevitMaps.CategoryName(element),
                    Name = element.Name,
                    Values = values,
                });
            }

            return result;
        }

        public ExportImageResult ExportImage(ExportImageRequest request)
        {
            var document = ActiveDocument;
            var directory = Path.GetDirectoryName(request.Path);
            var baseName = Path.GetFileNameWithoutExtension(request.Path);
            Directory.CreateDirectory(directory);

            document.ExportImage(new ImageExportOptions
            {
                ExportRange = ExportRange.VisibleRegionOfCurrentView,
                FilePath = Path.Combine(directory, baseName),
                ZoomType = ZoomFitType.FitToPage,
                PixelSize = 1600,
                HLRandWFViewsFileType = ImageFileType.PNG,
                ShadowViewsFileType = ImageFileType.PNG,
                ImageResolution = ImageResolution.DPI_150,
            });

            // Revit may append the view name to the file name; return the newest match.
            var written = new DirectoryInfo(directory).GetFiles(baseName + "*.png")
                .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            return new ExportImageResult { Path = written?.FullName };
        }

        public void SetDialogRules(DialogRulesRequest request)
        {
            _rules = request.Rules ?? new List<DialogRule>();
        }

        public DialogEventList TakeDialogEvents()
        {
            var list = new DialogEventList { Events = _dialogEvents.ToList() };
            _dialogEvents.Clear();
            return list;
        }

        // ---- Revit events (main thread) ----

        public void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            if (!_recording)
                return;
            foreach (var id in e.GetAddedElementIds())
                _changes.Added(Key(id));
            foreach (var id in e.GetModifiedElementIds())
                _changes.Modified(Key(id));
            foreach (var id in e.GetDeletedElementIds())
                _changes.Deleted(Key(id));
        }

        public void OnFailuresProcessing(object sender, FailuresProcessingEventArgs e)
        {
            if (!_recording)
                return;
            foreach (var message in e.GetFailuresAccessor().GetFailureMessages())
                _warnings.Add(message.GetSeverity() + ": " + message.GetDescriptionText());
        }

        public void OnDialogBoxShowing(object sender, DialogBoxShowingEventArgs e)
        {
            var message = (e as TaskDialogShowingEventArgs)?.Message ?? (e as MessageBoxShowingEventArgs)?.Message ?? string.Empty;
            var dialogEvent = new DialogEvent { Source = "bridge", DialogId = e.DialogId, Message = message };

            var rule = _rules.FirstOrDefault(r => Matches(r.Match, e.DialogId, message));
            if (rule != null && RevitMaps.TryAnswer(rule.Answer, out var code))
            {
                e.OverrideResult(code);
                dialogEvent.Answer = rule.Answer;
                dialogEvent.Handled = true;
            }

            _dialogEvents.Add(dialogEvent);
        }

        public void OnIdling(object sender, IdlingEventArgs e)
        {
            if (_pendingCommand == null || ++_idleTicks < IdleTicksAfterPost)
                return;

            var pending = _pendingCommand;
            _pendingCommand = null;
            pending.TrySetResult(new CommandResult
            {
                Status = "completed",
                DurationMs = _commandWatch.ElapsedMilliseconds,
                Message = "Revit returned to idle after the posted command. PostCommand gives no result code; check changes and dialogs.",
            });
        }

        // ---- helpers ----

        private Task<CommandResult> PostCommand(string command)
        {
            if (_pendingCommand != null)
                return Task.FromResult(Failed("Another posted command is still running."));

            var commandId = RevitCommandId.LookupCommandId(command);
            if (commandId == null && Enum.TryParse(command, true, out PostableCommand postable))
                commandId = RevitCommandId.LookupPostableCommandId(postable);
            if (commandId == null)
                return Task.FromResult(Failed("Unknown command id '" + command + "'."));
            if (!App.CanPostCommand(commandId))
                return Task.FromResult(Failed("Revit refuses to post '" + command + "' right now (CanPostCommand = false)."));

            _pendingCommand = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _idleTicks = 0;
            _commandWatch = Stopwatch.StartNew();
            App.PostCommand(commandId);
            return _pendingCommand.Task;
        }

        private CommandResult Invoke(RunCommandRequest request)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                var message = ServiceInvoker.Invoke(request.AssemblyPath, request.TypeName, request.MethodName, App, request.Argument);
                return new CommandResult { Status = "completed", DurationMs = watch.ElapsedMilliseconds, Message = message };
            }
            catch (Exception ex)
            {
                BridgeLog.Error("invoke", ex);
                return new CommandResult { Status = "failed", DurationMs = watch.ElapsedMilliseconds, Message = ex.GetType().Name + ": " + ex.Message };
            }
        }

        private static ParamValue ReadParameter(Element element, string name, string unit)
        {
            var value = new ParamValue { Name = name, Unit = UnitLabel(unit) };
            if (string.Equals(name, "Name", StringComparison.OrdinalIgnoreCase))
            {
                value.Found = true;
                value.Text = value.Display = element.Name;
                return value;
            }

            var parameter = name.StartsWith("BIP:", StringComparison.OrdinalIgnoreCase)
                ? element.get_Parameter((BuiltInParameter)Enum.Parse(typeof(BuiltInParameter), name.Substring(4), true))
                : element.LookupParameter(name);
            if (parameter == null)
                return value;

            value.Found = true;
            value.Display = parameter.AsValueString();
            switch (parameter.StorageType)
            {
                case StorageType.Double:
                    var raw = parameter.AsDouble();
                    value.Number = string.IsNullOrEmpty(unit) ? raw : UnitUtils.ConvertFromInternalUnits(raw, RevitMaps.Unit(unit));
                    break;
                case StorageType.Integer:
                    value.Number = parameter.AsInteger();
                    break;
                case StorageType.String:
                    value.Text = parameter.AsString();
                    break;
                case StorageType.ElementId:
                    value.Text = ElementIds.Key(parameter.AsElementId());
                    break;
            }

            return value;
        }

        private static string UnitLabel(string unit) => string.IsNullOrEmpty(unit) ? "internal" : unit;

        private static ElementRef Describe(Document document, string id)
        {
            var element = document.GetElement(ElementIds.Parse(id));
            return new ElementRef { Id = id, Category = RevitMaps.CategoryName(element), Name = element?.Name };
        }

        private static string Key(ElementId id) => ElementIds.Key(id);

        private static bool Matches(string pattern, string dialogId, string message)
        {
            if (string.IsNullOrEmpty(pattern))
                return false;
            if (pattern == "*")
                return true;
            return (dialogId ?? string.Empty).IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0
                   || (message ?? string.Empty).IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static CommandResult Failed(string message) => new CommandResult { Status = "failed", Message = message };
    }
}
