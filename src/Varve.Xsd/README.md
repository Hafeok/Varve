# Varve.Xsd

XML Schema 1.1 datatypes with SPARQL 1.1 operator semantics, with no
allocations on the numeric types and no dependencies.

- **Values, not terms.** `"1"^^xsd:integer` and `"01"^^xsd:integer` are two
  RDF terms with one value. This package answers what the value is, for the
  datatypes SPARQL 1.1 §17.3 dispatches on; term equality stays lexical and
  lives in `Varve.Rdf`.
- **Exact decimal**: a fixed-point `Int128` with 18 fractional digits, and a
  stated edge — overflow fails rather than rounding.
- **The date and time family** on XSD 1.1's seven-property model, with both
  orders it needs: the implicit-timezone total order SPARQL's operators use,
  and XML Schema's partial order under its own name.
- **Canonical forms** for every type, and `IsCanonical` on a lexical form
  without mapping it to a value — which is what a store's inline-id rule asks.

```csharp
if (XsdDecimal.TryParse("1.50"u8, out XsdDecimal value))
{
    Span<byte> canonical = stackalloc byte[64];
    value.TryFormat(canonical, out int written);   // "1.5"
}
```

Layer 0 of [Varve](https://github.com/Hafeok/Varve), a .NET-native
event-sourced RDF store and SPARQL toolkit. MPL-2.0.
