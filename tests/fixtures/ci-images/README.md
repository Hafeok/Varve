# CI image fixture

The failure path of `eng/ci-images.cs`: a gate that has never failed is a gate
nobody has tested (`docs/testing.md` §5).

```
dotnet run eng/ci-images.cs -- --root tests/fixtures/ci-images
```

exits 1 and names four references:

- `.github/services/compose.yaml`, `direct`: `ghcr.io/example/kept`, a
  mirrored image referenced upstream, the case #118 names;
- `.github/services/compose.yaml`, `hub`: `postgres:16`, Docker Hub by an
  implicit registry, and not listed;
- `.github/services/compose.yaml`, `stale`: the mirror, at a digest the list
  does not pin;
- `samples/Unlisted.cs`: an image the list does not name, started from C# with
  `AddContainer`.

These pass:
- the mirror at the listed digest (`mirrored`);
- a `mirror: false` row upstream at its digest (`outside`);
- this repository's own image (`IMAGE` in the workflow);
- a workflow expression, which is not a reference;
- `samples/Allowed.cs`, which is upstream and allowed. Its row's allowance
  names the file and `fixture job`, the file pins the digest, and that job
  sets the mirror repository.

The `ci-images-fixture` job in `eng/ci.cs` requires the exit code 1.
