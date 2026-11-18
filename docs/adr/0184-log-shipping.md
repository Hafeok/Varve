# 0184 — Log shipping: a byte-identical copy of a dataset in another store, written by create-if-absent

## Status

**Accepted — filed unaccepted by Replication part 2 (#166), 2026-11-18**
(ADR 0066). Acceptance is the maintainer's act on the pull request. It
follows the plan approved on #97 on 2026-10-27, section 5, and the
maintainer's answers to its delta plan, recorded on #166 on 2026-11-16.

**Amends** by dated amendment:
- [ADR 0135](0135-the-log-as-chunk-objects.md): a target's consolidation is
  done by its shipper;
- [ADR 0160](0160-extent-deletion-by-reader-announcements.md): the shipper
  is a reader of the source, and the target's maintenance writer;
- [ADR 0164](0164-the-tier-1-follower.md): a follower of a replica;
- [ADR 0166](0166-varve-management-and-the-vocabulary.md): `varve:shipsTo`
  and `varve:ShippingTarget`;
- [ADR 0213](0213-cached-derived-data-is-verified-before-use.md): the
  shipper copies sets as they are, verifies before it copies, and never
  rewrites;
- [ADR 0113](0113-health-and-readiness.md) and
  [ADR 0119](0119-headers-and-the-problem-catalogue.md):
  readiness gains `shipping`, and the catalogue three problem types.

Records decision set [`log-shipping`](../decisions/log-shipping.md).

[ADR 0185](0185-promotion-across-copies-and-the-lineage-record.md) makes a
copy authoritative. This ADR only makes the copy.

## Context

Replication part 1 (ADRs 0179–0181) replaces a writer within one store. It
does nothing for the loss of the store, or of its region. For that a
dataset needs a copy elsewhere that a writer can continue from.

`Dataset.ShipAsync` (ADR 0083) makes a copy once, into an empty target. It
re-creates the segments by append and writes a fresh catalog, so its
objects are not the source's, and it cannot be continued.

What the object-storage backend gives a shipper to work with:

- **The log is write-once objects.** An open segment is a sequence of chunk
  objects, `log/NNNNNNNN.chunks/CCCCCCCC`. A sealed segment is consolidated
  into one object, `log/NNNNNNNN.seg`, after which its chunks are deleted,
  except its fence chunks, which are kept for ever (ADRs 0135, 0136).
- **A chunk's bytes carry its epoch,** in a 136-byte header that is hashed
  and chained to the previous chunk of the same segment. The chain does not
  cross a segment boundary: chunk 0 of every segment has no predecessor.
  What links one segment to the next is the log's own chain of commit
  headers.
- **Extent sets are immutable and never rewritten under a name.** A merge,
  a rewrite and a repair each take a fresh name (ADRs 0158, 0213).
- **The catalog is one object, replaced in place,** whose bytes start with
  a wrapper naming the epoch of the writer that fenced it (ADR 0139).
- **A set is deleted only when no reader record holds a catalog version
  that named it** (ADR 0160).

## Decision

**1. A shipping target is a declaration.** A dataset's graph in the system
dataset may say where it is shipped:

```turtle
<…/datasets/orders> varve:shipsTo [ a varve:ShippingTarget ;
    varve:store "https://acct2.blob.core.windows.net/replicas" ;
    varve:prefix "orders" ;
    varve:provider "azure-blob" ;      # an id on the compatibility page
    varve:shippedOn "varve-2" ] .      # optional
```

- A target is a store and a prefix, or `varve:directory` with a path.
- **The source is always an object store.** A dataset on the file backend
  cannot be shipped (the maintainer's answer D10). A directory has no
  reader records for decision 7, no epochs, and its lease admits one
  process. `varve apply` refuses the declaration there, and the guide
  points to `ShipAsync` and to backup and restore.
- A dataset may have several targets.

**2. The shipper is a role, by default on the epoch holder.**
- It moves with a handover. It is woken by the writer's own commits, so it
  adds no probe of its own.
- **It runs off the sequencer,** over a read-only open of the source of its
  own. A slow or failing target never delays a commit and never makes one
  wait (ADR 0244): the shipper is not a step of maintenance.
- With `varve:shippedOn`, the named instance ships instead, tailing as a
  standby does on `Varve:Replication:ShipInterval`.
- **No timer decides anything.** The interval sets how soon a round runs,
  never whether what it writes is correct.
- **Two shippers at once are harmless.** Every write below is a create of
  identical bytes or a conditional replace.

**3. Nothing on a target is ever overwritten.** Every log object is
written by create-if-absent of bytes identical to the source's. A create
that finds the name taken reads it back: equal bytes are success, and
anything else is `shipping-diverged`, naming the slot. The shipper then
stops for that target and changes nothing.

**4. What is shipped, in this order, each round:**

1. **The lineage record** (ADR 0185), by create-if-absent, before the
   first object of a new target.
2. **`log/MANIFEST`,** by create-if-absent.
3. **Log objects, in slot order.**
   - Each chunk the source still has is created on the target with its
     header, byte for byte.
   - When the source has a segment's `.seg`, the target's `.seg` is
     created from it. Only then are the target's chunks of that segment
     deleted, except its fence chunks. This is the target's consolidation,
     and the shipper does it (ADR 0135 decision 4).
   - Where the source consolidated a segment before the shipper had copied
     its chunks, the target gets the `.seg` and the fence chunks, which the
     source keeps for ever.

   **What a target holds of one segment** is therefore one of three
   things, each of which the source itself held at some moment, and each of
   which reads as the same log:

   | When the source consolidated | The target holds | Read by |
   |---|---|---|
   | not yet | the chunks shipped so far | the chunks |
   | **after its chunks were shipped** | first the chunks; then the chunks and the `.seg`; then the `.seg` and the fence chunks | the chunks until the `.seg` exists, then the `.seg` |
   | before its chunks were shipped | the `.seg` and the fence chunks | the `.seg` |

   - **In the middle case the shipper never deletes a chunk before the
     `.seg` is on the target,** so there is no moment at which a byte of
     the segment is in neither. A shipper that stops between the two
     leaves both, which is a state the source's own consolidation passes
     through.
   - **Before it creates the `.seg`, the shipper checks that the target's
     chunks of that segment are a prefix of it,** payload by payload. A
     difference is `shipping-diverged`, and nothing is deleted.
   - **A follower of the target reads on across it.** It holds what it
     adopted from the chunks, and a read below that finds the chunks gone
     and takes the `.seg`, as a follower of a source does (ADR 0135
     decision 4, ADR 0164).
   - The target's `.seg` is the source's `.seg` byte for byte. The chunks
     it replaces are gone from both stores, at different times.
4. **Extent sets** that the source's catalog names and the target lacks:
   every blob of the set that exists, by create-if-absent.
5. **The catalog, last.** The source's bytes, wrapper included, by create
   or by conditional replace against what the shipper read on the target.
   Only when every set it names is on the target and its cursor is at or
   below the shipped log tail. So a target's catalog never names a set or
   a slot the target does not have.
6. **Retired sets on the target are deleted** by ADR 0160's rule against
   the target's own reader records.

**Not shipped:** `writer`, `readers/` and `probe/`. They are the target's
own coordination state.

**5. A set is verified before it is copied, and never rewritten.**
- The shipper checks each blob as ADR 0213 does before its create. A
  damaged blob is not shipped.
- That catalog version waits until the source's writer has rewritten the
  set under a fresh name, and readiness says
  `shipping: waiting-on-damaged-set`.
- A rewrite for an older reader (ADR 0213 point 7) is the source writer's.
  Its result reaches the target as a new set.

**6. Resume is by the target's tail.** At its start, and after any
failure, the shipper lists the target's `log/` once.
- It verifies every segment of the target that is still chunks as an open
  does: each chunk's header and bytes, and the chain within the segment. A
  chunk of the target that does not verify or does not follow is
  `shipping-diverged`: it says something about the target, never about
  the source's log.
- **It compares the target's tail with the source at the same slot:** the
  source's chunk if it still has it. If the source has consolidated that
  segment, the target's chunks are checked against the `.seg`, range by
  range against the content hash in each header, before the target's
  `.seg` is created.
- **Across a segment boundary the chunk chain says nothing, and the
  shipper does not read commits.** What holds there is two things:
  - every object on the target was created equal to the source's object
    of that name, or was refused, so a target written only by shippers of
    this source holds a prefix of the source's log;
  - **the commit header chain, which is the log reader's check.** Every
    follower of the target runs it as it replays, and so does a
    promotion, which replays what it continues (ADR 0185).
- A match continues from the next slot. Anything else is
  `shipping-diverged`.
- The shipper keeps no state of its own between rounds, and nothing per
  commit (ADR 0240). What it holds in memory it can read again from the
  target.

**7. The shipper is a reader of the source.**
- It announces one record per target, `readers/<reader>-ship-<target>`,
  where the last part is a short digest of the target's store and prefix.
  Two targets never share an interval.
- It announces the catalog version it is about to copy before its first
  read of a set, by announce then confirm, and moves the interval forward
  only after the target's catalog has been replaced.
- The log needs no hold. A chunk is deleted only after its `.seg` exists,
  and the shipper reads that instead.
- A shipper that has stopped holds its sets until it resumes or its record
  is forgotten. `retention` shows it (ADR 0162).

**8. The shipper is the target's maintenance writer, and never opens it as
a dataset.**
- It consolidates the target's chunks and deletes its retired sets. It
  never merges: sets arrive merged.
- It holds no memtable for the target and writes no run there.
- **Before promotion nothing but the shipper writes under a replica's
  prefix.** A replica is never opened for write (ADR 0185), and a
  follower's open flushes nothing (ADR 0244 point 2).

**9. A target is a verified store, or a directory.**
- At run time the shipper needs a verification record at the target's
  store (ADR 0183) whose suite is at or above this build's lowest, and the
  declared `varve:provider` verified on the compatibility page compiled
  into the build. Otherwise it stops with `409 store-unverified`, naming
  the page.
- `varve apply` checks the page, which is all it can see.
- **A directory target is exempt.** There, every write is an append of the
  next bytes after the tail has been checked, under the directory's lease.
  Byte identity means its segment files equal the concatenation of the
  source's chunks.
- **A directory target is not followed while it is shipped to.** Its gate
  is byte identity, and a dataset opened on it once the shipper has let
  go.

**10. A target that holds another dataset is refused.** A prefix whose
manifest names another dataset, or that has a log and no lineage naming
this source, is `409 shipping-target-not-empty`. A copy made by `ShipAsync`
is such a prefix.

**11. A target's failure is never the source's.**
- The shipper never abdicates, never releases, and never touches the
  source's writer record.
- It retries with its own back-off, one instance per target, shaped as the
  claim hold-off is and independent of it (ADR 0163).
- Readiness reports, per target: its state, the shipped log position, and
  **the shipped catalog's position beside it** (the maintainer's answer
  D11). It never turns the instance unready.
- **The shipper never makes the source checkpoint.** The source's
  checkpoint policy bounds how much log a promoted target replays at its
  first open, and the guide says so.

**12. A commit's metadata is shipped intact.** Shipping copies bytes and
re-encodes nothing, so each commit keeps its agent and its header
attachments: the management position (ADR 0165) and the idempotency key
(#132).

**13. A source fleet's system dataset is shipped ahead of the datasets
that refer to it** (the maintainer's answer D3, ADR 0185). In a round, the
`_fleet` target is brought to the source's head before any dataset commit
that records a later management position is shipped.

**14. What governs a read of a copy is the fleet that serves it.**
Grants, classifier bindings, TBox bindings and shapes bindings are
declarations in a fleet's own system dataset (ADR 0166). None is shipped
as something that governs access. A replica is only as protected as the
declarations made where it is served.

**15. Erasure mode across copies** is an open question owned by #203. A
shipped log and a shipped checkpoint carry only ciphertext of private
terms (spec §9, I9). The key store is never inside a dataset directory, so
it is never shipped. Erasure must take effect on every copy (I10). Before
erasure mode may be enabled on a dataset that has a `varve:ShippingTarget`,
or a target declared on a dataset in erasure mode, milestone 9 part 2 must
meet one of:
- **(a) the copies share the key store's destruction:** every copy
  resolves its subject keys from a key store whose destruction of a key is
  destruction for every copy, its backups included, before the `Erasure`
  commit is acknowledged; or
- **(b) erasure mode refuses a `varve:ShippingTarget`,** at apply time and
  at run time, with a problem naming this ADR.

A key store per copy, destroyed when that copy adopts the shipped
`Erasure` commit, meets neither: a target that nothing follows never
adopts it. Until #203 chooses, `varve:erasureMode` stays reserved.

## Alternatives considered

- **Extend `ShipAsync` to continue.** Its output is not the source's
  objects, so a second shipper, or a resumed one, has nothing to compare
  against. Byte identity is what makes two shippers harmless.
- **Ship commits through the change feed.** The target would then write
  its own log, with its own positions and header hashes. A promotion could
  not prove what it continues, and a commit's attachments would have to be
  carried by a second protocol.
- **Ship the writer record.** The target would then appear to have the
  source's writer. Its coordination state is its own.
- **Let the shipper rewrite a damaged set on the target.** Two stores
  would then hold different bytes under one name, which is the divergence
  this ADR exists to refuse.
- **A shipper state file,** to resume without listing. It would be a third
  thing to keep consistent with two stores. The target's tail is the
  state.
- **A directory as a source.** It needs a rule that protects a set from
  deletion while it is read, which no ADR has for a directory.
- **Make the source checkpoint when the shipped catalog falls behind.** It
  would bound a promoted open's time, and it would let a target's state
  decide when the source works (declined as D11).
- **Verify by provider name alone.** Rejected in ADR 0183.

## Consequences

- **Tier 3's RPO is the shipping lag at the moment of loss:** commits the
  source acknowledged and had not yet created on the target. Readiness
  shows it as positions.
- **A target costs the source retained sets** while its shipper is behind
  or stopped.
- **A promoted target's first open replays from the last shipped
  catalog.** Its memory is bounded (ADR 0244). Its time and local disk
  follow that distance.
- **Shipping to Azure Blob Storage needs that provider's result record,**
  which the optional leg writes (ADR 0183).
- **A follower of a replica is bounded by the target's catalog,** so the
  shipper ships the catalog in every round in which the source's moved.

## Checks

- **Checked against the accepted ADRs.** Touches:
  - **0083**: `ShipAsync` stands, as the one-shot bootstrap.
  - **0134, 0183**: every write is one of the five operations; a target is
    a verified store.
  - **0135, 0160, 0164, 0166, 0213**: amended.
  - **0136, 0179**: the shipper never claims, on either side.
  - **0137**: the shipper reaches the target as the instance's identity.
  - **0139, 0158, 0159**: sets and the catalog are copied as written.
  - **0162**: `retention` shows what a shipper holds.
  - **0163**: the back-off is not the claim hold-off.
  - **0165, 0168**: positions and attachments are shipped intact; a hop
    passes `Idempotency-Key` through.
  - **0217**: the lineage record is read by its rule.
  - **0240, 0244**: nothing per commit; no commit waits on a target.

  No conflict.
- **Layer ownership.** The shipper's copy and its checks are
  `Varve.Store.ObjectStorage`, layer 5. The declaration is
  `Varve.Management`, layer 4. The role and readiness are `Varve.Server`.
- **Analyzer rule.** None.
- **Tests.**
  - Tier 3 under failure injection, over in-memory stores: the shipper
    crashed at every step, target faults, and the source's consolidation
    and maintenance racing it. A follower of the target equals the
    reference model at its reported position.
  - **Byte identity:** every object that both stores hold is identical,
    and the log read from the target equals the source's, byte for byte, up
    to the shipped position. Checked at every step of every schedule, not
    only at its end.
  - **A segment consolidated after its chunks were shipped:** a follower of
    the target that adopted the chunks keeps reading the same log while
    the shipper creates the `.seg` and deletes the chunks, at a position
    inside that segment and at one after it, and with the shipper stopped
    between the create and the deletes.
  - The same for a segment the source consolidated before its chunks were
    shipped: a follower that starts afterwards reads the same log.
  - Two shippers at once ship one copy.
  - A target that diverged, a target that holds another dataset and an
    unverified target are each refused, with nothing written.
  - A commit waits for no target, with the target stalled.
  - Between Azurite and RustFS, and from each to a directory, in
    `shipping (services)`.
- **Open questions owned.** Erasure mode across copies: #203.
