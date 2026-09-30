# 0069 — One model namespace per package at layers 3 to 5; `Durability` is a log value

## Status

**Accepted.** 2026-09-30. Filed 2026-09-29 by session 3 of #43
(issue [#43](https://github.com/Hafeok/Varve/issues/43)), on the maintainer's
decision of where each type goes. The maintainer accepted its decision set,
`ModelNamespacesForLayers3To5`, on 2026-09-29, and this ADR on 2026-09-30.

It **supersedes in part**:

- **ADR [0065](0065-wrapper-types-and-the-store-log-namespace.md)**, one line
  of its *Decision* and nothing else: the list of what stays in `Varve.Store`
  as the engine names `Durability`. `Durability` moves to `Varve.Store.Log`.
  Everything else in that list, the table of moved types, the wrappers and
  the rest of 0065 stand.
- **ADR [0064](0064-varve-configuration-and-hot-path-rules.md)**, two bullets
  of its list of namespaces that are *not* model namespaces:
  "`Varve.Sparql.Evaluation` and its sub-namespaces" loses the sub-namespace
  `Varve.Sparql.Evaluation.Model`, and "`Varve.Sparql.Store`: an
  integration" gains a model namespace, `Varve.Sparql.Store.Model`. The root
  namespaces themselves stay undeclared, as 0064 says.

It extends to layers 3 to 5 the pattern of 0064's 2026-09-26 amendment
(`VarveConfigurationAndHotPathRules.SyntaxPackagesHaveOneModelNamespace`),
which gave each syntax package one model namespace.

## Context

`DD0010` reports a `[Contract]` member whose signature names a type declared
in the same assembly outside any `[DomainModel]` namespace. A contract is the
surface other assemblies implement or call, and the rule wants every type on
it to be either another contract or model data the package has declared as
such. After session 3 of #43 declared `Varve.Store.Log` and moved the log's
values into it (ADR 0065), five sites remain at layers 3 to 5:

| Contract member | Names | Declared in |
|---|---|---|
| `ISegmentStore.Durability` | `Durability` | `Varve.Store` |
| `QueryResults.Kind` | `QueryResultKind` | `Varve.Sparql.Evaluation` |
| `IServiceHandler.Execute` | `ServiceRequest`, `ServiceResult` | `Varve.Sparql.Evaluation` |
| `ILoadSource.LoadAsync` | `LoadedDocument` | `Varve.Sparql.Store` |

Each of the five is data: an enum, or a sealed class of get-only
properties, with no behaviour a caller depends on. None can be a `[Contract]`
itself. An enum cannot, and making a sealed value class an interface so that
the rule stops asking would invent an extension point no one needs. So each
must live in a model namespace.

ADR 0064 said that `Varve.Sparql.Evaluation` and its sub-namespaces, and
`Varve.Sparql.Store`, are not model namespaces. That was right of the
operators, compilers and request execution those namespaces hold, and the
session 2 amendment already found the same thing for the syntax packages:
the machinery is not the model, but the data its contracts exchange is.

`Durability` is a different case. ADR 0065 listed it under the engine, with
the storage contract that declares it. But it is not machinery. It is what a
backend promises once a flush returns (ADR 0018), a fact about how durable a
position of the log is, and the store reports it as such
(`Dataset.Durability`). It is a value the log's guarantees are stated in, and
`Varve.Store.Log` is already the model namespace for those values. Moving it
there changes what 0065 decided about one type, which ADR
[0068](0068-dated-amendments.md) makes a supersession, not an amendment.

## Decision

### One model namespace per package at layers 3 to 5

**A package at layers 3 to 5 whose contracts name its own data types has one
`[DomainModel]` namespace for them: the package's root namespace plus
`.Model`.** The root namespace, and every other sub-namespace, stay
undeclared. This is the syntax packages' rule (0064, amended 2026-09-26) one
layer band up, and the name is the one `Varve.Turtle.Model` and
`Varve.Sparql.Results.Model` already use.

`Varve.Store` is the exception it already was: its model namespace is
`Varve.Store.Log`, named for what is in it (ADR 0065), and this ADR does not
rename it.

| Namespace | Assembly | What it holds |
|---|---|---|
| `Varve.Sparql.Evaluation.Model` | `Varve.Sparql.Evaluation` | `QueryResultKind`, what a query form answers; `ServiceRequest` and `ServiceResult`, what a `SERVICE` handler is asked and answers (ADR 0055) |
| `Varve.Sparql.Store.Model` | `Varve.Sparql.Store` | `LoadedDocument`, what a `LOAD` source answers (ADR 0057) |

Each is declared by an assembly-level `[DomainModel]` attribute in its own
assembly's `DomainModel.cs`, citing this ADR's key for it.

`QueryResults` and its three subclasses stay in `Varve.Sparql.Evaluation`.
They are the contract (`EvaluationSurfaces.QueryResultsIsAClosedHierarchy`),
and they hold cursors and dispose them, which a model type does not.
`IServiceHandler`, `RefusingServiceHandler`, `ILoadSource` and the options
types stay where they are too.

### `Durability` is a log value

`Durability` moves from `Varve.Store` to `Varve.Store.Log`. Its members and
their meaning are unchanged (ADR 0018, ADR 0040). `ISegmentStore.Durability`
and `Dataset.Durability` keep their names and return the moved type.

### The primitives this brings under the model rules

Declaring a namespace a model brings its public types under `DD0013` (naked
primitives), `DD0016` (bool parameters), `DD0019` (immutability) and the rest.
The moved types report one thing between them: the `string` failure text of
`ServiceResult.Failure` and `LoadedDocument.Failure`.

**`FailureTextIsDisplayText`.** Both strings are the message of a failure:
why a `SERVICE` endpoint or a `LOAD` source gave no answer. The evaluator
puts the first into the `QueryEvaluationException` that fails the query, and
the update puts the second into the `SparqlUpdateException` that fails the
operation. A person reads them. No code parses or branches on them: whether
there is a failure (`ServiceResult.IsFailure`, a null
`LoadedDocument.Failure`) and `SILENT` are what the evaluator acts on. This is
`StoreLogSurfaces.UnavailableReasonIsDisplayText` and
`SyntaxModelSurfaces.ErrorMessagesAreDisplayText` again, for a failure a
handler reports. Both members cite it with `Scope = ExceptionScope.Boundary`.
The alternative is a wrapper, a `FailureText` over `string`. Each type holds
one string, so there is nothing for it to be swapped with, and the wrapper
would name the text without adding a rule to it.

## Alternatives considered

- **Declare the root namespaces a model.** One attribute per assembly, and no
  type moves. Rejected, as 0064 rejected it for `Varve.Store` and the syntax
  packages: `DD0019` would hold the evaluator's operators and the update's
  request execution to immutability, which is wrong for machinery.
- **One `[DomainModel]` per type**, leaving each where it is. Rejected: the
  attribute takes a namespace prefix, so declaring `Varve.Sparql.Evaluation`
  types one by one is not possible without declaring the namespace. And one
  namespace per package keeps the line between data and machinery where a
  reader looks for it, which is the reason the syntax amendment gave.
- **Make `ServiceResult` and `LoadedDocument` interfaces**, so the contracts
  name contracts. Rejected: an interface over two properties and a factory
  invites implementations that disagree about what a failure is, and it would
  be declared only to satisfy the rule.
- **`Durability` into a new `Varve.Store.Model`**, or left in `Varve.Store`
  with a `[DesignDecision]` on `ISegmentStore.Durability`. Rejected: a second
  model namespace in `Varve.Store` would split the log's values across two
  names for no reason, and the `DD0010` exception has no decision to cite. A
  backend's durability is a fact about the log, which is what
  `Varve.Store.Log` is for.
- **`Varve.Sparql.Evaluation.Results` and `Varve.Sparql.Store.Documents`**,
  names that say what is in them, as `Log` does. Rejected: the evaluator's
  model holds a result kind and the `SERVICE` exchange, which are not all
  results, and `.Model` is already the name the syntax packages use for the
  same role.

## Consequences

- **Four public types change namespace**, and `Durability` a fifth. None has
  shipped (every line is in `PublicAPI.Unshipped.txt`), so no released
  surface breaks. Callers add a `using`: the tests, the conformance harness
  and `Varve.Sparql.Store`'s request execution do. The smoke apps and the
  benchmarks name none of the five.
- **The five `DD0010` sites are gone.** Two other `DD0010` findings in
  `Varve.Store`, on `TermView` in the `DatasetView` and `StagingView`
  constructors, are not this ADR's: they are sorted by the two-bucket rule
  when they are taken up.
- **The branch is expected red** (ADR [0066](0066-expected-red-pull-requests.md))
  until the maintainer accepts this ADR's keys: the two `[DomainModel]`
  declarations and the two `Failure` members cite them.
- **ADR 0065's Status line names this ADR**, as ADR 0068 requires, and says
  which line it supersedes. 0065's text is not edited. So does 0064's.

## Checks

- **Checked against the accepted ADRs** (0001, 0003–0005, 0007–0018,
  0021–0068). Touches:
  - **0018** and **0040**: `Durability`'s members and meaning, and the
    storage contract's shape, are unchanged; only its namespace moves.
  - **0055**: `ServiceRequest` and `ServiceResult` carry what 0055 says,
    and `IServiceHandler` stays in `Varve.Sparql.Evaluation`, as
    `ServiceThroughAHandlerTheDefaultRefuses.ServiceHandlerContract` states.
  - **0057**: the public surface is still `UpdateOptions`, `ILoadSource`,
    `LoadedDocument` and `SparqlUpdateException`; `LoadedDocument` is in a
    sub-namespace.
  - **0064**: superseded in part, above. Its hot-path rules and its
    configuration stand.
  - **0065**: superseded in part, above.
  - **0066**: the citations are of unaccepted keys until accepted.
  - **0068**: a change of meaning, so a supersession, named in the
    superseded ADRs' Status lines.
- **Layer ownership.** `Varve.Sparql.Evaluation.Model` is layer 3,
  `Varve.Store.Log` layer 4 and `Varve.Sparql.Store.Model` layer 5. No
  reference changes.
- **Analyzer rule.** None new. `DD0010` enforces it, and `DD0013`–`DD0019`
  hold the declared namespaces.
- **Open questions owned.** None.
