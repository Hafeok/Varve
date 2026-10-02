# 0074 — The private id and entry layout, reserved; the key store refused by path

## Status

**Proposed — filed unaccepted by session 6a of #10, 2026-10-02** (ADR 0066).

Fixes, in format version 1 (ADR 0072), the bytes erasure mode will need at
milestone 9, so that turning it on is not a format change. Implements the file
backend's half of ADR [0018](0018-storage-abstraction.md)'s second
impossibility. Nothing here allocates a private id.

## Context

The specification's §12 milestone placement: "the first durable format reserves
the private id class, the private entry layout and the refusal of a key store
path inside the dataset directory." Erasure mode is milestone 9 (ADR 0019,
0023), with the cipher of ADR 0028. Reserving costs nothing while erasure mode
is off; retrofitting would be a format version.

A path refusal is only as good as its comparison. `/data/ds/../ds/keys`,
`data/ds/keys` relative to the working directory, `C:\Data\DS\keys` on a
case-insensitive volume, and `/data/ds/` with its trailing separator all name
places inside `/data/ds`; a string prefix test gets each of them wrong in one
direction or the other, and `/data/ds2` is not inside `/data/ds` although its
string begins with it.

## Decision

### The private class and entry

- **Class tag `10`** in bits 63–62 (ADR 0072). Counters from 1 in their own
  class, independent of content and never interned (ADR 0012).
- **A private dictionary entry** is term-entry kind `4`, after its id:
  the **key id** (16 bytes), the **synthetic IV `V`** (32 bytes, the HMAC-SHA-256
  tag of ADR 0028's SIV composition), and the **ciphertext**, as a varint
  length and its bytes. The ciphertext covers the whole term encoding (spec §1).
- **An `Erasure` commit's kind payload** is the key id, 16 bytes.
- **Settings field 2** is erasure mode, one byte.
- **The commit header's private counter** is always zero until erasure mode.

**The decoder reads all of these, and the store refuses them**: a log holding a
private entry, an `Erasure` commit or settings field 2 opens with a message that
this version does not support erasure mode. The test seam that appends an
`Erasure` commit for the subscription tests writes the 16-byte payload.

### The key store is refused inside the dataset directory

`FileStorageOptions.KeyStore`, a `KeyStoreDirectory`, is checked at open, and
`FileStorage.OpenAsync` refuses when it is inside the `DatasetDirectory` — even
though no key store exists until milestone 9. The option has no other consumer
yet, and says so.

### Directories are wrappers over a normalised absolute path

**`DatasetDirectory` and `KeyStoreDirectory`** are `readonly record struct`s in
`Varve.Store.Log`, each over a **normalised absolute path**: the constructor
resolves relative paths against the current directory, removes `.` and `..`
segments and duplicate separators, and drops a trailing separator (but not the
root's). `KeyStoreDirectory.IsWithin(DatasetDirectory)` compares the resolved
full paths segment by segment, **case-insensitively on Windows and
case-sensitively elsewhere**, and is true for the directory itself and anything
below it. No `string` path appears on `FileStorage` or `Dataset`; a host
converts from a string at its edge.

The containment rule is a property test: generated relative paths, `..`
segments, trailing separators, and case-only differences on Windows.

Symbolic links are **not** resolved: `IsWithin` is about names, and a key store
reached through a link into the dataset directory is a deployment error the
check does not see. Stated, not solved.

## Alternatives considered

- **Leave the private layout to milestone 9.** Then erasure mode is a format
  version, and every dataset written before it reads under rules written after.
- **A private entry as `(KeyId, ciphertext)` with the IV inside the
  ciphertext.** The same bytes, less legible: ADR 0028 names `V` as the SIV tag,
  and the format names it too.
- **Check by string prefix.** Wrong in both directions, above.
- **Resolve symbolic links** (`FileSystemInfo.ResolveLinkTarget`). Only for
  paths that exist, and only one level at a time; a key store path is often
  created after the check. Left to the key store's own deployment checks.
- **`string` paths with a helper**, as `FileStream` takes. Rejected by the
  maintainer: a path is a value with a rule (normalisation and containment), and
  a wrapper is where the rule lives (ADR 0065's reasoning).

## Consequences

- **Turning on erasure mode at milestone 9 is not a format change.** It is a
  settings commit and the first private allocation.
- **A log from a future version that uses erasure mode refuses to open here**,
  by name rather than by a parse error.
- **Two public types and one option** exist before their main consumer; the
  option's documentation says it is checked and otherwise unused.

## Checks

- **Checked against the accepted ADRs** (0001–0069). Implements **0018**'s
  second impossibility (`StorageAbstraction.NoKeysInDatasetDirectory`, which
  the refusal cites) and reserves for **0012**, **0019**, **0021**, **0023** and
  **0028**. Touches **0065** (wrappers in `Varve.Store.Log`) and **0072** (where
  the bytes are). No conflict with any.
- **Layer ownership.** `Varve.Store`, **layer 4**.
- **Analyzer rule.** None. `DD0013` keeps `string` off the two wrappers' users.
- **Open questions owned.** None. Q4, Q5, Q8 and Q9 stay with milestone 9.
