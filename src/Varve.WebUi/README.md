# Varve.WebUi

The Varve Web UI: a Blazor WebAssembly client of the server's public API,
with no endpoints of its own (#96). Part 1 is the operator console (#167);
part 2, the data explorer, comes after Inference (#95).

It is a host at layer 6, published as static files and never packed
(ADR 0200). It reads the server through `Varve.Protocol.Client` only
(ADR 0203). The server can serve it under `/ui/`, or any static host can
(ADR 0202).

It implements `docs/ui/`, the design source (ADR 0206). The two stylesheets
and the core mark are linked into `wwwroot` from there, never copied. The
components are organised by level, one namespace each, and a component uses
only lower levels:

| Level | Components |
|---|---|
| `Atoms` | `Button`, `Field`, `StateBadge`, `PositionStamp`, `TermChip`, `Tail` |
| `Molecules` | `KeyValue` and `KeyValueItem`, `LagMeter`, `Notice`, `ViewHead`, `Triple`, `AsOfBanner`, `CopyButton` |
| `Organisms` | `ResourceTable`, `CommitList`, `ProblemPanel`, `SourceBlock`, `ConfirmDialog`, `Panel`, `TripleList` |
| `Templates` | `Shell` |
| `Parts` | the domain-aware pieces that are not routed: the layout `MainLayout`, `ProblemView`, `ReadStamps` and `DriftNote` |
| `Pages` | the routed screens, the only level that reads the server |

A component never uses its own level: parts don't use parts, and pages don't
use pages. Where an organism shows what another renders, it takes a slot the
page fills: `ConfirmDialog`'s note and result, and `ResourceTable`'s problem.
Atoms to Templates know nothing of Varve's domain. Parts may use the client's
model types (`Varve.Protocol.Client.Model`), and only pages its services. A
problem becomes a `ProblemPanel`'s parameters in `ProblemView`, a state word
becomes a tone in `Tones`, the one mapping, and the drift note's wording lives
once, in `DriftNote`.

No element waits on a visual decision now. A lag is shown as a number beside
its readiness badge, and a `LagMeter` is used only where the API serves the
bound.

```bash
dotnet publish src/Varve.WebUi -c Release -o out   # needs the wasm-tools workload
```

`out/wwwroot` is the bundle. Its `appsettings.json` names the server in
`Varve:Server` when a static host serves it. Leave it empty when the server
serves the bundle itself.
