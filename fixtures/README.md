# Fixtures

Test/golden models the tool may open. Stored with Git LFS (`.gitattributes`).

- `revit/` — `.rvt` files, saved in the **lowest** Revit version they are used with (newer Revit upgrades
  the copy on open; older Revit cannot open newer files).
- `autocad/` — `.dwg` files.

Rules:

- Never put customer models here. Only models built for testing, with known contents.
- The runner always works on a copy; fixtures are never modified.
- Document what each fixture contains (counts, names, key parameter values) in a sibling `.md`, so test
  cases can cite it as an oracle source.
