# How `verification-next` was made

No release has written the verification record or the result record yet
(ADR 0183), so there are no published packages to run, as there are for the
other fixtures. This one is this branch's, as `store-next` is: the release
that ships `varve storage verify` is its writer, and regenerates it from its
own packages.

The bytes are what the code writes for a fixed workload, and two tests hold
current code to them:

| File | Written by | Workload | Held by |
|---|---|---|---|
| `varve-verification` | `ObjectStoreVerification.RunAsync` | a store that keeps the contract, 8 rounds, the clock at 2026-11-16T12:00:00Z | `Varve.Store.Tests` `Formats.VerificationRecordFormatTests` |
| `result-pass.yaml` | `varve storage verify --record` | the same, as provider `example-store` 1.2.3, run by `ci` | `Varve.Server.Tests` `StorageVerifyTests` |
| `result-fail.yaml` | `varve storage verify --record` | a store that ignores conditions, as provider `broken` 0, run by `maintainer` | `StorageVerifyTests` |

To write them again, run those tests with `VARVE_WRITE_VERIFICATION_FIXTURE`
set to this directory's parent, then `sha256sum` the three files into
`SHA256SUMS`. A change in any byte without a new format version is the
failure the tests exist for.
