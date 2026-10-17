---
set: shacl-hot-path-scope
namespace: varve
origin: "VARVE0003 findings on Varve.Shacl in SHACL, part 1 (#120)"
decisions:
  - key: ValueNodesAreGatheredPerFocusNode
    statement: "A focus node's value nodes are gathered once per shape and focus node into a list, with the set that keeps a path's values distinct, and every constraint of the shape reads that list; a path step opens the source's cursor once per node it steps from, never once per quad"
  - key: ConformanceIsMemoisedPerValidation
    statement: "Whether a node conforms to a shape, a node's types, a class's superclasses and the data graph's reifiers are computed once per validation into tables that grow with what they hold, and a cyclic component's greatest fixpoint keeps its assignment in one; the components that check conformance, class membership or reifiers read the tables"
  - key: TermsAreResolvedOncePerValidation
    statement: "A held node's term is externalised the first time a constraint needs it, and a shape constant is looked up in the data source the first time it is used, both kept in tables for the rest of the validation"
  - key: ConstraintStateIsMadeOncePerValidation
    statement: "What a constraint needs at every focus node and that does not depend on one — sh:in's resolved members, sh:uniqueValuesFor's keys over the shape's targets, a compiled pattern — is made the first time the constraint runs in a validation and kept on the run"
  - key: ComparisonsGatherTheirOtherSide
    statement: "The property pair components gather the other path's values and a set of each side once per focus node; sh:closed reads a value node's triples into a list and, by types, the properties its classes permit; the list components read a value node's list into a list; each grows with what it holds"
  - key: ResultsCostTheirRecord
    statement: "A validation result is a record added to the run's list, with the path a sh:closed result names; a value node that conforms costs nothing"
  - key: ExtensionsAreTheCallersCode
    statement: "An extension's constraint is given the value nodes as terms and handles in lists made per focus node, with a context whose callbacks report into the run, and runs the extension's code, which Varve.Shacl does not hold to its own rules"
  - key: PatternsMatchThroughTheBclRegex
    statement: "sh:pattern transcodes a value's string into a pooled buffer and matches it with System.Text.RegularExpressions.Regex, compiled once per validation per pattern under ShaclOptions.RegexTimeout"
---

# What the hot-path rules do not cover in Varve.Shacl

**Filed unaccepted** by SHACL, part 1 (#120, under milestone 8, #13) (ADR
0066); every citation is `CS0618` until the maintainer accepts them.

The session prompt holds what runs per focus node or per value node to
`[HotPath]`, as everywhere else (ADR 0064). `Constraint.Validate` is marked,
which holds every component's override to `VARVE0003`, and
`ValidationRun.ValidateFocus`, which runs once per shape and focus node. The
first build reported 190 findings. Most were fixed; what is left is sorted
here, each cited where the path crosses it.

**Fixed rather than excepted:**

- The value nodes were an `IReadOnlyList<Node>`, an interface parameter
  (`VARVE0004`) enumerated through a class; they are a `List<Node>` read by
  index.
- `sh:datatype` decoded the datatype IRI to a string for every value; it now
  compares the bytes.
- `sh:minLength` and `sh:maxLength` decoded every value to count its
  characters; they count the UTF-8 bytes that start a code point.
- `sh:languageIn` decoded every tag; it matches the tag's bytes against
  ranges encoded once, ASCII case-insensitively, as RFC 4647 basic filtering
  asks.
- `sh:uniqueLang` built a string key per value and a dictionary per focus
  node; it compares tags in place, pairwise over one focus node's values.
- `sh:in` built a set of its members per focus node; the set is made once per
  validation (`ConstraintStateIsMadeOncePerValidation`).
- `sh:closed` built its permitted set per value node; without `sh:ByTypes` the
  set is made once, when the shape is read.
- `sh:uniqueValuesFor` computed a key for every target at every value node,
  quadratic in the targets; the keys are counted once per validation.
- `sh:pattern` decoded every value to a new string; it transcodes into a
  pooled buffer and matches the span.
- A triple without reifiers was answered with a new empty list; it is
  answered with none.
- The shapes were an `IReadOnlyList<Shape>`; they are an `ImmutableArray`.
- `XsdDateTime.CompareXsd` and `XsdBoolean.CompareTo`, which allocate nothing,
  are marked in `Varve.Xsd`, as `XsdDate.CompareXsd` already was.

One accepted decision answered a finding without a new key:
`QuadSourceTermHandle.SourceSuppliesEquality` is why `NodeComparer.Equals`
calls the source's comparer.

**`ValueNodesAreGatheredPerFocusNode`.** `ValidationRun.ValueNodes`,
`PathEvaluator.Values` (its per-step search cites ADR 0152's
`ShaclPathsInShaclCore`) and `DataGraph`'s `Objects`, `Subjects`, `Has` and
`Triples`. The list is what every constraint reads. The alternative, a cursor
per constraint, walks the path once per constraint.

**`ConformanceIsMemoisedPerValidation`.** `ValidationRun.Conforms`,
`IsInstanceOf`, `IsSubclassOf` and `Reifiers`. ADR 0150's fixpoint is over these tables.

**`TermsAreResolvedOncePerValidation`.** `DataGraph.Term`, `Resolve` and
`TryHandle`. A handle is externalised once, and a constant is looked up, never
added (ADR 0151).

**`ConstraintStateIsMadeOncePerValidation`.** `ValidationRun.IsMember`,
`SharesKey` and `Pattern`. A shapes graph is shared between validations, which
may run at once, so the state is on the run, not the constraint.

**`ComparisonsGatherTheirOtherSide`.** `PairConstraint`, `ClosedConstraint`,
`ListConstraint` and `Lists.Members`. What is compared against is needed whole
before the first answer.

**`ResultsCostTheirRecord`.** `ResultSink.Add`. A result is output, as a
solution is the evaluator's (`EvaluationHotPathScope.SolutionCostsItsRow`).

**`ExtensionsAreTheCallersCode`.** `ExtensionConstraint.Validate`. The lists
and the context are `IShaclConstraint`'s contract (ADR 0148), as
`IExtensionFunction`'s argument array is the evaluator's
(`EvaluationHotPathScope.ExtensionFunctionsAreTheCallersCode`).

**`PatternsMatchThroughTheBclRegex`.** `PatternConstraint.IsMatch`. ADR 0153
compiles the translation to a BCL `Regex`; the alternative is a regular
expression engine of Varve's own.
