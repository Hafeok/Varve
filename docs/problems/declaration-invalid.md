# `declaration-invalid`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/declaration-invalid` |
| **Title** | The management declarations are not valid. |
| **Status** | `422` |
| **Members** | `findings` |

A commit to the fleet's system dataset would leave a management graph it
touches with a finding of the code validator (ADR 0166 point 4): a term the
vocabulary does not know, a value of the wrong kind, a blank node, a term
reserved for a later milestone, or an event-schema term in a graph whose
dataset's event declarations have not moved: a dataset's graph holds them
only once it says `varve:eventDeclarations varve:Moved`, which
`POST /manage/datasets/{name}/events/move` makes it say (ADR 0167). `findings` lists each, with its
`graph`, its `focus` node and `path` when it has them, and a `message`.
Nothing was committed.

**One finding is not the code validator's.** An apply that declares a
dataset a copy, with `varve:copyOf`, is refused with this problem when the
instance that answers can see the dataset on its store with no lineage
record naming that source: it is a dataset written in this fleet, and a
copy is made only by its source's shipper (ADR 0185 decision 10). The
finding's `graph` is the dataset's, its `path` is `varve:copyOf`, and it
has no `focus`.

## What to do

An operator: fix each finding in the TriG at the `graph`, `focus` and `path`
it names, and apply it again with `varve apply`. The terms are in
[`docs/vocabulary.md`](../vocabulary.md). An event-schema term is accepted
only once the dataset's event declarations have moved
([Moving event schemas](../operator/moving-event-schemas.md)).

For the finding at `varve:copyOf`: remove it if the dataset is this
fleet's own, or declare the copy under another dataset name
([the state](../operator/shipping/copy-declared-on-a-written-dataset.md)
says the same of a writer that meets the declaration).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
