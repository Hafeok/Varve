# Varve.WasmSmoke

A `browser-wasm` app that does six things on one page: parses N-Quads, reads
and writes Turtle and TriG, opens an in-memory `Varve.Store` dataset — commit,
pin, checkpoint, as-of, and a reopen from its own log — parses a SPARQL query
and an update, prints the algebra through the serialiser and parses it back,
evaluates queries over a store loaded from Turtle, and probes the
cryptographic primitives ADR 0028's composition and SPARQL's hash functions
need.

Turtle is there because N-Quads exercises none of what it adds — the statement
buffer, the prefix table, the blank node naming, the writer's state — so a
browser runtime that broke one of them would pass an N-Quads probe. The
document it reads carries a predicate-object list, an object list, a
collection, a nested blank node property list and an escape, and the TriG one a
graph block.

`wasm-experimental`, not Blazor. Constraint 3 is that Varve runs in a browser,
not that it runs in a web framework, and Blazor would add an ASP.NET Core
package tail with nothing to do with the claim.

## Running it

CI builds this and requires it to be warning-free; it does not run it, because
running it needs a browser and a server. To run it:

```bash
dotnet build tests/Varve.WasmSmoke -c Release
cd tests/Varve.WasmSmoke/bin/Release/net10.0/browser-wasm/AppBundle
python3 -m http.server 8731
```

Then open `http://localhost:8731/index.html`. The page prints the report, and
also leaves it on `globalThis.varveSmokeReport` so a driver can read it. A
report ending in `OK` is a pass; anything else names what failed.

It must be served over HTTP. Opening the file directly does not work: the
runtime is loaded as an ES module and fetches `_framework/` over the network.

## What it found

On .NET 10 in headless Chromium 141:

| Primitive | Browser | |
|---|---|---|
| `RandomNumberGenerator.Fill` | works | ADR 0028 |
| `SHA256` | works | ADR 0028, SPARQL `SHA256()` |
| `HMACSHA256` | works | ADR 0028 |
| `HKDF.DeriveKey` | works, deterministic | ADR 0028 |
| `CryptographicOperations.FixedTimeEquals` | works | ADR 0028 |
| `SHA1` | works | SPARQL `SHA1()` |
| `SHA384` | works | SPARQL `SHA384()` |
| `SHA512` | works | SPARQL `SHA512()` |
| `IncrementalHash` SHA-256 | works | RDFC-1.0 (ADR 0059) |
| `IncrementalHash` SHA-384 | works | RDFC-1.0, when selected |
| `MD5` | throws `CryptographicException` | SPARQL `MD5()`: the package's own, RFC 1321 |
| `Aes.Create()` | throws `PlatformNotSupportedException` | ADR 0020 |
| `AesGcm.IsSupported` | false | |
| `AesCcm.IsSupported` | false | |
| `ChaCha20Poly1305.IsSupported` | false | |

Read it in three parts. The last four are **ADR 0020's acceptance condition**,
and they failed it: no symmetric cipher of any kind is available in a browser,
which is a wider finding than the one that ADR was testing for. The first five
are **ADR 0028's first condition**, and they hold — 0028 builds a deterministic
AEAD out of exactly those primitives because they are the ones that run here.
The six between are the hash functions: SPARQL's (milestone 5b), of which
three are the platform's and `MD5` is not available, so the evaluator carries
its own; and the incremental SHA-256 and SHA-384 that RDFC-1.0 hashes with
(milestone 5c).

The table is **pinned in `Smoke.cs` and asserted**, so a runtime that changes
any row makes this fail. That is the correct behaviour in both directions: a
cipher appearing in the browser is the signal that ADR 0028's alternatives are
worth revisiting, and a primitive disappearing would break 0028 itself. It is
also why the app reports every row rather than throwing on the first failure.

**The store runs in the browser** (milestone 4, headless Chromium 141): two
commits, a pinned read, a checkpoint, an as-of read over it, and a reopen that
verifies the chain and loads the checkpoint. `Run` is asynchronous and the page
awaits it, because the storage contract is asynchronous throughout (ADR 0018)
and a browser has one thread to block.

**The parser runs in the browser** (milestone 5a, headless Chromium 141): a
query with a property path, a subquery, an aggregate and a reified triple is
parsed from UTF-16, written back through the serialiser, parsed again and
compared for the identical tree; an update goes the same way; an ill-formed
query is refused with its position. The trimmer kept every record's
synthesised equality and the dictionary's span lookup, which is what the
round trip proves.

**The evaluator runs in the browser** (milestone 5b, headless Chromium 141):
a Turtle document committed to the store, then over its pinned view a basic
graph pattern with a filter, an aggregate (`COUNT`, `SUM`, `AVG`), a `+`
closure, and the five hash functions of SPARQL over `"abc"`, each compared
whole with its expected answer. The platform's `MD5` throws here, so SPARQL's
`MD5()` is `Varve.Sparql.Evaluation`'s own RFC 1321 implementation, tested
against §A.5's vectors; the other four are the platform's, and the rows above
pin that they are available.

**Updates, result writing and canonicalisation run in the browser** (milestone
5c, headless Chromium 141): an `INSERT DATA` and a `DELETE/INSERT` in one
request, executed through `Varve.Sparql.Store` as one commit to an in-memory
store; a query over the result written as SPARQL results JSON; and a
three-quad dataset with a chain of blank nodes canonicalised with RDFC-1.0
under SHA-256 and SHA-384. Each is compared whole with the string the AOT host
produces from the same code (`Update.cs`, shared by both apps). The app is a
layer 6 host (ADR 0060), which is what lets it reference the layer 5
integration.

A `404` for `/favicon.ico` in the console is Chromium asking for one that the
app bundle does not contain. It is not a failure.

`CA1416` is suppressed in `Smoke.cs`. It is the claim under test, not noise:
the analyzer's annotation is one of the four sources that disagreed, and the
run is what settled it.
