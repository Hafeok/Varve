# Licence header fixture

The failure path of `eng/licence-headers.cs`, in the shape
`tests/fixtures/banned-api/` established: a gate that has never failed is a gate
nobody has tested (`docs/testing.md` §5).

There is no project file here. These two files are data for the gate, not code
to compile, and nothing globs them.

```
dotnet run eng/licence-headers.cs -- --root tests/fixtures/licence-header
```

fails with exit 1 and names `without-notice.cs`. The repository-wide run
excludes this directory, because `without-notice.cs` is supposed to be missing
its notice — the exclusion lifts when `--root` names the directory explicitly.
