# 0128 — `Varve.Delta`: the delta format as a package of its own, read for ever

## Status

**Accepted — filed unaccepted by milestone Client and integration of #93,
2026-10-13** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer on the Client and integration plan: the
format is a package of its own, not part of `Varve.Store`, because a consumer
that only reads the feed must never ship the store.

**Amends** ADR [0097](0097-the-change-feed-and-the-diff.md) (where the reader
and the writer live, and the format's lifetime) and ADR
[0102](0102-a-release-is-a-descriptor.md) (the descriptor gains
`delta-format`), each by a dated block of its own (ADR 0068). **Amends** ADR
[0103](0103-varve-protocol-client.md) by the block ADR 0127 adds: the client
references `Varve.Delta` and no longer references `Varve.Store`.

## Context

`application/vnd.varve.delta; version=1` (`docs/spec/change-feed.md`) is what
the commits resource writes. With `FollowAsync` (ADR 0129) it becomes the
contract other systems integrate against, and that contract has three gaps.

1. **The reader is out of reach.** `ChangeFeedReader` lives in `Varve.Protocol`,
   layer 5. `Varve.Protocol.Client` is also layer 5, and a same-layer reference
   is a violation (ADR 0060). The reader's records are typed with
   `Varve.Store.Log`'s `Position`, `CommitKind` and `CommitTimestamp`, so it
   cannot move below the store as it stands.
2. **The store would come with it.** Moving the reader into `Varve.Store` would
   make every application that follows a feed ship the store: the log, the
   projections, the file backend. Today the client references `Varve.Store`
   for one type, `ByteCount`, which is the same mistake made small.
3. **Nothing says the format is permanent.** ADR 0072 put the storage format
   under a read-forever rule, and ADR 0102 made that rule a reviewed line in
   every release. A consumer's parser of the delta format deserves the same
   promise, and has none.

The format is an RDF syntax: lines of canonical N-Triples terms with a small
header grammar, specified on its own page and versioned by a media-type
parameter. That is the same kind of thing as `Varve.Turtle`.

## Decision

1. **A new package, `Varve.Delta`, owns the format.**
   - It holds the reader (`ChangeFeedReader`), the writer (`DeltaWriter`), the
     record model (`FeedRecord`, `FeedChange` and their kinds), the
     server-sent-events framing (`change-feed.md` §3) and the format's version.
   - It is expressed over `Varve.Rdf`'s terms, quads, handles and
     `QuadDelta`, plus its own small header types:
     - `FeedPosition`, a non-negative position;
     - `FeedTimestamp`, a UTC instant with the format's seven-digit rendering
       and RFC 3339 parse;
     - `FeedCommitKind`, `Data`, `Settings` or `Erasure`.
   - Agent, cause, scope and attachments are `RdfTerm`s, as they were. An
     error record's problem is its type IRI, which must be under
     `https://w3id.org/varve/problems/`, as before.
2. **It is layer 3, not layer 2.**
   - It references `Varve.Rdf` (layer 1) and `Varve.Turtle` (layer 2), whose
     N-Quads term parser and canonical term writer it uses, so by dependency
     it sits one row above Turtle.
   - The alternative was a second parser of the N-Triples term grammar inside a
     layer-2 package. That is the duplication constraint 6's spirit forbids,
     with two parsers that could drift.
   - Layer 3 already holds two unrelated packages (`Varve.Sparql.Evaluation`,
     `Varve.Shacl`). The layer table in `AGENTS.md` gains it.
3. **The hosts map to it, and the store does not.**
   - `Varve.Protocol` maps a `Commit` (handles, `Position`, `CommitTimestamp`)
     to a `DeltaWriter` call and keeps `ChangeFeedWriter` as that adapter, the
     public face a store-backed host such as the CLI already uses.
   - `Varve.Store` does not reference `Varve.Delta`. Nothing in the log needs
     the wire format.
4. **The client no longer references `Varve.Store`.**
   - `ClientLimits.MaxResponseBytes` becomes a `long` count of bytes.
   - The client's closure is then `Varve.Delta`, the SPARQL syntax and results
     packages, the evaluator's `SERVICE` contract, Turtle, `Varve.Rdf` and
     `Varve.Iri`, with no log and no backend in it.
   - This is a breaking change under ADR 0118 and is listed in the release
     summary.
5. **Version 1 is frozen and read for ever.**
   - `DeltaFormat.Current => new(1)` is the version the writer writes. The
     media type is composed from it, not spelt as a literal in five places.
   - A consumer that parses version 1 must never break: no release changes
     what a version-1 document means or stops reading one.
   - A change a version-1 reader would misread is version 2, under the media
     type's `version` parameter (ADR 0097's rule). The server then writes 2
     only to a client that asks for it.
   - Change-feed.md's §2 is the specification of version 1. It changes only to
     clarify, never to alter what a document means.
6. **The release says so.**
   - The descriptor's `basis` gains **`delta-format: n`**, exactly one, equal
     to `DeltaFormat.Current` at the commit under release. It is resolved like
     `storage-format`, by reading the constant's file at that commit.
   - It is **required in every descriptor whose commit carries the constant**.
     Descriptors already cut predate it and stay valid: they are immutable,
     and their commits have no constant.
   - `--draft` fills it in. `eng/status.cs` reports it beside the storage
     format.

## Alternatives considered

- **The reader and the writer in `Varve.Store`.** Rejected by the maintainer:
  it puts the store in every consumer's closure (Context, point 2).
- **A layer-2 package with its own term parser.** Rejected in point 2.
- **The reader copied into the client.** Two readers of one format, one of
  them in a client library nobody tests against the server's writer. The
  chunk-boundary oracle would have to run twice.
- **No version line in the descriptor.** The read-forever promise would then
  rest on reviewers remembering it, which is exactly what ADR 0102 replaced
  for the storage format.

## Consequences

- `Varve.Protocol`'s public API loses `ChangeFeedReader`, `FeedRecord` and
  their kinds, which move to `Varve.Delta` with the header types renamed. The
  release summary lists the move. The feed's tests move with the reader and
  keep their assertions.
- A consumer of the feed references `Varve.Delta` or the client, and never the
  store.
- `eng/lib/Releases.cs`, `eng/release.cs`, `eng/lib/Status.cs`,
  `docs/releases.md` and the release fixtures change together.

## Checks

- **Checked against the accepted ADRs** (0001–0126). Touches **0060** (a
  third row-3 package, by dependency), **0072** (the rule extended to a second
  format), **0097** (amended), **0098** (labels unchanged), **0102**
  (amended), **0103** (amended), **0118** (renames now, not after 1.0). No
  conflict.
- **Layer ownership.** `Varve.Delta`, layer 3.
- **Analyzer rule.** None new: `ArchLayer` and `VARVE0002` already check the
  row.
- **Open questions owned.** None.
