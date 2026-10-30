# Back up and restore

## What the files are

A file dataset is one directory under `Varve:DatasetsRoot` (ADR 0072,
`docs/spec/storage-format.md`):

| Path | What | Loss means |
|---|---|---|
| `log/` | the source of truth: a manifest and append-only segments, **format version 1**, never rewritten, read for ever | the dataset |
| `derived/` | the projection's runs, the checkpoints — `extents/` and the `catalog` that names them (ADR 0139) — the commit index and bulk-load spills: rebuilt from the log on a cache miss | a slower open, and as-of reads that replay the log |
| `lock` | the lease of the process that holds the dataset (ADR 0075) | nothing; a stale one is taken over |

**A backup is a copy of the directory.** `log/` alone is enough; `derived/`
saves the replay. Every commit is flushed to the device before it is
acknowledged (`Synchronised`, ADR 0073), so the log on disk is never behind
what a client was told.

## Taking a copy

**Hot**: copy the directory while the server runs. The store is built for
it: a copy taken while the dataset was being written opens at its last closed
commit, and a torn tail is ignored (ADR 0071, the failure-injection suite).
A snapshot taken by the file system or the volume at one instant is the
cleanest hot copy.

**Cold**: declare the dataset closed first, so that the copy is exactly a
position. `$OPERATOR` is a token for the management audience (ADR 0163):

```sh
curl -X PUT -H "Authorization: Bearer $OPERATOR" -H 'Content-Type: application/json' \
  -d '{"state":"closed"}' https://varve.example/manage/datasets/people/state
cp -a /var/lib/varve/people /backup/people-$(date -u +%Y%m%dT%H%M%SZ)
curl -X PUT -H "Authorization: Bearer $OPERATOR" -H 'Content-Type: application/json' \
  -d '{"state":"open"}' https://varve.example/manage/datasets/people/state
```

Record the position with the copy: `varve info /backup/people-…` reads it
from the copy itself, or `GET /manage/datasets/people/status` before the close.

## Checkpoints

A checkpoint bounds the replay an open and an as-of read do (ADR 0078):

```sh
curl -X POST -H "Authorization: Bearer $OPERATOR" https://varve.example/manage/datasets/people/checkpoints
varve checkpoint /var/lib/varve/people        # the dataset must not be held by a server
```

A checkpoint is an **extent set**: the intervals of every quad over the window
since the previous checkpoint, in `derived/extents/`, named by
`derived/catalog` (ADRs 0138, 0139). Below the newest checkpoint an as-of read
replays no log at all; above it, only the log after it. The sets are
derived and rebuilt if lost; a dataset written by the previous release keeps
its full checkpoints in `derived/checkpoints/` until they are dropped.
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

```sh
varve storage readers forget https://account.blob.core.windows.net/varve/people \
  --reader 3f2a… --oldest 41 --newest 42
```

The forget acts only if the record still announces that interval. The writer
deletes what it held at its next maintenance round. A reader that was in fact
alive fails its next read of a deleted set with
[`reader-forgotten`](../problems/reader-forgotten.md) and attaches again.

A dataset in an object store is backed up by `ShipAsync` into a directory
(the migration path is the same call the other way), or by the store's own
replication; see [Azure Container Apps](azure-container-apps.md).

## Restoring

Put the directory under the root, under the name the dataset should have,
with the dataset declared closed, then declare it open:

```sh
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

`Dataset.ShipAsync` (ADR 0083) does the same from code, at an exact position,
into any storage: the manifest, the segments up to the position, and a
checkpoint. There is no `varve ship` yet; the roadmap has replica bootstrap
over HTTP.

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

```sh
$ varve lease /var/lib/varve/people
held by pid 4242 on db-1 since 2026-10-09T15:02:11Z
```

`varve lease --break <dir>` is for the one case the OS does not handle: a
filesystem that did not release a dead process's lock. It takes the lock to
prove the lease free; when it can, it removes `LOCK.owner`, reports whose it
was and exits 0. When the lock is held it **refuses**, names the holder and
exits 1, so that `--break` can never let a second process in on a filesystem
whose locks work. On a filesystem where locking fails for every caller, it
says so and names the filesystem as the problem: the dataset must move to one
that locks.

## The change feed as an incremental copy

A consumer that persists the last position it applied misses nothing by
resuming from it (`docs/spec/change-feed.md` §4):

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
