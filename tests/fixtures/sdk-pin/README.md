# SDK pin fixtures

The two paths of `eng/sdk-pin.cs` (#276; ADR 0224 point 5, amended
2026-11-11). A gate that has never failed is a gate nobody has tested
(`docs/testing.md` §5).

| Case | What it holds | Expected |
|---|---|---|
| `failing` | `actions/setup-dotnet` used directly, with a comment that is not the exemption; `dotnet workload install` in a workflow step; global.json's `sdk.workloadVersion` 10.0.404 against `sdk.version` 10.0.403 | exit 1, three findings |
| `passing` | the composite action with `workloads:`; a direct `actions/setup-dotnet` marked `sdk-pin: exempt` in its step; a comment naming `dotnet workload install`; the composite action's own direct use and workload install, which the gate does not read; `workloadVersion` equal to `version` | exit 0 |

```
dotnet run eng/sdk-pin.cs -- --root tests/fixtures/sdk-pin/failing
dotnet run eng/sdk-pin.cs -- --root tests/fixtures/sdk-pin/passing
```

The `sdk-pin-fixtures` job in `eng/ci.cs` requires exactly those exit codes.
