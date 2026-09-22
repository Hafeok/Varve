# 0020 — Cipher for erasure mode

## Status

**Failed its acceptance condition.** 2026-09-21. The decision below did not
survive verification and **must not be built on**. A successor has not been
decided; see *Verification* for what was measured and *What a successor has to
answer*.

[ADR 0023](0023-erasure-and-access-requests.md) decides that erasure is
crypto-shredding. This decides what does the encrypting.

This ADR was **Proposed** earlier on 2026-09-21 and brought options without
choosing. It was never accepted, so it is completed in place; the options that
lost are kept under *Alternatives considered*.

**Revisit condition** (see *Consequences*): the composition does not run on
browser WASM when verified on a real build. That verification is this ADR's
acceptance condition, not a nice-to-have — see *Verification*. Failing it
supersedes this ADR.

## Context

A private dictionary entry is `(KeyId, ciphertext)` covering the whole term
encoding — kind, datatype, language, lexical form
([spec](../spec/log-and-projection-model.md) §1). Four constraints pull against
each other.

1. **Every host, including the browser** (constraint 3).
2. **Deterministic bytes** (§2, §10): the same request sequence on two machines
   yields byte-identical `log/`, with an injected clock and, in erasure mode, an
   injected key store — and **nothing else in `log/` may depend on the machine
   or on randomness**.
3. **No equality oracle.** If two occurrences of the same plaintext under the
   same key encrypt identically, the ciphertext *is* a comparison anyone can run
   without the key — the same defect that rules out content-derived private ids
   (ADR 0012).
4. **Integrity, not just confidentiality.** A private entry that can be altered
   undetectably is worse than one that cannot be read.

## What is available — verified, not remembered

From [Cross-platform cryptography in .NET](https://learn.microsoft.com/dotnet/standard/security/cross-platform-cryptography)
(page last updated 2026-08-07):

| Primitive | Browser |
|---|---|
| AES-GCM | **❌** |
| AES-CCM | **❌** |
| ChaCha20Poly1305 | **❌** |
| SHA-1, SHA-2-256/384/512 | ✔️ |
| HMAC-SHA-1, HMAC-SHA-2-256/384/512 | ✔️ |
| MD5, SHA-3 family, KMAC family | ❌ |

Corroborated at the API level: [`AesGcm`](https://learn.microsoft.com/dotnet/api/system.security.cryptography.aesgcm)
is declared `[UnsupportedOSPlatform("browser")]`. **There is no BCL AEAD
primitive in the browser.**

Two caveats on the evidence, and they are why this ADR carries a condition:

- **That page's symmetric-encryption table has no Browser column at all.**
  Browser AES-CBC support is documented in [What's new in ASP.NET Core in .NET 7](https://learn.microsoft.com/aspnet/core/release-notes/aspnetcore-7.0#blazor),
  under *`System.Security.Cryptography` support on WebAssembly*, which lists the
  SHA and HMAC families, **AES-CBC**, **PBKDF2** and **HKDF** as working via
  SubtleCrypto with a managed fallback (tracking issue
  [dotnet/runtime#40074](https://github.com/dotnet/runtime/issues/40074)). The
  browser half of this decision therefore rests on a release note rather than
  the support matrix.
- The older [.NET 5 breaking-change note](https://learn.microsoft.com/dotnet/core/compatibility/cryptography/5.0/cryptography-apis-not-supported-on-blazor-webassembly)
  lists a much smaller supported set and is superseded in part. A reader who
  finds it first draws the wrong conclusion.

## Decision

**AES-CBC with HMAC-SHA-256, encrypt-then-MAC. One portable, deterministic
format on the BCL, identical on every host.**

- **Separate encryption and MAC keys, derived with HKDF** from the subject key.
  One key used for two purposes is the classic way to break a composed
  construction.
- **The MAC covers the IV, the ciphertext, the key id and the term id.** Binding
  the identifiers stops a valid ciphertext being moved to another entry or
  another key and still verifying.
- **Tag comparison with `CryptographicOperations.FixedTimeEquals`.** Verify
  before decrypting.
- **The IV is derived**, not random:

  ```
  IV = HKDF(key_iv, commit position ‖ allocation index within the commit)
  ```

  Both inputs are fixed by the log, so the bytes reproduce on any machine
  (constraint 2). Neither depends on the plaintext, so two occurrences of the
  same term under the same key land at different indices and encrypt differently
  (constraint 3).

  **A nonce derived from the plaintext — the obvious way to make encryption
  deterministic — would reintroduce exactly the equality oracle ADR 0012 rejects
  content-derived private ids for.** That is a hard constraint on any future
  change to this scheme, not a preference.

### A collision this decision documents and does not fix

Two clones of a log that have diverged can allocate different private terms at
the same position and allocation index, and would therefore reuse an IV under
the same key. Under CBC that leaks whether two plaintexts share a prefix.

It is not fixed, because it cannot arise in a system that works: **a store
refuses to open a log whose header chain does not verify, and refuses to
continue from a head that is not its own** (I6, ADR 0014). Divergent clones of
one dataset are already a refused state. Adding a random salt to close it would
cost constraint 2 — byte determinism — to defend against a configuration the
store rejects anyway. Recorded here so that whoever meets it knows it was seen.

## Alternatives considered

- **AES-GCM where supported, AES-CBC + HMAC in the browser.** Uses a real AEAD
  where one exists. Lost outright: two ciphertext formats mean the same logical
  dataset has different bytes depending on where it was written, which fails
  §10's byte-identical test and makes a dataset non-portable between hosts. §2
  assumes datasets are copied between machines; this option assumes they are not.
- **A managed AEAD implemented in Varve.** Constraint 1 permits it and it would
  give one format everywhere with a real AEAD construction. Lost because writing
  a cipher — and a side-channel-resistant one in portable managed code — is the
  thing nobody should do, and it would sit on the bulk-load write path.
- **Erasure mode unavailable in the browser.** Honest, cheap, and uses a
  standard primitive with no hand composition. Lost because it narrows
  constraint 3 for a feature rather than for the core, and the composition
  chosen above is a well-understood one rather than an invention.

Encrypt-then-MAC specifically, rather than MAC-then-encrypt or
encrypt-and-MAC: it is the composition that is provably secure given a secure
cipher and a secure MAC, and the only one where an invalid entry is rejected
without the cipher ever touching attacker-controlled bytes.

## Verification — this ADR's acceptance condition

The browser claim rests on a release note, so it is verified on a real build
rather than accepted on paper. `tests/Varve.WasmSmoke` runs the exact
composition — HKDF derive, AES-CBC encrypt, HMAC-SHA-256, `FixedTimeEquals`,
decrypt, round-trip assert — as a `browser-wasm` build executed in a real
browser. The procedure and its result are recorded below.

> **Result: failed.** 2026-09-21, milestone 3a.

**Procedure.** `tests/Varve.WasmSmoke` was published as a `browser-wasm` app
with `wasm-experimental` (not Blazor), relinked natively with `wasm-tools`, and
served over HTTP to headless Chromium 141 driven by Playwright 1.56.1. The page
calls a `[JSExport]` entry point that parses 200 quads and then probes each
cryptographic primitive the composition needs, one at a time, reporting what it
observed rather than stopping at the first failure. .NET SDK 10.0.401, runtime
pack 10.0.12.

**What was observed.**

| Primitive | Browser (.NET 10) |
|---|---|
| `RandomNumberGenerator.Fill` | works |
| `SHA256` | works |
| `HMACSHA256` | works |
| `HKDF.DeriveKey` | works, and is deterministic |
| **`Aes.Create()`** | **throws `PlatformNotSupportedException`: `Cryptography_AlgorithmNotSupported, Aes`** |
| `AesGcm.IsSupported` | false |
| `AesCcm.IsSupported` | false |
| `ChaCha20Poly1305.IsSupported` | false |

The parse half passed: 200 quads read in the browser, so constraint 3 holds for
the RDF stack. The cryptographic half did not.

**The finding is wider than the one this ADR was testing for.** This ADR chose
AES-CBC over AES-GCM, AES-CCM and ChaCha20Poly1305 because those three are
unsupported on browser WebAssembly, and rested AES-CBC's availability on a .NET
7 release note listing it among the algorithms enabled through SubtleCrypto.
That note is not true of .NET 10 as measured here. **No symmetric cipher of any
kind is available in a browser.** The choice this ADR makes is therefore not a
choice between ciphers with different browser support; every candidate has the
same browser support, which is none.

The documentation is not self-consistent on the point, which is why the ADR
demanded a build rather than a citation:

- [What's new in ASP.NET Core in .NET 7](https://learn.microsoft.com/aspnet/core/release-notes/aspnetcore-7.0#blazor)
  lists AES-CBC among the algorithms supported on WebAssembly.
- [Cryptography APIs not supported on Blazor WebAssembly](https://learn.microsoft.com/dotnet/core/compatibility/cryptography/5.0/cryptography-apis-not-supported-on-blazor-webassembly)
  lists the supported set as `RandomNumberGenerator`, `IncrementalHash` and the
  SHA family, and says everything else throws.
- [Cross-platform cryptography in .NET](https://learn.microsoft.com/dotnet/standard/security/cross-platform-cryptography#symmetric-encryption)
  gives a symmetric-encryption table with **no browser column at all**.
- The platform-compatibility analyzer (CA1416) says `Aes.Create()` is
  unsupported on browser.

Three of the four agree with the measurement. The release note does not, and it
is the one this ADR relied on.

**The result is pinned, not merely reported.** The smoke app asserts the table
above and fails if any row changes. A runtime that makes a symmetric cipher
available in the browser will therefore announce itself, which is the moment a
successor becomes decidable.

## What a successor has to answer

Not decided here. The measurement changes the question from *which cipher* to
*where the ciphertext is produced and consumed*, and the options are no longer
comparable on cryptographic grounds alone:

- **Do the cryptography in JavaScript**, through `crypto.subtle`, and treat the
  browser as a host that calls out rather than one that computes. SubtleCrypto
  offers AES-CBC, AES-GCM and HMAC — but it is asynchronous, which reaches into
  the shape of every read path that might touch a private term, and it puts a
  second implementation of the format's cryptography in a second language.
- **Ship a managed implementation** of one cipher, so that every host computes
  the same bytes with no platform dependency. That means a hand-written block
  cipher in the trusted path, which is the thing ADR 0020 was written to avoid.
- **Say that erasure mode does not run in a browser**, and that a browser host
  reads only datasets without private entries. The narrowest option, and the
  only one that costs nothing to build; it is a real reduction in what
  constraint 3 claims, and it should be stated in the constraint rather than
  discovered by a user.

Erasure mode is milestone 9. Nothing before it depends on this being settled,
which is why this ADR is left failed rather than replaced in haste.

## Consequences

**Hand composition is where cryptographic mistakes live.** The BCL offers no
composed encrypt-then-MAC primitive, so this is assembled from parts, and the
four requirements above are the assembly instructions rather than advice. A
change to any of them is a change to this decision.

**CBC pads, so ciphertext length leaks plaintext length to a 16-byte block.**
Length leakage is inherent to the design regardless of cipher; it belongs in the
stated guarantee rather than in a surprise.

**Crypto-shredding gives confidentiality after key destruction against an
adversary holding the ciphertext** — not perfect deletion. It does not defend
against an adversary who recorded the plaintext, who kept a key, or who breaks
the cipher later. ADR 0023 states what survives structurally; this is the
cryptographic half of the same honesty.

**The key store returns material that must never be written down.** I9 covers
`log/` and checkpoints; the same applies to swap, crash dumps and logs. That is
an implementation obligation this ADR creates and cannot enforce.

**Revisit condition.** If AES-CBC, HMAC-SHA-256 or HKDF do not run under
`browser-wasm` on a real build, this ADR is superseded rather than amended — the
whole decision rests on one format on every host, and losing the browser breaks
the premise rather than a detail.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0018, 0021–0023).
  Touches **0012** (the equality-oracle constraint is the same one that forbids
  content-derived private ids, and the IV derivation depends on the allocation
  index counters give), **0014** (SHA-2 is available on every host, so the
  header chain has no portability problem, and I6 is what makes the IV collision
  above unreachable), **0018** (the browser backend and the browser cipher stand
  or fall together), and **0023** (this is the part it does not decide). No
  conflict with any.
- **Layer ownership.** No new contract. The cipher is an implementation detail
  behind the key store and the private-entry encoding, both `Varve.Store`,
  **layer 4**. It must not appear in any contract signature: a reader asks for
  the term, not for a decryption.
- **Analyzer rule.** None new. **VARVE0007** keeps cryptographic types out of
  cross-package contracts; the ambient randomness ban from ADR 0011 is what
  makes the derived-IV scheme testable at all.
- **Open questions owned.**
  - **Q6 — cipher and availability per host.** Decided above; the availability
    half is cited and the browser half is pending verification.
  - **Q5 — lookup by private value.** `?x foaf:name "Emil"` cannot use an id.
    Either scan and decrypt, which is linear in the private entries under that
    key, or a keyed blind index — which is **deterministic across subjects and
    whose key is not per subject**, so destroying one subject's key does not
    destroy their index entries. It weakens I10 and needs justification rather
    than a shrug. A per-subject blind index is not an index: you would have to
    know the subject to use it. **Due at milestone 9.**
