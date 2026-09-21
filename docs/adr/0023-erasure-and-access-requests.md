# 0023 — Erasure by crypto-shredding, and access requests

## Status

**Accepted.** 2026-09-21. Supersedes [0019](0019-erasure-by-crypto-shredding.md).

Records [`docs/spec/log-and-projection-model.md`](../spec/log-and-projection-model.md)
§9, T4, I9 and I10. The **cipher** is [ADR 0020](0020-cipher-for-erasure-mode.md).

**What changed from 0019**, and why it is a supersession rather than an
amendment: 0019 made the *selector* — a function from a quad source and a data
subject to a set of quads — part of erasure, used both for access requests and
by `T4` to clean the current graph. Specification version 1 removes it from
erasure entirely. Access is defined by **key id**, `T4` retracts by **key id**,
and the selector survives only for datasets that have no key ids. That reverses
a stated element of the decision, so 0019 is superseded and kept.

The crypto-shredding decision itself, and everything 0019 recorded about why log
rewriting and dictionary redaction lose, carries over unchanged.

## Context

`docs/brief.md` lists "GDPR-style hard deletion in an append-only model" as a
tension to be resolved by ADR. It is the sharpest contradiction in the design:
the thesis is that the log is never rewritten, and the legal requirement is that
data about a person can be made to go away.

§2 sharpens it. The dataset directory is copied by tools the store cannot reach.
**Any mechanism that works by removing bytes erases nothing that has already
left the building** — and the design assumes copies have.

Erasure is only half the obligation. The same regulation that requires deletion
requires *access*: a data subject can ask what is held about them. 0019 treated
access as a by-product of the selector. Version 1 treats it as the primary
operation and derives erasure's own cleanup from the same definition, which is
what this ADR records.

## Decision

**Erasure is crypto-shredding: the data stays, the key is destroyed.** Erasure
mode is per-dataset, off by default, and is a `Settings` commit (ADR 0021). When
off, no private ids exist, no key store is consulted, and nothing here costs
anything beyond one reserved id class.

### Contracts owned by `Varve.Store`

**Key store.** Create a key for a data subject; resolve subject to `KeyId`;
fetch key material by `KeyId`; destroy a key.

> **Implementations live outside the dataset directory by construction, and the
> file backend refuses a key store path inside it.** This is the load-bearing
> rule, and it is the same rule as never checking a signing key into the
> repository it signs. A key inside the directory is copied with the directory,
> and destroying it locally then destroys nothing.

It is the **only mutable, deletable component in the system**, and it needs its
own backup policy that itself honours destruction — a backup that restores a
destroyed key un-erases a person.

**Classifier.** Given the pending operations and the pinned quad source, assign
term occurrences to data subjects. Free of SPARQL and SHACL (ADR 0005);
implementations derived from annotated shapes or from queries are **layer 5**.

**Classification gate.** In erasure mode a pre-commit validator should reject
literals under properties no shape has classified as private or as not personal,
because unclassified personal data that reaches the log can never be erased.
This is **validator policy in the integration layer** (ADR 0017); the store
provides the hook and takes no view. It cannot catch personal data inside free
text under a property a shape has declared not personal.

### Access is defined by key id

**`Access(K)`** for a key `K` is: every dictionary entry under `K`, every quad
in any commit that mentions such an id, **with the positions and timestamps at
which it was asserted and retracted**, and the metadata of commits whose agent
is under `K`. It is computed by scanning the log by key id, on demand, or from
an optional projection.

**Agents of other commits are included only when canonical or under `K`** —
GDPR Article 15(4): the right of access must not adversely affect the rights of
others, and the agent of an unrelated commit is another person.

**Scope.** `AllHistory` returns `Access(K)`. `Current` restricts it to quads in
`G_head`. A request may state the scope; otherwise the dataset's **default
access scope** applies (a setting, ADR 0021), and that setting defaults to
`AllHistory`. **The setting exists because the call differs between
controllers** — whether retained history is in scope for a subject access
request is a legal judgement about a particular deployment, and the store's job
is to make either answer available and recorded, not to make it.

Below an archive horizon, `AllHistory` fails explicitly unless the archive is
attached (T3). It never returns a partial answer.

### Without erasure mode

There are no key ids, so `Access(K)` has nothing to key on. Access is then
served by a **selector** — an optional contract from a quad source and a data
subject to a set of quads, evaluated over `G_head`. Such a dataset **can serve
access requests and can never serve erasure**, which is the honest statement of
what it bought by not turning erasure mode on.

### `T4 Erase(subject)`

1. Commit a normal `Data` commit retracting from `G_head` **every quad that
   mentions a term under the subject's key**, so the current graph holds no
   dangling structure. The caller may opt out.
2. Append an `Erasure` commit whose metadata carries the `KeyId`, agent and
   cause. Empty delta, exempt from I4.
3. Destroy the key in the key store.

Step 1 is **by key id**, not by a selector. That is the substantive change from
0019 and it is the right one: the thing being erased is defined by the key, the
key is what `T4` destroys, and routing the cleanup through a separate
subject-to-quads function introduced a second definition of "the subject's data"
that could disagree with the first. One definition, used by both access and
erasure, cannot.

Replicas and subscribers receive the `Erasure` commit through the log and
destroy their copy of the key — which is why ADR 0016 delivers `Erasure` commits
regardless of subscription filter.

### The two invariants

**I9, plaintext confinement — at all times, not only after erasure.** No file in
`log/`, no checkpoint and no record delivered to a filtered subscription
contains the plaintext of a private term or any key material. Plaintext derived
from private terms exists only in memory and under `derived/`, tagged with its
`KeyId` (ADR 0016).

**I10, erasure soundness.** After `Erase`, every private entry under the
destroyed key is unreadable in the log, in every checkpoint, **and in every copy
of the dataset directory made at any time**. I1–I8 are unaffected, because no
log byte changed.

### What this does not erase

**Structure survives**: shredded ids, the predicates between them, and links
from a shredded node to canonical terms — a shredded subject that `worksFor` a
public organisation. Re-identification from structure is a documented attack on
anonymised graphs. Where a link is itself identifying, the classifier can make
the object occurrence private too. **Whether the residue counts as anonymous is
a legal question** (Q9), and the engineering answer is the classifier's reach.

**A party given a key who keeps it is outside technical control.** Towards such
a party erasure is a contractual obligation. The store can say what it
destroyed; it cannot say what someone else kept.

### As-of reads become more stable, not less

Draft 1's "stable modulo erasure" is withdrawn. As-of reads are structurally
stable for ever. What changes after an erasure is the *readability* of private
terms, at every position at once, because there is one key.

## Alternatives considered

- **Rewrite the log — remove or redact the affected commits.** The obvious
  reading of "delete". Lost to §2 above all: copies exist the store cannot
  reach, so a local rewrite is theatre. It also destroys the header chain (ADR
  0014), invalidates every stored position, and turns every as-of read and
  subscriber resume point into a lie. An append-only log that is sometimes
  rewritten has the costs of both models and the guarantees of neither.
- **Id scrambling, or redacting the dictionary entry only.** Cheap, no
  cryptography, and what "we deleted the personal data" often means in practice.
  Lost on two grounds, the second decisive. It is **pseudonymisation, not
  erasure** — the structure and the original bytes may still be recoverable, and
  that distinction is exactly the one being elided. And **shared interned
  literals cannot be redacted per subject**: a canonical entry for a city, a job
  title or a date is referenced by thousands of subjects, so there is no
  per-subject edit to make. That is why private terms are a separate id class
  that is not interned — the mechanism had to make per-subject removal possible
  before it could be asked for.
- **Encryption at rest, or full-disk encryption.** Protects a stolen laptop.
  Does nothing here: the threat is a legitimate copy of the directory.
- **One key per dataset.** Simplest key management. Lost: erasing one subject
  would erase everyone.
- **Keeping the selector as erasure's definition** (0019's shape). Lost as
  above: two definitions of "the subject's data" that can disagree, where the
  key already provides one that cannot.
- **A fixed access scope**, either always `AllHistory` or always `Current`. Lost
  because the correct answer is a legal judgement about a deployment, and
  hard-coding either would put the store in the position of having made it.
- **Not supporting erasure at all**, and telling users to segregate personal
  data into a dataset they can delete wholesale. Legitimate and cheaper. Lost
  because "put the personal data somewhere else" is not available to a graph
  database: the value of the graph is the joins, and the joins are where the
  personal data is.

## Consequences

**The key store is the single point of failure for the entire guarantee.** A key
store backed up carelessly, or whose backups outlive a destruction, silently
voids I10. No amount of code in `Varve.Store` can enforce this, and it should be
documented as prominently as the guarantee.

**Classification happens at write time and what is missed cannot be recovered.**
There is no key to destroy. The gate reduces this and cannot close it.

**The commit's agent can itself be erased**, because §1 requires the agent to be
a `TermId` rather than an inline string. Easy to get wrong once and never be
able to fix.

**`Access(K)` is a log scan.** On demand it is proportional to the log; the
optional projection is the answer for a controller that fields requests often.
Either way it inherits the archive horizon and fails explicitly below it.

**Erasure mode is effectively a creation-time decision.** Turning it on later
does not retroactively make earlier terms private (ADR 0021), and turning it off
is refused while private entries exist.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0018, 0020–0022).
  Touches **0005** (all contracts SPARQL- and SHACL-free; shape- and
  query-driven implementations are layer 5), **0010** (`Erasure` is exempt from
  I4), **0012** (private ids are counter-allocated in their own class,
  independent of content and not interned), **0016** (projections tag derived
  plaintext with a `KeyId` and purge on the `Erasure` commit; filters never drop
  one), **0017** (the classification gate is validator policy, not store
  mechanism), **0018** (the key store is explicitly *not* a client of the
  storage contract), **0020** (the cipher, and its browser condition), **0021**
  (erasure mode and the default access scope are both settings, and erasure mode
  cannot be switched off while private entries exist), and **0022** (a readable
  private term compares by decrypted value, which is why the quad source supplies
  equality). No conflict with any.
- **Layer ownership.** **Key store**, **classifier** and **selector** contracts
  are `Varve.Store`, **layer 4**. Implementations driven by shapes or queries
  are **layer 5**; a cloud key vault is not a Varve package at all.
- **Analyzer rule.** None new, and one worth *not* inventing: "no plaintext in
  `log/`" is I9, a runtime property that §10 tests with a byte scan for
  high-entropy markers, not a shape an analyzer can see.
- **Open questions owned.**
  - **Q4** — how a shredded term appears in SPARQL results and serialisations:
    unbound, or an opaque IRI in a reserved scheme. Touches the serialisers and
    the server. **Due at milestone 9.**
  - **Q8** — key granularity. One key per data subject is assumed; data about
    two subjects in one term has no single owner. **Due at milestone 9.**
  - **Q9** — whether the structure surviving shredding counts as anonymous. A
    legal question; the engineering answer is the classifier's ability to make
    identifying links private. **Due at milestone 9.**
  - **Q7 is closed** by this decision: access is by key id, the scope defaults
    to `AllHistory`, and the default is a dataset setting because the call is the
    controller's.
