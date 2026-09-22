# 0028 — A deterministic AEAD built from HMAC-SHA-256

## Status

**Accepted.** 2026-09-22. Supersedes [0020](0020-cipher-for-erasure-mode.md).

[ADR 0023](0023-erasure-and-access-requests.md) decides that erasure is
crypto-shredding. This decides what does the encrypting, after 0020's choice
failed on a real build.

Records [`docs/spec/log-and-projection-model.md`](../spec/log-and-projection-model.md)
§9 and Q6.

**Two conditions, in *Consequences*.** The first is **discharged** — see the
amendment below. The second — an external cryptographic review before milestone
9 ships — is **open**, and until it is met this is a decision about what to
build and not a claim that it is sound.

### Amendment, 2026-09-22 — condition 1 is discharged; condition 2 remains open

The decision above is unchanged. This records the state of its two conditions,
because an ADR that carries conditions should say which of them have been met
and on what evidence rather than leaving a reader to reconstruct it.

**Condition 1 — the primitives run in a browser — is discharged**, on commit
`f4a7b0e`. The evidence is a test, not a report: `tests/Varve.WasmSmoke` probes
each primitive in a real browser and asserts the result against the pinned
table in *Consequences*, so the claim is re-made on every run rather than
having been made once. Observed in headless Chromium 141 on .NET 10:

```
crypto: RandomNumberGenerator=yes, SHA256=yes, HMACSHA256=yes, HKDF=yes,
        FixedTimeEquals=yes, AES-CBC=unsupported, AES-GCM=no, AES-CCM=no,
        ChaCha20Poly1305=no
```

All five primitives this construction needs are present. Two are checked for
more than presence: HKDF's derivation is checked to be **deterministic**, which
is what makes the synthetic IV portable, and `FixedTimeEquals` is probed in
both directions, because a comparison that says yes to everything is not a
comparison. The last four rows are the ones that failed ADR 0020 and are
asserted to still fail, so a symmetric cipher appearing in a browser reports
itself instead of going unnoticed.

**Condition 2 — external cryptographic review — remains open, and is due
before milestone 9 ships.** Nothing here implements the construction, and
nothing may present it as protecting anyone's data until the review is done.
Discharging the first condition does not weaken the second: portability was
never the part in doubt.

## Context

ADR 0020 chose AES-CBC with HMAC-SHA-256 in an encrypt-then-MAC composition,
conditionally, and made its own acceptance turn on a browser build because the
availability of AES-CBC on browser WebAssembly rested on a .NET 7 release note.
The build was run at milestone 3a and the condition failed.

What it measured, in headless Chromium 141 on .NET 10, from a
`wasm-experimental` app relinked with `wasm-tools` and served over HTTP:

| Primitive | Browser |
|---|---|
| `RandomNumberGenerator.Fill` | works |
| `SHA256` | works |
| `HMACSHA256` | works |
| `HKDF.DeriveKey` | works, and is deterministic |
| `CryptographicOperations.FixedTimeEquals` | works, and distinguishes |
| `Aes.Create()` | throws `PlatformNotSupportedException` |
| `AesGcm.IsSupported` | false |
| `AesCcm.IsSupported` | false |
| `ChaCha20Poly1305.IsSupported` | false |

**The finding is wider than the condition.** 0020 preferred AES-CBC *because*
the three AEADs have no browser support. None of the four has. The question
stops being which cipher and becomes where the ciphertext is produced, and the
three candidate successors 0020 wrote down were all answers to that question:
call out to SubtleCrypto, ship a hand-written block cipher, or drop the browser.

None of the three is taken. SubtleCrypto is asynchronous, and decryption sits in
term externalisation on the evaluator's synchronous path
([ADR 0022](0022-quad-source-term-handle.md)) — making that path asynchronous to
reach a browser API would reshape every read that might touch a private term,
and would put a second implementation of the format's cryptography in a second
language. A hand-written block cipher is the riskiest kind of home-made
cryptography: a table-driven AES leaks its key through cache timing, and a
constant-time one is a specialist artifact, in the trusted path, maintained by
us. Dropping the browser is the fallback if this decision does not survive
review, not the choice, because constraint 3 makes the browser a first-class
host rather than a nice-to-have.

The fifth row above is the one that matters. **SHA-256, HMAC-SHA-256, HKDF and a
constant-time comparison run synchronously on all three hosts.** A construction
that needs nothing else needs no new primitive, no interop and no reduction in
what the browser can do.

## Decision

**A deterministic, misuse-resistant AEAD in an SIV composition, built from
HMAC-SHA-256 alone.** The MAC is computed first over the associated data and the
plaintext; its output is both the authentication tag and the synthetic IV that
seeds the keystream.

### Keys

Two keys, derived from the data subject's key with HKDF-SHA-256 under distinct,
**versioned** info strings:

```
K_mac = HKDF-SHA-256(subject key, info = "varve/aead/v1/mac")
K_enc = HKDF-SHA-256(subject key, info = "varve/aead/v1/enc")
```

One key used for two purposes is the classic way to break a composed
construction. The version in the info string is what lets a future format
revision derive different keys from the same subject key rather than needing a
new subject key.

### Associated data

```
AD = version ‖ key id ‖ term id ‖ position ‖ allocation index
```

Every field is **fixed-width**, so `AD ‖ P` is an injective encoding of
`(AD, P)` and no pair of distinct inputs can be made to collide by moving bytes
across the boundary. Binding the identifiers is what stops a valid entry being
moved to another term, another key, or another position and still verifying.

### Encryption

```
V   = HMAC-SHA-256(K_mac, AD ‖ P)
S_i = HMAC-SHA-256(K_enc, V ‖ uint32be(i))     for i = 0, 1, 2, …
C   = P ⊕ (S_0 ‖ S_1 ‖ S_2 ‖ …) truncated to |P|
```

The stored entry is `(V, C)`.

### Decryption

Regenerate the keystream from the stored `V`, recover `P'`, recompute
`V' = HMAC-SHA-256(K_mac, AD ‖ P')`, and compare `V'` with `V` using
`CryptographicOperations.FixedTimeEquals`. **Release `P'` only on equality.**
This is the one ordering the construction allows — an SIV scheme cannot verify
before decrypting, because the tag is computed over the plaintext — and it is
why `P'` must be treated as untrusted until the comparison succeeds and must not
reach a caller, a log or a timing-observable branch before then.

### Tag length: 32 bytes

`V` is doing two jobs, and they want different sizes. As an authentication tag,
128 bits is the standard level and truncating to 16 bytes would be ordinary. As
the **seed of the keystream**, its width is what keeps two distinct
`(AD, P)` pairs from producing the same keystream: a collision in `V` is a
two-time pad. At 16 bytes that is a birthday bound around 2⁶⁴ distinct entries;
at 32 bytes it is not a consideration at any scale a dataset reaches.

One value serving two purposes is sized for the harder of the two. The cost is
32 bytes per private entry, on entries that are already a minority of a dataset
and already carry a separate id class.

### Padding: supported, off by default, a dataset setting

The construction is a stream cipher, so — unlike the CBC design this supersedes —
there is **no block requirement at all**, and `|C| = |P|` exactly. Padding is
therefore purely a length-hiding choice, not a structural one.

Deterministic padding to a multiple of 16 bytes (ISO/IEC 7816-4: a `0x80` byte
then zeros), **applied to `P` before the MAC**, keeps every property above: the
padded plaintext is what `V` is computed over, so determinism, injectivity and
authentication are unaffected, and unpadding happens after verification.

It is a [dataset setting](0021-dataset-settings-as-a-commit-kind.md), off by
default. Whether the length of a term is worth hiding is a judgement about a
particular dataset — an IRI's length says little, a free-text note's may say a
great deal — and this ADR declines to make it for everyone.

## Alternatives considered

- **AES-CBC with HMAC-SHA-256, encrypt-then-MAC** — ADR 0020's decision. Lost
  because `Aes.Create()` throws on browser WebAssembly, which is the measured
  fact above and not a preference.
- **SubtleCrypto through JavaScript interop.** The browser genuinely offers
  AES-CBC, AES-GCM and HMAC. Lost on asynchrony: `TryExternalise` is synchronous
  by ADR 0022 and sits under the evaluator, so an async cipher would make every
  read that might touch a private term async, and the cost would be paid by
  every host to serve one. It also means two implementations of one format's
  cryptography, in two languages, which is the kind of duplication that ends
  with them disagreeing.
- **A hand-written block cipher**, so that one managed implementation serves
  every host. Lost on risk. A table-driven AES leaks its key through cache
  timing; a constant-time implementation is a specialist artifact; and either
  would sit in the trusted path and be ours to maintain. Composing a standard
  primitive is the smaller risk than implementing a new one.
- **Erasure mode does not run in a browser.** The narrowest option and the
  cheapest to build. Held as the **fallback** if the review below rejects this
  construction, not taken now, because it is a real reduction in constraint 3
  and should be stated in the constraint rather than discovered by a user.
- **A 16-byte tag.** Halves the per-entry overhead and is the ordinary choice
  for an authentication tag. Lost to the keystream-seed reading above; recorded
  here so that a later decision to truncate is taken deliberately, with the
  birthday bound in view, rather than by omission.
- **RFC 5297 AES-SIV**, which is the standardised version of exactly this shape.
  Lost for the same reason as AES-CBC: it is AES, and there is no AES in the
  browser. The debt this leaves is the review condition below.

## Consequences

**Determinism, which the specification needs.** The same slot with the same
plaintext yields the same bytes, on every host, with no stored state — §10's
determinism test depends on it, and 0020 needed a derived IV to get it. Here it
falls out of the construction rather than being arranged.

**The divergent-clone collision that ADR 0020 decided to document is closed.**
0020's IV was a function of position and allocation index alone, so two clones
that diverged could encrypt different plaintexts at the same slot under the same
IV — a two-time pad. 0020 argued the store refuses divergent logs
([ADR 0014](0014-header-chain-and-divergence.md)), which is true of the store
and not true of the directory: both clones' bytes can end up in one Git
repository, and that is where the two ciphertexts meet. Here the plaintext is in
the MAC input, so different plaintexts at the same slot give independent
keystreams.

**The equality-oracle objection does not transfer.** [ADR 0012](0012-term-dictionary-and-id-scheme.md)
rejects content-derived private ids because they let an observer test whether
two entries hold the same term. A plaintext-derived IV would normally reintroduce
exactly that. It does not here, because the slot — key id, term id, position,
allocation index — is inside the MAC input, so two entries collide only when they
are the same term *at the same slot*, which is the same entry. Equality across
slots stays invisible. **This is load-bearing: any change that removes a slot
field from `AD` reintroduces the oracle**, and is a change to this decision.

**Condition 1 — the primitives run in a browser. Measured, and it holds** (and
is discharged as of the amendment above).
SHA-256, HMAC-SHA-256, HKDF and `FixedTimeEquals` all work on browser
WebAssembly under .NET 10; HKDF's derivation was checked to be deterministic and
`FixedTimeEquals` to distinguish as well as match. The table is pinned and
asserted in `tests/Varve.WasmSmoke`, so a runtime that withdraws any of them
fails the build rather than failing in the field.

**Condition 2 — external cryptographic review, before milestone 9 ships.**
**This is a custom instantiation of a standard composition, not RFC 5297.** The
shape is well understood and the primitive is standard, but the particular
construction — this key separation, this associated data, this keystream
derivation — has not been reviewed by anyone who does this for a living, and an
ADR is not a security proof. Until it has been, nothing may ship that presents
this as protecting anyone's data. **If the review rejects the construction, the
fallback is that erasure mode does not run in the browser**, and that reduction
in constraint 3 gets its own ADR rather than being absorbed silently.

**Nothing is implemented now.** Milestone 9 implements it, with checked-in
known-answer vectors that must produce identical bytes on CoreCLR, Native AOT
and browser WebAssembly. A construction whose whole argument is portability is
tested by running it on all three and comparing bytes, not by running it on one.

**Cost per private entry: 32 bytes**, plus up to 16 more when padding is on.
`|C| = |P|` otherwise, which is better than the CBC design's up-to-16 bytes of
mandatory padding.

**Four HMAC invocations for a short term** — one for `V`, one per 32-byte
keystream block, one to re-verify on read. Most terms are shorter than 32 bytes,
so the common case is two on write and two on read. That is more hashing than a
block cipher would do and it is not a hot path: private terms are a minority,
and the alternative was not having one.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0018, 0021–0027).
  Touches **0012** (the equality oracle, and why this does not reintroduce it),
  **0014** (the divergence argument this no longer has to rely on), **0021**
  (padding is a dataset setting), **0022** (externalisation is synchronous,
  which is what rules SubtleCrypto out) and **0023** (this is its cipher). No
  conflict with any.
- **Layer ownership.** The construction belongs to `Varve.Store`, layer 4.
  Nothing below layer 4 knows a cipher exists.
- **Analyzer rule.** None reserved. There is no code to check yet, and ADR 0004
  allocates ids to rules rather than to intentions.
- **Open questions owned.** **Q6** — cipher and availability per host — is
  answered by this ADR subject to its two conditions. **Q5** (lookup by private
  value) remains open and is milestone 9's; nothing here makes it easier, since
  a deterministic AEAD is still not a searchable encryption scheme.
