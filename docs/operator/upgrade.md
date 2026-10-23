# Upgrade

## What a new build reads

- **`log/` is format version 1 and is read for ever** from the first
  prerelease tag that wrote it (`v0.1.0-preview.1`, ADR 0029). A later
  Varve opens any dataset an earlier one wrote.
- **`derived/` carries its own version and is a cache.** Each build states
  the derived versions it reads; a file of another version is a cache miss
  and is rebuilt from the log (ADR 0072). This build writes extents in
  **derived format 5** (ADR 0177: a read inside a set seeks it) and every
  other derived file in format 4, and reads 2 to 4, and 5 for an extent. A
  dataset whose sets an earlier build wrote in format 4 opens as it was:
  reads inside those sets narrow them, as before, until maintenance rewrites
  each, oldest first, when no merge is due (ADR 0178). There is no step for
  the operator; with `Store:ExtentMerges:Enabled` false nothing is rewritten.
- **An older build does not read a newer derived format**: a downgrade sees
  the format-5 extents as cache misses, so its chain ends at the first such
  set and it reads the log from there. Nothing is lost; the first open after
  a downgrade replays.

### Instances on one object store

A fleet's followers and its writer may run different builds during a
rolling upgrade (ADR 0178):

- **Upgrade the followers first, then the writer.** A follower of this
  build reads format-4 and format-5 sets alike, so it serves whatever the
  writer writes.
- **A follower of an earlier build, `v0.1.0-preview.12` or before, behind a
  writer of this build**, still serves every position correctly: the catalog
  and the dictionary extents are still format 4, so it attaches, but its
  chain ends at the first format-5 set and it reads the log above that
  set's start. Its opens and as-of reads cost the log's distance there until
  it is upgraded. It never fails and never returns a wrong result.
- **A writer of an earlier build** finds the format-5 sets cache misses and
  writes its next checkpoint from the log; the sets it no longer names are
  deleted by the rule of reader records (ADR 0160), as any unnamed set is.

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
4. Start it. Watch `GET /ready`: `200` once every dataset is open and
   projected to its head; `503` lists the datasets still opening or failed,
   each with a reason.

Two processes never hold one dataset: the lease (ADR 0075) refuses the
second, so a new process started while the old one still drains waits on
`/ready` rather than corrupting anything. A rolling upgrade behind a load
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
