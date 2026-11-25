# 5. Deletion and history

The temperature sensor in room 3.01 is retired. This chapter deletes it, and
asks three questions: what is gone, what is left pointing at nothing, and
can we still see what it was? In SQL, deleting a row either cascades
(`ON DELETE CASCADE`), is refused by a foreign key, or, when no key was
declared, leaves other rows pointing at an id that no longer exists. Many
schemas avoid the question with a soft delete: a `deleted_at` column and a
`WHERE deleted_at IS NULL` on every query.

Everything up to [In Varve](#in-varve) is standard SPARQL. The sample,
[`samples/learn/05-deletion-and-history`](../../samples/learn/05-deletion-and-history/Program.cs),
runs it in Varve on chapters 1 and 2's slice, and every answer on this page
is what it printed.

## The delete you would write

<!-- ci: example — the SQL this chapter is set against, for comparison; no SQL runs -->
```sql
DELETE FROM points WHERE id = 'temp-3.01';
-- or, keeping it:
UPDATE points SET deleted_at = now() WHERE id = 'temp-3.01';
```

## What is said about the sensor

A resource is not a row. It is whatever statements mention it, and it can
be mentioned in two places: as the subject, in the statements about it, and
as the object, in the statements that link to it.

<!-- ci: eng/ci.cs learn — source samples/learn/05-deletion-and-history/queries/about.rq -->
```sparql
PREFIX :     <https://example.org/learn/>

SELECT ?p ?o
WHERE { :point-temp-3-01 ?p ?o }
ORDER BY ?p
```

<!-- ci: eng/ci.cs learn — source samples/learn/05-deletion-and-history/queries/links-to.rq -->
```sparql
PREFIX :     <https://example.org/learn/>

SELECT ?s ?p
WHERE { ?s ?p :point-temp-3-01 }
ORDER BY ?s
```

<!-- ci: eng/ci.cs learn — output samples/learn/05-deletion-and-history/expected/before.txt -->
```text
position 2
p          | o
-----------+-------------------------
rdf:type   | brick:Temperature_Sensor
rdfs:label | Temperature, 3.01
(2 rows)
s          | p
-----------+-------------
:room-3-01 | rec:hasPoint
(1 row)
```

## Deleting the statements about it

`DELETE WHERE { … }` deletes every statement its pattern matches. It is
`DELETE … WHERE` with the same pattern in both places:

<!-- ci: eng/ci.cs learn — source samples/learn/05-deletion-and-history/queries/retire-statements.ru -->
```sparql
PREFIX :     <https://example.org/learn/>

DELETE WHERE { :point-temp-3-01 ?p ?o }
```

<!-- ci: eng/ci.cs learn — output samples/learn/05-deletion-and-history/expected/retire-statements.txt -->
```text
Committed at position 3
p | o
--+--
(0 rows)
s          | p
-----------+-------------
:room-3-01 | rec:hasPoint
(1 row)
point            | type | name
-----------------+------+-----
:point-temp-3-01 |      |
(1 row)
```

The sensor's type and label are gone, but room 3.01 still says it has the
point. Nothing stopped the delete, and nothing cascaded: there is no foreign
key (chapter 2), so the link is just another statement, and it survived.
The room's points now include one about which nothing else is known.

## Deleting the links to it

So deleting a resource means deleting the statements where it is the
object too. With no foreign key to cascade, that is the writer's job:

<!-- ci: eng/ci.cs learn — source samples/learn/05-deletion-and-history/queries/retire-links.ru -->
```sparql
PREFIX :     <https://example.org/learn/>

DELETE WHERE { ?s ?p :point-temp-3-01 }
```

<!-- ci: eng/ci.cs learn — output samples/learn/05-deletion-and-history/expected/retire-links.txt -->
```text
Committed at position 4
s | p
--+--
(0 rows)
point | type | name
------+------+-----
(0 rows)
```

Done properly, it is one request with both deletes, so that no reader ever
sees the half-deleted sensor (chapter 4):

<!-- ci: eng/ci.cs learn — source samples/learn/05-deletion-and-history/queries/retire.ru -->
```sparql
PREFIX :     <https://example.org/learn/>

DELETE WHERE { :point-temp-3-01 ?p ?o } ;
DELETE WHERE { ?s ?p :point-temp-3-01 }
```

A shape can describe what a link must point to: REC's `rec:hasPoint` asks
for a `brick:Point`, and after the first delete, room 3.01's link points to
something that is not one. Validated against chapter 3's shapes, the
half-deleted state does not conform.

## In Varve

### Deleting what is not there changes nothing

The sample runs `retire.ru` after the sensor is gone. Both deletes match
nothing, so the request's net effect is empty:

<!-- ci: eng/ci.cs learn — source samples/learn/05-deletion-and-history/Program.cs#again -->
```csharp
CommitResult again = await SparqlUpdate.ExecuteAsync(
    dataset, SparqlParser.ParseUpdate(Show.File("retire.ru")), new UpdateOptions());
```

<!-- ci: eng/ci.cs learn — output samples/learn/05-deletion-and-history/expected/again.txt -->
```text
NoChange, the head still at position 4
```

`NoChange` is not an error. No commit is written and no position is used
([ADR 0010](../adr/0010-commit-model-and-effective-deltas.md)): a commit
records only what it actually changed.

### A delete is recorded, not destroyed

Varve keeps every commit, deletions included, and never rewrites one
([`docs/spec/log-and-projection-model.md`](../spec/log-and-projection-model.md)
§4). So the dataset as it was at any earlier position can be read back. The
sample noted the head before the sensor was retired:

<!-- ci: eng/ci.cs learn — source samples/learn/05-deletion-and-history/Program.cs#before -->
```csharp
Position before = dataset.Head;
```

and reads that position again at the end, with the same queries:

<!-- ci: eng/ci.cs learn — source samples/learn/05-deletion-and-history/Program.cs#as-of -->
```csharp
using (DatasetView then = await dataset.AsOfAsync(before))
{
    Show.Section("as-of");
    Console.WriteLine("as of position " + then.Position.Value);
    Show.Query(then, "about.rq");
    Show.Query(then, "links-to.rq");
}
```

<!-- ci: eng/ci.cs learn — output samples/learn/05-deletion-and-history/expected/as-of.txt -->
```text
as of position 2
p          | o
-----------+-------------------------
rdf:type   | brick:Temperature_Sensor
rdfs:label | Temperature, 3.01
(2 rows)
s          | p
-----------+-------------
:room-3-01 | rec:hasPoint
(1 row)
```

`AsOfAsync` returns a read-only view of the dataset at that position, and a
query runs on it as on the head (§6, R2). There is no `deleted_at` column to
remember: every query can be asked of the past. Chapter 6 is about that.

### Where Varve does more, and less, than a SQL developer assumes

- **More:** a `DELETE` is not destruction. The history keeps what was
  deleted, as a soft delete would, without a column or a filter.
- **Less:** so a `DELETE` does not remove data from the store. For personal
  data, that is not enough: a person's right to erasure needs it gone from
  every position, not just the head.

<!-- unshipped: #203 -->
> **Not in this release.** Erasing a data subject's personal data, so that
> no position shows it again, is erasure mode, #203. It is designed as
> crypto-shredding: the data is stored encrypted under a key per data
> subject, and erasing destroys the key
> ([ADR 0023](../adr/0023-erasure-and-access-requests.md), §9 of the log
> model).

To run the sample, from a clone of the repository:

<!-- ci: eng/ci.cs learn — eng/learn.cs --run runs every sample this way and compares its output with the page -->
```bash
dotnet run --project samples/learn/05-deletion-and-history
```

## Next

Chapter 6, temporal and audit, reads the past on purpose: level 3 before
its renovation, what the renovation changed, and who moved an asset, and
when.
