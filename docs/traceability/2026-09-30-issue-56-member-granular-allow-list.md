# The hot-path allow-list by member — session 4 of #43, closing #56

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt below is verbatim.
> The transcript itself is held by the maintainer. Developed with AI assistance
> under human review.

| | |
|---|---|
| **Issue** | [#56](https://github.com/Hafeok/Varve/issues/56), filed in session 3 of [#43](https://github.com/Hafeok/Varve/issues/43) |
| **Date** | 2026-09-30 |
| **Tool** | Claude Code 2.1.285, a cloud session, the same session as sessions 2 and 3 |
| **Model** | Not recorded in the repository: the tool is configured to keep model identifiers out of pushed artifacts. The session's own metadata and the transcript hold it |
| **Session identifier** | `session_012aNo8XhYGVT6rgfvDN69P7` |
| **Branch** | `claude/analyzers-layers-0-2-cl3hky`, restarted from `main` at `acb802b`, the merge of #53 |

## The prompt

> Session 2 (#53) is merged. Before the layer-3 and layer-4 work, close Varve #56: VARVE0003's allow-list admits individual members as well as types, with tests, and ADR 0064 gains a dated amendment. If analyzer #80's DD0004 misreport hits any static in Varve.Store or Varve.Sparql.Evaluation, stop and fix it upstream first as with preview.4 and preview.5; otherwise leave it for the benchmark and proceed.

## Report

### decision-driven-analyzers#80 does not reach the store or the evaluator

`DD0004` misreports a static property that computes its value and stores
nothing. On `main`, `Varve.Store` and `Varve.Sparql.Evaluation` build with
no override and no report, so the question was whether a site had been
reshaped or cited to get there. None was:

- **The computed static properties** in both projects return default values,
  UTF-8 literals as `ReadOnlySpan<byte>`, or `Solutions.Empty`'s
  `IEnumerator<ulong[]>`. None is reported, and none carries a
  `[DesignDecision]` for `DD0004`.
- **The three changes session 3 made for `DD0004`** there were true positives.
  Each held stored state: `Genesis`, a `byte[]` auto-property, and
  `NoRetractions`, a `static readonly` array of arrays, both in `Varve.Store`;
  and `Terms.Datatypes`, a `static readonly` array in the evaluator.

So #80 stays with the benchmark it was found in (`BsbmBenchmarks.Names`), and
the work went ahead without an upstream fix first.

### What changed

- **`AllowList`** (`src/Varve.Analyzers/HotPath.cs`) matches member entries.
  A type entry still admits every member. `Type.Member` admits that member
  and its overloads, matched against the callee's original definition. A
  trailing `*` is a prefix for either.
- **`HotPathDisciplineAnalyzer`** works out what a call runs: the method
  itself, or a property's getter, its setter, or both for a compound
  assignment, a `??=` or an increment. It admits the call if the type is
  listed or every member it runs is.
- **`.editorconfig`** lists `ImmutableArray`1` and `CancellationToken` by their
  non-allocating members, not as types:
  - `ImmutableArray`1`: `get_Item`, `get_Length`, `get_IsEmpty`,
    `get_IsDefault`, `get_IsDefaultOrEmpty`, `AsSpan`, `AsMemory` and
    `GetEnumerator`;
  - `CancellationToken`: `get_IsCancellationRequested`, `get_CanBeCanceled`
    and `ThrowIfCancellationRequested`.
- **ADR 0064** gains a block dated 2026-09-30 under *`VARVE0003`*, and its
  Status names it (ADR 0068).
- **`docs/rules/VARVE0003.md`** describes the three entry forms. Its old copy
  of the session-2 list, and its note that `HotPathScope.AllowListAddsNonAllocatingBclHelpers`
  was unaccepted, were out of date and are replaced by a pointer to
  `.editorconfig`.

### Tests

Nine analyzer tests are new. Before the change, seven failed; the two that
passed pin today's behaviour (a type entry admits every member, and an
unlisted member is reported). After it, all 96 pass:

| Test | Allow-list | Expect |
|---|---|---|
| a member on the list | `System.Math.Abs` | clean |
| the rest of its type | `System.Math.Abs`, calling `Max` | reported |
| every overload of a listed name | `System.Math.Abs` | clean |
| a type entry admits every member | `System.Math` | clean |
| a property by its accessor, an indexer by `get_Item` | `ImmutableArray`1.get_Length`, `.get_Item` | clean |
| an indexer, unlisted | `ImmutableArray`1.get_Length` only | reported |
| a listed getter, then a write | `StringBuilder.get_Length` | reported |
| a compound assignment, the getter only | `StringBuilder.get_Length` | reported |
| a compound assignment, both accessors | `.get_Length`, `.set_Length` | clean |

**The Varve build uses the new form.** With `ImmutableArray`1.get_Item` taken
off the list, `Terms.Datatype` in `Varve.Sparql.Evaluation` is reported for
the indexer. With it on, the whole solution builds with 0 errors and 0
warnings.

### Decisions filed, unaccepted, awaiting the maintainer

| Decision | Cited at |
|---|---|
| `VarveConfigurationAndHotPathRules.AllowListNamesMembers` | ADR 0064's 2026-09-30 amendment; the rule page |
| `HotPathScope.ImmutableArrayAndCancellationTokenByMember` | `.editorconfig`; the rule page |

Neither is cited from code, so neither is a `CS0618` and the pull request is
not red while they wait. `HotPathScope.AllowListAddsNonAllocatingValueTypes`
is left as accepted. Its clause admitting the two types by type was
conditional on #56, and the second key above is what discharges it.

### Gates

The results of `dotnet run eng/ci.cs` are in the pull request.
