---
set: span-boundary-counts
namespace: varve
origin: "a DD0013 finding in session 2 of #43"
decisions:
  - key: SpanWriterCountsAreInt
    statement: "A member that writes into a caller's span reports the elements written, or the length it needs, as int, the BCL span-writer convention, because the count indexes that caller's buffer and means nothing beyond it"
---

# Counts at the span boundary

**Unaccepted.** Filed by session 2 of #43, for the maintainer.

`DD0013` bans naked primitives on model and contract surfaces. It exempts the
parse and format boundary by name (`Parse`, `TryParse`, `Format`, `TryFormat`)
and by the framework's parse and format interfaces, because a primitive has to
enter somewhere. Varve's span writers have the same shape under other names.
`IriRef.TryResolve(base, reference, Span<byte> destination, out int written)`
resolves into the caller's buffer and reports the bytes it wrote.
`IriRef.ResolveLength` answers the length to allocate.

**The question.** Should that count be a wrapper type? ADR 0065 names
`ByteCount` for a log's lengths, at layer 4, and nothing below it.

**What this proposes.** No. The count is the index into a `Span<byte>` the
caller owns and the callee fills. It is compared with `destination.Length`,
passed to `Slice` and `stackalloc`, and never stored or passed on as a length
of anything else. A wrapper would be unwrapped at every one of those uses.
`TryFormat(Span<byte>, out int bytesWritten)` is the BCL's convention for
exactly this, and `DD0013` already exempts it under that name. A member cites
this decision with `Scope = ExceptionScope.Boundary`. Renaming the member
`TryFormat` would say it formats a value, which a resolver does not.

**What it does not cover.** A count or offset that leaves the call and means
something on its own, such as a position in a log or a length stored in a
record. That is ADR 0065's.
