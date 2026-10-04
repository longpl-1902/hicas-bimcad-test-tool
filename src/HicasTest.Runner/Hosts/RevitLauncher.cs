using System.Diagnostics;
using System.Xml.Linq;
using HicasTest.Runner.Bridge;
using HicasTest.Runner.Model;

namespace HicasTest.Runner.Hosts;

/// <summary>
/// Starts Revit with the bridge and the add-in under test loaded through temporary manifests in the
/// per-user Addins folder. Manifests are removed when the session ends.
/// </summary>
public sealed class RevitLauncher : IHostLauncher
{
    private const string BridgeAddinId = "6E0B3D52-8C1F-4F7A-9B0C-1C2D3E4F5A60";
    private const string BridgeManifestName = "HicasTest.Bridge.addin";
    private const string UnderTestManifestName = "HicasTest.UnderTest.addin";

    // Revit asks before loading unsigned add-ins. "Load Once" does not change trust settings.
    private static readonly DialogSpec[] StartupDialogs =
    {
        new() { Match = "Unsigned Add-In", Answer = "Load Once" },
        new() { Match = "Security", Answer = "Load Once" },
    };

    public async Task<HostSession> StartAsync(TestCase testCase, RunOptions options, DialogDriver dialogs, CancellationToken ct)
    {
        var year = testCase.Host.Version;
        var exe = testCase.Host.ExePath ?? HostCatalog.DefaultExe("revit", year);
        if (!File.Exists(exe))
            throw new FileNotFoundException($"Revit {year} not found at {exe}.");

        var bridgeDll = options.RevitBridgeDll(year);
        if (!File.Exists(bridgeDll))
            throw new FileNotFoundException($"Bridge for Revit {year} not built: {bridgeDll}. Run build.ps1.");

        var addinsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins", year.ToString());
        Directory.CreateDirectory(addinsDir);
        var bridgeManifest = Path.Combine(addinsDir, BridgeManifestName);
        var underTestManifest = Path.Combine(addinsDir, UnderTestManifestName);

        void Cleanup()
        {
            File.Delete(bridgeManifest);
            File.Delete(underTestManifest);
        }

        Process? process = null;
        try
        {
            WriteManifest(bridgeManifest, bridgeDll, "HicasTest.Bridge.Revit.App", BridgeAddinId, "HicasTest bridge");
            if (testCase.Addin != null)
                WriteUnderTestManifest(testCase.Addin, underTestManifest, year);

            var language = MachineConfig.Current.RevitLanguage;
            var arguments = string.IsNullOrWhiteSpace(language) ? "" : "/language " + language;
            // Shell start: Revit must not inherit our stdout, which is the MCP channel (it logs there).
            process = Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = true })
                      ?? throw new InvalidOperationException("Revit did not start.");
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

    private static void WriteUnderTestManifest(AddinSpec addin, string target, int year)
    {
        if (!string.IsNullOrEmpty(addin.Manifest))
        {
            // Copy the lane's manifest, making assembly paths absolute so they still resolve from the Addins folder.
            var document = XDocument.Load(addin.Manifest);
            var manifestDir = Path.GetDirectoryName(addin.Manifest)!;
            foreach (var assembly in document.Descendants("Assembly"))
                assembly.Value = Path.GetFullPath(Path.Combine(manifestDir, assembly.Value.Trim()));
            EnsureNotInstalledTwice(document, target, year);
            document.Save(target);
            return;
        }

        if (string.IsNullOrEmpty(addin.Assembly) || string.IsNullOrEmpty(addin.FullClassName) || string.IsNullOrEmpty(addin.AddinId))
            throw new ArgumentException("addin needs 'manifest', or 'assembly' + 'fullClassName' + 'addinId'.");
        WriteManifest(target, addin.Assembly, addin.FullClassName, addin.AddinId, "Add-in under test");
    }

    /// <summary>An installed copy with the same AddInId would load first and the test would run the wrong build.</summary>
    private static void EnsureNotInstalledTwice(XDocument manifest, string target, int year)
    {
        var ids = manifest.Descendants("AddInId").Select(e => e.Value.Trim()).Where(v => v.Length > 0).ToList();
        var folders = new[]
        {
            Path.GetDirectoryName(target)!,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Autodesk", "Revit", "Addins", year.ToString()),
        };

        foreach (var file in folders.Where(Directory.Exists).SelectMany(d => Directory.EnumerateFiles(d, "*.addin")))
        {
            if (Path.GetFileName(file) is BridgeManifestName or UnderTestManifestName)
                continue;
            var text = File.ReadAllText(file);
            var clash = ids.FirstOrDefault(id => text.Contains(id, StringComparison.OrdinalIgnoreCase));
            if (clash != null)
                throw new InvalidOperationException($"An installed add-in ({file}) has the same AddInId {clash}. Disable it on the test machine first.");
        }
    }

    private static void WriteManifest(string target, string assembly, string fullClassName, string addinId, string name)
    {
        new XDocument(
            new XElement("RevitAddIns",
                new XElement("AddIn", new XAttribute("Type", "Application"),
                    new XElement("Name", name),
                    new XElement("Assembly", assembly),
                    new XElement("FullClassName", fullClassName),
                    new XElement("AddInId", addinId),
                    new XElement("VendorId", "HICAS"),
                    new XElement("VendorDescription", "HICAS test tool")))).Save(target);
    }
}
