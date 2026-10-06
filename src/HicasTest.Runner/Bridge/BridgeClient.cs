using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HicasTest.Protocol;

namespace HicasTest.Runner.Bridge;

public sealed class BridgeException(string message) : Exception(message);

/// <summary>Client for a bridge's named pipe. Calls are serialized: the bridge answers one request at a time.</summary>
public sealed class BridgeClient : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Task<string?>? _pendingRead;

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly NamedPipeClientStream _pipe;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private int _nextId;

    private BridgeClient(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
        var utf8 = new UTF8Encoding(false);
        _reader = new StreamReader(pipe, utf8, false, 4096, leaveOpen: true);
        _writer = new StreamWriter(pipe, utf8, 4096, leaveOpen: true) { AutoFlush = true };
    }

    public static async Task<BridgeClient> ConnectAsync(string pipeName, TimeSpan timeout, CancellationToken ct)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(timeout);
            await pipe.ConnectAsync(linked.Token);
            return new BridgeClient(pipe);
        }
        catch
        {
            await pipe.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// One call at a time: a second caller waits for the pipe (e.g. a UI flow while the command call is pending).
    /// With <paramref name="timeout"/> the call gives up waiting; its late answer is skipped by the next call.
    /// </summary>
    public async Task<TResponse> CallAsync<TResponse>(string method, object? request, CancellationToken ct, TimeSpan? timeout = null)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout is { } t)
            limit.CancelAfter(t);

        try
        {
            await _gate.WaitAsync(limit.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new BridgeException($"{method}: the bridge is busy with another call (a command is still running).");
        }

        try
        {
            var message = new RpcRequest
            {
                Id = ++_nextId,
                Method = method,
                Payload = JsonSerializer.Serialize(request ?? new Empty(), Json),
            };
            await _writer.WriteLineAsync(JsonSerializer.Serialize(message, Json).AsMemory(), ct);

            RpcResponse response;
            do
            {
                string? line;
                try
                {
                    line = await NextLineAsync(limit.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new BridgeException($"{method}: no answer within {timeout?.TotalSeconds:0} s (the host is busy: command, pick or modal dialog).");
                }
                if (line == null)
                    throw new IOException("The bridge closed the pipe (host exited?).");
                response = JsonSerializer.Deserialize<RpcResponse>(line, Json) ?? throw new IOException("Empty response from bridge.");
            }
            while (response.Id != message.Id); // a late answer to an earlier call that timed out

            if (!response.Ok)
                throw new BridgeException($"{method}: {response.Error}");
            return JsonSerializer.Deserialize<TResponse>(response.Payload ?? "{}", Json)
                   ?? throw new IOException($"{method}: empty payload.");
        }
        finally
        {
            _gate.Release();
        }
    }

    // A read is never cancelled half-way (that would break the reader); a timed-out read is finished by the next call.
    private async Task<string?> NextLineAsync(CancellationToken ct)
    {
        _pendingRead ??= _reader.ReadLineAsync();
        var line = await _pendingRead.WaitAsync(ct);
        _pendingRead = null;
        return line;
    }

    public Task<BridgeInfo> InfoAsync(CancellationToken ct) => CallAsync<BridgeInfo>(Methods.Info, null, ct);

    public void Dispose()
    {
        _reader.Dispose();
        _writer.Dispose();
        _pipe.Dispose();
    }
}
