# Varve.Delta

The change feed's format, `application/vnd.varve.delta; version=1`
(`docs/spec/change-feed.md`): what a Varve server's commits resource and diff
write, and what a consumer of either reads. **Version 1 is read for ever**
(ADR 0128). Every later release reads a version-1 document with the same
meaning, and every release descriptor names the version it writes.

- **`ChangeFeedReader`**, a pull reader over UTF-8 that takes input in any
  pieces. A document read whole equals the same document split at every
  offset (the chunk-boundary oracle), and an error names its byte and line.
- **`DeltaWriter`**, which writes commit, diff and error records, and their
  server-sent-events framing, in canonical N-Triples terms. A record read back
  is written byte for byte as it was.
- **The model:** `FeedRecord`, `FeedChange`, `FeedPosition`, `FeedTimestamp`,
  `FeedCommitKind` and `FeedProblemType`. These are the format's own types,
  so **a consumer of the feed ships no store**: this package references
  `Varve.Rdf` and `Varve.Turtle`, and nothing else.

`Varve.Protocol.Client`'s `FollowAsync` reads the feed with it. `Varve.Protocol`
writes it from a store's commits.
