# IRIs

Functional specification for `Varve.Iri` (layer 0).

Status: Accepted. Changes only together with the ADR that motivates the change.

Scope: parsing, validation and reference resolution over UTF-8. No owned type,
no allocation, and **no `System.Uri`** — the ban is in `eng/BannedSymbols.txt`
and is proved by a fixture that fails the build.

## 1. Normative references

- **RFC 3986**, *Uniform Resource Identifier (URI): Generic Syntax*. §3 the
  syntax components, §4.1 URI-reference, §5 reference resolution, §5.2.4
  `remove_dot_segments`, §5.3 recomposition, §5.4 the resolution examples used
  as this specification's test vectors.
- **RFC 3987**, *Internationalized Resource Identifiers (IRIs)*. §2.2 the ABNF,
  which is RFC 3986's with `ucschar` and `iprivate` added to the character
  classes.
- **RDF 1.1 Concepts**, §3.2 *IRIs*: an RDF IRI is an absolute IRI per RFC 3987
  §2.2, optionally with a fragment identifier, and **must be normalised only in
  the ways RDF permits** — see §4 below.

## 2. What is validated

An input is a sequence of UTF-8 bytes. Validation answers two questions
separately:

- **Well-formedness** against the RFC 3987 `IRI-reference` production, which
  yields the five components — scheme, authority, path, query, fragment — as
  **byte ranges into the input**. Nothing is copied.
- **Absoluteness.** An IRI is absolute when it has a scheme. RDF requires
  absolute IRIs; a relative reference is well-formed and not usable as an RDF
  term until it is resolved.

**Two predicates, and they are not the same question.** `IsAbsolute` answers
*is this a usable RDF IRI* — well-formed **and** carrying a scheme.
`StartsWithScheme` answers only *does this begin with `scheme ":"`*
(RFC 3986 §3.1), and says nothing about the rest.

The distinction is load-bearing for a caller that has turned validation off.
Whether a reference must be resolved against a base is settled by the scheme
alone; folding well-formedness into that decision makes a malformed absolute
IRI look relative, so `<http://a/%zz>` under `ValidateIris = false` would be
resolved rather than taken as written, and rejected for having no base when
there is none. `StartsWithScheme` is what `Varve.Turtle` asks in that case, and
`IsAbsolute` is what it asks in the validating one. Neither changed the other:
`IsAbsolute` still means what it always meant, and the second predicate was
added rather than the first weakened.

`StartsWithScheme` is deliberately the weaker test, so it must never stand in
for validation. A term built from an IRI that only starts with a scheme is as
well-formed as the caller's input was.

Errors carry a **kind and a byte offset**. The offset is the first byte at
which the input could not continue, not the start of the construct — a caller
reporting a position wants to point at the character that broke.

The character classes are RFC 3987's, which extend RFC 3986's with `ucschar`
(U+00A0–U+D7FF, U+F900–U+FDCF, U+FDF0–U+FFEF and the supplementary planes,
excluding the non-characters) and `iprivate`. Validation therefore requires
**UTF-8 decoding**, and invalid UTF-8 is an error in its own right
(`InvalidUtf8`) rather than an invalid-character error, because the two have
different causes and different fixes.

## 3. Reference resolution

RFC 3986 §5, applied to IRIs per RFC 3987 §6.5. Two entry points:

- `ResolveLength(base, reference)` — the exact byte length of the result, so a
  caller can size a buffer without allocating a trial one.
- `TryResolve(base, reference, destination, out written)` — writes into the
  caller's buffer.

**Strict resolution only.** RFC 3986 §5.2.2 allows a non-strict mode in which a
reference whose scheme matches the base's is treated as relative; §5.4.2 shows
the difference as `"http:g"` resolving to `"http:g"` for strict parsers and
`"http://a/b/c/g"` otherwise. We are strict. The non-strict mode exists for
backward compatibility with pre-RFC-2396 parsers and has no place in a store
that must round-trip IRIs byte for byte.

### Test vectors

RFC 3986 §5.4, in full, with base `http://a/b/c/d;p?q`. All forty-two are
implemented as tests; the abnormal examples are not optional.

| §5.4.1 normal | | | |
|---|---|---|---|
| `g:h` → `g:h` | `g` → `http://a/b/c/g` | `./g` → `http://a/b/c/g` | `g/` → `http://a/b/c/g/` |
| `/g` → `http://a/g` | `//g` → `http://g` | `?y` → `http://a/b/c/d;p?y` | `g?y` → `http://a/b/c/g?y` |
| `#s` → `http://a/b/c/d;p?q#s` | `g#s` → `http://a/b/c/g#s` | `g?y#s` → `http://a/b/c/g?y#s` | `;x` → `http://a/b/c/;x` |
| `g;x` → `http://a/b/c/g;x` | `g;x?y#s` → `http://a/b/c/g;x?y#s` | `` (empty) → `http://a/b/c/d;p?q` | `.` → `http://a/b/c/` |
| `./` → `http://a/b/c/` | `..` → `http://a/b/` | `../` → `http://a/b/` | `../g` → `http://a/b/g` |
| `../..` → `http://a/` | `../../` → `http://a/` | `../../g` → `http://a/g` | |

| §5.4.2 abnormal | | | |
|---|---|---|---|
| `../../../g` → `http://a/g` | `../../../../g` → `http://a/g` | `/./g` → `http://a/g` | `/../g` → `http://a/g` |
| `g.` → `http://a/b/c/g.` | `.g` → `http://a/b/c/.g` | `g..` → `http://a/b/c/g..` | `..g` → `http://a/b/c/..g` |
| `./../g` → `http://a/b/g` | `./g/.` → `http://a/b/c/g/` | `g/./h` → `http://a/b/c/g/h` | `g/../h` → `http://a/b/c/h` |
| `g;x=1/./y` → `http://a/b/c/g;x=1/y` | `g;x=1/../y` → `http://a/b/c/y` | `g?y/./x` → `http://a/b/c/g?y/./x` | `g?y/../x` → `http://a/b/c/g?y/../x` |
| `g#s/./x` → `http://a/b/c/g#s/./x` | `g#s/../x` → `http://a/b/c/g#s/../x` | `http:g` → `http:g` *(strict)* | |

The four that matter most are `../../../g` and `../../../../g` — which must not
climb above the root — and `g?y/../x` and `g#s/../x`, which must **not** apply
`remove_dot_segments` to the query or the fragment.

## 4. Normalisation — what we do and do not do

Resolution is a rewrite, so it normalises by construction. Everything else is
refused, because **RDF IRI equality is byte equality** (RDF 1.1 Concepts §3.2:
IRIs are equal if and only if they compare equal as strings), and a store that
silently normalised would break the round-trip the conformance suites test.

**We do, as part of resolution only:**

- `remove_dot_segments` (§5.2.4) on the path, and on the merged path (§5.3).
- Component recomposition (§5.3).

**We do not, ever:**

| Not done | Why |
|---|---|
| Scheme or host case normalisation (§6.2.2.1) | `HTTP://X` and `http://x` are different RDF IRIs. Folding them would merge distinct terms. |
| Percent-decoding of unreserved characters (§6.2.2.2) | Changes the bytes, and `%7E` and `~` are different RDF IRIs. |
| Percent-encoding of anything | The input is what the document said. |
| Punycode or IDNA mapping | An IRI's host is an `ireg-name`; converting it produces a different IRI. |
| Unicode normalisation (NFC or otherwise) | RFC 3987 §5.3.2 recommends NFC *for creating* IRIs, not for comparing given ones. |
| Default-port removal, empty-path insertion | Syntax-based normalisation we are not asked for. |

A caller who wants a normalised form asks for one explicitly. This
specification provides no such function at milestone 3a; if one is ever added
it is a separate operation with a separate name, never a side effect of
validation.

## 5. Interaction with the parsers

`Varve.Turtle` validates every `IRIREF` it reads, unless `ParseOptions.ValidateIris`
is turned off. Validation is over the **unescaped** bytes: an `IRIREF` may
contain `UCHAR` escapes (N-Triples grammar §2, production [8]), and the escape
is resolved before the character class is checked, because `<http://example/ >`
is a bad IRI — which is what the suite's `nt-syntax-bad-uri-*` cases test.

N-Triples and N-Quads require absolute IRIs and have no base, so resolution is
not exercised by their conformance suites. Turtle and TriG do exercise it: a
document's retrieval IRI is the initial base, `@base` and `BASE` rebind it
mid-document, and a later base is itself resolved against the earlier one.

Which of the two absoluteness predicates a parse uses follows
`ValidateIris`, per §2: validating, `IsAbsolute`; not validating,
`StartsWithScheme`. The difference is visible only on input that is malformed
and absolute, which is exactly the input the non-validating mode exists to
pass through.

## 6. Open questions

None. The one deliberate omission — an explicit normalisation function — is
recorded in §4 as a non-goal rather than a question.
