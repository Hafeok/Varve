# `unavailable`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/unavailable` |
| **Title** | The dataset cannot answer now. |
| **Status** | `503` |
| **Members** | `reason`, `failure` |

The dataset cannot answer now: its projection failed, it is closing, or a dataset asked to open did not (ADRs 0094, 0106).

A dataset that failed to open answers `reason` `failed`, with `failure`
saying whether a retry can help (ADR 0242); `/manage/datasets` and the
dataset's `…/state` carry the same `failure` beside its `reason`:
- `damaged`: its log does not verify (a hash does not chain, a record is out of order). Trying again will not fix it.
- `configuration`: the server's configuration or build cannot open it: a storage root or object store that is not configured, or a format this build does not read.
- `transient`: anything else. The server tries to open it again.

A write this instance would forward (ADR 0168) is answered with `reason`
`no-writer` when no instance holds the dataset's writer epoch. A forward
whose writer could not be reached, or whose answer was lost, is
[`commit-outcome-unknown`](commit-outcome-unknown.md) (ADR 0214): it is
sent once and never again, and whether the writer received any of it is
unknown here, so the client decides.

## What to do

- `failure` `damaged`: restore the dataset from a backup ([Restoring](../operator/backup-and-restore.md#restoring)); a restart does not help.
- `failure` `configuration`: correct the setting the `detail` names (`Varve:DatasetsRoot`, `Varve:ObjectStorage`), or upgrade to a build that reads the dataset's format.
- `failure` `transient`, or no `failure`: retry after a short wait; the server keeps trying to open it. If it persists, the `detail` and the server's log say why.
- `reason` `no-writer`: wait for a writer to claim the epoch, or start one ([The fleet](../operator/fleet.md#who-writes-a-dataset-placement)).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
