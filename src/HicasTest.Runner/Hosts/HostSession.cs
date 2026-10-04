using System.Diagnostics;
using HicasTest.Protocol;
using HicasTest.Runner.Bridge;

namespace HicasTest.Runner.Hosts;

/// <summary>A running host process with a connected bridge. Disposing ends the process and undoes launch-time setup.</summary>
public sealed class HostSession(Process process, BridgeInfo info, BridgeClient client, Action cleanup) : IDisposable
{
    public Process Process { get; } = process;
    public BridgeInfo Info { get; } = info;
    public BridgeClient Client { get; } = client;

    public void Dispose()
    {
        Client.Dispose();
        try
        {
            // The fixture is a throwaway copy; never wait on "save changes?" prompts.
            if (!Process.HasExited)
                Process.Kill(entireProcessTree: true);
            Process.WaitForExit(30_000);
        }
        catch (InvalidOperationException)
        {
            // already gone
        }
        finally
        {
            cleanup();
            Process.Dispose();
        }
    }
}

public interface IHostLauncher
{
    Task<HostSession> StartAsync(Model.TestCase testCase, RunOptions options, DialogDriver dialogs, CancellationToken ct);
}
