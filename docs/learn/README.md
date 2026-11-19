# Learning track: Varve for SQL developers

This track is for someone who knows SQL well and RDF not at all. SPARQL's
syntax is the easy part. What trips people up are modelling habits that
are right in a relational database and wrong in a graph. Each chapter
takes one habit and one question, shows the SQL you would write, and sets
Varve's way beside it on the same question.

Every chapter works on the same data, a property portfolio described in
[RealEstateCore](https://w3id.org/rec) (REC), a published vocabulary for
buildings. It starts as a slice small enough to read whole: one site, one
building, two levels, five rooms, a few assets and points. From chapter 3
it grows into a generated portfolio of many buildings. A chapter uses only
the REC terms introduced by then: [the terms, by chapter](terms.md).

Every chapter has a sample under [`samples/learn/`](../../samples/learn/),
a small console project that references the `Varve.*` packages and nothing
else. Every block of output on a page is what its sample printed. CI runs
each sample and fails when the page and the output differ. Chapters 1 to 9
run embedded, in your own process, with no server and no container.

## Chapters

1. [Rows as resources](01-rows-as-resources.md): a room as a resource and
   its properties, against a `rooms` table.
2. [Links and patterns](02-links-and-patterns.md): "every room in the
   building" as a property path, against a recursive CTE.

<!-- unshipped: #94 -->
> **Not in this release.** Chapters 3 to 11 and the appendix are being
> written, in this order, under #94: schema without migrations; commits;
> deletion and history; temporal and audit; CDC and the outbox;
> multi-tenancy and row-level security; named graphs; from embedded to
> server; Varve and domain-driven design; and an appendix, SQL habits that
> hurt here. Chapter 12, ontologies and inference, is written by the
> Inference milestone, #95.

## How to run a chapter

You need the .NET SDK that [`global.json`](../../global.json) names. From a
clone of the repository:

<!-- ci: eng/ci.cs learn — eng/learn.cs --run runs every sample this way and compares its output with the page -->
```bash
dotnet run --project samples/learn/01-rows-as-resources
```
