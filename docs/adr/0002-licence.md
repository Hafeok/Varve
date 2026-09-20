# 0002 — Licence: Apache-2.0

## Status

Accepted. 2026-09-20.

## Context

`docs/brief.md`, constraint 6: open source, permissive licence. That rules out
copyleft but leaves the choice between the two permissive licences that matter
in the .NET ecosystem, MIT and Apache-2.0.

Two facts about this project bear on the choice. It is a database and a query
engine — the class of software where patents are most often asserted, and where
query optimisation and index structures are the usual subject matter. And it is
intended to be adopted as a set of libraries by other people's products, some of
them commercial, whose legal review will read the licence before the code.

## Decision

Apache-2.0. The full text is in `LICENSE`, verbatim from apache.org.

The deciding clause is §3, the patent grant: every contributor grants users a
patent licence covering their contribution, and that grant terminates for anyone
who initiates patent litigation over the work. MIT grants copyright rights and
says nothing about patents, which leaves a contributor free to contribute code
and later assert a patent reading on it. For a store and a query engine that is
a real exposure, not a theoretical one.

§4(b) also requires modified files to carry a notice of change, which makes a
downstream fork's divergence visible without relying on the fork to say so.

## Alternatives considered

- **MIT.** Shorter, universally understood, and the most common licence in .NET
  open source. Rejected for the patent grant alone. Nothing else separates them
  for our purposes: both permit commercial use, modification and redistribution
  without reciprocity. If the patent consideration did not apply, MIT would win
  on brevity.
- **BSD-3-Clause.** Equivalent to MIT plus a non-endorsement clause. Same patent
  gap. No advantage over MIT here.
- **MPL-2.0.** File-level copyleft. Rejected: constraint 6 says permissive, and
  file-level reciprocity is exactly the term that makes a legal review slow for
  the commercial adopters we want.
- **Dual MIT/Apache-2.0**, as the Rust ecosystem does. Rejected: it exists to
  paper over a compatibility concern that does not apply in .NET, and it doubles
  the licence text every adopter must read for no benefit we can name.

## Consequences

Every source file may carry the §4 boilerplate header. We do not require it —
the licence applies to the work regardless, and per-file headers are noise that
goes stale. `LICENSE` at the root and the `PackageLicenseExpression` in each
packable project are the record.

Packable projects set `<PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>`.
That is due at milestone 3 with the first packable project, not now.

Apache-2.0 is one-way compatible with GPLv3: we can be consumed by GPLv3 work,
but we cannot take in GPLv3 code. Combined with constraint 6's prohibition on
copying from Oxigraph — which is MIT/Apache-2.0 dual licensed, so this is not
the binding reason — the practical rule stands: read other implementations for
behaviour, write our own.

**Open question.** The `LICENSE` appendix carries the unfilled boilerplate
template, `Copyright [yyyy] [name of copyright owner]`. The copyright holder has
not been named — whether that is an individual or a company is a decision the
project owner has to make, and it is not one to guess. Until it is filled in,
the licence still applies; only the recommended notice is incomplete. Resolve
before the first package is published to nuget.org.
