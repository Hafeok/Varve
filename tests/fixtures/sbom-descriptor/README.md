# The SBOM descriptor rule's fixtures

Cases for `eng/sbom.cs --fixtures` (ADR 0102, amended 2026-10-27; ADR 0225,
amended 2026-11-06). A gate that has never failed is a gate nobody has tested
(`docs/testing.md` §5).

A release descriptor names every artefact with the SHA-256 of its SBOM, and
`eng/sbom.cs --descriptor` holds each line to the SBOM it generated. Each case
here holds `sbom.txt`, the lines a generation wrote, and `descriptor.yaml`.
Nothing is generated, so the cases do not move with the repository's
dependencies. A `pass-` case must match. A `fail-` case must be refused and
print every line of its `expect.txt`, which is what makes it fail for its own
reason.

```
dotnet run eng/sbom.cs -- --fixtures tests/fixtures/sbom-descriptor
```

The `sbom-fixtures` job in `eng/ci.cs` runs it.

| Case | The one flaw |
|---|---|
| `pass-asset-digest-matches` | none: every generated line, the Web UI bundle's `asset:` line among them, is in the descriptor with its digest |
| `fail-asset-digest-differs` | the `asset:` line's digest is not the generated SBOM's |
| `fail-asset-line-missing` | the descriptor has no `asset:` line for the Web UI bundle's SBOM |

That a descriptor must carry the line at all, before any SBOM is generated,
is `eng/release.cs`'s rule, and its case is
`tests/fixtures/releases/missing-asset-line`.
