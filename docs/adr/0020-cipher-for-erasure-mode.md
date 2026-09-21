# 0020 — Cipher for erasure mode

## Status

**Proposed.** 2026-09-21.

[ADR 0019](0019-erasure-by-crypto-shredding.md) decides that erasure is
crypto-shredding. It does not decide what does the encrypting. This ADR
verifies what is actually available, states the constraints, brings the options,
and **does not pick**.

## Context

A private dictionary entry is `(KeyId, ciphertext)`, where the ciphertext covers
the whole term encoding — kind, datatype, language, lexical form
([spec](../spec/log-and-projection-model.md) §1). The cipher choice therefore has
to satisfy four constraints at once, and they pull against each other.

1. **Every host, including the browser.** Constraint 3 makes the browser a
   first-class host. A cipher that is unavailable there makes erasure mode a
   server-only feature.
2. **Deterministic bytes.** §2 and §10: the same request sequence on two
   machines yields byte-identical `log/` directories, with clock and randomness
   injected. A randomly generated IV per term is not deterministic in
   production, only in a test harness.
3. **No equality oracle.** If two occurrences of the same plaintext under the
   same key produce the same ciphertext, the ciphertext *is* an equality
   comparison anyone can run without the key. That is the same defect that makes
   content-derived private ids unacceptable in ADR 0012, and it would arrive by
   the back door through a deterministic nonce.
4. **Integrity, not just confidentiality.** A private entry that can be altered
   undetectably is worse than one that cannot be read.

## What is actually available — verified, not remembered

From Microsoft Learn, [Cross-platform cryptography in
.NET](https://learn.microsoft.com/dotnet/standard/security/cross-platform-cryptography)
(page last updated 2026-08-07):

| Primitive | Browser |
|---|---|
| AES-GCM | **❌** |
| AES-CCM | **❌** |
| ChaCha20Poly1305 | **❌** |
| SHA-1, SHA-2-256, SHA-2-384, SHA-2-512 | ✔️ |
| HMAC-SHA-1, HMAC-SHA-2-256/384/512 | ✔️ |
| MD5, HMAC-MD5 | ❌ |
| SHA-3 family, KMAC family | ❌ |

Corroborated at the API level: the
[`AesGcm`](https://learn.microsoft.com/dotnet/api/system.security.cryptography.aesgcm)
class is declared with `[UnsupportedOSPlatform("browser")]`.

**So the specification's guess in Q6 is correct: `AesGcm` is unsupported on
browser WASM.** So are the other two AEAD primitives the BCL offers.

Two caveats on the evidence, stated because they matter:

- **The symmetric-encryption table on that page has no Browser column at all.**
  It lists Windows, Linux, macOS, Apple mobile and Android and stops. Browser
  AES-CBC support is documented elsewhere — in [What's new in ASP.NET Core in
  .NET 7](https://learn.microsoft.com/aspnet/core/release-notes/aspnetcore-7.0#blazor),
  under *`System.Security.Cryptography` support on WebAssembly*, which lists
  SHA-1/256/384/512, the HMAC equivalents, **AES-CBC**, **PBKDF2** and **HKDF**
  as working on WebAssembly via SubtleCrypto with a managed fallback (tracking
  issue [dotnet/runtime#40074](https://github.com/dotnet/runtime/issues/40074)).
  This is a documentation gap rather than a capability gap, but it means the
  browser cipher story rests on a release note rather than on the support
  matrix, and it should be re-verified against a running browser build before it
  is decided.
- The older [.NET 5 breaking-change
  note](https://learn.microsoft.com/dotnet/core/compatibility/cryptography/5.0/cryptography-apis-not-supported-on-blazor-webassembly)
  lists a much smaller supported set. It predates the .NET 7 work and is
  superseded in part; a reader who finds it first will draw the wrong
  conclusion.

## Options

### C1 — AES-CBC + HMAC-SHA-256, encrypt-then-MAC

One format on every host, composed from primitives that are available
everywhere.

- Satisfies constraints 1 and 4, and portability: a dataset written in a browser
  is byte-identical to one written on a server.
- Requires correct composition, and this is where cryptographic mistakes live:
  separate encryption and MAC keys derived with HKDF, the MAC computed over
  `IV ‖ ciphertext`, a constant-time comparison, and verification *before*
  decryption. The BCL offers no composed primitive, so this is hand-assembled —
  exactly the kind of thing constraint 4's "prefer the BCL" would rather avoid
  and here cannot.
- CBC needs padding, so ciphertext length leaks the plaintext length to a
  16-byte block. Length leakage is inherent to the design regardless of cipher
  and should be stated in the guarantee rather than discovered.

### C2 — AES-GCM where supported, AES-CBC + HMAC in the browser

Uses a real AEAD primitive where one exists.

- **Breaks determinism and portability outright.** Two ciphertext formats mean
  the same logical dataset has different bytes depending on where it was
  written, which fails §10's byte-identical test and makes a dataset
  non-portable between hosts. §2 assumes datasets are copied between machines;
  this option assumes they are not.
- Listed because it is the option someone will propose, and the reason it fails
  is worth having written down.

### C3 — A managed AEAD implemented in Varve

Constraint 1 permits it — managed code is all that is required — and it would
give one format everywhere with a real AEAD construction.

- Writing a cipher is the thing nobody should do, and a side-channel-resistant
  one in portable managed code is harder still.
- It must also be fast enough for a bulk load, where every private term is
  encrypted on the write path.
- Not recommended, listed for completeness.

### C4 — Erasure mode unavailable in the browser

Restrict erasure mode to hosts with a BCL AEAD, and refuse to open an
erasure-mode dataset in a browser.

- Honest, cheap, and uses a standard primitive with no hand composition.
- Narrows constraint 3 for one feature rather than for the core. Whether that is
  acceptable depends on whether a browser host is ever expected to hold personal
  data — which is a product question, not a cryptographic one.

## The nonce problem, and why it is not a detail

Constraints 2 and 3 appear to contradict: determinism wants the IV to be a
function of something reproducible, and no-equality-oracle wants two identical
plaintexts to encrypt differently.

They are reconcilable, and the reconciliation is the design:

```
IV = KDF(key_iv, commit position ‖ allocation index within the commit)
```

Both inputs are fixed by the log, so the bytes are reproducible on any machine.
Neither depends on the plaintext, so two occurrences of the same term under the
same key land at different positions or different allocation indices and produce
different ciphertexts.

**A nonce derived from the plaintext — the obvious way to make encryption
deterministic — reintroduces exactly the equality leak that ADR 0012 rejects
content-derived private ids for.** Whoever decides this ADR should treat that as
a hard constraint rather than a preference.

One consequence to check when deciding: an IV that is a function of position
means re-encrypting the same term at a *different* position yields different
bytes, so the dictionary genuinely cannot intern private entries — which is
what §1 already says, now for a second and independent reason.

## Consequences

**Whatever is chosen, the guarantee needs stating precisely.** Crypto-shredding
gives confidentiality after key destruction against an adversary who has the
ciphertext, not perfect deletion. It does not defend against an adversary who
recorded the plaintext, who kept a key, or who breaks the cipher later. And
length is leaked. ADR 0019 already states what survives structurally; this is
the cryptographic half of the same honesty.

**The key store returns material that must never be written down.** I9 covers
`log/` and checkpoints; the same applies to swap, crash dumps and logs. That is
an implementation obligation this ADR creates and cannot enforce.

**HKDF is available on every host**, so deriving per-purpose subkeys —
encryption, MAC, IV — from one subject key is not itself a portability problem,
whichever option wins.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0019). Touches
  **0012** (the equality-oracle constraint is the same one that forbids
  content-derived private ids), **0014** (SHA-2 is available everywhere, so the
  header chain has no portability problem), **0018** (the browser backend and
  the browser cipher story stand or fall together), and **0019** (this is the
  part 0019 deliberately did not decide). No conflict with any.
- **Layer ownership.** No new contract. The cipher is an implementation detail
  behind the key store and the private-entry encoding, both owned by
  `Varve.Store` at **layer 4**. It must not appear in any contract signature: a
  reader of a private term asks for the term, not for a decryption.
- **Analyzer rule.** None new. Two existing reservations apply: **VARVE0007**
  keeps cryptographic types out of cross-package contracts, and the ambient
  clock and randomness ban from ADR 0011 is what makes the derived-IV scheme
  testable.
- **Open questions owned.**
  - **Q6 — cipher and availability per host.** The availability half is answered
    above and cited. The choice among C1–C4, and the IV derivation, remain open.
    **Due before erasure mode is implemented**, and the browser claim should be
    re-verified against a running WASM build first, because it rests on a
    release note rather than the support matrix.
  - **Q5 — lookup by private value.** `?x foaf:name "Emil"` cannot use an id.
    Either scan and decrypt, which is linear in the private entries under that
    key, or a keyed blind index. **A blind index is deterministic across
    subjects and its key is not per subject**, so destroying one subject's key
    does not destroy their blind-index entries — it weakens I10 and needs
    justification rather than a shrug. A per-subject blind index is not an
    index: you would have to know the subject to use it, which is the thing the
    lookup was for. Due with the same decision.
