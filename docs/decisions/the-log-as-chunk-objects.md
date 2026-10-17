---
set: the-log-as-chunk-objects
namespace: varve
adr: 0135
decisions:
  - key: ChunkPerFlush
    statement: "An open log segment is a sequence of write-once chunk objects named log/NNNNNNNN.chunks/CCCCCCCC, one per flush that has bytes, each created by create-if-absent, so group commit pays exactly one create per batch"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: ChunkHeader
    statement: "Every chunk object is a backend header followed by log bytes: epoch, segment, chunk number, offset, length, the bytes' SHA-256, the previous chunk's header hash and its own; the log's bytes are the concatenation of the payloads and no byte of format 1 changes"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: LostResponseReadsBack
    statement: "A create whose outcome is unknown is retried, and a create that finds the object reads it back and compares; the same bytes are the earlier attempt's success"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: ConsolidatedBySealAndMaintenance
    statement: "A sealed segment is consolidated by the seal itself into log/NNNNNNNN.seg, byte-identical to the file backend's segment; maintenance then deletes every chunk of it but its fence chunks, off the commit path, and does again a consolidation that failed"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: SealedSegmentsReadThroughTheCache
    statement: "A sealed segment is read from a local copy in the cache directory, downloaded whole on first read; the open segment's bytes are held in memory, at most one segment's length"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: ReplicatedDurability
    statement: "The object-storage backend declares Replicated: a flush returns when a store with conditional writes has acknowledged the object, and what that survives is the store's own replication"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: StorageMaintenanceIsCalledByTheDataset
    statement: "A backend with maintenance of its own implements IStorageMaintenance, which the dataset's maintenance calls off the sequencer under DatasetOptions.Maintenance"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: ObjectStorageModel
    statement: "Varve.Store.ObjectStorage.Model is the object-storage backend's model namespace and holds its options, its access, the writer instance and epoch, and the writer record"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
---

The rulings of [ADR 0135](../adr/0135-the-log-as-chunk-objects.md), filed unaccepted by milestone
Object storage of #92 (ADR 0066).
