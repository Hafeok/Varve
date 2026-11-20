# Upgrade

## What a new build reads

Every format a release writes that another reads is in the
[format inventory](../formats/README.md) (ADR 0216), with its version, the
release that introduced it, its rule — *read forever*, *rebuilt*,
*versioned* or *reserved* — and the fixture that release wrote, which every
build's tests read. The log and the coordination records are read forever;
everything under `derived/` is rebuilt.

- **`log/` is format version 1 and is read for ever** from the first
  prerelease tag that wrote it (`v0.1.0-preview.1`, ADR 0029). A later
  Varve opens any dataset an earlier one wrote. Each build is checked
  against the previous release this way: the `upgrade (previous release)`
  job installs the newest tagged release's `Varve.Server` tool from
  nuget.org, writes a dataset with it, and opens it with the build, in place
  and from its `log/` alone, at every position (`eng/upgrade.cs`). Older
  releases are covered by their fixtures. **Derived format 6 has no fixture
  written by the release that shipped it** (`v0.1.0-preview.16`): its
  fixture, `store-next`, was written from the branch, and no object-store
  fixture carries it yet (#302).
- **`derived/` carries its own version and is a cache.** Each build states
  the derived versions it reads; a file of another version is a cache miss
  and is rebuilt from the log (ADR 0072). This build writes extents in
  **derived format 5** (ADR 0177: a read inside a set seeks it), runs,
  dictionaries, commit indexes and projections' envelopes in **derived
  format 6** (ADR 0213: every block checked before it is used), and the
  small files whose whole directory is hashed in format 4. It reads 2 to 4
  for the kinds that are not checked (state, graphs, catalog) and for an
  extent set's shared blobs, 5 for an extent and only 6 for a checked kind.
  A run, a checkpoint, a dictionary or a local commit index this machine
  wrote in any format below 6 (2, 3 or 4, by any earlier release) is a miss
  under this build and is rebuilt once, from the log or from the sets. A
  dataset whose sets an earlier build wrote in format 4 opens as it was:
  reads inside those sets narrow them, as before, until maintenance rewrites
  each, oldest first, when no merge is due and no reader of an earlier build
  is present (ADR 0178, below). There is no step for the operator; with
  `Store:ExtentMerges:Enabled` false nothing is rewritten.
- **An older build does not read a newer derived format**: a downgrade to
  `v0.1.0-preview.15` or earlier sees the format-6 runs, dictionaries and
  commit indexes as misses, and one before format 5 sees the extents as
  misses too, so its chain ends at the first such set and it reads the log
  from there (ADR 0213 point 7). The log is never rewritten, so nothing is
  lost. No CI job runs a downgrade.

### Instances on one object store

A fleet's followers and its writer may run different builds during a
rolling upgrade (ADR 0178). **The store is safe in either order**; the
order only decides how much log an older follower replays.

- **Recommended: the followers first, then the writer.** A follower of this
  build reads format-4 and format-5 sets alike, so it serves whatever the
  writer writes, and nothing replays the log.
- **Why the other order is safe too.** Every reader record says the newest
  derived format its reader reads (`maxDerived`; a record of
  `v0.1.0-preview.14` or before does not say, which counts as 4). While any
  present record says 4, the writer rewrites no format-4 set, and its
  merges and splits of them keep format 4; only its new checkpoints are
  format 5. So a follower of an earlier build, behind a writer of this
  build, keeps its chain of format-4 sets and reads only the log above them
  — the windows of the writer's new sets — not the whole history. It never
  fails and never returns a wrong result.
- **When the old followers are gone**, restarted on this build or
  forgotten (`varve storage readers forget`), every record says 5 and the
  writer's maintenance rewrites the format-4 sets as format 5, the oldest
  first, one a round. `varve storage readers list` shows each record's
  `maxDerived`, so the operator sees what holds the rewrite back.
- **The same holds for format 6** (ADR 0213). While a present record says
  `maxDerived` 5 or less — a follower of `v0.1.0-preview.15` or before — a
  set's dictionary and commit index, which followers read, are written in
  format 4, as that follower reads them, and stay unchecked; so do the sets
  already there. Readiness reports them as `uncheckedSets` under
  `retention`. When no such record remains, maintenance rewrites them in
  format 6, the oldest first, one a round, and the count falls to 0.
  Upgrading or forgetting the older followers ends the window.
- **A writer of an earlier build** (a downgrade) finds the format-5 sets
  cache misses and writes its next checkpoint from the log; the sets it no
  longer names are deleted by the rule of reader records (ADR 0160), as any
  unnamed set is.

A build that cannot read a `log/` refuses with a message naming the version
found and the versions it reads; that is a defect to report, not a case
that happens by design.

## The order

1. Take a copy ([Back up and restore](backup-and-restore.md)); a volume
   snapshot is enough.
2. Stop the server with `SIGTERM` and wait for exit 0: it drains requests,
   ends live feeds with a `shutdown` event and closes every dataset, which
   writes its projection state, so the new build opens in the time of the
   tail, not the log.
3. Replace the executable: `dotnet tool update --global Varve.Server`, or the
   new single file.
4. Start it. Watch `GET /health/ready`: `200` once every dataset is open and
   projected to its head; `503` lists the datasets still opening or failed,
   each with a reason.

Between steps 3 and 4, with no server holding the directory, the new build
can show that it reads a dataset before it serves it: the same id and head
the old build reported, and the past as it was.

<!-- ci: ci.yml upgrade — eng/upgrade.cs: the previous release's tool writes the dataset, this build's info, export --as-of at every position and query read it -->
```sh
varve info /var/lib/varve/people
varve export /var/lib/varve/people --as-of position:41 > /tmp/people-41.nq
```

Two processes never hold one dataset: the lease (ADR 0075) refuses the
second, so a new process started while the old one still drains waits on
`/health/ready` rather than corrupting anything. A rolling upgrade behind a load
balancer is therefore one instance at a time, each with its own datasets;
two instances serving one directory is not a supported configuration.

## The command line

The tool is the server: updating `Varve.Server` updates `varve`. The
credential file's format is the same across builds, and a token it holds
stays valid for as long as the issuer says.

## Configuration

A setting this build does not know **refuses to start**, naming the key
(ADR 0115); a setting whose value is wrong refuses to start too, with the
error named. Read the changelog's section for the release before upgrading:
a renamed setting is listed there, and `varve serve --print-config` with the
new build and the old configuration shows every effective value and its
source, and lists what the build refuses, before anything serves.

## Not run in CI

What on this page no CI job runs, and why (ADR 0248):

- **Upgrading one instance, as a server.** The `upgrade (previous release)`
  job (ci.yml `upgrade`) writes with the previous release's command line and
  reads with this build's; it runs no server of either, so the stop,
  `dotnet tool update` and start sequence, and a server's projection state
  written by the old build and read by the new, are not run. It reaches
  nuget.org, so it is not a required check.
- **A rolling upgrade of a fleet.** Old and new builds side by side on one
  store are simulated through reader records only (`ObjectFormatUpgradeTests`
  and `UncheckedSetTests`, ci.yml `build`); no job runs a real older
  instance, so a rolling upgrade is untested until #318.
- **A downgrade.** No job runs one, and none can from this build.
