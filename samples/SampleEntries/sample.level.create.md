# Contract: sample.level.create

The contract file of the sample feature (convention: docs/test-entries.md). Test cases cite it as their `source`.

## Entries

| Entry | Kind | Writes | Request | Result |
|---|---|---|---|---|
| `sample.level.create` | main (the use case) | yes | `LevelRequest` | `LevelResult` |
| `sample.level.create.validate` | step | no | `LevelRequest` | `LevelValidation` (errors + prompts the flow would raise) |
| `sample.level.create.plan` | step | no | `LevelRequest` | `LevelPlan` (a duplicate name is planned as renamed) |
| `sample.level.create.apply` | step | yes | `LevelPlan` | `LevelResult` |

## Request

| Field | Type | Rule |
|---|---|---|
| `name` | string | required, not blank |
| `elevationMm` | number | millimetres, within ±200000 |

## Result (`LevelResult`)

| Field | Meaning |
|---|---|
| `status` | `created`, `renamed` (name existed, renamed), `cancelled` (name existed, user cancelled), `invalid` |
| `name`, `elevationMm`, `levelId` | the level that was created (null when none) |
| `errors` | reasons when `invalid` |

## Prompts

| Id | Severity | Raised when | Options (default first) |
|---|---|---|---|
| `sample.level.duplicate-name` | confirm | a level with that name exists (case-insensitive) | `rename`, `cancel` |

- `rename` → the first free `name (n)`, n starting at 2.
- `cancel` → nothing changes, `status = cancelled`.

## Host effects

- One named transaction `Sample: create level`; one `Level` element per accepted request; no warnings.
