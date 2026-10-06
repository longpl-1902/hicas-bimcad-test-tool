using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using HicasTest.Bridge.Core;
using HicasTest.Protocol;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HicasTest.Bridge.AutoCAD
{
    /// <summary>
    /// AutoCAD side of the protocol. Members run on the main thread in application context (AcadDispatcher).
    /// Only entities are recorded; table/dictionary churn is ignored.
    /// </summary>
    public sealed class AcadHostOperations : IHostOperations
    {
        private const int DefaultQueryLimit = 500;

        private readonly ChangeLog _changes = new ChangeLog();
        private readonly Dictionary<string, ObjectId> _ids = new Dictionary<string, ObjectId>();
        private Database _recordedDatabase;

        public AcadHostOperations(string acadVersion, int pid)
        {
            Info = new BridgeInfo
            {
                Pid = pid,
                Host = "autocad",
                HostVersion = acadVersion,
                PipeName = "hicastest-autocad-" + pid,
                BridgeVersion = typeof(AcadHostOperations).Assembly.GetName().Version.ToString(),
                ProtocolVersion = Methods.ProtocolVersion,
            };
        }

        public BridgeInfo Info { get; }

        private static Document ActiveDocument =>
            AcApp.DocumentManager.MdiActiveDocument ?? throw new InvalidOperationException("No active drawing.");

        public DocumentInfo OpenDocument(OpenDocumentRequest request)
        {
            var document = AcApp.DocumentManager.Open(request.Path, false);
            AcApp.DocumentManager.MdiActiveDocument = document;
            return new DocumentInfo { Title = System.IO.Path.GetFileName(document.Name), Path = document.Name };
        }

        public void CloseDocument(CloseDocumentRequest request)
        {
            var document = ActiveDocument;
            if (request.Save)
                document.CloseAndSave(document.Name);
            else
                document.CloseAndDiscard();
        }

        public void StartRecording()
        {
            StopListening();
            _changes.Clear();
            _ids.Clear();
            _recordedDatabase = ActiveDocument.Database;
            _recordedDatabase.ObjectAppended += OnObjectAppended;
            _recordedDatabase.ObjectModified += OnObjectModified;
            _recordedDatabase.ObjectErased += OnObjectErased;
        }

        public ChangeSet StopRecording()
        {
            StopListening();
            var document = ActiveDocument;
            using (document.LockDocument())
            using (var transaction = document.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var result = new ChangeSet
                {
                    Added = _changes.AddedIds.Select(h => Describe(transaction, h)).ToList(),
                    Modified = _changes.ModifiedIds.Select(h => Describe(transaction, h)).ToList(),
                    Deleted = _changes.DeletedIds.Select(h => new ElementRef { Id = h }).ToList(),
                };
                transaction.Commit();
                return result;
            }
        }

        public Task<CommandResult> RunCommandAsync(RunCommandRequest request)
        {
            switch ((request.Mode ?? string.Empty).ToLowerInvariant())
            {
                case "commandline":
                    return SendCommand(request.Command);
                case "invoke":
                    return Task.FromResult(Invoke(request));
                default:
                    return Task.FromResult(Failed("Unsupported mode '" + request.Mode + "' for AutoCAD. Use commandline or invoke."));
            }
        }

        public QueryResult Query(QueryRequest request)
        {
            var document = ActiveDocument;
            var limit = request.Limit > 0 ? request.Limit : DefaultQueryLimit;
            var only = new HashSet<string>(request.OnlyIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var where = request.Where ?? new List<Filter>();
            var reads = (request.Read ?? new List<ParamRead>()).Select(r => r.Name)
                .Concat(where.Select(f => f.Param)).Distinct().ToList();
            var result = new QueryResult();

            using (document.LockDocument())
            using (var transaction = document.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var blockTable = (BlockTable)transaction.GetObject(document.Database.BlockTableId, OpenMode.ForRead);
                var modelSpace = (BlockTableRecord)transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in modelSpace)
                {
                    var dxfName = id.ObjectClass.DxfName;
                    if (!string.IsNullOrEmpty(request.Category) && request.Category != "*"
                        && !string.Equals(dxfName, request.Category, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var handle = id.Handle.ToString();
                    if (only.Count > 0 && !only.Contains(handle))
                        continue;

                    var entity = (Entity)transaction.GetObject(id, OpenMode.ForRead);
                    var values = reads.Select(name => ReadValue(transaction, entity, name)).ToList();
                    if (!where.All(f => FilterEvaluator.Matches(values.First(v => v.Name == f.Param), f)))
                        continue;

                    if (result.Elements.Count == limit)
                    {
                        result.Truncated = true;
                        break;
                    }

                    result.Elements.Add(new ElementSnapshot { Id = handle, Category = dxfName, Name = NameOf(transaction, entity), Values = values });
                }

                transaction.Commit();
            }

            return result;
        }

        public ExportImageResult ExportImage(ExportImageRequest request)
        {
            throw new NotSupportedException("Image export for AutoCAD is planned for phase 2.");
        }

        // AutoCAD has no dialog-override event; the runner's FlaUI driver answers dialogs.
        public void SetDialogRules(DialogRulesRequest request)
        {
        }

        public DialogEventList TakeDialogEvents() => new DialogEventList();

        public EntryListResult ListEntries(EntryListRequest request) => EntryInvoker.List(request.AssemblyPath);

        // Application context: the document is locked for the entry, as for invoke mode.
        public EntryCallResult CallEntry(EntryCallRequest request)
        {
            var document = ActiveDocument;
            using (document.LockDocument())
                return EntryInvoker.Call(request, document);
        }

        // ---- database events ----

        private void OnObjectAppended(object sender, ObjectEventArgs e)
        {
            if (e.DBObject is Entity)
                _changes.Added(Remember(e.DBObject.ObjectId));
        }

        private void OnObjectModified(object sender, ObjectEventArgs e)
        {
            if (e.DBObject is Entity && !e.DBObject.IsErased)
                _changes.Modified(Remember(e.DBObject.ObjectId));
        }

        private void OnObjectErased(object sender, ObjectErasedEventArgs e)
        {
            if (!(e.DBObject is Entity))
                return;
            var handle = Remember(e.DBObject.ObjectId);
            if (e.Erased)
                _changes.Deleted(handle);
            else
                _changes.Added(handle); // un-erased (undo)
        }

        // ---- helpers ----

        private Task<CommandResult> SendCommand(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
                return Task.FromResult(Failed("commandline mode needs a command."));

            var document = ActiveDocument;
            var commandName = commandLine.Trim().Split(' ', '\n')[0].TrimStart('_', '.', '-', '+').ToUpperInvariant();
            var tcs = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var watch = Stopwatch.StartNew();

            CommandEventHandler ended = null, cancelled = null, failed = null;
            void Finish(CommandEventArgs e, string status)
            {
                if (!string.Equals(e.GlobalCommandName, commandName, StringComparison.OrdinalIgnoreCase))
                    return; // nested or transparent command
                document.CommandEnded -= ended;
                document.CommandCancelled -= cancelled;
                document.CommandFailed -= failed;
                tcs.TrySetResult(new CommandResult { Status = status, DurationMs = watch.ElapsedMilliseconds });
            }

            ended = (s, e) => Finish(e, "completed");
            cancelled = (s, e) => Finish(e, "cancelled");
            failed = (s, e) => Finish(e, "failed");
            document.CommandEnded += ended;
            document.CommandCancelled += cancelled;
            document.CommandFailed += failed;

            var text = commandLine.EndsWith("\n", StringComparison.Ordinal) ? commandLine : commandLine + "\n";
            document.SendStringToExecute(text, true, false, false);
            return tcs.Task;
        }

        private static CommandResult Invoke(RunCommandRequest request)
        {
            var watch = Stopwatch.StartNew();
            var document = ActiveDocument;
            try
            {
                string message;
                using (document.LockDocument())
                    message = ServiceInvoker.Invoke(request.AssemblyPath, request.TypeName, request.MethodName, document, request.Argument);
                return new CommandResult { Status = "completed", DurationMs = watch.ElapsedMilliseconds, Message = message };
            }
            catch (Exception ex)
            {
                BridgeLog.Error("invoke", ex);
                return new CommandResult { Status = "failed", DurationMs = watch.ElapsedMilliseconds, Message = ex.GetType().Name + ": " + ex.Message };
            }
        }

        private static ParamValue ReadValue(Transaction transaction, Entity entity, string name)
        {
            var value = new ParamValue { Name = name, Unit = "drawing" };
            string text = null;
            double? number = null;

            if (string.Equals(name, "Handle", StringComparison.OrdinalIgnoreCase))
                text = entity.Handle.ToString();
            else if (string.Equals(name, "Layer", StringComparison.OrdinalIgnoreCase))
                text = entity.Layer;
            else if (string.Equals(name, "BlockName", StringComparison.OrdinalIgnoreCase))
                text = entity is BlockReference ? NameOf(transaction, entity) : null;
            else if (name.StartsWith("ATTR:", StringComparison.OrdinalIgnoreCase))
                text = ReadAttribute(transaction, entity as BlockReference, name.Substring(5));
            else
            {
                var property = entity.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (property == null || !property.CanRead)
                    return value;
                var raw = property.GetValue(entity, null);
                if (raw is double d) number = d;
                else if (raw is int i) number = i;
                else if (raw is short s) number = s;
                else text = raw?.ToString();
            }

            value.Found = text != null || number.HasValue;
            value.Text = text;
            value.Number = number;
            value.Display = number.HasValue ? number.Value.ToString(CultureInfo.InvariantCulture) : text;
            return value;
        }

        private static string ReadAttribute(Transaction transaction, BlockReference block, string tag)
        {
            if (block == null)
                return null;
            foreach (ObjectId attributeId in block.AttributeCollection)
            {
                var attribute = (AttributeReference)transaction.GetObject(attributeId, OpenMode.ForRead);
                if (string.Equals(attribute.Tag, tag, StringComparison.OrdinalIgnoreCase))
                    return attribute.TextString;
            }
            return null;
        }

        private static string NameOf(Transaction transaction, Entity entity)
        {
            if (!(entity is BlockReference block))
                return null;
            if (!block.IsDynamicBlock)
                return block.Name;
            var definition = (BlockTableRecord)transaction.GetObject(block.DynamicBlockTableRecord, OpenMode.ForRead);
            return definition.Name;
        }

        private ElementRef Describe(Transaction transaction, string handle)
        {
            var id = _ids[handle];
            var reference = new ElementRef { Id = handle, Category = id.ObjectClass.DxfName };
            if (!id.IsErased && transaction.GetObject(id, OpenMode.ForRead) is Entity entity)
                reference.Name = NameOf(transaction, entity);
            return reference;
        }

        private string Remember(ObjectId id)
        {
            var handle = id.Handle.ToString();
            _ids[handle] = id;
            return handle;
        }

        private void StopListening()
        {
            if (_recordedDatabase == null)
                return;
            _recordedDatabase.ObjectAppended -= OnObjectAppended;
            _recordedDatabase.ObjectModified -= OnObjectModified;
            _recordedDatabase.ObjectErased -= OnObjectErased;
            _recordedDatabase = null;
        }

        private static CommandResult Failed(string message) => new CommandResult { Status = "failed", Message = message };
    }
}
