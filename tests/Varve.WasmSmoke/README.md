# Varve.WasmSmoke

A `browser-wasm` app that does three things on one page: parses N-Quads, reads
and writes Turtle and TriG, and probes the cryptographic primitives ADR 0028's
composition needs.

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
| `SHA256` | works | ADR 0028 |
| `HMACSHA256` | works | ADR 0028 |
| `HKDF.DeriveKey` | works, deterministic | ADR 0028 |
| `CryptographicOperations.FixedTimeEquals` | works | ADR 0028 |
| `Aes.Create()` | throws `PlatformNotSupportedException` | ADR 0020 |
| `AesGcm.IsSupported` | false | |
| `AesCcm.IsSupported` | false | |
| `ChaCha20Poly1305.IsSupported` | false | |

Read it in two halves. The last four are **ADR 0020's acceptance condition**,
and they failed it: no symmetric cipher of any kind is available in a browser,
which is a wider finding than the one that ADR was testing for. The first five
are **ADR 0028's first condition**, and they hold — 0028 builds a deterministic
AEAD out of exactly those primitives because they are the ones that run here.

The table is **pinned in `Smoke.cs` and asserted**, so a runtime that changes
any row makes this fail. That is the correct behaviour in both directions: a
cipher appearing in the browser is the signal that ADR 0028's alternatives are
worth revisiting, and a primitive disappearing would break 0028 itself. It is
also why the app reports every row rather than throwing on the first failure.

A `404` for `/favicon.ico` in the console is Chromium asking for one that the
app bundle does not contain. It is not a failure.

`CA1416` is suppressed in `Smoke.cs`. It is the claim under test, not noise:
the analyzer's annotation is one of the four sources that disagreed, and the
run is what settled it.
