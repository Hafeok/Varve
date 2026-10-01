---
set: model-namespaces-for-layers-3-to-5
namespace: varve
adr: 0069
decisions:
  - key: PackagesAtLayers3To5HaveOneModelNamespace
    statement: "Each package at layers 3 to 5 whose contracts name its own data types has one DomainModel namespace for them, the package's root namespace plus Model; the engine namespaces themselves stay undeclared"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-29T00:00:00Z
  - key: EvaluationModel
    statement: "Varve.Sparql.Evaluation.Model is the evaluator's model namespace and holds QueryResultKind, ServiceRequest and ServiceResult"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-29T00:00:00Z
  - key: SparqlStoreModel
    statement: "Varve.Sparql.Store.Model is the integration's model namespace and holds LoadedDocument"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-29T00:00:00Z
  - key: DurabilityIsALogValue
    statement: "Durability, what a storage backend promises once a flush returns, is a value in Varve.Store.Log and not engine in Varve.Store"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-29T00:00:00Z
  - key: FailureTextIsDisplayText
    statement: "ServiceResult.Failure and LoadedDocument.Failure are strings: the text a failed SERVICE call or LOAD gives the error a person reads, which nothing compares, parses or routes on, because whether there is a failure is the half a program reads"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-29T00:00:00Z
  - key: ColumnIndexIsAWrapper
    statement: "A column of a SELECT's solutions is a ColumnIndex, a readonly record struct over its position in SolutionResults.Variables in Varve.Sparql.Evaluation.Model, and SolutionResults takes it where it took an int"
  - key: MaxRecordBytesIsAByteCount
    statement: "DatasetOptions.MaxRecordBytes is a ByteCount, as SegmentBytes is, and opening a dataset refuses one below 64 bytes or above what a record's 32-bit length can carry"
  - key: ImplicitTimezoneIsATimeSpan
    statement: "EvaluationOptions.ImplicitTimezoneOffset is a TimeSpan, a whole number of minutes from minus 14 to plus 14 hours, and replaces ImplicitTimezoneOffsetMinutes"
---

# Model namespaces for layers 3 to 5

**Accepted** by the maintainer on 2026-09-29. Filed by session 3 of #43. The rulings of
[ADR 0069](../adr/0069-model-namespaces-for-layers-3-to-5.md), which is
`Accepted`, one line each. The ADR is the narrative; this is what code cites.

`PackagesAtLayers3To5HaveOneModelNamespace` is the syntax packages' ruling
(`VarveConfigurationAndHotPathRules.SyntaxPackagesHaveOneModelNamespace`) one
layer band up. `EvaluationModel` and `SparqlStoreModel` are what each
assembly's `[DomainModel]` declaration cites.

Received from earlier sets: `DurabilityIsALogValue` supersedes the one line of
`WrapperTypesAndTheStoreLogNamespace.LogIsTheModel`'s ADR that kept
`Durability` in `Varve.Store` as engine; that key's statement is unchanged.

**`FailureTextIsDisplayText`.** Bringing `ServiceResult` and `LoadedDocument`
under `DD0013` reports their `Failure` strings. Each is the text of the
`QueryEvaluationException` or `SparqlUpdateException` that a failed `SERVICE`
call or `LOAD` becomes, read by a person; the evaluator acts on whether there
is a failure, never on its words. Both members cite this with
`Scope = ExceptionScope.Boundary`. The alternative is a `FailureText` wrapper
over `string`, which names the text but adds no rule to it.

**`ColumnIndexIsAWrapper`, `MaxRecordBytesIsAByteCount`,
`ImplicitTimezoneIsATimeSpan`.** Filed by the close of session 3 of #43, on the
maintainer's decision, unaccepted. They are the rulings of ADR 0069's amendment
of 2026-10-01. None of the three is in a model namespace, so no `DD` rule
reported them. The session-3 audit found them on public surfaces. None is cited
from code; `ColumnIndex`'s remarks name its key.
