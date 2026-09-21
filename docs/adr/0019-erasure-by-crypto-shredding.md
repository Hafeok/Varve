# 0019 — Erasure by crypto-shredding

## Status

Accepted. 2026-09-21.

Records [`docs/spec/log-and-projection-model.md`](../spec/log-and-projection-model.md)
§9, T4, I9 and I10. The **cipher** is not decided here; see [ADR 0020](0020-cipher-for-erasure-mode.md).

## Context

`docs/brief.md` lists "GDPR-style hard deletion in an append-only model" as a
tension to be resolved by ADR rather than by accident. It is the sharpest
contradiction in the whole design: the architectural thesis is that the log is
never rewritten, and the legal requirement is that data about a person can be
made to go away.

§2 makes it sharper still. The dataset directory is copied, backed up,
synchronised and checked into version control by people and tools the store
cannot reach. **Any erasure mechanism that works by removing bytes from the log
erases nothing that has already left the building** — and the design assumes
copies have.

## Decision

**Erasure is crypto-shredding: the data stays, the key is destroyed.**

### Erasure mode is per-dataset and off by default

When off, no private ids exist, no key store is consulted, and nothing in this
decision costs anything beyond one reserved id class. A dataset that holds no
personal data pays nothing, which is what makes the mechanism acceptable to
carry in the core at all.

### Three contracts, all owned by `Varve.Store`

**Key store.** Create a key for a data subject; resolve subject to `KeyId`;
fetch key material by `KeyId`; destroy a key.

> **Implementations live outside the dataset directory by construction, and the
> file backend refuses a key store path inside it.** This is the load-bearing
> rule of the whole design, and it is the same rule as never checking a signing
> key into the repository it signs. A key inside the directory is copied with
> the directory, and then destroying it locally destroys nothing.

The key store is **the only mutable, deletable component in the system**. It
needs its own backup policy, and that policy must itself honour destruction — a
backup that restores a destroyed key un-erases a person.

**Classifier.** Given the pending operations and the pinned quad source, assign
term occurrences to data subjects. Free of SPARQL and SHACL (ADR 0005);
implementations derived from annotated shapes or from queries are **layer 5**
integrations.

**Selector.** From a quad source and a data subject to a set of quads. Used for
access requests, and by `T4` to clean the current graph.

### `T4 Erase(subject)`

1. Optionally commit a normal `Data` commit retracting the selector's result
   from `G_head`, so the current graph holds no dangling structure.
2. Append an `Erasure` commit whose metadata carries the `KeyId`, agent and
   cause. Empty delta, exempt from I4.
3. Destroy the key in the key store.

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

**Structure survives.** Shredded ids, the predicates between them, and links
from a shredded node to canonical terms — a shredded subject that `worksFor` a
public organisation. This is a known limitation and it is stated rather than
finessed: re-identification from structure is a documented attack on anonymised
graphs. Where a link is itself identifying, the classifier can make the object
occurrence private too. **Whether the residue counts as anonymous is a legal
question, not a technical one.**

**A party that was given a key and keeps it is outside technical control.**
Towards such a party, erasure is a contractual obligation. The store can say
what it destroyed; it cannot say what someone else kept.

### As-of reads become *more* stable, not less

Draft 1's "stable modulo erasure" is withdrawn. As-of reads are structurally
stable for ever. What changes after an erasure is the *readability* of private
terms — at every position at once, because there is only one key.

## Alternatives considered

- **Rewrite the log: remove or redact the affected commits.** The obvious
  reading of "delete". Rejected by §2 above all: copies exist that the store
  cannot reach, so a local rewrite is theatre. It also destroys the header chain
  (ADR 0014), invalidates every position anyone has stored, and turns every
  as-of read and every subscriber resume point into a lie. An append-only log
  that is sometimes rewritten has the costs of both models and the guarantees of
  neither.
- **Id scrambling, or redacting the dictionary entry only.** Keep the quads,
  drop or randomise the term's dictionary entry. Cheap, needs no cryptography,
  and is what "we deleted the personal data" often means in practice. Rejected
  on two grounds, the second decisive. It is **pseudonymisation, not erasure**:
  the structure and the original bytes may still be recoverable, and the
  regulators' distinction between the two is exactly the one being elided. And
  **shared interned literals cannot be redacted per subject** — a canonical
  entry for a city, a job title or a date is referenced by thousands of
  subjects, so there is no per-subject edit to make. That is precisely why
  private terms are a separate id class that is *not interned*: the mechanism
  had to make per-subject removal possible before it could be asked for.
- **Encryption at rest, or full-disk encryption.** Protects a stolen laptop.
  Does nothing here: the threat is a legitimate copy of the directory, which is
  decrypted by whoever holds the volume.
- **One key per dataset.** Simplest key management by far. Rejected: erasing one
  subject would erase everyone, which makes the mechanism unusable for the case
  it exists for.
- **Erase by dropping projections and rebuilding.** Addresses the derived copies
  and none of the log. Handled the other way round in ADR 0016: projections
  carry the `KeyId` and purge, *and* the log is shredded.
- **Not supporting erasure at all**, and telling users to segregate personal
  data into a separate dataset they can delete wholesale. A legitimate design,
  and cheaper. Rejected because "put the personal data somewhere else" is not
  available to a graph database — the value of the graph is the joins, and the
  joins are where the personal data is.

## Consequences

**The key store is the single point of failure for the entire guarantee.** A key
store that is backed up carelessly, or whose backups outlive a destruction,
silently voids I10. This is an operational requirement that no amount of code in
`Varve.Store` can enforce, and it should be documented as prominently as the
guarantee itself.

**Classification happens at write time, and what is missed cannot be recovered.**
Unclassified personal data that reaches the log can never be erased, because
there is no key to destroy. The classification gate (ADR 0017) reduces this and
cannot close it: it cannot see personal data inside free text under a property a
shape has declared not personal.

**The commit's agent can itself be erased.** §1 requires that the agent, and any
other value that can identify a person, is a `TermId` rather than an inline
string. That is what lets the person who made a commit be a private term — an
easy thing to get wrong once and never be able to fix.

**Erasure mode is a creation-time decision in practice.** Turning it on later
does not retroactively make earlier terms private, so a dataset that may ever
need erasure should be created with it on. Whether the store should refuse to
enable it on a non-empty dataset, or enable it going forward with a stated
limitation, is an implementation decision for whoever builds it.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0018). Touches
  **0005** (all three contracts are SPARQL-free and SHACL-free; shape-driven and
  query-driven implementations are layer 5, the same pattern 0005 sets for
  SPARQL Update), **0010** (the `Erasure` commit is the one exemption from I4),
  **0012** (private ids are random and not interned, and *why*), **0016**
  (projections tag derived plaintext with a `KeyId` and purge on the `Erasure`
  commit; filters never drop one), **0017** (the classification gate is validator
  policy, not store mechanism), and **0018** (the key store is explicitly *not* a
  client of the storage contract). No conflict with any.
- **Layer ownership.** The **key store**, **classifier** and **selector**
  contracts are owned by `Varve.Store` at **layer 4**, free of SPARQL and SHACL.
  Implementations driven by shapes or queries are **layer 5**. Key store
  implementations are layer 5 or outside the repository entirely — a cloud key
  vault is not a Varve package.
- **Analyzer rule.** None new, and one worth *not* inventing: "no plaintext in
  `log/`" is I9, and it is a runtime property that §10 tests with a byte scan for
  high-entropy markers, not a shape an analyzer can see.
- **Open questions owned.**
  - **Q4** — how a shredded term appears in SPARQL results and serialisations:
    unbound, or an opaque IRI in a reserved scheme. Touches the serialisers at
    milestone 5 and the server at milestone 7. **Due before erasure mode ships,
    and no later than milestone 7.**
  - **Q7** — whether an access request defaults to `G_head` or to every quad
    ever asserted. Retained history is still processing. **This needs legal
    input and is not mine to resolve.** Due before erasure mode ships.
  - **Q8** — key granularity. One key per data subject is assumed; data about
    two subjects in one term, such as a joint account label, has no single
    owner. Due before erasure mode ships.

  All three are due against a milestone that does not yet exist on the roadmap;
  placing erasure on it is part of this milestone's consistency pass.
