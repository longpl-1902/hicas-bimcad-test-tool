# Proposed text patches for hicas-bim-cad-skills

Paths are relative to `hicas-bimcad/` in that repo. Each patch is small and keeps existing labels
(`lint-story.mjs` reads Vietnamese labels — none of them are renamed).

## 1. `skills/addin-story/SKILL.md` — project facts (Step 0 JSON example)

Replace the `automationBridge` example and add `testBuilds`:

```json
"automationBridge": "hicas-test (HicasTest MCP: list_hosts, run_test_case, qa_session_*) | none",
"testBuilds": { "2024": "src/X/bin/Debug/R2024/X.addin", "2026": "src/X/bin/Debug/R2026/X.addin" },
"testFixtures": "tests/fixtures/ (test/golden .rvt/.dwg only)",
```

## 2. `skills/addin-story/SKILL.md` — Phase 5 step 4

Replace the paragraph "Level-B machine evidence (optional…)" with:

> 4. **Level-B machine evidence (optional, saves the user time):** if `automationBridge` is `hicas-test`, run the
>    `b-auto-run` skill for this story (YAML in `F/b-cases/`, evidence in `F/evidence/host/<year>/`, ledger
>    `F/b-auto-ledger.csv`). Cases end as "Chờ xác nhận — có bằng chứng máy" at best; a human still confirms.
>    The tool only opens fixtures from `testFixtures`, always on a copy. Never touch customer models.

## 3. `skills/addin-story/SKILL.md` — Phase 6 step 5

Append to the `qa-handover.md` bullet:

> … For B cases run by `b-auto-run`, put the report path in the evidence column and add the blind-first note
> from the template.

## 4. `skills/addin-story/assets/templates/qa-handover.md`

After the status legend add:

```markdown
> Bằng chứng máy (HicasTest): `MATCH/MISMATCH/NOT-RUN/ERROR` chỉ là bằng chứng, không phải Pass.
> Trong giai đoạn đo độ chính xác: **chạy kịch bản tay trước**, ghi kết quả, rồi mới mở báo cáo máy;
> điền `human_verdict` / `disagreement_cause` vào `b-auto-ledger.csv`.
```

and in every B script add an optional step:

```markdown
6. Báo cáo máy: <đường dẫn report-*.md theo từng năm host, hoặc "chưa chạy">
```

## 5. `skills/redmine-us-writer-verified/SKILL.md` and qa-handover scripts — optional automation block

For B cases, the manual script may carry a machine-readable block so the YAML needs no guessing
(removes the `translation` error class). Put it in the **qa-handover script**, not in the contract table,
so `lint-story.mjs` is unaffected:

```markdown
Tự động hoá (tuỳ chọn):
- Host/năm: Revit 2024, 2026
- Fixture: tests/fixtures/revit/basic_mep.rvt
- Lệnh: CustomCtrl_%CustomCtrl_%HICAS%Tools%Keyplan   (AutoCAD: HC_KEYPLAN ALL)
- Hộp thoại: "Keyplan options" → OK
```

## 6. `skills/addin-batch/SKILL.md` — Level-B queue

Before "For the head of the queue tell the user exactly…" insert:

> If `automationBridge` is `hicas-test`, first run `b-auto-run` for every lane in `b-test` state (one lane at a
> time; each run uses a fresh host process with the lane's build). Add a column `máy` to `b-queue.md`
> (`MATCH n / MISMATCH n / NOT-RUN n`). Lanes with MISMATCH go to the front of the human queue.

## 7. `agents/evaluator.md` — rubric line

Add: "A HicasTest `MATCH` is machine evidence, not a Pass. A level-B case marked Pass on tool output alone = 0."

## 8. Plugin `.mcp.json` (new)

```json
{ "mcpServers": { "hicas-test": { "command": "${LOCALAPPDATA}/Programs/HicasTest/mcp/hicastest-mcp.exe" } } }
```

Check that the marketplace expands `${LOCALAPPDATA}` on Windows; otherwise keep `install.ps1 -RegisterMcp`.
