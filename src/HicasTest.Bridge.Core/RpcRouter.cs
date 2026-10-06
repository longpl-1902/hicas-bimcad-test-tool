using System;
using System.Threading.Tasks;
using HicasTest.Protocol;

namespace HicasTest.Bridge.Core
{
    /// <summary>Maps protocol methods to <see cref="IHostOperations"/> calls on the main thread.</summary>
    public sealed class RpcRouter
    {
        private const int DefaultCommandTimeoutSec = 300;

        private readonly IMainThreadDispatcher _dispatcher;
        private readonly IHostOperations _ops;

        public RpcRouter(IMainThreadDispatcher dispatcher, IHostOperations ops)
        {
            _dispatcher = dispatcher;
            _ops = ops;
        }

        public async Task<RpcResponse> HandleAsync(RpcRequest request)
        {
            try
            {
                var payload = await DispatchAsync(request.Method, request.Payload).ConfigureAwait(false);
                return new RpcResponse { Id = request.Id, Ok = true, Payload = payload };
            }
            catch (Exception ex)
            {
                var root = ex.GetBaseException();
                BridgeLog.Error("rpc " + request.Method, ex);
                return new RpcResponse { Id = request.Id, Ok = false, Error = root.GetType().Name + ": " + root.Message };
            }
        }

        private async Task<string> DispatchAsync(string method, string payload)
        {
            switch (method)
            {
                case Methods.Info:
                    return JsonCodec.Serialize(_ops.Info);

                case Methods.OpenDocument:
                    var open = JsonCodec.Deserialize<OpenDocumentRequest>(payload);
                    return await OnMainThread(() => _ops.OpenDocument(open)).ConfigureAwait(false);

                case Methods.CloseDocument:
                    var close = JsonCodec.Deserialize<CloseDocumentRequest>(payload);
                    return await OnMainThread(() => { _ops.CloseDocument(close); return new Empty(); }).ConfigureAwait(false);

                case Methods.StartRecording:
                    return await OnMainThread(() => { _ops.StartRecording(); return new Empty(); }).ConfigureAwait(false);

                case Methods.StopRecording:
                    return await OnMainThread(() => _ops.StopRecording()).ConfigureAwait(false);

                case Methods.RunCommand:
                    return JsonCodec.Serialize(await RunCommandAsync(JsonCodec.Deserialize<RunCommandRequest>(payload)).ConfigureAwait(false));

                case Methods.Query:
                    var query = JsonCodec.Deserialize<QueryRequest>(payload);
                    return await OnMainThread(() => _ops.Query(query)).ConfigureAwait(false);

                case Methods.ExportImage:
                    var image = JsonCodec.Deserialize<ExportImageRequest>(payload);
                    return await OnMainThread(() => _ops.ExportImage(image)).ConfigureAwait(false);

                case Methods.SetDialogRules:
                    var rules = JsonCodec.Deserialize<DialogRulesRequest>(payload);
                    return await OnMainThread(() => { _ops.SetDialogRules(rules); return new Empty(); }).ConfigureAwait(false);

                case Methods.TakeDialogEvents:
                    return await OnMainThread(() => _ops.TakeDialogEvents()).ConfigureAwait(false);

                case Methods.EntriesList:
                    var entryList = JsonCodec.Deserialize<EntryListRequest>(payload);
                    return await OnMainThread(() => _ops.ListEntries(entryList)).ConfigureAwait(false);

                case Methods.EntriesCall:
                    var entryCall = JsonCodec.Deserialize<EntryCallRequest>(payload);
                    return await OnMainThread(() => _ops.CallEntry(entryCall)).ConfigureAwait(false);

                default:
                    throw new NotSupportedException("Unknown method '" + method + "'.");
            }
        }

        private async Task<CommandResult> RunCommandAsync(RunCommandRequest request)
        {
            var started = await _dispatcher.InvokeAsync(() => _ops.RunCommandAsync(request)).ConfigureAwait(false);
            var timeoutSec = request.TimeoutSec > 0 ? request.TimeoutSec : DefaultCommandTimeoutSec;
            var finished = await Task.WhenAny(started, Task.Delay(TimeSpan.FromSeconds(timeoutSec))).ConfigureAwait(false);
            if (finished != started)
                return new CommandResult { Status = "timeout", DurationMs = timeoutSec * 1000L, Message = "No completion signal from the host." };
            return await started.ConfigureAwait(false);
        }

        private async Task<string> OnMainThread<T>(Func<T> work)
        {
            var result = await _dispatcher.InvokeAsync(work).ConfigureAwait(false);
            return JsonCodec.Serialize(result);
        }
    }
}
