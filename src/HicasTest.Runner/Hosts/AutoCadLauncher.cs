using System.Diagnostics;
using System.Text;
using HicasTest.Runner.Bridge;
using HicasTest.Runner.Model;

namespace HicasTest.Runner.Hosts;

/// <summary>
/// Starts AutoCAD with a startup script that NETLOADs the bridge and the add-in under test.
/// Folders outside TRUSTEDPATHS trigger a security prompt; the dialog driver answers "Load once".
/// </summary>
public sealed class AutoCadLauncher : IHostLauncher
{
    private static readonly DialogSpec[] StartupDialogs =
    {
        new() { Match = "Security", Answer = "Load once" },
    };

    public async Task<HostSession> StartAsync(TestCase testCase, RunOptions options, DialogDriver dialogs, CancellationToken ct)
    {
        var year = testCase.Host.Version;
        var exe = testCase.Host.ExePath ?? HostCatalog.DefaultExe("autocad", year);
        if (!File.Exists(exe))
            throw new FileNotFoundException($"AutoCAD {year} not found at {exe}.");

        var bridgeDll = options.AutoCadBridgeDll(year);
        if (!File.Exists(bridgeDll))
            throw new FileNotFoundException($"Bridge for AutoCAD {year} not built: {bridgeDll}. Run build.ps1.");

        var script = Path.Combine(Path.GetTempPath(), $"hicastest-{Guid.NewGuid():N}.scr");
        File.WriteAllText(script, BuildScript(bridgeDll, testCase.Addin?.Assembly), Encoding.ASCII);
        void Cleanup() => File.Delete(script);

        Process? process = null;
        try
        {
            // Shell start: the host must not inherit our stdout, which is the MCP channel.
            process = Process.Start(new ProcessStartInfo(exe, $"/nologo /b \"{script}\"") { UseShellExecute = true })
                      ?? throw new InvalidOperationException("AutoCAD did not start.");
            dialogs.Watch(process.Id, StartupDialogs);

            var info = await BridgeDiscovery.WaitForAsync(process, options.StartupTimeout, ct);
            var client = await BridgeClient.ConnectAsync(info.PipeName, TimeSpan.FromSeconds(30), ct);
            return new HostSession(process, info, client, Cleanup);
        }
        catch
        {
            HostProcess.KillQuietly(process);
            Cleanup();
            throw;
        }
    }

    internal static string BuildScript(string bridgeDll, string? addinDll)
    {
        static string Quote(string path) => "\"" + path.Replace('\\', '/') + "\"";

        var script = new StringBuilder();
        script.AppendLine("_.FILEDIA 0");
        script.AppendLine("_.NETLOAD " + Quote(bridgeDll));
        if (!string.IsNullOrEmpty(addinDll))
            script.AppendLine("_.NETLOAD " + Quote(addinDll));
        script.AppendLine("_.FILEDIA 1");
        return script.ToString();
    }
}
