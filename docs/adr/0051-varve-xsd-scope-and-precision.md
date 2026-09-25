# 0051 — `Varve.Xsd`: scope, precision policy, canonical forms, and value comparison

## Status

**Accepted.** 2026-09-24. Records [`docs/spec/xsd.md`](../spec/xsd.md). The
position — `Varve.Xsd` owns canonical lexical forms and replaces the store's
private integer check — was the maintainer's at the close of milestone 4; the
precision policy and the dateTime comparison were decided at the start of
milestone 5 on the plan's questions.

## Context

`Varve.Xsd` has been the empty box at layer 0 since ADR 0003. Nothing needed
it: the syntax suites test lexical forms, the store compares terms by lexical
form (ADR 0022, 0024), and the one place a value mattered — whether an
`xsd:integer` literal takes an inline id — was answered by a private
seventeen-line function in `Varve.Store` (ADR 0012's amendment, ADR 0045).

The evaluator needs values. SPARQL 1.1 Query §17.3 maps its operators onto
XPath operators over `xsd:numeric`, `xsd:string`, `xsd:boolean` and
`xsd:dateTime`; §17.4 defines functions over them; §17.4.1.7's `RDFterm-equal`
is value equality for these types and term equality for everything else. All
of that is a lexical-to-value mapping, a value space, an order, and a
canonical form back, per datatype. That is a library, and it is a layer 0
library because nothing in it knows what a term is.

Four things need deciding before it is written: what is in and what is out;
how exact "exact decimal" is and what happens at the edge; which order two
dateTimes compare by when one has no timezone; and where canonical forms
live, because two implementations of "is this canonical" will one day
disagree.

## Decision

### Scope

**In**, with the SPARQL 1.1 operator semantics of §17.3 and §17.4:

| Group | Datatypes | XSD 1.1 Part 2 |
|---|---|---|
| Exact decimal | `xsd:decimal` | §3.3.3 |
| The integer family | `xsd:integer` and its derived types: `long`, `int`, `short`, `byte`, `nonNegativeInteger`, `positiveInteger`, `nonPositiveInteger`, `negativeInteger`, `unsignedLong`, `unsignedInt`, `unsignedShort`, `unsignedByte` | §3.4.13–§3.4.25 |
| IEEE | `xsd:double`, `xsd:float` | §3.3.5, §3.3.4 |
| | `xsd:boolean`, `xsd:string` | §3.3.2, §3.3.1 |
| Date and time | `xsd:dateTime`, `xsd:dateTimeStamp`, `xsd:date`, `xsd:time`, `xsd:gYearMonth`, `xsd:gYear`, `xsd:gMonthDay`, `xsd:gDay`, `xsd:gMonth` | §3.3.7–§3.3.14, §3.4.28, §D.2 |
| Durations | `xsd:duration`, `xsd:yearMonthDuration`, `xsd:dayTimeDuration` | §3.3.6, §3.4.26, §3.4.27 |

Every value space is XML Schema 1.1's, not 1.0's: year zero exists, a
timezone offset may be `+14:00`, `+INF` is a double. Each type parses from
`ReadOnlySpan<byte>` and `ReadOnlySpan<char>`, formats its canonical form
into a `Span`, and the numeric types do both without allocating.

**Out**, and stated so that nobody looks for them: `xsd:hexBinary`,
`xsd:base64Binary`, `xsd:anyURI`, `xsd:QName`, `xsd:NOTATION`, and the string
types derived from `xsd:string` (`normalizedString`, `token`, `language`,
`Name`, and the rest of §3.4.1–§3.4.12). SPARQL's operators never dispatch on
them; a literal of one is a term with a datatype IRI, compared by lexical
form, exactly as it is today. If a later milestone needs one, it is an
additive change to this package and an amendment here.

### Precision policy

**`XsdDecimal` is a fixed-point `Int128` with 18 fractional digits.** The
value is `mantissa / 10^18`; the integral part ranges to about ±1.7 × 10²⁰.
This is Oxigraph's representation, chosen for the same reasons: exact, fixed
width, no allocation, and wide enough that an overflow is a finding rather
than an occurrence.

- **Parsing** rejects a lexical form that needs more than 18 fractional digits
  (trailing zeros beyond the eighteenth are not digits that need keeping and
  are accepted) or whose integral part exceeds the range. The literal remains
  a valid RDF term; only its *value* is unavailable.
- **Arithmetic** that overflows fails: the `Try` forms return false, the
  operators throw `OverflowException`. SPARQL §17.3 permits an implementation
  to raise a type error here (XPath Functions and Operators 3.1 §4.2.1 lets
  it raise `FOAR0002`), and an evaluator turns the failure into an unbound
  result, exactly as for a division by zero.
- **Division** is exact to 18 fractional digits and rounds toward zero at the
  nineteenth. The alternative, round-half-even, is what `xsd:decimal` would
  want if it had a precision; it does not, and truncation is the rule
  Oxigraph applies, which makes the differential run comparable.

**`XsdInteger` is a checked `Int64`**, and every derived integer type is a
range check on the same value, not a separate type. `xsd:integer` is
unbounded in XML Schema; this is a policy, and its edge is stated:

- A lexical form outside ±2⁶³ is **a valid term and an unavailable value**.
  `"99999999999999999999"^^xsd:integer` is the term it always was, equal by
  lexical form to itself and to nothing else (ADR 0024). Arithmetic and
  ordered comparison on it are type errors (SPARQL §17.2, unbound); `sameTerm`
  and `RDFterm-equal` by lexical form still work.
- The top half of `xsd:unsignedLong` is therefore unavailable as a value.
- Promotion from integer to decimal never overflows, because ±2⁶³ fits inside
  the decimal's integral range. That property is why `Int64` rather than
  `Int128`: an integer wider than the decimal's integral part would make
  promotion — the operator mapping's most common step — a fallible operation.
- The width is one field. Widening it is an amendment, not a redesign.

**`XsdDouble` and `XsdFloat`** are IEEE 754 binary64 and binary32, with XML
Schema's lexical grammar rather than .NET's: `INF`, `-INF`, `+INF`, `NaN`,
no hexadecimal, no thousands separators, no `Infinity`. Canonical forms are
§3.3.5.2's and §3.3.4.2's (`1.0E0`, `-0.0E0`, `NaN`).

### Canonical forms live here, and the store calls in

`Varve.Xsd` is the one implementation of "is this lexical form canonical for
its datatype", and of the canonical mapping itself. **`Varve.Store`'s private
integer check is deleted and replaced by a call into `Varve.Xsd`**; the
boolean check goes the same way. `Varve.Store` at layer 4 referencing
`Varve.Xsd` at layer 0 is a legal downward reference (ADR 0003).

The inline rule (ADR 0012's amendment) is unchanged in meaning: a literal
takes an inline id only when its lexical form is canonical. What changes is
that the definition of canonical is now the one the evaluator also uses, so
`"01"^^xsd:integer` cannot be canonical to one and not the other.

### Value comparison

The comparisons this package supplies are the ones SPARQL 1.1 §17.3 maps its
operators to, and the specification names each:

- **Numerics**: the XPath total order over the promoted type (§17.3's operand
  promotion, F&O §4.2), with `NaN` unordered as IEEE has it.
- **Strings**: code-point order (`fn:compare` under the codepoint collation),
  which over valid UTF-8 is byte order.
- **Booleans**: `false < true`.
- **dateTime**: **the implicit-timezone total order.** SPARQL 1.1 §17.3 maps
  `<` on `xsd:dateTime` to `op:dateTime-less-than`, and XPath Functions and
  Operators 3.1 §10.4 defines that operator with the dynamic context's
  *implicit timezone* supplied to any operand that lacks one. The implicit
  timezone is an evaluator setting, defaulting to UTC. Under it every pair of
  dateTimes is comparable.

  **The XSD partial order stays available in `Varve.Xsd`** — §D.2.1 and
  §E.3.4, with the ±14:00 imputation that leaves some mixed pairs
  indeterminate — as a second, explicitly named comparison. It is what XML
  Schema validation means by order, and what a SHACL comparison constraint
  may need at milestone 8.

  **5b verifies the choice against the SPARQL 1.1 query evaluation suite.**
  If the suite disagrees with the total order, the correction is a dated
  amendment here superseding this paragraph, not a quiet switch.

  > **Amended 2026-09-25** (milestone 5b). **Verified: no case of the SPARQL
  > 1.0 and 1.1 evaluation suites disagrees with the total order for
  > `xsd:dateTime`**, and this paragraph stands. **`xsd:date`, `xsd:time`
  > and the `g` types** — `gYear`, `gYearMonth`, `gMonth`, `gMonthDay`,
  > `gDay` — **compare by the XSD partial order**, an indeterminate pair
  > being a type error. SPARQL 1.1 §17.3's operator mapping names
  > `xsd:dateTime` alone among the date and time types, so the implicit
  > timezone of `op:dateTime-less-than` does not reach them; comparing them
  > at all is §17.3.1's operator extension, and the suite decides which
  > order that extension takes: `sparql10/open-world`'s `date-1`
  > (`FILTER(?v = "2006-08-23"^^xsd:date)`) expects neither
  > `"2006-08-23Z"` nor `"2006-08-23+00:00"`, and `date-2` (the same with
  > `!=`) expects neither of them either — an indeterminate comparison in
  > both, which the total order with a UTC default would make equal.
  > Recorded in `docs/spec/sparql-evaluation.md` §7.4 and §13.1, where
  > `docs/spec/xsd.md` left the mapping to 5b. **SPARQL 1.2 may supersede
  > it.** The 1.2 Query draft of this date still maps `xsd:dateTime` alone
  > in §17.3; if 1.2 adds date and time operators, or its evaluation cases
  > — those blocked until roadmap slice 6b among them — expect another
  > order, the correction is a further dated amendment here.
- **Durations**: the four-reference-dateTime order of §3.3.6.1, partial for
  `xsd:duration` and total for the two derived types.

### No numeric package

`System.Int128`, `System.Numerics.BigInteger` (in tests, as an oracle) and
`System.Decimal` (in tests, as an oracle) are the BCL. No third-party numeric
library enters the register.

## Alternatives considered

- **`BigInteger` unscaled with an `int` scale** for decimal — arbitrary
  precision, exactly XSD's value space. Rejected: every operation allocates,
  on the path a `FILTER` runs per candidate, and constraint 5 puts allocation
  per quad among the defects. The fixed-point form has an edge; the edge is
  stated and fails loudly.
- **`System.Decimal`** for `xsd:decimal` — 96-bit mantissa, 28 significant
  digits, in the BCL. Rejected: its scale floats with the value, so the
  precision policy would be "it depends", and its range (±7.9 × 10²⁸) is
  narrower on the integral side than the `Int128` form while promising more
  fractional digits than any SPARQL suite needs.
- **`Int128` for the integer family.** Removes the `unsignedLong` limit.
  Rejected above: promotion to decimal would become fallible, and the
  differential run against Oxigraph, which uses `i64`, would disagree at the
  edge for no gain.
- **A separate type per derived integer datatype** (`XsdByte`, `XsdShort`,
  …). Type-safe. Rejected: thirteen structs that differ in two constants, and
  the operator mapping promotes them all to the same `xsd:integer` before
  doing anything.
- **The XSD partial order as SPARQL's comparison**, with an indeterminate
  pair a type error. Defensible, and it is a reading some implementations
  take. Rejected because §17.3 names the XPath operator and the XPath
  operator names the implicit timezone; adopting the partial order would be
  following XML Schema where SPARQL points at XPath. Kept available, and kept
  falsifiable by the evaluation suite.
- **Leaving canonical forms in the store**, since the store is the only
  caller today. Rejected: the evaluator becomes a second caller in 5b, and
  two definitions of canonical diverge on the first `"+1"`.

## Consequences

- **`Varve.Store` gains a layer 0 dependency.** Its restore closure and its
  trimmed size grow by one small assembly. The alternative was a second
  definition of canonical.
- **Out-of-range literals are valid terms with no value.** Every function in
  this package that maps a lexical form to a value can say "no", and the
  evaluator must treat "no" as a type error rather than a parse failure. The
  distinction is the one ADR 0024 draws between a term and its value, and it
  is why the store's inline check asks "is it canonical" before it asks "does
  it fit".
- **The implicit timezone becomes an evaluator setting** in 5b, with a
  documented default of UTC, and a query's answer to a mixed-timezone
  comparison depends on it. That is what XPath specifies; it is stated rather
  than hidden.
- **Two orders on dateTime exist and are named differently**, so a caller
  cannot take one for the other by accident.
- **The gate for this package is indirect until 5b.** The W3C suites do not
  test XSD datatypes directly; the SPARQL evaluation suite does, at one
  remove. Until then the property tests and the differential test against the
  examples in the XML Schema 1.1 text are the gate, and the specification
  says so.

## Checks

- **Checked against the accepted ADRs** (0001–0050) and the specification.
  Touches **0003** (layer 0, and the store's downward reference), **0012** and
  **0045** (the inline rule's canonical check, now implemented here), **0022**
  and **0024** (value comparison is the evaluator's; nothing here touches
  term equality or what a graph contains), **0038** (Oxigraph as the
  tie-breaker for division rounding, and the differential run this makes
  comparable), and **0050** (the accessor hands the evaluator a `long`, which
  this package's integer is). No conflict with any.
- **Layer ownership.** **`Varve.Xsd`, layer 0.** No Varve dependencies.
- **Analyzer rule.** None.
- **Open questions owned.** None. The dateTime comparison carries a
  verification obligation in 5b, recorded above, not an open question.
