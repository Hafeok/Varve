# Upgrade

## What a new build reads

- **`log/` is format version 1 and is read for ever** from the first
  prerelease tag that wrote it (`v0.1.0-preview.1`, ADR 0029). A later
  Varve opens any dataset an earlier one wrote.
- **`derived/` carries its own version and is a cache.** Each build states
  the derived versions it reads; a file of another version is a cache miss
  and is rebuilt from the log (ADR 0072). This build writes **derived
  format 3** (ADR 0109: runs carry a term filter) and reads **2 and 3**: a
  format-2 dataset opens as it did and migrates as maintenance rewrites its
  runs, with no step for the operator.
- **An older build does not read a newer derived format**: a downgrade sees
  the format-3 runs as cache misses and rebuilds them from the log. Nothing
  is lost; the first open after a downgrade replays.

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

A setting this build does not know is ignored by the binder; a setting whose
value is wrong refuses to start, with the error named. Read the changelog's
section for the release before upgrading: a renamed setting is listed there,
and the server's start-up errors say which value it refused.
