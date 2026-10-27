# Varve.WebUi

The Varve Web UI: a Blazor WebAssembly client of the server's public API,
with no endpoints of its own (#96). Part 1 is the operator console (#167);
part 2, the data explorer, comes after Inference (#95).

It is a host at layer 6, published as static files and never packed
(ADR 0200). It reads the server through `Varve.Protocol.Client` only
(ADR 0203). The server can serve it under `/ui/`, or any static host can
(ADR 0202).

The markup is unstyled and semantic until `docs/ui/` lands. Elements that
wait on a visual decision carry `data-ui-pending`, naming the decision.

```bash
dotnet publish src/Varve.WebUi -c Release -o out   # needs the wasm-tools workload
```

`out/wwwroot` is the bundle. Its `appsettings.json` names the server in
`Varve:Server` when a static host serves it. Leave it empty when the server
serves the bundle itself.
