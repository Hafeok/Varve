# 0064 — Varve's configuration of the `DD` rules, and the hot-path rules `VARVE0003` and `VARVE0004`

## Status

**Accepted.** 2026-09-25. Decided by the maintainer on the adoption plan
(issue [#43](https://github.com/Hafeok/Varve/issues/43)). **Supersedes ADR
[0026](0026-hotpath-attribute.md) in part**: `[HotPath]` becomes the
attribute `DecisionDriven.Analyzers` generates, and `eng/HotPathAttribute.cs`
is retired. The rest of 0026 stands: marking is a judgement made while
writing, the attribute is internal to each assembly and never on an API
baseline, and it is matched by full name. Renumbers ADR 0004's reserved
hot-path rule as recorded by ADR [0062](0062-adopting-decisiondriven-analyzers.md).
It is the Varve form of the draft ADR-A13, with two corrections listed under
Context.

## Context

ADR 0062 moves the generic rules to `DecisionDriven.Analyzers`. What remains
for Varve is to say how the package is configured, which is four MSBuild
properties, `[DomainModel]` declarations and a few `.editorconfig` options.
It must also write the one rule family that knows a Varve fact: the hot-path
discipline that protects constraint 5, *allocation per quad is a defect*.

ADR-A13 drafted both. Checked against `src/` and the accepted ADRs, it needs
five corrections. The last three are small.

1. **`ArchContractTypeAssemblies` cannot be one global value.**
   - A13 lists `Varve.Rdf;Varve.Iri;Varve.Xsd`. That is right for most of the
     family, but wrong for layer 3.
   - `Varve.Sparql.Evaluation`'s contracts take algebra types: the evaluator's
     entry point takes a `Query` (ADR 0048), and `IServiceHandler` receives a
     `Service` node (ADR 0055).
   - ADR 0004's 2026-09-25 amendment widened the reserved contract rule to
     admit exactly that.
   - Adding `Varve.Sparql` globally would admit an algebra type onto
     `Varve.Store`'s contracts with no diagnostic, and ADR 0005 forbids
     exactly that: the store is SPARQL-free, and a store contract typed in
     algebra terms "would make the dependency real even though the reference
     points the right way".
   - So the widening goes where it is true, in the layer-3 project files.
2. **A13's `[DomainModel]` list guessed at names.** The list below is what
   `src/` contains, as of `main` at `6a65d41`.
3. **A13 made `ArchCompositionRoot` true for `Varve.Server` and the CLI only.**
   ADR 0060 made the composition root the executable: every layer-6 project,
   which today is the two smoke apps and the benchmarks.
4. **A13's `System.Reflection.*` ban cannot be a namespace ban.** The
   namespace holds `AssemblyMetadataAttribute`, which the build itself writes
   into every assembly to carry its layer (ADR 0003). It also holds the
   assembly-information attributes the SDK generates. The ban is on the
   members that inspect and invoke, which is what constraint 2 is about.
5. **A13 placed everything in `Directory.Build.props`.** `ArchLayer` is per
   project (ADR 0003's declaration mechanism, unchanged), and so is the
   layer-3 widening in (1).

## Decision

### The `DD` configuration

| Setting | Value | Where |
|---|---|---|
| `ArchFamily` | `Varve` | `Directory.Build.props` |
| `ArchLayer` | The project's layer per ADR 0060's table: `Varve.Iri`, `Varve.Xsd` 0; `Varve.Rdf` 1; `Varve.Turtle`, `Varve.Sparql`, `Varve.Sparql.Results` 2; `Varve.Sparql.Evaluation` 3; `Varve.Store` 4; `Varve.Sparql.Store` 5; `Varve.AotSmoke`, `Varve.WasmSmoke`, `Varve.Benchmarks` 6. Test assemblies and `Varve.Analyzers` declare none. It replaces `VarveLayer`, one property, read everywhere `VarveLayer` is read today. | each project file |
| `ArchCompositionRoot` | `true` on every layer-6 project, and nowhere else (ADR 0060). `Varve.Server` and the CLI join when they exist. | each layer-6 project file |
| `ArchContractTypeAssemblies` | `Varve.Rdf;Varve.Iri;Varve.Xsd` | `Directory.Build.props` |
| `ArchContractTypeAssemblies` | the global value **plus `Varve.Sparql`** | layer-3 project files only: `Varve.Sparql.Evaluation` today. `Varve.Shacl` takes the same line only if, when it exists, its contracts take algebra types. That is decided then, not here |
| `dd_rule_id_prefixes` | `VARVE` | `.editorconfig` |
| `dd_banned_names` | **Unset.** The package's default list contains all five names ADR 0004 banned (`Common`, `Core`, `Utils`, `Helpers`, `Abstractions`) and adds `Utilities`, `Shared`, `Misc`, `Internal` and `Extensions`. The option replaces the list rather than extending it, so the only thing setting it could do here is narrow it. | — |

**Any further widening of `ArchContractTypeAssemblies` is per project, in
that project's file, and cites its reason there.** The first expected case is
`Varve.Sparql.Store` at layer 5 naming `Varve.Store`, when its contracts take
store types. A global widening is never the answer, because it silently widens
`Varve.Store`'s contracts too.

### The `[DomainModel]` namespaces

Verified in `src/`. Each is declared by an assembly-level `[DomainModel]`
attribute in its own assembly, citing the decision that defines that model:

| Namespace | Assembly | What it holds |
|---|---|---|
| `Varve.Iri` | `Varve.Iri` | `IriRef`, its components and errors |
| `Varve.Xsd` | `Varve.Xsd` | the XSD value types and their comparison (ADR 0051) |
| `Varve.Rdf` | `Varve.Rdf` | terms, quads, the handle, `IQuadSource`, the delta, the overlay, and `InMemoryDataset` as an immutable value assembled by a sealed builder (ADR [0067](0067-inmemorydataset-is-a-value-built-by-a-builder.md)). The whole assembly is under this prefix: the package matches sub-namespaces too, and `DD0006` keeps every public type under the root |
| `Varve.Sparql.Algebra` | `Varve.Sparql` | the algebra node types (ADR 0048) |
| `Varve.Store.Log` | `Varve.Store` | commits, positions and the log's values, per ADR [0065](0065-wrapper-types-and-the-store-log-namespace.md). Today these types share the root `Varve.Store` namespace with the engine; 0065 moves them |
| `Varve.Shacl.Reports` | `Varve.Shacl` | **planned**; the assembly does not exist (milestone 8) |

Not model namespaces:

- `Varve.Sparql.Evaluation` and its sub-namespaces, `Varve.Sparql.Parsing` and
  `Varve.Sparql.Writing`: operators, compilers and parsers;
- `Varve.Turtle`, `Varve.Sparql.Results`: syntaxes;
- `Varve.Sparql.Store`: an integration;
- the root `Varve.Store` namespace, which is the engine: `Dataset`, the views,
  the storage contract and its memory backend.

### `BannedSymbols.txt` additions

Each entry cites this ADR in its comment and its message (ADR 0063):

- **`dynamic`**, by banning `Microsoft.CSharp.RuntimeBinder`: the brief bans it
  (constraint 2), and nothing bans it today.
- **The reflection members that inspect or invoke**:
  - `MethodBase.Invoke`, `PropertyInfo.GetValue`/`SetValue` and
    `FieldInfo.GetValue`/`SetValue`;
  - `Type.GetMethod(s)`, `GetProperty`/`GetProperties`,
    `GetField`/`GetFields`, `GetMember(s)` and `GetConstructor(s)`.

  These join the existing entries for `Reflection.Emit`, `Type.GetType(string)`,
  `Activator` and `Assembly.Load*`. Not the `System.Reflection` namespace, for
  the reason under Context. `System.Reflection.Metadata` is not affected.
- **`System.Linq.Enumerable`** in projects with `ArchLayer` 0 to 4, test
  assemblies excepted. It is in a second list, `eng/BannedSymbols.Model.txt`,
  added by layer the way `BannedSymbols.Deterministic.txt` is added by opt-in.
  This is **stricter than the brief**, which bans LINQ in marked hot paths
  only. It is taken anyway: layers 0 to 4 are where every per-quad path lives,
  and one hot path missed by a mark would allocate per quad with no diagnostic.
  The cost is small and was measured: one source file in `src/` uses
  `System.Linq`, in `Varve.Sparql.Evaluation`, and it is rewritten in session
  3 of #43.

### `VARVE0003` — hot-path discipline (tier 1, error)

Within a `[HotPath]` member, or every member of a `[HotPath]` type, none of the
following is allowed:

- a boxing conversion;
- a lambda or local function that captures;
- `new` of an array or a reference type;
- string concatenation or interpolation;
- a `params` call;
- LINQ;
- `foreach` over an enumerator that is not a struct;
- `async`. A `ValueTask` state machine is allowed only where a
  `[DesignDecision]` cites the I/O decision.

Nor may it call a member that is not itself `[HotPath]`, unless the member is
a BCL member on the allow-list: `Span<T>`, `ReadOnlySpan<T>`, `MemoryMarshal`,
`BinaryPrimitives`, `Unsafe`, `ArrayPool<T>`, `Vector*`. The allow-list is
configuration (`varve_hot_path_allowed_types` in `.editorconfig`), not code. A
call from a hot path to an ordinary Varve member is the error that keeps the
two worlds apart.

### `VARVE0004` — hot-path signature (tier 1, error)

A `[HotPath]` member does not take or return `IEnumerable<T>` or `Task`. It
does not take an interface-typed parameter, unless the parameter's type is a
`[Contract]` type itself marked `[HotPath]`. It may be `ref struct`-based and
may use `ref`, `in` and `scoped`.

### The attribute (supersedes ADR 0026's placement)

`[HotPath]` is **`DecisionDriven.HotPathAttribute`**, which the package's
generator emits as an `internal` type into each compilation
(`DecisionsAsTypes.AttributesAreSourceGenerated`). It takes one argument: the
decision the hot path answers to, for Varve the ledger entry for constraint
5's allocation rule. `VARVE0003` and `VARVE0004` match it by full name, which
is the technique 0026 chose, now with the generator's name.
`eng/HotPathAttribute.cs` and its link in `Directory.Build.targets` are removed
in session 2 of #43. Every existing mark is rewritten to cite its decision.

Each rule has a page, `docs/rules/VARVE0003.md` and `VARVE0004.md`, citing
this ADR, and tests with a violating and a conforming case per diagnostic
(ADR 0004). Both land in session 2.

## Alternatives considered

- **`ArchContractTypeAssemblies` including `Varve.Sparql` globally**, as the
  simplest reading of ADR 0004's amended wording. Rejected under Context (1):
  it lets an algebra type onto a store contract, and ADR 0005 is precisely
  about that dependency.
- **`[DomainModel]` on `Varve.Store` as a whole.** No namespace move, one
  attribute. Rejected: it declares the engine a model, so `Dataset` and the
  views would be held to DD0019's immutability. Either the rule would be
  wrong about them or they would have to be rewritten into something they are
  not. The log is the model and the engine is not, and ADR 0065 draws that
  line in the namespace.
- **Hot-path rules in the generic package.** Rejected for now, as A13 says:
  the allow-list and the boxing and closure rules are shaped by Varve's budget
  per quad. A second product line that needs them argues for moving them, by
  a later ADR in the analyzer repository.
- **Allocation enforced by BenchmarkDotNet only.** Rejected: a benchmark
  finds a regression after it is written, and the analyzer prevents the
  class of code that causes it. The allocation tests stay as the second net.
- **Keep `eng/HotPathAttribute.cs`** and teach the rules both names. Rejected:
  two attributes meaning one thing, one of which cannot cite a decision.
  Citing a decision is the point of the change.
- **LINQ banned only inside `[HotPath]`**, as the brief states it. It would be
  sufficient if every hot path were marked. Rejected, because the failure it
  guards against is an unmarked hot path, and that is the one case a rule
  keyed on the mark cannot see.

## Consequences

- **Every packable project gains an `ArchLayer`** (a rename of `VarveLayer`),
  and the four layer-6 projects gain `ArchCompositionRoot`. The layer-3
  project gains one line widening its contract vocabulary.
- **Four `[DomainModel]` declarations land in session 2** (`Varve.Iri`,
  `Varve.Xsd`, `Varve.Rdf`, `Varve.Sparql.Algebra`) and one in session 3
  (`Varve.Store.Log`), each before any `[Contract]` in its assembly, as the
  package README requires.
- **The existing `[HotPath]` marks gain a decision argument**, and the parser
  cores, the term arena, the quad cursors, the evaluator's operator loops and
  the index scans become callable only from hot-path code or through the
  allow-list. The boundary between them and the rest becomes explicit, which
  is the point. Some of today's marked methods will turn out to call ordinary
  helpers; each is fixed or unmarked, never suppressed.
- **The allocation tests** (zero per quad for N-Triples and N-Quads, 48 bytes
  per row in the evaluator) stay, and become the second net behind the
  compiler.
- **`Varve.Sparql.Evaluation`'s one use of LINQ is rewritten**, in session 3.

## Checks

- **Checked against the accepted ADRs** (0001, 0003–0005, 0007–0018,
  0021–0063). Touches:
  - **0003** and **0060**: the layer table, transcribed into `ArchLayer`
    unchanged.
  - **0004**: the banned-symbol file, extended under its own rules.
  - **0005**: the reason `Varve.Sparql` is not global.
  - **0026**: superseded in part, above.
  - **0048** and **0055**: the layer-3 contracts that need the algebra.
  - **0051**: `Varve.Xsd`'s scope.
  - **0062** and **0063**.

  The specification is not touched.
- **Layer ownership.** Unchanged for every package.
- **Analyzer rule.** Allocates `VARVE0003` (hot-path discipline) and
  `VARVE0004` (hot-path signature). Both are implemented in session 2 of #43.
- **Open questions owned.** None. ADR 0003's open question 1 (`Varve.Shacl`
  and the evaluator) stays open. The `Varve.Shacl` rows above are
  placeholders that assume nothing about how it is answered.
