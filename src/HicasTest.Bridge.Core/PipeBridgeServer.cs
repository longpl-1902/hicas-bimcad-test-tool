using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HicasTest.Protocol;

namespace HicasTest.Bridge.Core
{
    /// <summary>
    /// Named-pipe server, one client at a time, one JSON object per line.
    /// Runs on a background thread; host work goes through <see cref="RpcRouter"/>.
    /// </summary>
    public sealed class PipeBridgeServer : IDisposable
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        private readonly string _pipeName;
        private readonly RpcRouter _router;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public PipeBridgeServer(string pipeName, RpcRouter router)
        {
            _pipeName = pipeName;
            _router = router;
        }

        public void Start()
        {
            Task.Run(() => AcceptLoopAsync(_cts.Token));
        }

        public void Dispose()
        {
            _cts.Cancel();
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            BridgeLog.Info("pipe server listening on " + _pipeName);
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using (var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                               PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                    {
                        await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                        await ServeClientAsync(pipe).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    BridgeLog.Error("pipe server", ex);
                }
            }
        }

        private async Task ServeClientAsync(Stream pipe)
        {
            using (var reader = new StreamReader(pipe, Utf8, false, 4096, true))
            using (var writer = new StreamWriter(pipe, Utf8, 4096, true) { AutoFlush = true })
            {
                string line;
                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    if (line.Length == 0)
                        continue;

                    RpcResponse response;
                    try
                    {
                        var request = JsonCodec.Deserialize<RpcRequest>(line);
                        response = await _router.HandleAsync(request).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        response = new RpcResponse { Ok = false, Error = "Bad request: " + ex.Message };
                    }

                    await writer.WriteLineAsync(JsonCodec.Serialize(response)).ConfigureAwait(false);
                }
            }
        }
    }
}
