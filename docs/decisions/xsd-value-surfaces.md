---
set: xsd-value-surfaces
namespace: varve
origin: "DD0013 findings on Varve.Xsd in session 2 of #43"
decisions:
  - key: XsdComponentsAreSpecIntegers
    statement: "The components of a Varve.Xsd value, year, month, day, hour, minute, timezone offset in minutes, a duration's months and a decimal's scale, are the integers XSD 1.1 Part 2 defines them as, and the members that take or expose one use int or long"
  - key: XsdOrderingsReturnInt
    statement: "A Compare or CompareCodePoints member of Varve.Xsd returns int, negative, zero or positive, as Comparison of T does"
  - key: XsdDecimalConvertsToIeeePrimitives
    statement: "XsdDecimal.ToDouble and ToSingle return the IEEE primitive, the value space a numeric promotion casts an xsd:decimal to"
---

# The primitives on Varve.Xsd's surface

**Unaccepted.** Filed by session 2 of #43, for the maintainer.

Declaring `Varve.Xsd` a `[DomainModel]` namespace (ADR 0064) brings every
public member under `DD0013`: no naked primitives on a model surface. ADR 0065
decided wrappers for the log's coordinates and for a quad count, and says
nothing about `Varve.Xsd`. Three kinds of primitive are left. Each is a real
design question, not code that merely looked like this, so each is filed rather
than wrapped or exempted in passing.

**`XsdComponentsAreSpecIntegers`.** `XsdDate.Year`, `.Month`, `.Day`,
`XsdTime.Hour`, `.TimezoneOffset`, a duration's `Months`, `XsdDecimal.Scale`,
and the `implicitTimezoneOffset` the ordering members take. XSD 1.1 Part 2's
seven-property model defines each as an integer property, and ADR 0051 scopes
`Varve.Xsd` to that specification's value spaces and orders. `DD0013`'s case is real:
`new XsdDate(year, month, day)` compiles with month and day swapped. The
alternative is a wrapper per component (`Year`, `Month`, `Day`, `Minutes`…),
about ten new public types on a layer-0 package, which the adoption's expected
API diff did not include. This key keeps the integers. Rejecting it means the
wrappers, in a change of their own.

**`XsdOrderingsReturnInt`.** `XsdDate.Compare(left, right, implicitTimezoneOffset)`
and its siblings, and `XsdString.CompareCodePoints`. They return what
`Comparison<T>` and `IComparable<T>.CompareTo` return. Where an order is
partial, `Varve.Xsd` already returns `PartialOrdering`; these are the total
orders ADR 0051 states.

**`XsdDecimalConvertsToIeeePrimitives`.** `XsdDecimal.ToDouble()` and
`ToSingle()`. A named conversion out to another value space, which `DD0015`
prefers to an implicit operator. The alternative returns `XsdDouble` and
`XsdFloat`.

Each member cites its key with `Scope = ExceptionScope.Boundary`, on the member
and never on the type: a type-level citation would also cover the members
`DD0013` reports in error (decision-driven-analyzers#59).
