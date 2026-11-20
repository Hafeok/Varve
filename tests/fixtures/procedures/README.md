# Procedure marker fixtures

The two paths of `eng/procedures.cs` (ADR 0248, #129): every procedure in
the operator's guide names the job that runs it, or says why none can. A
gate that has never failed is a gate nobody has tested (`docs/testing.md` §5).

| Case | What it holds | Expected |
|---|---|---|
| `failing` | a block with no marker; markers naming a job and a workflow that do not exist; a `none` with no reason; a `none` on a page with no "Not run in CI" section; an exemption for a page whose blocks are all marked, and one for a page the guide does not have | exit 1, seven findings |
| `passing` | a block run by a workflow's job and by an `eng/ci.cs` job, with a note; a marked block inside a list; an `example`; a `none` listed under "Not run in CI"; an exempt page with an unmarked block | exit 0 |

```
dotnet run eng/procedures.cs -- --root tests/fixtures/procedures/failing
dotnet run eng/procedures.cs -- --root tests/fixtures/procedures/passing
```

The `procedures-fixtures` job in `eng/ci.cs` requires exactly those exit codes.
