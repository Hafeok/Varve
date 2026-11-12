# `eng/release.cs --sbom`'s rewrite fixtures

Cases for `eng/release.cs --sbom-fixtures` (#248). Each is a directory: a
`descriptor.yaml`; an `sbom/` directory as `eng/sbom.cs` writes it (its
`sbom.txt`, and SPDX documents whose `creationInfo.created` is what matters);
`today.txt`, the day the rewrite runs; and, for a `pass-` case,
`expected.yaml`, the descriptor the rewrite must write, byte for byte. A
`fail-` case must be refused. The pipeline's `release-sbom-fixtures` job runs
them on every build.

| Case | Why |
|---|---|
| `pass-rewritten-on-a-later-day` | the block is replaced by `sbom.txt`, and `date:` moves from 2026-11-01 to the day the SBOMs were created |
| `pass-placeholder-from-draft` | `--draft`'s placeholder line is replaced the same way |
| `fail-sboms-created-another-day` | the SBOMs were created at 2026-11-01, not the rewrite's day: digests and date would disagree (ADR 0224 point 2) |
| `fail-no-sbom-block` | a format 1 descriptor has no block; its date stays |
