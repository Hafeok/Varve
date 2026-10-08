# 0091 — `Varve.Protocol` and `Varve.Server`: endpoint groups, the update seam, policy names

## Status

**Accepted — filed unaccepted by milestone 7a of #11, 2026-10-07** (ADR 0066).
Decided by the maintainer on the 7a plan: "ISparqlUpdateExecutor in
Varve.Protocol bound by the host; note in 0060 the revisit condition (a third
integration needing the same)". Acceptance is the maintainer's act on the pull
request.

**Revisit condition:** a third integration needs to be called from protocol
code the way `Varve.Sparql.Store` is. Two seams of this shape are bookkeeping.
Three are a layer, and ADR 0060's table should then be superseded with protocols
between integrations and hosts. ADR 0060 records the same condition by a dated
amendment.

## Context

Milestone 7 builds the first server. The brief puts "SPARQL 1.1 Protocol, Graph
Store Protocol, service description … plus endpoints for the event-sourced
features" there. ADR 0060 makes every executable layer 6 and reserves the
composition root to it. A protocol implementation is not an executable. It is
a library a host mounts, and more than one host will mount it: the server, the
test host of the conformance suites, and later an Aspire integration or an
application that embeds a SPARQL endpoint in its own ASP.NET Core process.

The protocol code needs the evaluator (layer 3), the store (layer 4) and the
syntaxes (layer 2). It is therefore layer 5 by ADR 0060's table, beside
`Varve.Sparql.Store`. It also needs SPARQL Update, which `Varve.Sparql.Store`
implements, and a same-layer reference is a violation (ADR 0003, `DD0001`).

## Decision

1. **Two packages.**
   - **`Varve.Protocol`, layer 5.** A library holding:
     - the SPARQL 1.1 Protocol, the Graph Store Protocol, the service
       description, the change feed and the diff;
     - the mapping of a request onto the store;
     - the change-feed reader.

     Its endpoints are `IEndpointRouteBuilder` extensions over minimal APIs and
     make no host assumption. Nothing in it references an authentication type.
   - **`Varve.Server`, layer 6.** The executable and the composition root:
     configuration, hosting, authentication and the wiring of datasets,
     clocks and validators.
2. **The update seam.** `Varve.Protocol` declares `ISparqlUpdateExecutor`, one
   member that executes a parsed update against a dataset with commit metadata
   and an optional expected position. It returns the store's `CommitResult`.
   The host binds it. `Varve.Server` binds it to `SparqlUpdate.ExecuteAsync`.
   Queries need no seam: the pin, evaluate, release-on-end choreography of
   ADR 0052 is written once in `Varve.Protocol` over layers 3 and 4.
3. **The datasets are the host's.** `Varve.Protocol` asks an `IDatasetResolver`
   for the dataset a request names, and the host owns the mapping (ADR 0093).
4. **Authorisation is three policy names**: `varve:read`, `varve:write` and
   `varve:admin`. The host registers policies under those names and decides
   what satisfies them; the resource each is decided on is the request's
   `DatasetName`.
   - **The endpoints ask, rather than carry metadata.** `POST /sparql` is a
     query (read) or an update (write) depending on its body, which endpoint
     metadata cannot express. So every endpoint authorises imperatively, first
     and before anything is read or resolved, through the host's
     `IAuthorizationService`. It is given in `ProtocolOptions` and is
     required: there is no default that would leave a host open by omission.
   - A refusal is a challenge when the caller is not authenticated (`401`
     through the host's default scheme) and a forbid otherwise (`403`). Both
     come with no body that names the dataset or the permission.
   - A host with no authentication — the conformance host, anonymous mode —
     registers the three policies as allowing everyone.
   - The caller's identity reaches the protocol through `ICallerIdentity`,
     which the host implements from the token (ADR 0094). In anonymous mode
     the host binds an identity that names no one.

   These are authorisation types (`Microsoft.AspNetCore.Authorization`) and the
   abstractions' challenge and forbid. No authentication scheme, handler or
   token type is referenced.
5. **The contract vocabulary of `Varve.Protocol` is widened in its project
   file** to `Varve.Store` and `Varve.Sparql`, with the reason given there. Its
   contracts name a `Dataset`, a `CommitResult` and an `Update`. The global
   value stays as ADR 0064 set it.
6. **`System.Uri` is allowed in `Varve.Protocol` and `Varve.Server`**, and
   nowhere below them, by a per-project banned-symbols file. This is ADR 0004's
   own prescription, recorded there by a dated amendment. An IRI is still a
   `Varve.Iri` value. `Uri` is for transport addresses: an authority, a
   metadata address, the request's own address for direct graph identification.

## Alternatives considered

- **Supersede ADR 0060's table: integrations 5, protocols 6, hosts 7.** The
  honest shape if protocols become a family. Rejected for now by the
  maintainer: one seam with one implementation does not pay for a layer. The
  revisit condition above is when it does.
- **One package, `Varve.Server`, with the protocols inside.** Every host would
  have to be this server. The conformance suites would then have to run
  against the composition root, not the protocol, and an embedding application
  could not mount an endpoint.
- **A delegate in place of `ISparqlUpdateExecutor`.** Same coupling, without a
  name to cite with `[Contract]`, which `DD0009` requires of a public delegate
  anyway.
- **Authorisation inside `Varve.Protocol`**, with the claims map as a protocol
  option. It would make the protocol package an authentication package. ADR 0037
  puts the mapping in the server.

## Consequences

- A host that mounts the protocols wires one more thing: the update executor.
  The conformance host does it in one line, as the server does.
- `Varve.Protocol` references the ASP.NET Core shared framework (a
  `FrameworkReference`, no package) and is packable. `Varve.Server` is an
  executable, published Native AOT (ADR 0101).
- The public API baselines of both packages start in this milestone.

## Checks

- **Checked against the accepted ADRs** (0001–0090) and specification 1.5.
  Touches:
  - **0003/0060**: layer 5 for the library, layer 6 for the host, no
    same-layer reference;
  - **0005**: the store learns nothing;
  - **0037**: authentication stays in the server;
  - **0052**: the query choreography is written once, here;
  - **0064**: the contract vocabulary is widened per project.

  No conflict.
- **Layer ownership.** `Varve.Protocol` layer 5; `Varve.Server` layer 6.
- **Analyzer rule.** None new. `DD0001` enforces the layer, and the banned
  symbols enforce `System.Uri` below layer 5.
- **Open questions owned.** None.
