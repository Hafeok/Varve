# 0184 — Log shipping: a byte-identical copy of a dataset in another store, written by create-if-absent

## Status

**Accepted — filed unaccepted by Replication part 2 (#166), 2026-11-18**
(ADR 0066), and accepted by the maintainer on 2026-11-24, written by his delegate
(ADR 0239). It
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
  readiness gains `shipping`, and an apply's answer a notice header;

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
n:orders a varve:Dataset ; varve:datasetName "orders" ;
    varve:storage varve:ObjectStorage ;
    varve:shipsTo n:replica .

n:replica a varve:ShippingTarget ;
    varve:store "https://acct2.blob.core.windows.net/replicas" ;
    varve:prefix "orders" ;
    varve:provider "azure-blob" ;      # an id on the compatibility page
    varve:shippedOn "varve-2" .        # optional
```

- **A target is a named node of the dataset's graph.** A management graph
  holds no blank node (ADR 0175), so the target is not written inline.
- **An address carries no credential** (ADR 0037): `https://…` or
  `s3://…`, with no user info, no query and no fragment. A SAS token in an
  address is refused when the declaration is read.

- **A target is a store and a prefix. Anything else is refused at apply.**
  A directory is not a target (the maintainer's ruling of 2026-11-20 on
  #166): `varve:directory` is not in the vocabulary. Directory targets are
  #391, after 1.0.
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
- **How an instance reaches an S3-compatible target is its own
  configuration:** `Varve:Replication:Stores` gives the endpoint, the
  region and the STS endpoint for an `s3://{bucket}` address, which a
  bucket's name does not carry. It holds no key (ADR 0037). An Azure
  target needs no entry.
- **On the writer a round runs only when the dataset's head or its catalog
  has moved** since the last complete round, so an idle dataset costs a
  shipper no request of either store. A segment the source consolidates
  while idle, and a retired set, reach the target with the next commit.
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
   - **A target advances only through its next chunk slot.** That slot is
     the one create a shipper and a promotion's fence can both want (ADR
     0185 decision 4), so the store decides between them, as it decides
     between two writers (ADR 0136 decision 3). A `.seg` is therefore
     created only for a segment the target holds sealed in chunks:
     - a segment whose sealing chunk was shipped is sealed already;
     - **for a segment the source consolidated before its end was
       shipped,** the sealing chunk is gone from the source. The shipper
       then creates, at the target's next slot of that segment, first the
       source's kept fence chunks while they are that slot, and then **one
       chunk it composes: the rest of the segment's bytes, sealing it.**
       Its header is the shipper's, with the highest epoch the target's log
       shows; its bytes are the source's. A reader verifies it as any
       chunk. Then the `.seg` is created, and the chunk is deleted with the
       segment's other data chunks.
     - **If that create finds a promotion's fence,** the shipper stops as
       `shipping-diverged`, having created neither the chunk nor the
       `.seg`. If it finds another shipper's chunk of the same bytes, it
       carries on.
     - **What it costs:** the bytes of a segment the source consolidated
       before it was shipped are uploaded twice, once in that chunk and
       once as the `.seg`. That is every segment of a target that is being
       seeded or has fallen a segment behind, and none of one that keeps
       up.
     - **Why the `.seg` is not created directly.** ADR 0185 decision 4
       refuses a promotion whose fence is not at the end of the log, but
       that check runs once, as the promotion's last write. A shipper that
       had listed the target before the promotion could create the `.seg`
       after it: the name is free, so nothing refuses the create. The
       object would then hold commits of the source that were not on the
       target before the fence, at positions the promoted writer has
       written. Run with the composed chunk turned off, that is what
       happens. So the chunk stays, and the cost with it. Removing the
       second upload needs another way to take the slot, and is #403,
       after 1.0.

   **What a target holds of one segment** is therefore one of three
   things, each of which the source itself held at some moment, and each of
   which reads as the same log:

   | When the source consolidated | The target holds | Read by |
   |---|---|---|
   | not yet | the chunks shipped so far | the chunks |
   | **after its chunks were shipped** | first the chunks; then the chunks and the `.seg`; then the `.seg` and the fence chunks | the chunks until the `.seg` exists, then the `.seg` |
   | before its end was shipped | the chunks shipped so far; then those and the chunk that seals it; then the `.seg` and the fence chunks | the chunks until the `.seg` exists, then the `.seg` |

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
6. **Retired sets on the target are deleted,** after the catalog. A set
   goes only when all of these hold:
   - the target is still a replica (ADR 0185);
   - the target's catalog does not name it, and its sequence is below that
     catalog's version;
   - no reader record of the target holds a version that could name it
     (ADR 0160). The shipper does not know at which version the set was
     removed, so it takes the target's catalog's own version, which is
     never too early;
   - **the source has deleted it.** A shipper that holds a catalog naming
     the set keeps it on the source by its reader record (decision 7). So
     a set that another shipper of this target is still copying is never
     removed under it.

   The target therefore keeps a retired set at least as long as the source
   does.

**Not shipped:** `writer`, `readers/` and `probe/`. They are the target's
own coordination state.

**`proof/` is neither shipped nor touched.** The name is reserved beside
`log/` and `derived/` (ADR 0216, amended 2026-11-18, #340): no release
writes it yet, and every reader ignores it and never deletes it. The
shipper names what it lists, on both stores: `log/`, one segment's chunks,
and the extents the catalog names. Whether a copy carries `proof/` is
#292's to decide.

**5. A set is verified before it is copied, and never rewritten.**
- **The shipper checks each blob whole before its create,** as a reader
  of the set checks the blocks it reads (ADR 0213): the header, the
  directory under the header's hash, and then every block that carries a
  check. `ExtentBlobCheck.Verifies` in `Varve.Store` is that check, and it
  is tested over every blob of a set with each byte changed in turn.
- **A damaged blob is not shipped,** and the catalog that names its set is
  not published on the target that round. The log is shipped as before:
  a round ships the log first.
- That catalog version waits until the source's writer has rewritten the
  set under a fresh name, and readiness says
  `shipping: waiting-on-damaged-set`, with the blob and the position the
  log has reached. It has
  [a page](../operator/shipping/waiting-on-damaged-set.md). The shipper
  reads the blob again each round.
- **A blob from before the checks** (ADR 0213 point 7) has none below its
  directory. It is read whole all the same, and passes when every block
  decodes, which is all a reader of it knows.
- **What it costs:** one pass over each blob, on the shipping instance,
  once per blob per target. The blob is already on local disk for the
  copy.
- A rewrite for an older reader (ADR 0213 point 7) is the source writer's.
  Its result reaches the target as a new set.

**6. Resume is by the target's tail.** At its start, and after any
failure, the shipper lists the target's `log/` once.
- It verifies every segment of the target that is still chunks as an open
  does: each chunk's header and bytes, and the chain within the segment. A
  chunk of the target that does not verify or does not follow is
  `shipping-diverged`: it says something about the target, never about
  the source's log.
- **The shipper checks each source chunk before its create:** the
  header's own hash and the hash of its bytes, and that it follows the
  chunk before it. A chunk that fails is not shipped, and nothing is
  written for it. Readiness says `shipping: source-chunk-damaged`, with
  the slot, and has
  [a page](../operator/shipping/source-chunk-damaged.md). The shipper
  reads the chunk again each round, since a read can be damaged where the
  object is not.
- **It compares the target's tail with the source at the same slot:** the
  source's chunk if it still has it and the two are equal. If the source
  has consolidated that segment, the target's chunks are checked against
  the `.seg`, range by range against the content hash in each header,
  before the target's `.seg` is created. The target's last chunk may then
  be the one a shipper composed to seal the segment (decision 4).
- **The source always has every slot.** No release truncates a log: a
  log keeps every commit from position 1, as a segment's chunks or as its
  `.seg` (`Dataset.HeaderHashAsync` relies on the same). So "the source
  no longer has it" has one meaning here, a consolidation, and the bytes
  are in the `.seg`. **When a release can remove log history, this
  decision is wrong as written, and #292 owns the question.**
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

**9. A target is a verified store, and what verifies it is a record at the
store.**
- **The gate is at run time, and it is the record.** The shipper needs a
  verification record at the target's store (ADR 0183) whose suite is at
  or above this build's lowest. Without one it writes nothing to the
  target and reports the state `store-unverified` on readiness, with what
  to run. It tries again with its back-off, so a store verified afterwards
  is shipped to without a restart.
- **The compatibility page is a notice, never a refusal** (the maintainer's
  ruling of 2026-11-20 on #166). `varve apply` accepts a target whose
  provider the page does not list as verified. Its answer says in-band, per
  such target, that the project has not tested that provider and that
  shipping starts once the store holds a verification record:
  - the response carries one `Varve-Notice` header per target,
    `shipping-provider-untested; dataset=…; provider=…; state=…`;
  - `varve apply` prints each as a sentence, and exits 0.
- **Why.** The page says what the project tested. The record says what was
  run against this store. An operator who ran the suite against a store the
  project never tried has the evidence the gate asks for.
- **The page's states are compiled into the server** for the notice, from
  `src/Varve.Server/object-store-providers.txt`, which
  `eng/object-stores.cs` writes with the page and `eng/status.cs` holds to
  it.
- **No request is answered `store-unverified`.** It is a readiness state
  only, as `shipping-diverged`, `shipping-target-not-empty` and
  `replica-promoted` are: each is met only by the shipper, which answers no
  request, and each has its detail on readiness and a line in the log.

**10. A target that holds another dataset is refused.** A prefix whose
manifest names another dataset, or that has a log and no lineage naming
this source, is the state `shipping-target-not-empty`. Nothing is written
to it, and the shipper stops for that target. A copy made by `ShipAsync` is
such a prefix.

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
- **Where the dataset is a promoted copy,** each earlier fleet's system
  dataset goes the same way, from `fleets/<id>/` on the shipping fleet's
  store (ADR 0185 decision 8).
- **A fleet that ships always has its system dataset on object storage.**
  A shipping target is declared on a dataset of `varve:ObjectStorage`
  (decision 1), and an instance serves such a dataset only when it is
  configured with an object store. That instance's fleet keeps its system
  dataset on the same store, under `_fleet/`. The system dataset is on the
  file backend only in a fleet of one with no object store, and such a
  fleet has no dataset a target can be declared on and runs no shipper.
  So this decision never meets a system dataset on the file backend.

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
  - A target that diverged, a target that holds another dataset and a
    target whose store holds no verification record are each refused, with
    nothing written.
  - An apply of a target on a provider the page has not verified is
    applied, with its notice.
  - A commit waits for no target, with the target stalled.
  - Between Azurite and RustFS, in `shipping (services)`.
- **Open questions owned.** Erasure mode across copies: #203.

## Amendment, 2026-11-24 — a store named over plain http on a loopback host

Filed by Replication part 2 (#166) (ADR 0066). It adds no ruling and changes
no statement, and was read by the maintainer on 2026-11-25.

**What was found.** Decision 1 says an address is `https://…` or `s3://…`.
The `shipping (services)` job ships between Azurite and RustFS, and Azurite
is reached at `http://127.0.0.1:10000/devstoreaccount1/{container}`. A
declaration that names it, as a target or as a copy's source (ADR 0185
decision 10), was refused at apply. So neither direction of the job could
be declared.

**The change.** `varve:store` may be `http://…` when its host is a loopback
host: `localhost`, an address in `127.0.0.0/8`, or `[::1]`, with an optional
port. Every other `http://` address is refused as before, and so is one
with user info, a query or a fragment.

- **Why this and not more.** It is what the Azure adapter already reaches
  over plain http: a loopback host and nothing else
  (`AzureBlobStore`). A declaration can now name what the adapter can open.
- **What it is for.** A development emulator beside the instance. No
  deployed store is named this way.
- **Authentication is unchanged.** An address still carries no credential
  (ADR 0037).

**Checks.** `ShippingTargetTests`, for a target and for a copy's source.
`ShippingServiceTests`, the `shipping (services)` job: a fleet on Azurite
ships to RustFS and a fleet on RustFS ships to Azurite; each copy is served
by a second fleet, renders the events of commits the source fleet wrote,
refuses a write, is promoted with `varve promote`, and is written; the old
source's shipper stops as `replica-promoted`.

## Amendment, 2026-11-24 — a shipper follows another shipper's chunk at the sealing slot

Filed by Replication part 2 (#166) (ADR 0066). It adds no ruling and changes
no statement, and was read by the maintainer on 2026-11-25:
`TheShipperRunsOffTheSequencer` already says two shippers at once are
harmless.

**What was wrong.** Decision 4 says of the chunk a shipper composes to seal
a segment: "If it finds another shipper's chunk of the same bytes, it
carries on." The code carried on only for the same sealing chunk. With two
shippers at once there is another chunk it can find there: one of the
source's own, which the other shipper copied before the source
consolidated the segment. It holds the first of the bytes the composed
chunk would hold, and not all of them. The shipper reported
`shipping-diverged`, and nothing had diverged.

**The change.** At the sealing slot a shipper follows any chunk that is not
a fence, chains to the chunk before it, and holds the source's bytes at its
offset, as the consolidated segment has them. If that chunk does not seal
the segment, the shipper composes the rest at the slot after it. A chunk
with no bytes, or with other bytes, stops it as before: that is a
promotion's fence, or a divergence.

**Checks.** `LogShipperTests`,
`a_shipper_that_finds_another_shippers_chunk_of_the_source_at_the_slot_seals_through_the_next`,
which fails without the change. The order was met once by
`a_shipper_started_again_resumes_from_the_targets_tail_and_two_at_once_ship_one_copy`
in a whole pipeline run.
