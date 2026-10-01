# VARVE0003 — A hot path does not allocate, and calls only hot-path code

| | |
|---|---|
| **Category** | `Varve.HotPath` |
| **Tier** | 1 (error) |
| **Motivated by** | [ADR 0064](../adr/0064-varve-configuration-and-hot-path-rules.md), `VarveConfigurationAndHotPathRules.HotPathDiscipline` and `.HotPathAllowListIsConfiguration` |
| **Protects** | The brief's constraint 5, *allocation per quad is a defect* (`BriefHardConstraints.AllocationPerQuadIsADefect`) |

## Principle

A member marked `[HotPath]` runs once per quad, or once per row, and a
per-quad allocation is a defect. A benchmark finds one after it is written;
this rule refuses the class of code that causes it while it is being written.
The allocation tests stay as the second net.

## What it reports

Inside a `[HotPath]` member, or any member of a `[HotPath]` type (and the
lambdas and local functions declared in them):

| Finding | Why |
|---|---|
| a boxing conversion | a heap object per value |
| a lambda or local function that captures, `this` included | a closure object per call |
| `new` of an array or a reference type, collection expressions included | the allocation itself |
| string concatenation or interpolation | a string per call |
| a `params` call that builds an array | a hidden array per call; a `params ReadOnlySpan<T>` call is fine |
| LINQ (`System.Linq.Enumerable`, `Queryable`) | an iterator and usually a delegate |
| `foreach` over an enumerator that is a class | an enumerator object; arrays, strings and spans are lowered to index loops |
| `async` | a state machine |
| a call to a member that is not `[HotPath]` | a hot path that calls ordinary code is only as disciplined as that code |

**A call** is an invocation, a property access, a struct's explicit
constructor, and a user-defined operator or conversion. A callee is fine when
it, its property, or a type containing it is `[HotPath]`; when it implements a
member of an interface marked `[HotPath]` or overrides a member marked
`[HotPath]`; when it is a local function or
lambda of the hot member itself; when it carries
`[DesignDecision(..., Scope = ExceptionScope.HotPath)]`; or when it, or its
type, is a BCL member or type on the allow-list.

**What is held to the rule**: a member marked `[HotPath]`, a member of a type
marked `[HotPath]`, a member implementing a member of an interface marked
`[HotPath]`, and an override of an abstract or virtual member marked
`[HotPath]`, since a call through the base would otherwise reach code nobody
checked. The generated attribute cannot be put on an interface as a whole
(decision-driven-analyzers#61), so an interface is marked member by member.

**Not checked**: a test assembly (`*.Tests`). It is not shipped, and a test
double implementing a hot contract, such as a stub quad source, allocates per
call on purpose. The DD contract rules draw the same line.

**Not findings**, deliberately:

- a class's constructor and its field and property initializers: they run
  when the object is made, and a class made per quad is reported where it is
  made, at the hot caller's `new`. A struct's constructor runs per value and
  is checked;
- a static constructor and a static field or property initializer: they run
  once per type. A static property's getter runs per call and is checked;
- anything under a `throw`: the exception and its message are built on the
  path that ends the operation, not the path per quad, and a hot path that
  could not report malformed input would have to be wrong about it instead;
- an array's `Length`, which is an instruction, not a call;
- attribute arguments, which are metadata.

`[HotPath]` is `DecisionDriven.HotPathAttribute`, generated into each
compilation by `DecisionDriven.Analyzers`, and matched **by full name** (ADR
0064, superseding ADR 0026's placement). It takes the decision the hot path
answers to: `[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]`.

## Configuration

`varve_hot_path_allowed_types`, in `.editorconfig`, is a comma-separated list.
Each entry is one of these:

| Entry | Admits | Example |
|---|---|---|
| a type's full metadata name | every member of the type | `System.Span`1` |
| that name, a dot and a member's metadata name | that member and its overloads, and nothing else of the type | `System.Math.Abs` |
| either, ending in `*` | every name with that prefix | `System.Numerics.Vector*` |

A property is named by the accessor an access runs: `get_Length` for a read,
`set_Length` for a write, and both for a compound assignment or an increment,
each of which must be listed. An indexer's accessors are `get_Item` and
`set_Item`. A member entry is matched against the callee's original definition,
so `ImmutableArray`1.get_Item` covers `ImmutableArray<T>[i]` for every `T`.
Member entries were added by ADR 0064's amendment of 2026-09-30
(`VarveConfigurationAndHotPathRules.AllowListNamesMembers`, #56).

The list itself is in `.editorconfig`, with the decisions that admit each part:
ADR 0064's eight, the non-allocating helpers span code is written with
(`HotPathScope.AllowListAddsNonAllocatingBclHelpers`), the value types
(`HotPathScope.AllowListAddsNonAllocatingValueTypes`), and the members of
`ImmutableArray<T>` and `CancellationToken` that do not allocate
(`HotPathScope.ImmutableArrayAndCancellationTokenByMember`). The list is
configuration, not code
(`VarveConfigurationAndHotPathRules.HotPathAllowListIsConfiguration`): **unset
means empty**, so the rule allows nothing the repository did not write down. A
type or member from a `Varve.*` assembly never matches, whatever it is called.

## False-positive story

Tier 1: every input is a symbol or an operation in the compilation. Where it
reads as wrong, it is usually the allow-list being exact.
`BitOperations.PopCount` does not allocate, and is still reported, because it
is not on the list. (`ImmutableArray<T>.ToArray` is reported too, and rightly:
it allocates, and only its type's members that do not are listed.) The answers
are to inline
what is needed, to mark a Varve helper `[HotPath]` and hold it to the rules, or
to change the list, which is a change to a decided list and goes through the
decision that made it.

Suppressing it is not an answer: `DD0008` reports any `#pragma`,
`[SuppressMessage]` or `.editorconfig` downgrade for a `VARVE` id
(`dd_rule_id_prefixes = VARVE`). The exception path is
`[DesignDecision(..., Scope = ExceptionScope.HotPath)]` on the member or its
type, citing a filed decision (ADR 0062). A `[DesignDecision]` with another
scope answers another rule (a pool's `Pool` for DD0004, say) and exempts
nothing here. `async` in
particular is allowed only where a `[DesignDecision]` cites the I/O decision
that needs it.

## Violating example

```csharp
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
public int M(int x) => Helper.Twice(x);
```

```text
error VARVE0003: 'C.M(int)' is [HotPath] and calls 'Helper.Twice(int)', which is not [HotPath].
Decide: mark 'Helper.Twice(int)' [HotPath] and hold it to the same rules, or inline what the hot path needs from it | mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.HotPath)] citing the accepted decision that says so.
Do not add the attribute without a decision that answers this; if the reason is only that the code already looked like this, take the design change.
```

## Conforming example

```csharp
internal static class Helper
{
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public static int Twice(int x) => x + x;
}
```

## See also

- [`VARVE0004`](VARVE0004.md): the hot path's signature.
- `tests/Varve.Analyzers.Tests/HotPathDisciplineAnalyzerTests.cs`: a violating
  and a conforming case for every finding above.
