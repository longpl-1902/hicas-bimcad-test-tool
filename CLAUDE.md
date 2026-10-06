# CLAUDE.md

Tool that runs hicas-bimcad level-B test cases inside real Revit / AutoCAD. Read
[docs/architecture.md](docs/architecture.md) before changing code, and
[docs/accuracy-study.md](docs/accuracy-study.md) before changing verdicts or reports.

## Hard rules

- README.md is for the team and written in Vietnamese; docs/ and code comments are English.
- **The tool never writes Pass.** Verdicts are `MATCH / MISMATCH / NOT-RUN / ERROR`. Do not add any path
  that marks a plugin case Pass; that is option 2 and needs an explicit decision.
- **Bridges (`HicasTest.Bridge.*`, `HicasTest.Protocol`, `HicasTest.Contracts`) take no third-party packages.** They run inside the
  host next to the add-in under test. Only host API reference packages (`Nice3point.Revit.Api.*`,
  `AutoCAD.NET`) with `ExcludeAssets="runtime"`. JSON there = `DataContractJsonSerializer`.
- Bridge code must compile for **every host year 2021–2027** (net48, net8, net10): no records, `init`, ranges,
  `string.Contains(string, StringComparison)`. Year-specific API → `#if REVIT2024_OR_GREATER` etc. from
  `build\HostVersions.props`. Supported years live in three places: `build\HostVersions.props`, `build.ps1`,
  `HostCatalog.cs` — change them together. Pin API package versions; never float them.
- Host API calls only on the host's main thread, via the dispatcher (`IMainThreadDispatcher`).
  Revit: `RevitDispatcher` (ExternalEvent). AutoCAD: `AcadDispatcher` (application context; lock the document).
- DataContract deserialization skips constructors: treat every list in a request DTO as possibly null on the bridge side.
- Expected values in test cases must cite an independent `source`; the validator enforces it — do not weaken it.
- Never open customer models; fixtures are copied before every run.

## Layout

- `src/HicasTest.Protocol` — wire DTOs (netstandard2.0)
- `src/HicasTest.Contracts` — `[HicasTestEntry]`, `TestContext`, `TestMode` for add-in test assemblies (netstandard2.0, no deps;
  the bridge reaches it by name/reflection only)
- `samples/SampleEntries` — reference implementation of the test-entry convention and the live test (not in build.ps1/CI);
  design in [docs/test-entries.md](docs/test-entries.md)
- `src/HicasTest.Bridge.Core` — pipe server, router, change log, filters (net48; net8.0)
- `src/HicasTest.Bridge.Revit` / `.AutoCAD` — host adapters, one build per host version
- `src/HicasTest.Runner` — cases, launchers, FlaUI dialogs/UI driver, QA sessions, assertions, reports, ledger, machine config
- `integration/hicas-bimcad` — original proposal, applied in hicas-bimcad 1.1.0; change skills in the
  hicas-bim-cad-skills repo, not here
- `src/HicasTest.Cli` (`hicastest`) / `src/HicasTest.Mcp` (`hicastest-mcp`)
- `tests/HicasTest.Runner.Tests` — host-free tests (xunit)

## Build and test

```powershell
./build.ps1                                                       # all bridge versions + runner
dotnet build src/HicasTest.Bridge.Revit -p:RevitVersion=2026      # one version
dotnet test tests/HicasTest.Runner.Tests
```

## CI/CD

`.github/workflows/ci.yml` (windows-latest): `build.ps1 -Package -Version <v>`, unit tests, `build/smoke-test.ps1`
on the zip. Tags `v*` create a GitHub Release. If you change the package layout, the publish step or the MCP tool
set, update `build/smoke-test.ps1` too and run it locally before pushing.
