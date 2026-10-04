using System.Diagnostics;
using System.Text.Json;
using HicasTest.Bridge.Core;
using HicasTest.Protocol;

namespace HicasTest.Runner.Bridge;

/// <summary>Reads the discovery files written by running bridges.</summary>
public static class BridgeDiscovery
{
    public static IReadOnlyList<BridgeInfo> List()
    {
        if (!Directory.Exists(DiscoveryFile.Directory))
            return Array.Empty<BridgeInfo>();

        var result = new List<BridgeInfo>();
        foreach (var file in Directory.EnumerateFiles(DiscoveryFile.Directory, "*.json"))
        {
            var info = TryRead(file);
            if (info != null && IsAlive(info.Pid))
                result.Add(info);
        }
        return result;
    }

    public static async Task<BridgeInfo> WaitForAsync(Process process, TimeSpan timeout, CancellationToken ct)
    {
        var file = DiscoveryFile.PathFor(process.Id);
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (process.HasExited)
                throw new InvalidOperationException($"Host process exited (code {process.ExitCode}) before the bridge started.");
            if (File.Exists(file) && TryRead(file) is { } info)
                return info;
            await Task.Delay(1000, ct);
        }
        throw new TimeoutException($"No bridge announced itself for pid {process.Id} within {timeout.TotalSeconds:0}s. Check %LOCALAPPDATA%\\HicasTest\\logs.");
    }

    private static BridgeInfo? TryRead(string file)
    {
        try
        {
            return JsonSerializer.Deserialize<BridgeInfo>(File.ReadAllText(file), BridgeClient.Json);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null; // being written right now, or stale garbage
        }
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            return !Process.GetProcessById(pid).HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
