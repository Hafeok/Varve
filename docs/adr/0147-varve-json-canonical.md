# 0147 — `Varve.Json.Canonical`: one RFC 8785 implementation, at layer 0

## Status

**Accepted — filed unaccepted by milestone Client and integration, part 2, of
#93, 2026-10-15** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer on the part 2 plan:
- canonical JSON gets its own package rather than public API on
  `Varve.JsonLd`, by brief principle 3;
- `Varve.JsonLd`'s use of it is a move, with its tests green across it.

## Context

`Varve.JsonLd` writes an `rdf:JSON` literal's lexical form in RFC 8785 form
(ADR 0123, `JsonLiteralsAreCanonical`), through its internal `Jcs`. Events
(ADR 0143) and schemas (ADR 0145) are now written in the same form, and their
bytes are under the read-forever rule. Two implementations of one canonical
form would drift, and a drift is a changed byte.

`Varve.JsonLd` has one reason to change: JSON-LD. Making it the home of
canonical JSON would give it a second. `Varve.Events` would then reference
JSON-LD only to write JSON.

## Decision

1. **`Varve.Json.Canonical`, layer 0, references no Varve package.** It holds
   RFC 8785's three rules as functions over UTF-8:
   - `CanonicalJson.WriteString`, for strings (§3.2.2.2);
   - `CanonicalJson.FormatNumber` and `WriteNumber`, for numbers as
     ECMAScript's `Number::toString` (§3.2.2.3);
   - `CanonicalJson.CompareNames`, for member order by UTF-16 code units
     (§3.2.3).

   It also holds a small forward-only writer, `CanonicalJsonWriter`, whose
   objects take their members in canonical order and refuse any other.
2. **`Varve.JsonLd` uses it.**
   - `Jcs` keeps only the walk over JSON-LD's own tree, and calls the package
     for each string, number and name comparison.
   - The code moves; the behaviour does not.
   - The JSON-LD suites and the package's tests pass across the move. The
     moved tests and new vectors from RFC 8785's appendix are the package's.
3. **The bytes are under the read-forever rule.** Any change to what the
   package writes for an existing input is refused by the golden files of the
   events, the schemas and the JSON-LD `rdf:JSON` cases together.

## Alternatives considered

- **Public API on `Varve.JsonLd`.** It is refused by the maintainer, for the
  reason above.
- **A second copy in `Varve.Events`.** It is the drift this ADR exists to
  prevent.
- **`System.Text.Json`'s writer.** It escapes non-ASCII and HTML-sensitive
  characters by default, and RFC 8785 forbids that. Its number formatting is
  .NET's shortest round-trip, not ECMAScript's layout. Neither is
  configurable to RFC 8785.

## Consequences

- One more layer-0 package, with no dependency. The README's package table
  and `AGENTS.md`'s layer table gain it.
- `Varve.JsonLd` references it, and so does `Varve.Events`.

## Checks

- **Checked against the accepted ADRs** (0001–0133). It touches **0003**
  (layers; a package at 0), **0123** (amended: where `Jcs` lives), and **0143**
  and **0145** (its users). No conflict.
- **Layer ownership.** `Varve.Json.Canonical`, layer 0.
- **Analyzer rule.** None.
- **Open questions owned.** None.
