# Test machine setup

The tool drives real, licensed Revit / AutoCAD on Windows. Standard cloud CI cannot run it;
use a dev PC or a self-hosted Windows runner signed in with an Autodesk licence.

## Once per machine

1. Install the host years you test. Supported: Revit and AutoCAD 2021–2027. `hicastest hosts` shows
   which are installed and which bridges are built.
2. .NET SDK 10 (builds every year, including 2027 / net10) — `dotnet --list-sdks`.
3. Git LFS (`git lfs install`) for the fixture models.
4. Start each host once by hand: accept the licence, dismiss "What's new" / home-screen prompts.
   The tool answers add-in security prompts with **Load once** and never changes trust settings.
5. **Disable installed copies of the add-ins you test.** If the product version of an add-in is installed
   in `%APPDATA%` or `%PROGRAMDATA%\Autodesk\Revit\Addins\<year>`, the runner refuses to start
   (same AddInId would load the wrong build).
6. AutoCAD: optionally add `artifacts\bridges\autocad\<year>` and the lane build folders to `TRUSTEDPATHS`
   in a dedicated test profile to avoid the security prompt. Do this by hand; the tool does not change it.

## Dev machine with a dev manifest

A manifest that loads a build from a worktree on every Revit start (for example `ExternalTool.addin` with `<Assembly>` pointing at
`<repo>\Outcome\<Addin>.dll`) loads that build into the test host too. For entries cases, HicasTest compares where the add-in's
assemblies were loaded from with the folder of the test assembly and stops with `ERROR ... would not test the build under test`
when they differ (typically: testing a lane worktree while the dev manifest points at the main worktree). Point the manifest at the
build under test, or disable it, then run again. Manifests are matched by `AddInId` only for the add-in under test; a dev manifest
with another id (`ClientId`) is not detected beforehand, only by this check.

## Build

```powershell
./build.ps1
```

Bridges land in `artifacts\bridges\<revit|autocad>\<year>\`. Point the runner elsewhere with
`--bridges <dir>` or the `HICASTEST_BRIDGES` environment variable.

## Run

```powershell
dotnet run --project src/HicasTest.Cli -- validate examples
dotnet run --project src/HicasTest.Cli -- run <lane>/.harness/features/<id>/b-cases `
    --out <lane>/.harness/features/<id>/evidence/host `
    --ledger <lane>/.harness/features/<id>/b-auto-ledger.csv --repeat 2
```

Do not use the machine while a run is in progress: the dialog driver clicks windows of the host process.

## Hooking into hicas-bimcad

In the add-in repo's `.harness/addin-story.json` set:

```json
"automationBridge": "MCP server 'hicas-test' (validate_test_case, run_test_case, list_bridges, query_elements)"
```

and register the MCP server for Claude Code (adjust the path):

```json
{
  "mcpServers": {
    "hicas-test": {
      "command": "D:/hicas-tools/bimcad-test-tool/src/HicasTest.Mcp/bin/Release/net8.0-windows/hicastest-mcp.exe",
      "env": { "HICASTEST_BRIDGES": "D:/hicas-tools/bimcad-test-tool/artifacts/bridges" }
    }
  }
}
```

The plugin's rule still holds: results are `Chờ xác nhận — có bằng chứng máy` until a human confirms.

## Troubleshooting

- Bridge log: `%LOCALAPPDATA%\HicasTest\logs\bridge-<pid>.log`
- Running bridges: `hicastest bridges`
- Startup timeout → the host showed a dialog nobody answered; watch the screen during the first run.
