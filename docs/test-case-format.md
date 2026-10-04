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
  mode: postcommand          # postcommand (Revit) | commandline (AutoCAD) | invoke
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
