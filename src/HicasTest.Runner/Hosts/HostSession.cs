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

internal static class HostProcess
{
    /// <summary>Ends a host that failed to start properly so it does not stay behind on a startup dialog.</summary>
    public static void KillQuietly(Process? process)
    {
        if (process == null)
            return;
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            process.WaitForExit(30_000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // already gone or exiting
        }
        finally
        {
            process.Dispose();
        }
    }
}

public interface IHostLauncher
{
    Task<HostSession> StartAsync(Model.TestCase testCase, RunOptions options, DialogDriver dialogs, CancellationToken ct);
}
