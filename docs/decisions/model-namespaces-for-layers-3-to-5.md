---
set: model-namespaces-for-layers-3-to-5
namespace: varve
adr: 0069
decisions:
  - key: PackagesAtLayers3To5HaveOneModelNamespace
    statement: "Each package at layers 3 to 5 whose contracts name its own data types has one DomainModel namespace for them, the package's root namespace plus Model; the engine namespaces themselves stay undeclared"
  - key: EvaluationModel
    statement: "Varve.Sparql.Evaluation.Model is the evaluator's model namespace and holds QueryResultKind, ServiceRequest and ServiceResult"
  - key: SparqlStoreModel
    statement: "Varve.Sparql.Store.Model is the integration's model namespace and holds LoadedDocument"
  - key: DurabilityIsALogValue
    statement: "Durability, what a storage backend promises once a flush returns, is a value in Varve.Store.Log and not engine in Varve.Store"
  - key: FailureTextIsDisplayText
    statement: "ServiceResult.Failure and LoadedDocument.Failure are strings: the text a failed SERVICE call or LOAD gives the error a person reads, which nothing compares, parses or routes on, because whether there is a failure is the half a program reads"
---

# Model namespaces for layers 3 to 5

**Unaccepted.** Filed by session 3 of #43, for the maintainer. The rulings of
[ADR 0069](../adr/0069-model-namespaces-for-layers-3-to-5.md), which is
`Proposed`, one line each. The ADR is the narrative; this is what code cites.

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
