# Formats

Every format a Varve release writes that another reads, with its version, the
release that introduced it, the rule it is kept by, and the fixture that
release wrote ([ADR 0216](../adr/0216-the-format-inventory.md)). The rules:

- **read forever:** every later release reads it as written. The log, the
  delta, the CloudEvents rendering, the event schemas and the coordination
  records are read by people and programs Varve does not ship.
- **rebuilt:** a derived format is a cache. A later release reads it, or
  rebuilds it from the log, to the same results (ADR 0072, ADR 0139).
- **versioned:** a saved plan names positions. A later release reads its
  format while it can, and refuses an unknown one, telling the operator to
  plan again.

Each fixture is under [`testdata/formats/`](../../testdata/formats/), written
by its generator against the published packages of the release named. The
format gate, `dotnet run eng/formats.cs`, checks this table against the code:
a version constant that does not match its row fails, as does a row with
neither a fixture nor a named gap, a fixture whose `SHA256SUMS` does not
verify, and a fixture no row names. The tests that read each fixture are
named in its README.

<!-- The format gate reads the table between these markers. -->
<!-- formats -->
| Format | Version | Constant | Introduced by | Rule | Fixture |
|---|---|---|---|---|---|
| Log (`VRVM`, `VRVL`, `VRVT`, records) | 1 | `src/Varve.Store/LogFormat.cs` `Version` | v0.1.0-preview.1 | read forever | `store-preview.1` |
| Inline-set encoding | 1 | `src/Varve.Store/LogFormat.cs` `InlineSetVersion` | v0.1.0-preview.1 | read forever | `store-preview.1` |
| Inline-set encoding | 2 | — | not built (#114) | read forever | gap: written with #114, by the release that ships it |
| Object chunk header `VRVC` | 1 | `src/Varve.Store.ObjectStorage/ChunkHeader.cs` `Version` | v0.1.0-preview.8 | read forever | `objects-preview.8` |
| Writer record | 1 | `src/Varve.Store.ObjectStorage/WriterRecordFormat.cs` `Version` | v0.1.0-preview.8 | read forever | `objects-preview.8` |
| Writer record, with `address` and the `placement` release | 1 | — | v0.1.0-preview.10 | read forever | `objects-preview.10` |
| Reader record `readers/<id>` | 1 | `src/Varve.Store.ObjectStorage/ReaderRecordFormat.cs` `Version` | v0.1.0-preview.11 | read forever | `objects-preview.11` |
| Reader record, with `maxDerived` | 1 | — | v0.1.0-preview.15 | read forever | `objects-preview.15` |
| Lineage record (Replication) | — | — | not on `main` (#175) | read forever | gap: written by the release that lands #175 |
| Delta (`application/vnd.varve.delta`) | 1 | `src/Varve.Delta/DeltaFormat.cs` `Current` | v0.1.0-preview.6 | read forever | `delta-preview.6` |
| CloudEvents rendering | unversioned | — | v0.1.0-preview.7 | read forever | `events-preview.7` |
| Event schemas | unversioned | — | v0.1.0-preview.7 | read forever | `events-preview.7` |
| Event schemas, with management revisions `m{p}` | unversioned | — | v0.1.0-preview.13 | read forever | `events-preview.13` |
| `varve apply` saved plan | 1 | `src/Varve.Management/ManagementPlan.cs` `Format` | v0.1.0-preview.10 | versioned | `plan-preview.10` |
| Derived | 1 | — | no release (replaced within preview.1) | rebuilt | gap: no release wrote it |
| Derived | 2 | — | v0.1.0-preview.1 | rebuilt | `store-preview.1` |
| Derived | 3 | — | v0.1.0-preview.2 | rebuilt | `store-preview.2` |
| Derived | 4 | `src/Varve.Store/DerivedFormat.cs` `Version` | v0.1.0-preview.8 | rebuilt | `objects-preview.8` |
| Derived, extents | 5 | `src/Varve.Store/DerivedFormat.cs` `ExtentVersion` | v0.1.0-preview.15 | rebuilt | `objects-preview.15` |
| Derived, checked | 6 | `src/Varve.Store/DerivedFormat.cs` `CheckedVersion` | the release after v0.1.0-preview.15 | rebuilt | `store-next` |
<!-- /formats -->

## What a fixture's bytes may differ by

Current code writes each fixture's workload, and the bytes must equal the
fixture of the format's current version (`AFormatChangeWithoutANewVersionFails`).
One value is normalised, because it is the build's and not the format's:

- **A reader record's `maxDerived`** is the derived format the announcing
  build reads (ADR 0178). Format 1 carries it; its value is the build's.
