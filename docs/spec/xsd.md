# XSD datatypes

Functional specification for `Varve.Xsd` (layer 0).

Status: Accepted. Changes only together with the ADR that motivates the
change. Decided by [ADR 0051](../adr/0051-varve-xsd-scope-and-precision.md).

Scope: the value spaces, lexical mappings and canonical mappings of the XML
Schema datatypes SPARQL's operators dispatch on, and the comparisons and
arithmetic those operators need. No RDF term, no graph, no store: a value here
is a value, and which term it came from is somebody else's business.

**What this package is not.** It does not change term equality. RDF 1.1
Concepts §3.3 makes literal equality character by character over lexical
form, datatype IRI and language tag, and `Varve.Rdf` keeps it that way
permanently (`rdf-model.md` §2, ADRs 0022 and 0024). `"1"^^xsd:integer` and
`"01"^^xsd:integer` are two terms with one value. This package answers what
the value is; the evaluator at layer 3 is the only thing that asks.

## 1. Normative references

- **W3C XML Schema Definition Language (XSD) 1.1 Part 2: Datatypes** — W3C
  Recommendation, 5 April 2012. Cited as *XSD* by section. §3.3 the primitive
  datatypes, §3.4 the other built-in datatypes, §D the built-up value spaces
  (D.1 numerical, D.2 date/time and its seven-property model), §E the
  function definitions the lexical and canonical mappings are written in.
- **SPARQL 1.1 Query Language** — W3C Recommendation, 21 March 2013. §17.1
  operand data types, §17.2 filter evaluation and type errors, §17.3 the
  operator mapping, §17.4 the function definitions, §17.5 the constructor
  functions.
- **XQuery 1.0 and XPath 2.0 Functions and Operators (Second Edition)** — the
  edition SPARQL 1.1 cites for `op:` and `fn:` functions. Where this document
  cites **XPath and XQuery Functions and Operators 3.1** (W3C Recommendation,
  21 March 2017) it is for section numbers of definitions that did not change
  between the two: §4.2 arithmetic on numerics, §4.3 comparison on numerics,
  §10.4 comparison on durations, dates and times.

**XSD 1.1, not 1.0**, throughout. The differences that matter here: year 0
exists and years are proleptic Gregorian with astronomical numbering; the
timezone offset ranges over `-14:00` to `+14:00`; `+INF` is a lexical form of
`double` and `float`; `dateTimeStamp`, `yearMonthDuration` and
`dayTimeDuration` are built-in. SPARQL 1.1 cites XSD 1.0 in places and its
test suite predates 1.1; where the two could produce a different answer on a
suite case, 5b records it here as a finding rather than silently preferring
one.

## 2. Scope

In scope, with the type that carries each:

| XSD datatype | Type | XSD |
|---|---|---|
| `decimal` | `XsdDecimal` | §3.3.3, §D.1 |
| `integer`, `long`, `int`, `short`, `byte`, `nonNegativeInteger`, `positiveInteger`, `nonPositiveInteger`, `negativeInteger`, `unsignedLong`, `unsignedInt`, `unsignedShort`, `unsignedByte` | `XsdInteger`, with the derived type as a range | §3.4.13–§3.4.25 |
| `double` | `XsdDouble` | §3.3.5 |
| `float` | `XsdFloat` | §3.3.4 |
| `boolean` | `XsdBoolean` | §3.3.2 |
| `string` | `XsdString` (static; the value *is* the string) | §3.3.1 |
| `dateTime`, `dateTimeStamp` | `XsdDateTime` | §3.3.7, §3.4.28 |
| `date` | `XsdDate` | §3.3.9 |
| `time` | `XsdTime` | §3.3.8 |
| `gYearMonth`, `gYear`, `gMonthDay`, `gDay`, `gMonth` | `XsdGYearMonth`, `XsdGYear`, `XsdGMonthDay`, `XsdGDay`, `XsdGMonth` | §3.3.10–§3.3.14 |
| `duration` | `XsdDuration` | §3.3.6 |
| `yearMonthDuration` | `XsdYearMonthDuration` | §3.4.26 |
| `dayTimeDuration` | `XsdDayTimeDuration` | §3.4.27 |

`XsdDatatype` names each of these, and `XsdDatatype.TryFromIri` maps a
datatype IRI to one, so that a consumer can ask "which of these is this
literal" once and dispatch.

**Out of scope**, and a literal of any of these stays a term compared by
lexical form with no value behind it: `hexBinary` (§3.3.15), `base64Binary`
(§3.3.16), `anyURI` (§3.3.17), `QName`, `NOTATION`, and the string-derived
types `normalizedString`, `token`, `language`, `NMTOKEN`, `Name`, `NCName`,
`ID`, `IDREF`, `ENTITY` and their list forms (§3.4.1–§3.4.12). SPARQL 1.1
§17.3 dispatches on none of them.

## 3. Representation and precision

Every value is a `readonly struct` with no heap object behind it, and every
parse and every format is allocation-free for the numeric types. The date
and time types allocate nothing either; the seven-property model fits in a
few fields.

### 3.1 decimal

**A fixed-point `Int128` with 18 fractional digits.** The value is
`mantissa × 10⁻¹⁸`. The representable set is therefore the multiples of 10⁻¹⁸
whose magnitude is below 2¹²⁷ × 10⁻¹⁸, an integral part of about
±1.7 × 10²⁰.

XSD's `decimal` is unbounded in both directions (§3.3.3: "i / 10ⁿ where i and
n are integers and n ≥ 0"). This is a policy, and it is Oxigraph's, chosen so
that the differential run (ADR 0038) compares like with like:

- **Parsing** a lexical form with more than 18 significant fractional digits
  fails. Trailing zeros are not significant: `1.0000000000000000000` (nineteen
  zeros) is `1`. A form whose integral part does not fit fails.
- **Arithmetic** that leaves the representable set fails. F&O 3.1 §4.2 requires
  a dynamic error `FOAR0002` on decimal overflow, and the evaluator turns a
  failure here into a type error (SPARQL §17.2). On underflow XPath requires
  `0.0`, and that is what truncation to 18 digits produces.
- **Division** is exact to 18 fractional digits and truncates toward zero at
  the nineteenth. `1 / 3` is `0.333333333333333333`.
- **A failure never throws from a `Try` form** and always throws from an
  operator: `TryAdd`, `TryDivide` and the rest return `false`; `+`, `/` and the
  rest throw `OverflowException` or `DivideByZeroException`. The evaluator
  uses the `Try` forms.

### 3.2 integer

**A checked `Int64`.** XSD's `integer` is unbounded (§3.4.13). The edge is
stated once, in ADR 0051, and here:

- A lexical form outside `[-2⁶³, 2⁶³ − 1]` **fails to parse as a value and
  remains a valid term**. `"99999999999999999999"^^xsd:integer` is the term
  it always was: `sameTerm` and `RDFterm-equal` compare it by lexical form,
  and `+`, `<` and `xsd:integer(…)` on it are type errors.
- The derived types are ranges over the same `Int64`, checked at parse time
  when the caller names the derived datatype: `"300"^^xsd:byte` is a term
  whose value is unavailable, because 300 is outside §3.4.19's range.
  `unsignedLong`'s upper half (above 2⁶³ − 1) is unavailable for the same
  reason the base type is.
- Promotion to `decimal` never fails, because ±2⁶³ fits inside `decimal`'s
  integral range. That property is why the width is 64 and not 128.
- Arithmetic is checked; overflow fails as for decimal. F&O 3.1 §4.2 permits
  an implementation with limited-precision integers to raise `FOAR0002`, and
  this one does.

### 3.3 double and float

IEEE 754 binary64 and binary32, as `System.Double` and `System.Single`. The
lexical grammar is XSD's (§3.3.5.2, §3.3.4.2): an optional sign, digits with
an optional point, an optional exponent, or one of `INF`, `+INF`, `-INF`,
`NaN`. `.NET`'s own parser accepts `Infinity`, `∞`, thousands separators,
leading and trailing whitespace and hexadecimal forms; none of those are XSD
lexical forms, and the parser here rejects them. Rounding from the decimal
lexical form to the nearest binary value is `floatingPointRound` (§D.1),
which is round-to-nearest-even, which is what `Utf8Parser` and
`double.Parse` do for a form they accept.

### 3.4 The seven-property model

`XsdDateTime` and its relatives are the model of XSD §D.2.1: `year` (an
integer, no bound beyond `int`), `month` 1–12, `day` 1–31 constrained by the
month and the year, `hour` 0–23, `minute` 0–59, `second` a decimal in
`[0, 60)`, and `timezoneOffset` an integer number of minutes in `[-840, 840]`
or absent. Each datatype leaves some properties absent: `date` has no time
of day, `time` no date, `gYear` only the year, and so on, exactly as §D.2.1's
table lists. `24:00:00` on a `dateTime` or `time` maps to `00:00:00` of the
following day (§3.3.7.2, `endOfDayFrag`).

`second` is an `XsdDecimal`, so fractional seconds keep 18 digits and no
precision is lost between parse and format. Leap seconds are not
representable (§D.2.1 says so of the datatypes themselves).

`dateTimeStamp` (§3.4.28) is a `dateTime` whose timezone offset is required.
It shares `XsdDateTime` and differs only in what parsing under that datatype
accepts.

### 3.5 duration

The two-property model of §3.3.6.1: `months` (an `Int64`) and `seconds` (an
`XsdDecimal`), with the constraint that they do not have opposite signs.
`yearMonthDuration` is the subset with `seconds = 0` and `dayTimeDuration`
the subset with `months = 0`, each its own type so that the total order each
has (§3.4.26, §3.4.27) is a `CompareTo` and not a `PartialOrdering`.

## 4. Lexical and canonical mappings

Each type has `TryParse(ReadOnlySpan<byte>)`, `TryParse(ReadOnlySpan<char>)`,
`TryFormat(Span<byte>, out int written)` and `TryFormat(Span<char>, …)`, and a
static `IsCanonical(ReadOnlySpan<byte>)` that answers whether a lexical form
is the canonical one **without mapping it to a value** — which is the form
the store's inline rule needs (ADR 0012's amendment, ADR 0045), because
canonicality is a property of the string and a string too long to be a value
can still be judged.

The lexical spaces are XSD's, transcribed here where an implementer would
otherwise guess; the canonical mappings are XSD's §E and §D.1 functions,
named.

### 4.1 decimal — §3.3.3.1, `decimalCanonicalMap`

```
decimalLexicalRep  ::= ('+' | '-')? ( digit+ ('.' digit*)? | '.' digit+ )
```

Canonical (`decimalCanonicalMap`, §D.1.1): an integer value has **no decimal
point** (`noDecimalPtCanonicalMap`: `1`, `-12`, `0`); any other value has a
point with at least one digit on each side, no leading `+`, no leading zeros
except the one before the point, no trailing zeros after it
(`decimalPtCanonicalMap`: `0.5`, `-12.25`). `-0` is `0`.

### 4.2 integer — §3.4.13.1, §3.4.13.2

```
integerLexicalRep  ::= ('+' | '-')? digit+
```

Canonical: no `+`, no leading zeros, `-0` is `0`. The derived types share the
lexical space and the canonical form and differ only in range (§3.4.14–§3.4.25).

`IsCanonical` is the check the store performs before allocating an inline id.
It accepts a digit string of any length, because a value that does not fit in
`Int64` is still either canonical or not.

### 4.3 double and float — §3.3.5.2, §3.3.4.2, `doubleCanonicalMap`, `floatCanonicalMap`

```
doubleRep  ::= (('+' | '-')? ( digit+ ('.' digit*)? | '.' digit+ ) (('e' | 'E') ('+' | '-')? digit+)?)
             | 'INF' | '+INF' | '-INF' | 'NaN'
```

Canonical: `scientificCanonicalMap` — one non-zero digit before the point, at
least one digit after it, `E`, an exponent with no `+` and no leading zeros:
`1.0E0`, `-1.5E-3`, `1.0E10`. Zero is `0.0E0`, negative zero `-0.0E0`,
`INF`, `-INF` and `NaN` as themselves (`specialRepCanonicalMap`). The mantissa
is the shortest one that round-trips (`doubleCanonicalMap`'s smallest `c`),
which is what .NET's shortest-round-trip formatting produces, so the digits
come from `Utf8Formatter` with the exponent rewritten into XSD's shape.

### 4.4 boolean — §3.3.2.2, `booleanCanonicalMap`

Lexical: `true`, `false`, `1`, `0`. Canonical: `true`, `false`.

### 4.5 string — §3.3.1

Every string is its own lexical form and its own canonical form. `XsdString`
has no value type; it has one function, code-point comparison (§5.2).

### 4.6 dateTime and the family — §3.3.7.2 to §3.3.14.2, §D.2.2, §E.3.5, §E.3.6

```
dateTimeLexicalRep ::= yearFrag '-' monthFrag '-' dayFrag 'T'
                       ((hourFrag ':' minuteFrag ':' secondFrag) | endOfDayFrag) timezoneFrag?
yearFrag           ::= '-'? (([1-9] digit digit digit+) | ('0' digit digit digit))
monthFrag          ::= ('0' [1-9]) | ('1' [0-2])
dayFrag            ::= ('0' [1-9]) | ([12] digit) | ('3' [01])
hourFrag           ::= ([01] digit) | ('2' [0-3])
minuteFrag         ::= [0-5] digit
secondFrag         ::= ([0-5] digit) ('.' digit+)?
endOfDayFrag       ::= '24:00:00' ('.' '0'+)?
timezoneFrag       ::= 'Z' | ('+' | '-') (('0' digit | '1' [0-3]) ':' minuteFrag | '14:00')
```

with the day constrained to the month (§3.3.7.2, *Day-of-month
Representations*), and the other datatypes taking the fragments §D.2.2
assigns them: `date` is `yearFrag '-' monthFrag '-' dayFrag timezoneFrag?`,
`time` the time-of-day half, `gYearMonth` `yearFrag '-' monthFrag`, `gYear`
`yearFrag`, `gMonthDay` `'--' monthFrag '-' dayFrag`, `gDay` `'---' dayFrag`,
`gMonth` `'--' monthFrag`, each with an optional `timezoneFrag`.

Canonical (`dateTimeCanonicalMap` and its fragments, §E.3.6): the year has
four digits or as many as it needs above 9999 with no leading zeros
(`yearCanonicalFragmentMap`); month, day, hour and minute two digits; the
second two digits with a fraction only when it is non-zero and then with no
trailing zeros (`secondCanonicalFragmentMap`); `24:00:00` is never written,
because the value it maps to is the next day's midnight; the timezone is `Z`
for zero and `±hh:mm` otherwise (`timezoneCanonicalFragmentMap`). **No
conversion to UTC**: the timezone offset is a property of the value, and the
canonical form of `2026-09-24T10:00:00+02:00` is that string.

### 4.7 duration — §3.3.6.2, §3.4.26.1, §3.4.27.1, `durationCanonicalMap`

```
durationLexicalRep ::= '-'? 'P' ((duYearFrag duMonthFrag? | duMonthFrag) duTimeFrag?
                               | duDayFrag duTimeFrag? | duTimeFrag)
duYearFrag         ::= digit+ 'Y'         duMonthFrag  ::= digit+ 'M'
duDayFrag          ::= digit+ 'D'         duTimeFrag   ::= 'T' (duHourFrag duMinuteFrag? duSecondFrag? | duMinuteFrag duSecondFrag? | duSecondFrag)
duHourFrag         ::= digit+ 'H'         duMinuteFrag ::= digit+ 'M'
duSecondFrag       ::= (digit+ | digit+ '.' digit* | '.' digit+) 'S'
```

`yearMonthDuration` disallows the day and time fragments; `dayTimeDuration`
disallows the year and month fragments. Canonical (`durationCanonicalMap`):
the sign, `P`, then years and months with the months reduced below 12
(`duYearMonthCanonicalFragmentMap`), then days, hours, minutes and seconds
with each reduced below its unit and zero fragments omitted
(`duDayTimeCanonicalFragmentMap`); a zero duration is `PT0S`. `P1Y13M` is
`P2Y1M`; `PT90M` is `PT1H30M`; `P0D` is `PT0S`.

**A zero `yearMonthDuration` has no canonical form in XSD** (§3.4.26.1: its
canonical form as a `duration`, `PT0S`, is outside the derived type's lexical
space). This package writes `P0M` for it, which is in the lexical space and
is the only zero form with no day-time fragment; stated here because it is a
choice the specification leaves open.

## 5. Value comparison and the SPARQL operator mapping

SPARQL 1.1 §17.3 maps each operator, by operand types, to an XPath operator.
This section says what each of those operators is over this package's types,
so that the evaluator's job is dispatch and nothing else.

### 5.1 Numerics

`op:numeric-equal`, `op:numeric-less-than`, `op:numeric-greater-than` (F&O
§4.3) after **type promotion** (§17.3: "SPARQL follows XPath's scheme for
numeric type promotions"): integer → decimal → float → double; the result type
of a binary operation is the wider operand's. `XsdNumeric` is the promoted
view: a `readonly struct` holding one of the four, with `Compare`, and the
four arithmetic operations, promoting as XPath does and failing as §3 says.

`NaN` compares unordered: `NaN = NaN` is false, `NaN < x` is false, and
`XsdNumeric.Compare` reports it as such rather than choosing a side. An
`ORDER BY` over doubles that contains `NaN` is 5b's to specify.

### 5.2 Strings

`fn:compare` under the codepoint collation (§17.3), which orders by Unicode
code point. Over valid UTF-8, code-point order is byte order, so
`XsdString.CompareCodePoints(ReadOnlySpan<byte>, ReadOnlySpan<byte>)` is a
sequence comparison of the bytes and allocates nothing. The `char` overload
compares by code point, not by UTF-16 code unit, which differ above the BMP.

### 5.3 Booleans

`op:boolean-equal`, `op:boolean-less-than`, `op:boolean-greater-than`:
`false < true`.

### 5.4 dateTime

**The evaluator's comparison is the implicit-timezone total order** (ADR
0051). §17.3 maps `<` on two `xsd:dateTime` operands to
`op:dateTime-less-than`, and F&O §10.4 defines that operator as: if either
operand has no timezone, the implicit timezone from the dynamic context is
supplied to it, and the two are then compared on the time line. Under it
every pair is comparable.

```csharp
int XsdDateTime.Compare(XsdDateTime a, XsdDateTime b, int implicitTimezoneOffsetMinutes)
```

The implicit timezone is an evaluator setting, defaulting to UTC (offset 0).
It is a parameter here and never ambient: this package has no clock and no
locale.

**The XSD partial order stays available**, under a name that says what it is:

```csharp
PartialOrdering XsdDateTime.CompareXsd(XsdDateTime a, XsdDateTime b)
```

§D.2.1 and `timeOnTimeline` (§E.3.4): two values that both have a timezone,
or both lack one, compare by their position on the time line; when exactly
one has a timezone, the other is compared with `+14:00` and with `-14:00`
imputed, and the pair is `Indeterminate` unless both imputations agree.
`PartialOrdering` is `Less`, `Equal`, `Greater` or `Indeterminate`.

Equality under the total order is equality of time-line position under the
implicit timezone; under the partial order it is XSD's, which holds only
when both timezone offsets are present or both absent.

The same two comparisons exist on every seven-property type, because
`timeOnTimeline` is defined over all of them (§E.3.4 fills absent properties
with the values it names, `1972-12-31` and midnight). SPARQL 1.1 §17.3 maps
only `xsd:dateTime`; the others are available for 5b to map or refuse as the
evaluation suite decides. **Decided at 5b** (ADR 0051's amendment of
2026-09-25): the other seven-property types take the partial order, as
`sparql10/open-world`'s `date-1` and `date-2` require.

**5b verifies this section against the SPARQL 1.1 query evaluation suite.**
A case that disagrees with the total order is a finding recorded here and in
a dated amendment to ADR 0051, not a quiet switch to the partial order.

### 5.5 Durations

§3.3.6.1: two durations are ordered by adding each to the four reference
dateTimes `1696-09-01T00:00:00Z`, `1697-02-01T00:00:00Z`,
`1903-03-01T00:00:00Z` and `1903-07-01T00:00:00Z`; the pair is ordered when
all four results agree and `Indeterminate` otherwise. `P1M` and `P30D` are
indeterminate; `P1M` and `P31D` are ordered (`P1M < P31D`, because no month
is longer than 31 days and one of the four February references makes it
strictly shorter). `XsdDuration.CompareXsd` returns a `PartialOrdering`;
`XsdYearMonthDuration` and `XsdDayTimeDuration` compare totally, by months
and by seconds.

SPARQL 1.1 §17.3 maps no operator to durations; they are here because
§17.4.5's functions and SPARQL 1.2's are defined over them, and because
adding a `dayTimeDuration` to a `dateTime` (§E.3.3, `dateTimePlusDuration`)
is the arithmetic `xsd:dateTime` comparison with an implicit timezone is
built on.

### 5.6 Everything else is a term

`RDFterm-equal` (§17.4.1.7) on two literals whose datatype this package does
not know is term equality, and a type error when the two are not equal —
which is `Varve.Rdf`'s `RdfTerm.Equals` and not this package's concern. This
package never sees such a literal.

## 6. Arithmetic and the functions that need it

The operations §17.3's arithmetic rows and §17.4.4's functions need, each
promoting as §5.1 says and failing as §3 says:

| Operation | F&O | Notes |
|---|---|---|
| `+`, `-`, `*` | §4.2.1–§4.2.3 | integer × integer is integer, overflow fails |
| `/` | §4.2.4 | integer / integer is **decimal** (§17.3's note); decimal division truncates at 18 digits; float and double divide as IEEE, `x / 0` is `±INF` or `NaN` for them and a failure for integer and decimal |
| `abs`, `round`, `ceil`, `floor` | §4.4.1–§4.4.4 | `round` is round-half-up toward positive infinity, F&O's `fn:round`, not `Math.Round`'s banker's default |
| unary `-`, `+` | §4.2.7, §4.2.8 | |

For the date and time functions of §17.4.5 (`year`, `month`, `day`, `hours`,
`minutes`, `seconds`, `timezone`, `tz`), the seven properties are exposed as
they are; `timezone` returns an `XsdDayTimeDuration` (`PT2H` for `+02:00`) and
`tz` the lexical timezone fragment (`+02:00`, `Z`, or empty), which is why the
duration types are in scope at all.

`XsdDateTime.Add(XsdDayTimeDuration)` and `Add(XsdYearMonthDuration)` are
§E.3.3's `dateTimePlusDuration`, with the normalisation of §E.3.1 (a month
overflow carries into the year; a day beyond the month's length carries).

## 7. Casting — SPARQL §17.5

The constructor functions `xsd:integer(…)`, `xsd:decimal(…)`, `xsd:float(…)`,
`xsd:double(…)`, `xsd:boolean(…)`, `xsd:dateTime(…)` and `xsd:string(…)` are
XPath casts, and §17.5's table of which casts are allowed from which source
types is the evaluator's. What this package supplies is the value-to-value
half: `XsdDecimal.FromInteger`, `XsdInteger.TryFromDecimal` (truncating),
`XsdDouble.FromDecimal`, `XsdBoolean.FromNumeric` (zero and `NaN` are false),
and the lexical half through `TryParse`, since a cast from a string is a
parse of its lexical form.

## 8. Tests, and the gate

The W3C suites do not test XML Schema datatypes directly. The SPARQL 1.1
query evaluation suite does, one step removed, and it is this package's
gate from 5b. Until then the gate is:

- **Round trip.** For every type, over generated values: parse the canonical
  form of a value and get the value back; format a parsed lexical form and
  parse it again and get the same value. Generators reach the edges: 18
  fractional digits, `Int64.MinValue`, `-0.0`, `NaN`, year 0 and negative
  years, `24:00:00`, `+14:00`, `P0D`, a duration with both signs' worth of
  fragments.
- **Idempotence.** Canonical form is a fixed point: format, parse, format
  gives the same bytes, and `IsCanonical` holds exactly on formatted output.
- **Order.** Where the specification says total — every numeric type,
  boolean, string, the two derived durations, and dateTime under the implicit
  timezone — `Compare` is antisymmetric, transitive and total over generated
  triples. Where it says partial — `xsd:duration`, and dateTime under XSD's
  order — the same properties hold over the comparable pairs, and generated
  incomparable pairs (one timezone absent and the imputations disagreeing;
  `P1M` against `P30D`) are reported `Indeterminate` and never ordered.
- **Arithmetic against an oracle.** Decimal and integer arithmetic against
  `System.Numerics.BigInteger` scaled by 10¹⁸, and `System.Decimal` where both
  ranges hold the operands, over generated pairs; a difference is a defect
  here, never in the oracle.
- **The XSD 1.1 examples.** Every lexical form and canonical form the
  specification's own text gives as an example (`1.0E0`, `-0.0E0`, `P2Y1M`,
  `2002-10-10T12:00:00-05:00`, and the rest) parses to the value it says and
  formats to the canonical form it gives.

The store's inline rule is tested against this package's `IsCanonical`
directly: `"1"` inline, `"01"`, `"+1"` and `"1.0"` not (ADR 0012).

## 9. Open questions

None. Two obligations are recorded rather than open: 5b verifies §5.4's
choice against the evaluation suite, and 5b decides the ordering of `NaN` in
`ORDER BY` (§5.1).
