# Varve.Iri

IRI validation and reference resolution over UTF-8, with no allocations and no
dependencies.

- **RFC 3987 §2.2** for the syntax, including `ucschar` and `iprivate` — this
  is IRIs, not URIs, so non-ASCII is ordinary rather than an escape hatch.
- **RFC 3986 §5** for reference resolution, strictly, tested against every
  normal and abnormal example in §5.4.
- **Nothing normalises.** RDF IRI equality is byte equality (RDF 1.1 Concepts
  §3.2), so scheme and host case, percent-encoding and Unicode normalisation
  are left exactly as the input had them. Resolution rewrites by construction
  and is the only operation that changes bytes. `System.Uri` cannot do this: it
  lowercases schemes and hosts and resolves dot segments eagerly.

```csharp
if (IriRef.TryValidate("http://example.org/a/b"u8, out IriComponents parts, out IriError error))
{
    Span<byte> destination = stackalloc byte[64];

    if (IriRef.TryResolve("http://example.org/a/b"u8, "../c/d"u8, destination, out int written))
    {
        // http://example.org/c/d
    }
}
```

Layer 0 of [Varve](https://github.com/Hafeok/Varve), a .NET-native
event-sourced RDF store and SPARQL toolkit. MPL-2.0.
