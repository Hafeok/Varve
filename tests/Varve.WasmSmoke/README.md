# Varve.WasmSmoke

A `browser-wasm` app that does two things on one page: parses N-Quads, and
probes the cryptographic primitives ADR 0020's composition needs.

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

The crypto half is **ADR 0020's acceptance condition**, and it failed. On .NET
10 in headless Chromium 141:

| Primitive | Browser |
|---|---|
| `RandomNumberGenerator.Fill` | works |
| `SHA256` | works |
| `HMACSHA256` | works |
| `HKDF.DeriveKey` | works, deterministic |
| `Aes.Create()` | throws `PlatformNotSupportedException` |
| `AesGcm.IsSupported` | false |
| `AesCcm.IsSupported` | false |
| `ChaCha20Poly1305.IsSupported` | false |

No symmetric cipher of any kind is available. The table is **pinned in
`Smoke.cs` and asserted**, so a runtime that changes any row makes this fail —
which is the signal that ADR 0020's successor can be revisited, and is the
reason the app reports rather than throws on the first failure.

`CA1416` is suppressed in `Smoke.cs`. It is the claim under test, not noise:
the analyzer's annotation is one of the four sources that disagreed, and the
run is what settled it.
