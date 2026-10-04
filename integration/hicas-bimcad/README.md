# Integration with the hicas-bimcad plugin

The plugin lives in `longpl-1902/hicas-bim-cad-skills` (marketplace `hicas-skills`). This folder holds
**proposed** changes for that repo — review here, then copy them over. Nothing in the plugin is changed yet.

## Where the tool fits today

| Plugin step | What exists | Gap |
|---|---|---|
| `redmine-us-writer-verified` → test contract | Cases with `Cấp A/B`, oracle source, verifier | B cases are free text; no host year, fixture, command id → the agent must guess when writing YAML (the `translation` error class in the study) |
| `addin-story` Phase 5.4 "Level-B machine evidence" | `automationBridge` in `.harness/addin-story.json`, read-only dumps on a model the **user** opened | The tool launches its own host on a fixture copy and runs the command; Phase 5.4 text must say how to call it and where evidence goes |
| `addin-story` Phase 6 `qa-handover.md` | Manual scripts per B case, status table | No place for the machine report link, the ledger, or the blind-first rule |
| `addin-story.json` | `deployVersions`, `build` | No mapping year → build output (manifest/dll) the tool should load |
| `addin-batch` `b-queue.md` | Human tests lanes one by one | Tool can pre-run every lane unattended (fresh host per run) before the human |
| QA ad-hoc testing | — | New skill `qa-test-session`: QA asks in plain words, Claude drives the host step by step with screenshots |

## Proposed changes (in order of value)

1. **New skill `b-auto-run`** — [skills/b-auto-run/SKILL.md](skills/b-auto-run/SKILL.md): translate B scripts to YAML,
   validate, run on every deploy year installed, attach evidence, append ledger. Called from addin-story Phase 5.4
   and addin-batch.
2. **New skill `qa-test-session`** — [skills/qa-test-session/SKILL.md](skills/qa-test-session/SKILL.md): interactive
   QA testing with step screenshots and a report.
3. **Text patches** to existing files — [patches.md](patches.md): addin-story Phase 5.4 / Phase 6 / project facts,
   `qa-handover.md` template, addin-batch B queue, optional automation block in the writer's B cases.
4. **Plugin `.mcp.json`** — register the server so every teammate gets the tools with the plugin:

   ```json
   { "mcpServers": { "hicas-test": { "command": "${LOCALAPPDATA}/Programs/HicasTest/hicastest-mcp.exe" } } }
   ```
   (or keep per-user registration via `install.ps1 -RegisterMcp`).

## Rules that do not change

- Level B and `[Critical]` are never Pass without a human. Tool verdicts are `MATCH/MISMATCH/NOT-RUN/ERROR`;
  status stays `Chờ xác nhận — có bằng chứng máy`.
- Only test/golden fixtures. Expected values from the contract's independent source.
- Maker ≠ checker: the agent that wrote the code does not interpret its own B results as confirmation.
