# 0183 — Object store verification: the suite as shipped code, a record at the store, and a generated compatibility page

## Status

**Accepted — filed unaccepted by Replication part 2 (#166) for #117,
2026-11-16** (ADR 0066). Acceptance is the maintainer's act on the pull
request. The plan it follows was approved on #97 on 2026-10-27, and its
delta plan answered on #166 on 2026-11-16.

**Amends** by dated amendment:
- [ADR 0134](0134-the-object-store-contract.md): the contract suite is
  shipped code, and a second broken reference store gates it;
- [ADR 0216](0216-the-format-inventory.md): the verification record and the
  result record join the inventory;
- [ADR 0217](0217-coordination-records-read-conservatively.md): the
  verification record is read by its rule.

Records decision set [`verified-object-stores`](../decisions/verified-object-stores.md).

## Context

Object storage has no standard for what Varve relies on: a create-if-absent
and a replace-if-unchanged that hold under concurrency. Stores that call
themselves S3-compatible differ exactly there.

ADR 0134 answers this in two places, and neither reaches an operator:

- **The probe** (decision 9) runs at every open for write. It refuses a
  store that ignores the conditional headers, as Garage does. It cannot
  detect a store that enforces them one request at a time and loses races.
- **The contract suite's race properties** (decision 7) detect that. They
  were test code: `tests/Varve.Store.Tests/ObjectStorage/ObjectStoreContractCases.cs`,
  run against the stores CI starts. An operator with another store could
  not run them, and "supported" was a sentence in an ADR.

Replication part 2 ships a log to a second store (#166). A log shipped to a
store that loses races can be forked without either side being told. So a
shipping target must be a store the suite has passed, and the operator
needs a way to run it and a place to see what has been run.

## Decision

**1. The contract suite is shipped code.**
`ObjectStoreVerification` in `Varve.Store.ObjectStorage` holds the seven
cases of ADR 0134: the five operations and both race properties.
- **One suite.** The tests against the in-memory store, Azurite, RustFS and
  a real account call the same cases. Nothing in `tests/` restates them.
- **Sixteen racers,** as ADR 0134 decision 7 has it. The number of rounds
  of each race is a parameter, eight by default.
- **A verdict, not an exception.** A case the store breaks is the run's
  **counterexample**: the case's name and what was seen. The run stops at
  the first one. A failure to reach the store is still an exception,
  because it says nothing about the contract.
- **The suite has a version,** an integer that starts at 1 and rises when a
  case is added or made stricter. A build names the lowest version it
  accepts, which is the current one today.

**2. `varve storage verify <store>` runs it.** The address is a container
or a bucket, with no dataset after it, reached as the operator's identity
(ADR 0137).
- **In a scratch prefix.** Every object the run creates is under
  `varve-verify/<random>/` at the store's root. The prefix is listed and
  removed afterwards, whatever the verdict. What could not be removed is
  reported by name, and is never read again.
- **Its exit code is the verdict:** 0 for a pass, 1 with the counterexample
  printed.

**3. A pass writes a verification record at the store's root.** The object
`varve-verification` holds the record's format, the suite's version, the
racers and rounds, and when it ran, as JSON an operator can read.
- **A failed run removes it.** A store that no longer passes is not left
  marked as one that did.
- **It guards against a mistake, not an adversary.** Anyone who can write
  to the store can write the record. It answers "did someone run the suite
  against this endpoint", so that a target declared by a typing error, or
  on a store nobody tried, is refused. It is not evidence about a store
  whose operator wants to deceive Varve, and nothing treats it as such.
- **It is at the root, not per prefix** (the maintainer's answer D1 on
  #166). One run covers every dataset in the container or bucket.
- **Replication reads it** (ADR 0184, to come): a shipping target needs a
  record there whose suite version is at or above the build's lowest.

**4. The verification record is read conservatively** (ADR 0217). A record
of a format this build does not read, or one that does not parse, is **not
a verification**. The answer is to run the command again with this build.

**5. A result record per provider and version** is the evidence, under
`testdata/object-stores/results/`.
- `varve storage verify --record <file>` writes one. It names the
  provider's id, the server's version, the deployment or region, the
  suite's version, the date, the racers and rounds, the result, the
  counterexample if there is one, who ran it (`ci` or `maintainer`), and a
  `signed-off-by` line.
- **The version and the deployment are the operator's statement,** given as
  options. The five operations do not show a server's version, and the
  command does not guess one.
- **A record is added by pull request,** like any other evidence.
- **An emulator is its own provider.** Azurite's record verifies Azurite,
  at its version. It does not verify Azure Blob Storage, whose record comes
  from a real account on the optional leg.

**6. The compatibility page is generated.**
`dotnet run eng/object-stores.cs` writes `docs/operator/object-stores.md`
from `testdata/object-stores/providers.yaml` and the result records. Each
provider is in one of four states:

| State | Means |
|---|---|
| **verified** | a passing result record for that version, run by CI or by the maintainer |
| **untested** | an adapter exists, and no record |
| **unsupported** | a recorded failure, or a documented design limit, with the reason |
| **no adapter** | Varve has no adapter for it |

- **`eng/status.cs` fails when the page and the records disagree,** so the
  page is never edited by hand.
- **Garage is unsupported,** from its documentation and the probe.
- **MinIO's community edition is untested,** with the note that it is no
  longer distributed as binaries.

**7. Two broken stores gate the suite,** both in-memory test doubles:
- `IgnoresConditions` overwrites on a create and replaces against any
  version. The probe refuses it, and so does the suite.
- `LosesRaces` checks each condition and then writes without holding
  anything between the two. **It passes the probe and fails the race
  property.** It is the store the probe cannot see, and the reason the
  suite is the evidence.

The command must pass against Azurite and RustFS, and fail with the
counterexample against both broken stores.

## Alternatives considered

- **Keep the suite in `tests/` and publish its results by hand.** An
  operator with a store CI does not start has then nothing to run, and the
  page is prose that drifts from the evidence.
- **Ship the suite as a separate tool.** A second artefact to build, sign
  and document, for seven cases the object-storage package already
  depends on nothing new to run.
- **A verification record per dataset prefix.** Stricter, and every new
  shipping target would need a run of its own. The contract is the
  store's, not the prefix's (answered as D1 on #166).
- **Sign the record.** A signature needs a key, and Varve holds none: its
  only credentials are OIDC tokens (ADR 0037). A signed record would also
  claim more than the record means.
- **Trust the provider's name and skip the record.** That is the weaker
  reading the approved plan offered. A provider's name says what a store
  claims to be, not that anyone ran the suite against this endpoint.
- **Count Azurite's record as Azure Blob Storage's.** An emulator shares
  the service's protocol, not its concurrency. The race property on one
  says nothing about the other.
- **Read the server's version from response headers.** `IObjectStore` is
  five operations and shows no header (ADR 0134 decision 1). Widening the
  contract for a label was not worth it.
- **Run every case after a failure.** The first counterexample is what an
  operator acts on, and a store that broke one case has no verdict to
  refine.

## Consequences

- **"Supported" is now a table with evidence.** ADR 0134's sentence "the
  supported stores are those the suite has passed" points at the page.
- **Azure Blob Storage is untested on the page until the optional leg's
  record is added.** The leg (`azure-objects.yml`) writes one when the
  maintainer runs it.
- **The CLI's closure gains code and no dependency.** `Varve.Server`
  already references the object-storage package and both adapters.
- **A raised suite version makes every older record stale.** A store
  verified under version 1 must be verified again by a build whose lowest
  accepted version is 2. That is the point of the version.
- **The scratch prefix needs write and delete access at the store's
  root.** An identity scoped to one dataset's prefix cannot run the
  command.

## Checks

- **Checked against the accepted ADRs.** Touches:
  - **0011**: the scratch prefix's name is drawn from the caller's
    randomness, and the record's time from the caller's clock.
  - **0037, 0137**: the command authenticates as the server does. No key.
  - **0105**: one more subcommand of `varve storage`.
  - **0134**: amended. The contract itself is unchanged.
  - **0162**: `varve storage` reads and changes nothing. `verify` writes,
    and only under its scratch prefix and the one record.
  - **0216, 0217**: amended.
  - **0224, 0225**: no new package. The `tool` SBOM's digest changes with
    the code.

  No conflict.
- **Layer ownership.** The suite and the verification record are
  `Varve.Store.ObjectStorage`, layer 5. The command and the result record's
  writer are `Varve.Server`, layer 6. The page's generator is `eng/`.
- **Analyzer rule.** None.
- **Tests.**
  - The suite passes against the in-memory store, and fails with its
    counterexample against `IgnoresConditions` and `LosesRaces`, on every
    OS.
  - `LosesRaces` passes ADR 0134's probe.
  - A pass writes the record, a failure removes it, and the scratch prefix
    is empty after either.
  - A record of an unknown format reads as no verification.
  - The command passes against Azurite and RustFS in `storage (services)`.
  - `eng/status.cs` refuses a page edited by hand.
- **Open questions owned.** None. What a shipping target must show is
  ADR 0184's.
