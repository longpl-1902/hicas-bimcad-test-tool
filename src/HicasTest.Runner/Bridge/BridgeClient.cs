using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HicasTest.Protocol;

namespace HicasTest.Runner.Bridge;

public sealed class BridgeException(string message) : Exception(message);

/// <summary>Client for a bridge's named pipe. Not thread-safe: one call at a time.</summary>
public sealed class BridgeClient : IDisposable
{
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

    public async Task<TResponse> CallAsync<TResponse>(string method, object? request, CancellationToken ct)
    {
        var message = new RpcRequest
        {
            Id = ++_nextId,
            Method = method,
            Payload = JsonSerializer.Serialize(request ?? new Empty(), Json),
        };
        await _writer.WriteLineAsync(JsonSerializer.Serialize(message, Json).AsMemory(), ct);

        var line = await _reader.ReadLineAsync(ct) ?? throw new IOException("The bridge closed the pipe (host exited?).");
        var response = JsonSerializer.Deserialize<RpcResponse>(line, Json) ?? throw new IOException("Empty response from bridge.");
        if (!response.Ok)
            throw new BridgeException($"{method}: {response.Error}");
        return JsonSerializer.Deserialize<TResponse>(response.Payload ?? "{}", Json)
               ?? throw new IOException($"{method}: empty payload.");
    }

    public Task<BridgeInfo> InfoAsync(CancellationToken ct) => CallAsync<BridgeInfo>(Methods.Info, null, ct);

    public void Dispose()
    {
        _reader.Dispose();
        _writer.Dispose();
        _pipe.Dispose();
    }
}
