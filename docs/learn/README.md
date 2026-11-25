# Learning track: RDF and SPARQL for SQL developers, with Varve

This track is for someone who knows SQL well and RDF not at all. It
teaches RDF and SPARQL, the W3C standards, and Varve where it runs them.
SPARQL's syntax is the easy part. What trips people up are modelling habits
that are right in a relational database and wrong in a graph. Each chapter
takes one habit and one question, shows the SQL you would write, and sets
the RDF way beside it on the same question.

Each chapter keeps the two apart. The data and the queries are standard
RDF and SPARQL, and hold in any RDF store. A section headed "In Varve" at
the end of each chapter shows how the sample runs them in Varve, and what
Varve adds. The chapters on commits, history, change feeds, row-level
access and the server are about what Varve does beyond the standards, and
say so.

Every chapter works on the same data, a property portfolio described in
[RealEstateCore](https://w3id.org/rec) (REC), a published vocabulary for
buildings. It starts as a slice small enough to read whole: one site, one
building, two levels, five rooms, a few assets and points. From chapter 3
a generated portfolio of two buildings and 400 rooms joins it. A chapter
uses only the REC terms introduced by then.

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
3. [Schema without migrations](03-schema-without-migrations.md): a new kind
   of sensor with no `ALTER TABLE`, and REC's shapes as the schema, against
   a lookup table and a `CHECK`.
4. [Commits](04-commits.md): splitting a room in one request, and two
   writers on one reading, against `BEGIN … COMMIT` and a version column.
5. [Deletion and history](05-deletion-and-history.md): retiring a sensor,
   what a delete leaves behind and what is remembered, against a foreign
   key and a soft delete.
6. [Temporal and audit](06-temporal-and-audit.md): level 3 before its
   renovation, and who moved a display, against system-versioned tables.

The REC terms each chapter introduces are in [the terms, by
chapter](terms.md).

<!-- unshipped: #94 -->
> **Not in this release.** Chapters 7 to 11 and the appendix are being
> written, in this order, under #94: CDC and the outbox;
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
