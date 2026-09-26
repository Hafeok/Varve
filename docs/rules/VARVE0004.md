# VARVE0004 — A hot path's signature does not force allocation or dispatch

| | |
|---|---|
| **Category** | `Varve.HotPath` |
| **Tier** | 1 (error) |
| **Motivated by** | [ADR 0064](../adr/0064-varve-configuration-and-hot-path-rules.md), `VarveConfigurationAndHotPathRules.HotPathSignature` |
| **Protects** | The brief's constraint 5, *allocation per quad is a defect* (`BriefHardConstraints.AllocationPerQuadIsADefect`) |

## Principle

`VARVE0003` checks a hot path's body. Its signature is a promise every caller
has to keep, and some signatures cannot be kept without allocating: an
`IEnumerable<T>` return is an enumerator per call, a `Task` is a state machine,
and an interface-typed parameter is a virtual call per element the JIT cannot
see through, usually with a boxed struct behind it.

## What it reports

On a `[HotPath]` method or property, or a member of a `[HotPath]` type:

| Finding | |
|---|---|
| returns `IEnumerable<T>` or `IEnumerable` | as the declared type |
| takes `IEnumerable<T>` or `IEnumerable` | |
| returns or takes `Task` or `Task<T>` | `ValueTask` is not reported |
| takes an interface-typed parameter | unless the interface is a `[Contract]` itself marked `[HotPath]` |

A type parameter constrained to an interface (`TSource : IQuadSource`) is not an
interface-typed parameter. It is the shape that lets the JIT specialise, and
the one the rule steers to. `ref struct`s, `ref`, `in` and `scoped` are what a
hot signature is made of.

## Configuration

None. `[HotPath]` and `[Contract]` are matched by full name, as for
`VARVE0003`.

## False-positive story

Tier 1 and decidable: parameter and return types. The case that reads as a
false positive is an interface that really is on the hot path, such as the
quad source. That is answered in the design, by declaring it a `[Contract]`
marked `[HotPath]`, which holds every implementation to `VARVE0003`. Otherwise
the exception path is `[DesignDecision]` on the member, citing a filed decision
(ADR 0062). Suppression is `DD0008`.

## Violating example

```csharp
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
public int M(ISource source) => source.Next();
```

```text
error VARVE0004: 'C.M(ISource)' is [HotPath] and takes interface 'ISource' as parameter 'source', which is not a [Contract] marked [HotPath].
Decide: take a type parameter constrained to the interface so the JIT can specialise, or a struct | declare the interface a [Contract] and mark it [HotPath], holding every implementation to the rules | mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.HotPath)] citing the accepted decision that says so.
Do not add the attribute without a decision that answers this; if the reason is only that the code already looked like this, take the design change.
```

## Conforming example

```csharp
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
public int M<TSource>(TSource source) where TSource : ISource => source.Next();
```

## See also

- [`VARVE0003`](VARVE0003.md): the hot path's body.
- `tests/Varve.Analyzers.Tests/HotPathSignatureAnalyzerTests.cs`.
