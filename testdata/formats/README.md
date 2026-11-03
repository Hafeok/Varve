# Format fixtures, written by the releases that introduced them

One directory per fixture ([ADR 0216](../../docs/adr/0216-the-format-inventory.md));
[`docs/formats/README.md`](../../docs/formats/README.md) says which format
versions each holds, and `eng/formats.cs` checks the two agree. Each
fixture's `SHA256SUMS` covers every file but its generator, and the tests
check it before they read a byte.

**How they were made.** `generator/` holds the program: `Program.cs`, and
`gen.csproj` naming the release's published packages from nuget.org (and,
for the events, the release's own test support, `Support.cs`, from its tag,
less what needs a test package). Each was built and run on its own, outside
this repository, as

```
cd <scratch copy of generator/>
dotnet run -c Release -- <out> 120 40        # store-* and objects-*
dotnet run -c Release -- records.delta <out> # delta-preview.6
dotnet run -c Release -- inputs <out>        # events-preview.*
dotnet run -c Release -- <out>               # plan-preview.10
```

and `<out>` copied here without the dataset's runtime files (`LOCK`,
`LOCK.owner`, `derived/.gitignore`). The dataset id and the clock are fixed, so
a release writes the same bytes on every run. `derived/` is kept as
`derived.v<N>` so that no `.gitignore` hides it. `store-next` is built with a
project reference to this branch's `src/Varve.Store`, since no release has
written derived format 6 yet; the release that ships it is its writer.

Regenerating a fixture with a later release defeats it: it is that release's
bytes, kept.

| Fixture | Release | What it holds | Read by |
|---|---|---|---|
| `store-preview.1` | v0.1.0-preview.1 | `log/`, `derived.v2/`, the operations | `Varve.Store.Tests` `Formats.StoreFormatTests` |
| `store-preview.2` | v0.1.0-preview.2 | `log/`, `derived.v3/`, the operations | `StoreFormatTests` |
| `store-next` | this branch | `log/`, `derived.v6/`, the operations | `StoreFormatTests` |
| `objects-preview.8` | v0.1.0-preview.8 | `objects/`: chunks, writer record, derived 4 | `Formats.ObjectFormatTests` |
| `objects-preview.10` | v0.1.0-preview.10 | `objects/`, the writer record with `address`, released for `placement` | `ObjectFormatTests`, `Formats.CoordinationRecordTests` |
| `objects-preview.11` | v0.1.0-preview.11 | `objects/`, with a reader record | `ObjectFormatTests` |
| `objects-preview.15` | v0.1.0-preview.15 | `objects/`, a reader record with `maxDerived`, derived 5 | `ObjectFormatTests` |
| `delta-preview.6` | v0.1.0-preview.6 | `version-1.delta`, the records | `Varve.Delta.Tests` `FormatFixtureTests` |
| `events-preview.7` | v0.1.0-preview.7 | `schemas/`, `cloudevents/` | `Varve.Events.Tests` `FormatFixtureTests` |
| `events-preview.13` | v0.1.0-preview.13 | `schemas/` with management revisions, `cloudevents/` | `Varve.Events.Tests` `FormatFixtureTests` |
| `plan-preview.10` | v0.1.0-preview.10 | `format-1.plan.json`, the plan | `Varve.Management.Tests` `FormatFixtureTests` |

The expectations are the generator's own: the operations the store workload
issued (`expectations/operations.tsv`, a position, `+` or `-`, a quad), the
records the release's delta reader gave, and the plan its reader gave back.
They depend on neither the release's store code nor the current one.
