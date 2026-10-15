# A dataset written by v0.1.0-preview.6, derived format 3

The fixture for ADR 0139's "a format-3 `derived/` migrates as it runs": a
dataset written by the previous release, opened by this one.

- `log/` — storage format 1, 120 commits, seeded, with retractions in three
  graphs.
- `derived.v3/` — what that release left under `derived/`: full checkpoints
  at 40, 80 and 120 with their graph-version indexes (kind 2 and 5), the
  projection's state and a run (kinds 3 and 1), all version 3. It is not named
  `derived/` because a dataset's `derived/.gitignore` would hide it; the test
  copies it to `derived/` in a temporary directory.

`SHA256SUMS` holds each file's SHA-256, checked by the test before it reads a
byte.

**How it was made.** `generator/Program.cs` is the program, built against
the tag as a console project of its own (net10.0, nullable and implicit
usings on) with one project reference, to the tag's `src/Varve.Store`:

```
git worktree add --detach ../previous v0.1.0-preview.6
dotnet run -c Release -- <out> 120 40
```

It is kept as text here, in no project, so nothing in this repository builds it.

The dataset id and the clock are fixed, so the same tag writes the same bytes.
Regenerating it with a later release defeats its purpose: it is the previous
release's bytes, kept.
