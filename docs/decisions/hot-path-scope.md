---
set: hot-path-scope
namespace: varve
origin: "VARVE0003 findings on layers 0 to 2 in session 2 of #43"
decisions:
  - key: AllowListAddsNonAllocatingBclHelpers
    statement: "The hot-path allow-list also admits the BCL helpers span parsing is written with that neither allocate nor call back into user code: Index, Range, MemoryExtensions, Rune, Utf8, HashCode, Math, Int32, and the ArgumentException throw helpers"
  - key: DirectivesAreNotPerQuad
    statement: "A Turtle or TriG directive keeps its own copy of what it binds and calls the caller's prefix and base handlers, because a directive happens once per binding and not once per quad"
  - key: GeneratedLabelClaimsAreRecorded
    statement: "The Turtle parser records a document's own g-form blank node labels in collections when it first meets them, the one place it allocates in proportion to its input, because a streaming parser must honour a claim it cannot foresee"
---

# What the hot-path rules do not cover

**Unaccepted.** Filed by session 2 of #43, for the maintainer.

`VARVE0003` (ADR 0064) holds a `[HotPath]` member to constraint 5, *allocation
per quad is a defect*. Marking the parser cores, the term arena and the quad
cursors in layers 0 to 2 found three places where the rule, applied as
written, reports something constraint 5 does not forbid. Each is filed here
rather than suppressed, worked around, or quietly unmarked.

**`AllowListAddsNonAllocatingBclHelpers`.** ADR 0064 lists seven BCL types a hot
path may call: `Span<T>`, `ReadOnlySpan<T>`, `MemoryMarshal`,
`BinaryPrimitives`, `Unsafe`, `ArrayPool<T>` and `Vector*`. The list is
configuration (`HotPathAllowListIsConfiguration`), and its content is ADR 0064's
prose, not a ledger key. Span code cannot be written with those seven alone:

- `span[a..b]` converts through `System.Index` and `System.Range`;
- `IndexOf`, `SequenceEqual` and `AsSpan` are on `System.MemoryExtensions`;
- UTF-8 is validated and decoded with `System.Text.Unicode.Utf8` and
  `System.Text.Rune`;
- a guard clause is `ArgumentOutOfRangeException.ThrowIfNegative`;
- a hash is `System.HashCode`, a bound is `Math.Max`, and a generated blank
  node's number is written by `int.TryFormat` into a span.

None allocates, and none calls back into Varve. They are added in
`.editorconfig` (`varve_hot_path_allowed_types`). The alternative is to
rewrite each call inline in Varve, which duplicates the BCL.

**`DirectivesAreNotPerQuad`.** `@prefix` and `@base` copy the IRI they bind and
call the caller's `OnPrefix`/`OnBase` (ADR 0030's `PrefixesReportedAsDeclared`).
The scanner is a hot type because statements are per quad. A directive is per
binding, and a document has a handful. `TurtleScanner.Directive`,
`TurtleState.BindPrefix` and `.SetBase` cite this.

**`GeneratedLabelClaimsAreRecorded`.** `BlankNodeNaming` lets a document use
`_:g0`, the form the parser invents for `[]`, and honours the claim. The
collections behind that exist only once a document writes such a label, so an
ordinary document allocates nothing for them. The one that does allocates per
distinct claimed label. The members that touch them cite this; `Mint` and
`GeneratedIndex` stay checked. The alternative, reserving a label form no
document may use, is impossible: the grammar lets a document write any label
the parser can emit.
