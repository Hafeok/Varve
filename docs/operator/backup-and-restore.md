# Back up and restore

## What the files are

A file dataset is one directory under `Varve:DatasetsRoot` (ADR 0072,
`docs/spec/storage-format.md`):

| Path | What | Loss means |
|---|---|---|
| `log/` | the source of truth: a manifest and append-only segments, **format version 1**, never rewritten, read for ever | the dataset |
| `derived/` | the projection's runs, the checkpoints — `extents/` and the `catalog` that names them (ADR 0139) — the commit index and bulk-load spills: rebuilt from the log on a cache miss | a slower open, and as-of reads that replay the log |
| `derived/LOCK`, `derived/LOCK.owner` | the lease of the process that holds the dataset, and whose it is (ADR 0075) | nothing; a stale one is taken over |

**A backup is a copy of the log.** `log/` alone is enough; `derived/`
saves the replay. The two are kept by different rules
([formats](../formats/README.md), ADR 0216): log format 1 is *read forever*,
so every later release opens a copy taken today, and everything under
`derived/` is *rebuilt*, a cache any release reads or writes again from the
log to the same answers. Every commit is flushed to the device before it is
acknowledged (`Synchronised`, ADR 0073), so the log on disk is never behind
what a client was told.

**No key is ever in a copy.** Varve holds no keys: ADR 0074 reserves private
ids and refuses a key store, and erasure by destroying keys (ADR 0019) is
not built (#203). A copy holds every term in the clear, as the dataset does,
so treat it as the data it is.

A dataset in an object store is the same log in other objects, under its
prefix (`docs/spec/storage-format.md` §§6–8, ADR 0139):

| Object | What | Rule | In a copy |
|---|---|---|---|
| `log/MANIFEST`, `log/NNNNNNNN.chunks/CCCCCCCC`, `log/NNNNNNNN.seg` | the log: its manifest, write-once chunks, and segments consolidated from them (ADR 0135) | read forever | **always** |
| `derived/extents/…`, `derived/catalog` | the checkpoints' extent sets, and the catalog that names them (ADR 0139); the rest of `derived/` is in each instance's local cache | rebuilt | yes, to save the replay |
| `writer` | who holds the writer epoch, and every claim (ADR 0136) | read forever | **never**, for a restore target |
| `readers/<id>` | what each instance that reads the store announces it reads (ADR 0160) | read forever | **never**, for a restore target |
| `proof/` | reserved; nothing writes it | reserved | nothing to copy |

The writer record and `readers/` describe instances, not the dataset. A
copied writer record names the source's holder and its internal address,
which a forwarder routes writes to (ADR 0181), and an epoch the target never
claimed. A copied reader record holds, in the target, every set its reader
announced, so the target's writer deletes none of them until the record is
forgotten, and the bytes stay retained (see [Readers and retained
bytes](#readers-and-retained-bytes)). Without either, the first writer to
open the copy claims an epoch and writes a record of its own, and each
instance that reads it announces itself.

**A full disk fails the write that met it, and nothing that was
acknowledged** (ADR 0214). The commit that needed the space is refused with
`507` and the problem type `storage-full`, and its client is not told it
committed. The dataset keeps serving reads at the position it had, and
`/health/ready` stays ready, naming the dataset under `writesRefused`. Free
space; nothing needs restarting. The next write recovers the dataset's log
itself, as an open would, and commits. Until there is space, each write is
refused `storage-full` again. A full disk while the dataset opens fails the
open; the next open with space succeeds. A checkpoint or maintenance write
that meets a full disk fails on its own, and a later round retries it. Every
release is gated on these outcomes, on Linux and Windows, at every allocation
the backend makes.

## Taking a copy

**Hot**: copy the directory while the server runs. The store is built for
it: a copy taken while the dataset was being written opens at its last closed
commit, and a torn tail is ignored (ADR 0071, the failure-injection suite).
A snapshot taken by the file system or the volume at one instant is the
cleanest hot copy.

**Cold**: declare the dataset closed first, so that the copy is exactly a
position. `$OPERATOR` is a token for the management audience (ADR 0163):

<!-- ci: ci.yml build; eng/ci.cs test-server — BackupRestoreTests: closed, log/ alone and log/ with derived/ copied, declared open -->
```sh
curl -X PUT -H "Authorization: Bearer $OPERATOR" -H 'Content-Type: application/json' \
  -d '{"state":"closed"}' https://varve.example/manage/datasets/people/state
cp -a /var/lib/varve/people /backup/people-$(date -u +%Y%m%dT%H%M%SZ)
curl -X PUT -H "Authorization: Bearer $OPERATOR" -H 'Content-Type: application/json' \
  -d '{"state":"open"}' https://varve.example/manage/datasets/people/state
```

The copy carries `derived/LOCK` and `derived/LOCK.owner` too; they are the
closed server's, and the process that opens the copy takes them over.
`cp -a /var/lib/varve/people/log /backup/people-…/log` copies the log alone,
which is the smaller copy and restores the same dataset.

Record the position with the copy: `varve info /backup/people-…` reads it
from the copy itself, or `GET /manage/datasets/people/status` before the close.

### In an object store

A copy of a dataset in an object store is every object under its prefix's
`log/` and `derived/`, and nothing else (the table above). With the store's
own tools, into another container or prefix:

<!-- ci: ci.yml build; ci.yml storage-services — ObjectCopyTests and AzuriteObjectCopyTests copy the same objects with the store's own calls, on the in-memory store and on Azurite; azcopy itself is not run -->
```sh
azcopy copy 'https://account.blob.core.windows.net/varve/people/log' \
  'https://backup.blob.core.windows.net/varve/people-20261119' --recursive
azcopy copy 'https://account.blob.core.windows.net/varve/people/derived' \
  'https://backup.blob.core.windows.net/varve/people-20261119' --recursive
```

`aws s3 sync` with the two prefixes does the same on S3. Take it with the
dataset declared closed, as above, and the copy is exactly a position.

A copy taken while the writer runs, and any **geo-replicated or
asynchronously replicated copy** (the store's own replication; see [Azure
Container Apps](azure-container-apps.md)), holds objects in an order the writer did
not choose, so it can catch a swap half done:

- **a catalog whose newest set has not arrived**, or one whose sets were
  merged and deleted before the new catalog arrived. A set the catalog names
  and the copy lacks ends the chain there, a cache miss: the copy opens with
  every answer, read from the log above the sets it holds (ADR 0139);
- **a log without its newest chunks.** The copy opens at the last commit it
  holds whole, as a copy of a directory taken mid-write does. What a client
  was told committed after that is not in it, so a replica's lag is what
  a restore from it loses.

Both are what `ObjectCopyTests` opens. A copy missing a chunk in the middle
of the log, with later ones present, is not tested (#363); do not restore from one.

`Dataset.ShipAsync` (ADR 0083) copies a dataset from code, into a directory
or a container, at an exact position: the log up to it and the newest
checkpoint at or below it. When there is no such checkpoint it **writes one
into the source first**, so it needs the source open for write: the writer,
not a follower or a read-only open. There is no `varve ship` yet.

## Checkpoints

A checkpoint bounds the replay an open and an as-of read do (ADR 0078):

<!-- ci: ci.yml build — CliTests' checkpoint, and ManagementTests' POST …/checkpoints -->
```sh
curl -X POST -H "Authorization: Bearer $OPERATOR" https://varve.example/manage/datasets/people/checkpoints
varve checkpoint /var/lib/varve/people        # the dataset must not be held by a server
```

A checkpoint is an **extent set**: the intervals of every quad over the window
since the previous checkpoint, in `derived/extents/`, named by
`derived/catalog` (ADRs 0138, 0139). Below the newest checkpoint an as-of read
replays no log at all; above it, only the log after it. The sets are
derived and rebuilt if lost. Full checkpoints in `derived/checkpoints/` left
by a release before derived format 6 are misses for this build: they are not
read, and the log answers instead (ADR 0213).
Dropping a checkpoint merges its set into the next, so no answer changes.

A dataset's `CheckpointPolicy` can write them on a schedule of commits; the
server's defaults write none, so an operator who wants bounded opens takes
them, as above, or relies on maintenance's merged runs. There is no budget on
how many to keep: the writer merges sets in the background (ADR 0158).

### Merges

Every read of the present merges one run per set, so the writer's maintenance
merges sets after each checkpoint, by the policy of ADR 0159:
- **Similar sizes.** The oldest run of `FanOut` adjacent sets whose largest is
  at most `FanOut` times their smallest merges into one.
- **Pressure.** Past the bound, the adjacent run with the fewest bytes merges.
- **The cap.** A set past half of `MaxSetBytes` is not merged again, and no
  merge makes a set past it. It bounds one merge's work and the largest blob
  an upload carries, not reads: an as-of read inside a set seeks it, at the
  cost of its matches (ADR 0177).

At the defaults (`Varve:Store:ExtentMerges`: `FanOut` 4, `MaxSetBytes`
256 MiB, see [Configure](configure.md)) a chain holds at most 15 sets below
half the cap, plus the capped ones. Measured on REC size M (5,049,846 quads,
25,000 commits) on files, a checkpoint every hundredth of the history:

| | Without merges, 10 checkpoints | With merges, 100 checkpoints |
|---|---:|---:|
| Sets in the chain | 10 | 5 |
| Head: a quad's lookup, p50 / p99 | 59 / 124 µs | 12 / 38 µs |
| Head: one subject's quads, p50 / p99 | 9.0 / 41 µs | 4.6 / 13 µs |
| Head: an exact count, p50 / p99 | 38 / 87 µs | 15 / 31 µs |
| As-of inside the largest set, before #133 (narrowing) | 2.4 s (183 MiB) | 2.7 s (211 MiB) |
| As-of inside the largest set, a quad's lookup, since #133 | — | 4.4 / 15.0 µs (211 MiB) |
| As-of at a set's end | < 0.1 ms | < 0.1 ms |

**Write amplification** was 1.77: merges rewrote 746 MB for the 420 MB the
checkpoints wrote; with format-5 extents (#133), 1.86, for 3.5% smaller
extents. A higher `FanOut` rewrites less and keeps more sets.

**Sizing `MaxSetBytes`.** The cap no longer bounds what an as-of read costs,
so it can be raised; at REC M it should not be, since at 1 GiB the chain
held 7 sets against 5 and head lookups were slower (21 against 15 µs at
p50). Raise it when the dataset is many times the cap: the sets past half
of it, about `2 × total / MaxSetBytes`, are never merged again, and every
read of the present merges one run each, so a 40 GiB dataset at 256 MiB
keeps over 300 of them. Between 1 and 2 GiB keeps that count in the tens,
at the cost of longer merges and more bytes rewritten. An order extent is
about a seventh of its set, far below an object store's single upload
(5,000 MiB on Azure, 5 GiB on S3), which bounds the cap from above. `CheckpointPolicy.Keep` still drops the oldest sets, merging each into
the next, at no cost to any answer.

### Readers and retained bytes

On a filesystem the writer deletes a merged set's files once no read of the
process uses them. In an object store, other instances may still read them,
so every instance announces the catalog versions it reads in a reader record,
and the writer deletes a set only when no record holds it (ADR 0160). Until
then the store keeps it, and its bytes are *retained*.

<!-- ci: ci.yml storage-services — ObjectStorageServerTests, against Azurite -->
```sh
varve storage https://account.blob.core.windows.net/varve/people
varve storage readers list https://account.blob.core.windows.net/varve/people
```

`varve storage` prints the catalog's version, the sets per permutation, each
set's window and bytes, the sets no catalog names that the store still keeps,
and each record with the bytes it retains. `/health/ready` reports the same as
`retention`, with `uncheckedSets`, the sets still read without their block
checks because a follower of an earlier build is announced (ADR 0213, see
[Upgrade](upgrade.md)); it never turns unready because of either.

A record holds what it announces until its instance moves on, ends, starts
again under the same id, or is forgotten. A reader that is gone — a replica
removed without a clean stop, a killed `varve query` — keeps its record. When
`readers list` shows one that retains bytes and you know it is gone, forget it
with the interval list showed:

<!-- ci: ci.yml storage-services — ObjectStorageServerTests, against Azurite -->
```sh
varve storage readers forget https://account.blob.core.windows.net/varve/people \
  --reader 3f2a… --oldest 41 --newest 42
```

The forget acts only if the record still announces that interval. The writer
deletes what it held at its next maintenance round. A reader that was in fact
alive fails its next read of a deleted set with
[`reader-forgotten`](../problems/reader-forgotten.md) and attaches again.

A copy of an object-store dataset takes no `readers/` (see [In an object
store](#in-an-object-store)), so the records that hold sets in the source
hold nothing in a restored copy.

## Restoring

Put the directory under the root, under the name the dataset should have,
with the dataset declared closed, then declare it open. Move the directory
it replaces out of the root first; declared closed, the server holds nothing in it:

<!-- ci: ci.yml build; eng/ci.cs test-server — BackupRestoreTests: each copy restored over a dataset that moved on, then its id, head and every position checked -->
```sh
curl -X PUT -H "Authorization: Bearer $OPERATOR" -H 'Content-Type: application/json' \
  -d '{"state":"closed"}' https://varve.example/manage/datasets/people/state
mv /var/lib/varve/people /backup/people-replaced
cp -a /backup/people-20261008T120000Z /var/lib/varve/people
curl -X PUT -H "Authorization: Bearer $OPERATOR" -H 'Content-Type: application/json' \
  -d '{"state":"open"}' https://varve.example/manage/datasets/people/state
```

A dataset not declared yet is declared with `varve apply`, as a `File`
dataset of that name; its writer opens the directory it finds there (ADR
0163). Declaring a dataset never empties a directory that holds one.
The restored dataset is the one that was copied — same dataset id, same log
bytes up to the copied position — so a client that kept a position resumes
its feed from it. **Two copies of one dataset that both take commits
diverge** (ADR 0014); restore to one place.

**In an object store**, restore to a prefix that holds nothing. A dataset's
prefix is its name, `{name}/` in the server's container
([Configure](configure.md#the-object-store--objectstorage)), so copy the
`log/` and `derived/` objects under a name no dataset uses, as they were
copied out, and declare an `ObjectStorage` dataset of that name. The first writer to open it claims an
epoch of its own and writes its own writer record; the copy's id, head and
every answer are the source's, and it takes writes after its head
(`ObjectCopyTests`, ci.yml `build` and `storage-services`). Restoring over
the prefix a dataset still uses would leave that dataset's later chunks,
its writer record and its readers beside the copy, which nothing tests.

## A stale lease

A file dataset is leased by the process that has it open: `derived/LOCK`,
held by an OS lock, and `derived/LOCK.owner`, which says whose it is
(process id, machine, since when; ADR 0075). The lock goes with the process:
a crash releases it, and a start after a crash opens the dataset as usual.
What a start does meet is a **predecessor still draining** (a rolling
restart, a stop that is taking its thirty seconds), and ADR 0116 says what
happens then:

- the server retries the open every second for `Varve:Lease:WaitFor`
  (30 s by default), logging who holds it each time;
- when the wait ends, the dataset is **failed** with the holder as its
  reason, which `GET /health/ready` and `GET /datasets` report, and the
  server starts for the others. A configured dataset that does not open
  still refuses the start (ADR 0101).
- **Nothing ever forces a held lock.** A lock the OS still holds has a live
  holder; a timer that broke it would be deciding a slow process is dead.

`varve lease <dir>` says what the lease is now: it prints the owner file and
tries to take the lock, releasing it at once; exit 0 free, 1 held.

<!-- ci: ci.yml build — CliTests' lease case -->
```sh
$ varve lease /var/lib/varve/people
owner: process 4242 on db-1, since 2026-10-09T15:02:11Z
lock: held
```

`varve lease --break <dir>` is for the one case the OS does not handle: a
filesystem that did not release a dead process's lock. It takes the lock to
prove the lease free; when it can, it removes `LOCK.owner` and `LOCK`,
reports whose they were and exits 0. When the lock is held it **refuses**, names the holder and
exits 1, so that `--break` can never let a second process in on a filesystem
whose locks work. On a filesystem where locking fails for every caller, it
says so and names the filesystem as the problem: the dataset must move to one
that locks.

## Damaged data

Damage is told apart by where it is, and only one kind needs a backup.

- **`derived/` damaged** (a run, a checkpoint's extents, a dictionary, a
  commit index). Every block is checked before it is used (ADR 0213). A
  block that does not match its check is a cache miss: the blob is put
  aside, that part of the answer is read again from the log, and no read
  fails or answers differently. The writer writes a damaged set again on its
  next round. Nothing is for the operator to do, except to watch
  `varve.store.derived.damaged` ([Observe](observe.md#metrics)): a count
  that keeps rising is the disk, or the object store, under `derived/`
  failing. Check it, or move the dataset; deleting `derived/` is never
  needed, and is safe.
- **A bulk load's spill damaged.** The load's own sorted input, read back
  before its commit, did not match what it wrote
  ([`load-spill-damaged`](../problems/load-spill-damaged.md)). Nothing was
  committed. Running the same `varve load` again is right; if it recurs, the
  disk under the dataset, where the spills are (`derived/bulk/`), is
  failing.
- **`log/` damaged.** A segment's hash does not chain, or a record is out of
  order: the dataset fails to open with `failure` `damaged`
  ([`unavailable`](../problems/unavailable.md)). The log is the source of
  truth and nothing rebuilds it: [restore](#restoring) the dataset from a
  copy. A restart does not help.

## The change feed as an incremental copy

A consumer that persists the last position it applied misses nothing by
resuming from it (`docs/spec/change-feed.md` §4):

<!-- ci: ci.yml build — CliTests: feed, and a server reached with --token -->
```sh
varve feed https://varve.example/datasets/people/ --from "$LAST" --token "$TOKEN" >> people.delta
```

The records replay in order onto any store, `Varve.Delta`'s `ChangeFeedReader`
reading them with Varve types; version 1 of the format is read by every later
release (ADR 0128). It is a copy of the content, not of the log:
positions, agents and timestamps travel with it, but a dataset rebuilt from a
feed is a new dataset with a new id.

## An export

`varve export <dir-or-url>` writes the dataset at its head as N-Quads (or
TriG), or one graph as N-Triples or Turtle, and `--as-of position:<n>` on a
directory writes the past. It is what to hand to another system; it carries
no history.

## Not run in CI

Every procedure on this page names the job that runs it. What those jobs do
not run, and why (ADR 0248):

- **`azcopy` and `aws s3 sync`.** `ObjectCopyTests` and
  `AzuriteObjectCopyTests` copy the same objects with the store's own calls;
  no job holds a second storage account or bucket to copy into.
- **A store's own geo or asynchronous replication.** The two states it can
  leave, a catalog ahead of its sets and a log behind its newest chunk, are
  built by hand on the in-memory store (`ObjectCopyTests`, ci.yml `build`
  and `storage-services`); no job replicates an account. A copy missing a
  chunk in the middle of its log is not tested (#363).
- **Restoring over a prefix a dataset still uses.** Not tested, and not
  what this page says to do.
