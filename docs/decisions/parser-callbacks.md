---
set: parser-callbacks
namespace: varve
origin: "a DD0009 finding on Varve.Turtle in session 2 of #43"
decisions:
  - key: ErrorHandlerIsOptInRecovery
    statement: "A parser reports each rejected line or statement to the ErrorHandler the caller supplies, which says whether to carry on, and with no handler the first error stops the parse"
---

# The parser's error callback

**Unaccepted.** Filed by session 2 of #43, for the maintainer.

`DD0009` requires every public delegate in a layered project to cite the
decision that made it a contract. `Varve.Turtle`'s four delegates are
covered like this:

- `QuadHandler` cites ADR 0024's `TermViewIsARefStruct`: the view is valid only
  for the parse callback, which is why the callback is a custom delegate.
- `PrefixHandler` and `BaseHandler` cite ADR 0030's `PrefixesReportedAsDeclared`.
- `ErrorHandler` has no ledger key to cite.

Its ruling exists. `docs/spec/n-triples.md` (Status: Accepted) says recovery is
off by default, `ParseOptions.OnError` is null unless a caller supplies one,
and with no handler the first error stops the parse. ADR 0030 makes the
statement Turtle's recovery unit, as the line is N-Triples'. The spec is not
enumerated into the ledger, only the ADRs are (ADR 0062), so the ruling has no
key. This files it. Accepting it transcribes an accepted specification, and
the maintainer does that, not a session (ADR 0066).
