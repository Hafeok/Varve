---
set: varve-xsd-scope-and-precision
namespace: varve
adr: 0051
decisions:
  - key: XsdScope
    statement: "Varve.Xsd covers decimal, the integer family, double, float, boolean, string, the date and time types and the durations, with XSD 1.1 value spaces and SPARQL 1.1 operator semantics"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
  - key: XsdTypesOutOfScope
    statement: "hexBinary, base64Binary, anyURI, QName, NOTATION and the types derived from xsd:string are out of scope and compare by lexical form"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
  - key: XsdParsesAndFormatsSpans
    statement: "Each Varve.Xsd type parses from UTF-8 and UTF-16 spans and formats its canonical form into a span, the numeric types without allocating"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
  - key: DecimalIsFixedPointInt128
    statement: "XsdDecimal is a fixed-point Int128 with 18 fractional digits that rejects forms needing more, fails on overflow and truncates division toward zero"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
  - key: IntegerIsCheckedInt64
    statement: "XsdInteger is a checked Int64, the derived integer types are range checks on it, and a form outside its range is a valid term with no value"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
  - key: IeeeWithXsdGrammar
    statement: "XsdDouble and XsdFloat are IEEE binary64 and binary32 with XML Schema's lexical grammar and canonical forms"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
  - key: CanonicalFormsDefinedInXsd
    statement: "Varve.Xsd is the one definition of a canonical lexical form, and Varve.Store's inline check calls it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
  - key: ValuelessLiteralIsATypeError
    statement: "A lexical form with no value in range is a type error to the evaluator, never a parse failure"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
  - key: ScalarValueOrders
    statement: "Numerics compare by the XPath total order over the promoted type with NaN unordered, strings by code point, and booleans false before true"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
  - key: DateTimeImplicitTimezoneOrder
    statement: "xsd:dateTime compares by the implicit-timezone total order, the implicit timezone being an evaluator setting that defaults to UTC"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
  - key: XsdPartialOrderNamedApart
    statement: "The XSD partial order on date and time values is available as a second, separately named comparison"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
  - key: DateAndGTypesPartialOrder
    statement: "xsd:date, xsd:time and the g types compare by the XSD partial order, an indeterminate pair being a type error"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: DurationOrders
    statement: "Durations compare by the four-reference-dateTime order, partial for xsd:duration and total for the two derived types"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
  - key: NoNumericPackage
    statement: "No third-party numeric library enters the register, and BigInteger and Decimal serve only as test oracles"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
---

The rulings of [ADR 0051](../adr/0051-varve-xsd-scope-and-precision.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

The 2026-09-25 amendment verified `DateTimeImplicitTimezoneOrder` against the suites and left it
unchanged, so that key keeps the ADR's date; the rule it added for the other date and time
types carries the amendment's.
