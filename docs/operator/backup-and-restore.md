# Back up and restore

## What the files are

A file dataset is one directory under `Varve:DatasetsRoot` (ADR 0072,
`docs/spec/storage-format.md`):

| Path | What | Loss means |
|---|---|---|
| `log/` | the source of truth: a manifest and append-only segments, **format version 1**, never rewritten, read for ever | the dataset |
| `derived/` | the projection's runs, checkpoints, the commit index and bulk-load spills: rebuilt from the log on a cache miss | a slower open |
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

**Cold**: close the dataset first, so that the copy is exactly a position:

```sh
curl -X POST -H "Authorization: Bearer $ADMIN" https://varve.example/datasets/people/close
cp -a /var/lib/varve/people /backup/people-$(date -u +%Y%m%dT%H%M%SZ)
curl -X POST -H "Authorization: Bearer $ADMIN" https://varve.example/datasets/people/open
```

Record the position with the copy: `varve info /backup/people-…` reads it
from the copy itself, or `GET /datasets/people/status` before the close.

## Checkpoints

A checkpoint bounds the replay an open and an as-of read do (ADR 0078):

```sh
curl -X POST -H "Authorization: Bearer $ADMIN" https://varve.example/datasets/people/checkpoints
varve checkpoint /var/lib/varve/people        # the dataset must not be held by a server
```

Checkpoints live in `derived/checkpoints/` and are rebuilt if lost. A
dataset's `CheckpointPolicy` can write them on a schedule of commits; the
server's defaults write none, so an operator who wants bounded opens takes
them, as above, or relies on maintenance's merged runs.

## Restoring

Put the directory under the root, under the name the dataset should have,
and open it:

```sh
cp -a /backup/people-20261008T120000Z /var/lib/varve/people
curl -X POST -H "Authorization: Bearer $ADMIN" https://varve.example/datasets/people/open
```

`open` finds a directory copied under the root since the server started,
and the next start discovers it too (ADR 0105); `PUT` is for a new, empty
dataset and answers `409 dataset-exists` for one that is there.
The restored dataset is the one that was copied — same dataset id, same log
bytes up to the copied position — so a client that kept a position resumes
its feed from it. **Two copies of one dataset that both take commits
diverge** (ADR 0014); restore to one place.

`Dataset.ShipAsync` (ADR 0083) does the same from code, at an exact position,
into any storage: the manifest, the segments up to the position, and a
checkpoint. There is no `varve ship` yet; the roadmap has replica bootstrap
over HTTP.

## The change feed as an incremental copy

A consumer that persists the last position it applied misses nothing by
resuming from it (`docs/spec/change-feed.md` §4):

```sh
varve feed https://varve.example/datasets/people/ --from "$LAST" --token "$TOKEN" >> people.delta
```

The records replay in order onto any store, Varve's `ChangeFeedReader`
reading them with Varve types. It is a copy of the content, not of the log:
positions, agents and timestamps travel with it, but a dataset rebuilt from a
feed is a new dataset with a new id.

## An export

`varve export <dir-or-url>` writes the dataset at its head as N-Quads (or
TriG), or one graph as N-Triples or Turtle, and `--as-of position:<n>` on a
directory writes the past. It is what to hand to another system; it carries
no history.
