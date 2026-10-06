# Test case format (YAML)

One file per level-B case. The agent translates it from the case's manual script in
`qa-handover.md` (`Kịch bản test tay cho case cấp B`). Relative paths resolve from the YAML file.

```yaml
id: US-1234-AC-02            # unique, used for folders and the ledger
ticket: "1234"
case: AC-02
level: B
autoPassEligible: false      # reserved for option 2; ignored by the runner today
source: qa-handover.md#AC-02 # the manual script this file was translated from

host:
  app: revit                 # revit | autocad
  version: 2024              # 2021–2027; override per run with --host-version
  # exePath: optional override

addin:                       # the build under test (lane worktree output)
  manifest: ../../src/MyAddin/bin/Debug/MyAddin.addin   # Revit: existing manifest, or…
  # assembly: …/MyAddin.dll  + fullClassName + addinId   # Revit: generate a manifest
  # assembly: …/MyAddin.dll                              # AutoCAD: NETLOADed

model: ../../fixtures/revit/basic_mep.rvt   # copied before every run, never modified

run:
  mode: postcommand          # postcommand (Revit) | commandline (AutoCAD) | invoke | entries (see below)
  command: "CustomCtrl_%CustomCtrl_%Hicas%Tools%MyCommand"
  timeoutSec: 300
  # invoke mode:
  # assembly: …/MyAddin.dll
  # type: MyAddin.Testing.Entry
  # method: CreateViews
  # argument: '{"level":"Level 1"}'

dialogs:                     # Revit TaskDialogs via the bridge, other windows via FlaUI
  - match: "Confirm"         # title / dialog id / message contains (case-insensitive)
    answer: "Yes"            # button text (FlaUI) or Ok|Cancel|Yes|No|Close|CommandLink1-4 (Revit)

expect:
  - kind: added
    category: OST_Views
    count: 1
    source: "ticket #1234 AC-02"
  - kind: param
    category: OST_PipeCurves
    scope: changed           # changed = only elements added/modified by the command; all = whole model
    where:
      - { param: "System Type", op: eq, value: "Domestic Cold Water" }
    param: Diameter
    unit: mm
    equals: 50
    tolerance: 0.5
    source: "comment #3 on #1234"
  - kind: no-warnings

evidence:
  image: true
```

## Test entries (run.mode: entries)

For the logic and flow of a feature, without a person or a UI: the case calls the add-in's **test entries**
(public static methods marked `[HicasTestEntry]` in its test assembly, which call the same use case as the ribbon
command; convention and rules in [test-entries.md](test-entries.md)). Calls run in order in one host session, so a
call sees what the previous one did.

```yaml
run: { mode: entries, assembly: ../src/MyAddin.Testing/bin/R2024/MyAddin.Testing.dll, timeoutSec: 120 }  # timeout per call
calls:
  - id: cancel-branch
    entry: keyplan.create                        # main gate; step gates: keyplan.create.validate / .plan (read-only) / .apply
    argument: { level: "Level 1", scale: 100 }   # plain 100 is a number, "100" a string; or argumentFile: request.json
    answers: [ { id: keyplan.duplicate-name, option: cancel } ]   # scripted answers to the feature's prompts
    expectResult:                                # checks on the returned JSON (dotted path, [n] indexes)
      - { path: status, equals: cancelled, source: "ticket #1234 AC-05" }
    expectPrompts:                               # prompts the feature must (not) raise
      - { id: keyplan.duplicate-name, severity: confirm, answer: cancel, source: "ticket #1234 AC-05" }
    expect:                                      # model checks on what THIS call changed (same kinds as below)
      - { kind: added, count: 0, source: "ticket #1234 AC-05" }
expect:                                          # model checks on all calls together
  - { kind: no-warnings }
```

- `expectResult`: one operator per check — `equals` (numbers with `tolerance`), `min`/`max`, `exists`, `count` (array or object size), `contains`. Names match case-insensitively.
- `expectPrompts`: `raised: false` asserts the prompt did not occur; `severity` and `answer` (the answer it got, scripted or default).
- Every check needs a `source`, as everywhere. A prompt without a scripted answer takes its default and is shown as such in the report; an answer for a prompt that never came is noted.
- A call that throws is a MISMATCH (`entry returns without error`) and the remaining calls are not run.
- A test entry must not show UI. If a window stays up (the host would hang), the case ends as `ERROR` with a screenshot, and the host is closed.
- Entries do not export a view image unless `evidence: { image: true }`.
- Working example: [examples/revit-entries-level.yaml](../examples/revit-entries-level.yaml) with the sample assembly `samples/SampleEntries`.

## Choosing the host version

- `host.version` is the default year for the case.
- `hicastest run … --host-version 2026` (MCP: `hostVersion`) runs the same cases on another year.
- If that year is not installed or its bridge is not built, the case is `NOT-RUN` with the reason;
  `hicastest hosts` lists what this machine can run.
- The fixture must be saved in a Revit version **≤** the year you test (Revit upgrades a copy on open
  but cannot open newer files). The same applies to DWG formats in old AutoCAD releases.
- The add-in build must target that year too (`addin.manifest` / `addin.assembly` of that year's output).

## Expectation kinds

| Kind | Checks | Needs `source` |
|---|---|---|
| `added` / `modified` / `deleted` | count of changed elements (optionally per `category`) — `count`, or `min`/`max` | yes |
| `count` | number of elements matching `category` + `where` in the model | yes |
| `param` | every selected element's `param` equals `equals` (± `tolerance`, in `unit`) or lies in `min`..`max`; fails if nothing is selected | yes |
| `no-warnings` | the command produced no Revit failure messages | no |
| `command-status` | command result is `equals` (default `completed`) | no |

`deleted` elements have no category (they are gone), so `category` is not allowed there.

## Categories and parameters

- **Revit:** `category` is a `BuiltInCategory` name (`OST_Walls`). `param` is a parameter name as shown
  in Revit, `BIP:<BuiltInParameter>` for built-ins, or `Name`. Numeric values are converted to `unit`
  (`mm cm m ft in m2 ft2 m3 deg`); without `unit` the raw internal value (feet) is compared.
- **AutoCAD:** `category` is the DXF name (`LINE`, `INSERT`, `LWPOLYLINE`, `*`); only model space is
  searched. `param` is `Handle`, `Layer`, `BlockName`, `ATTR:<TAG>`, or any public property of the
  entity (`Length`, `Radius`, `Rotation`). Values are in drawing units; no conversion.

## Filter operators

`eq`, `ne`, `contains`, `gt`, `ge`, `lt`, `le`, `exists`. Numbers compare numerically
(`tolerance` applies to `eq`/`ne`), everything else as text.
