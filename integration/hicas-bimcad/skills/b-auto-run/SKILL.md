---
name: b-auto-run
description: Runs the level-B cases of a story automatically in real Revit/AutoCAD with the HicasTest tool (MCP server 'hicas-test') and attaches machine evidence to qa-handover.md. Use from addin-story Phase 5.4 / Phase 6 or addin-batch when a story has B cases and the tool is installed. Never marks a case Pass.
---

# b-auto-run

Turns the B scripts of one story into HicasTest YAML cases, runs them, and records **machine evidence**.
The case status stays `Chờ xác nhận — có bằng chứng máy`; a human confirms (docs/accuracy-study.md of the tool).

## Inputs

- `F` = `.harness/features/<id>/` with `test-contract.md` and `qa-handover.md` (B scripts).
- `.harness/addin-story.json`: `platform`, `deployVersions`, and `testBuilds` (year → manifest/dll of the build).
- MCP server `hicas-test` (tools: `list_hosts`, `validate_test_case`, `run_test_case`, `query_elements`).

## Steps

1. `list_hosts` → years that are `ready` on this machine. Target years = `deployVersions` ∩ ready.
   None → stop, tell the user which year to install or build.
2. For each B case (skip `[Critical]`-only visual checks), write `F/b-cases/<case>.yaml`
   (format: tool docs/test-case-format.md):
   - `source: qa-handover.md#<case>`; `host.version` = lowest target year.
   - `model`: the fixture named in the script — must be a test/golden file. Unknown → mark the case
     `Chưa chạy được (thiếu fixture)` and ask; never pick a customer model.
   - `run`: from the script's button/command. Revit ribbon button → `CustomCtrl_%CustomCtrl_%<Tab>%<Panel>%<Button>`;
     AutoCAD → command line + prompt answers.
   - `expect`: one entry per value in `Kết quả đúng`, copying the value, unit, tolerance **and its source** verbatim.
     Do not invent expectations the contract does not state.
   - If the script's automation block exists (patches.md §5), use it as-is instead of guessing.
3. `validate_test_case F/b-cases` → fix until all ok.
4. For each target year: `run_test_case path=F/b-cases outputDir=F/evidence/host/<year>
   ledger=F/b-auto-ledger.csv repeat=2 hostVersion=<year>`.
5. In `qa-handover.md`, per B case: put the report path(s) in the evidence column and set status
   `Chờ xác nhận — có bằng chứng máy` (MATCH) or keep `Chờ xác nhận` + note `máy: MISMATCH/ERROR/NOT-RUN — <reason>`.
   Never `Pass`.
6. Report (≤ 8 lines, Vietnamese): counts MATCH / MISMATCH / NOT-RUN / ERROR per year, flaky cases
   (`runs_consistent = no`), and the reminder for the tester:
   **chạy kịch bản tay trước, rồi mới mở báo cáo máy** (blind-first), then fill `human_verdict` in the ledger.

## Accuracy study (while it is active)

Also run once with a deliberately broken build when the user provides one: same command with `build=mutant`.
Every case touching the broken behaviour should be `MISMATCH`; a `MATCH` there is a false pass — report it first.
