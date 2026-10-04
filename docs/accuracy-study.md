# Accuracy study — can the tool be trusted with level-B cases?

Status: **active** (option 1). Owner: HICAS BIM/CAD team.

## Why this study exists

The hicas-bimcad plugin splits every test contract into:

- **Level A** — runs without the host; the verifier agent can confirm it.
- **Level B** — needs Revit/AutoCAD, a real model/DWG or visual judgement; **only a human can confirm it**.
- **[Critical]** — always needs a human, even when level A passed.

This tool runs level-B cases automatically. Until we have numbers showing it is accurate, it only
produces **machine evidence**: the case status stays `Chờ xác nhận — có bằng chứng máy` and a human
still confirms it. This study collects the numbers that decide whether some kinds of checks may later
get an automatic Pass (option 2, "B-auto").

## Rules during the study

1. The tool **never writes Pass**. Its verdicts are `MATCH`, `MISMATCH`, `NOT-RUN`, `ERROR`, on purpose
   different words from the plugin's statuses.
2. **Human tests first, blind.** The tester runs the normal `qa-handover.md` script and records the
   verdict and values *before* opening the tool report. Reading the report first anchors the tester
   and hides exactly the errors we want to find.
3. The tool only opens **test/golden fixtures** named in the contract — never customer models.
4. Expected values come from the contract's independent source (`Kết quả đúng (kèm nguồn)`), never
   from running the code under test.

## Workflow per story

```
dev done (addin-story Phase 5/6)
  → agent translates each B script in qa-handover.md into a YAML case   (.harness/features/<id>/b-cases/)
  → hicastest run  (normal build, --repeat 2)                            → report + ledger rows
  → hicastest run  (mutant build, --build mutant)                        → report + ledger rows
  → human runs the B scripts blind, fills human_verdict in the ledger
  → human opens the tool report, compares, fills disagreement_cause
```

## What we measure

For each case and run, compare the tool verdict with the human verdict:

|                 | Human: Pass | Human: Fail |
|-----------------|-------------|-------------|
| **Tool: MATCH**    | agree       | **false pass — the dangerous one** |
| **Tool: MISMATCH** | false fail (costs a recheck) | agree |

Plus:

- **Coverage** — share of B cases the tool could run at all (`NOT-RUN` / `ERROR` count against it).
- **Flakiness** — the two normal runs disagree → case is flaky, fix before it counts.
- **Mutation catch rate** — on a deliberately broken build, share of cases the tool reports `MISMATCH`.
  This finds false passes without waiting for real bugs.
- **Time** — tool seconds versus human minutes per case.

## Disagreement causes (fill one per disagreement)

| Code | Meaning | Fix goes to |
|---|---|---|
| `translation` | YAML does not say what the manual script says (wrong param, filter, unit) | YAML / translation prompt |
| `tool-bug` | Bridge or runner recorded/read the wrong thing, timing, unhandled dialog | this repo |
| `oracle` | Expected value in the contract is wrong | ticket writer (`redmine-us-writer-verified`) |
| `human-error` | Tester misread or mis-clicked | — (still counts in the log) |
| `flaky` | Runs disagree with each other | this repo / fixture |

## Ledger

One CSV per lane: `.harness/features/<id>/b-auto-ledger.csv`. The tool writes the left columns;
the human fills the right ones.

| Column | Filled by | Values |
|---|---|---|
| `timestamp` | tool | ISO 8601 |
| `ticket`, `case` | tool | from YAML |
| `check_kinds` | tool | e.g. `added;param` |
| `build` | tool | `normal` / `mutant` |
| `tool_verdict` | tool | `MATCH` / `MISMATCH` / `NOT-RUN` / `ERROR` |
| `runs_consistent` | tool | `yes` / `no` / `single` |
| `tool_seconds` | tool | number |
| `report` | tool | path to the case report |
| `human_verdict` | human | `pass` / `fail` |
| `disagreement_cause` | human | code from the table above, empty if agree |
| `human_minutes` | human | number |
| `reviewer` | human | name |
| `notes` | human | free text |

## Mutant builds

For each story in the study, make at least one broken build on a throwaway branch in the lane
worktree, e.g.:

- write a different parameter value than the contract says,
- skip creating one element / block,
- swallow the last step of the command.

Run the tool with `--build mutant`. Every B case touching the broken behaviour must come out
`MISMATCH`. A `MATCH` here is a false pass.

## Exit criteria — when option 2 may be proposed

Measured **per check kind** (`added`, `deleted`, `count`, `param`, `no-warnings`, …):

- ≥ 3 stories and ≥ 30 B cases in total, including mutant runs;
- **0 false passes** for that check kind;
- ≤ 1 flaky case, and it was fixed;
- coverage ≥ 70 % of the B cases in the studied stories.

Check kinds that meet the bar may be proposed for "B-auto". Visual judgement and `[Critical]` cases
stay human regardless. Option 2 needs changes in the plugin (`redmine-us-writer-verified` level column,
`addin-story` rules and evaluator rubric, `lint-story.mjs`) and is a separate decision.

## Pilot selection

Pick 2–3 stories whose B cases are mostly deterministic (counts and parameter values) on fixtures we
own. Avoid stories that are mostly visual for the first round.
