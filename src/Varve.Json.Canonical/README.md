# Varve.Json.Canonical

RFC 8785, the JSON Canonicalization Scheme, over UTF-8, with no dependency
(ADR 0147). It is the one implementation of the canonical form in Varve:
`Varve.JsonLd` writes `rdf:JSON` literals with it, and `Varve.Events` writes
CloudEvents, schemas and AsyncAPI documents with it. What it writes for an
input never changes, because those bytes are read for ever.

- **`CanonicalJson`**: the three rules as functions.
  - `WriteString` escapes only `"`, `\` and the control characters (§3.2.2.2).
  - `FormatNumber` and `WriteNumber` render a double as ECMAScript's
    `Number::toString` does (§3.2.2.3).
  - `CompareNames` orders member names by their UTF-16 code units (§3.2.3).
- **`CanonicalJsonWriter`**: a forward-only writer into an
  `IBufferWriter<byte>`. An object's members must arrive in canonical order;
  a member out of order, a non-finite number, or a value where a name is due
  throws, so a caller cannot write a non-canonical document by accident.
