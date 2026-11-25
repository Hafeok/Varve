# 4. Commits

So far every change has been one statement or one file. Real changes are
several statements that only make sense together. This chapter splits room
3.02 in two and moves its sensor, as one change, and then has two writers
change the same thing at once. In SQL, that is a transaction, `BEGIN … COMMIT`,
and, for two writers, a version column or `SELECT … FOR UPDATE`.

As before, everything up to [In Varve](#in-varve) is standard SPARQL. The
sample, [`samples/learn/04-commits`](../../samples/learn/04-commits/Program.cs),
runs it in Varve, with chapter 3's REC shapes as the schema, and every
answer on this page is what it printed.

## The transaction you would write

<!-- ci: example — the SQL this chapter is set against, for comparison; no SQL runs -->
```sql
BEGIN;
UPDATE rooms SET name = '3.02 Open office, east',
                 net_area = net_area / 2, seats = seats / 2
 WHERE id = '3.02';
INSERT INTO rooms (id, level_id, name, net_area, seats)
SELECT '3.04', level_id, '3.04 Open office, west', net_area, seats
  FROM rooms WHERE id = '3.02';
UPDATE points SET space_id = '3.04' WHERE id = 'temp-3.02';
COMMIT;
```

## Level 3, before

<!-- ci: eng/ci.cs learn — source samples/learn/04-commits/queries/level-3.rq -->
```sparql
PREFIX rec:  <https://w3id.org/rec#>
PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>
PREFIX :     <https://example.org/learn/>

SELECT ?room ?name ?area ?seats ?point
WHERE {
  :level-3 rec:hasPart ?room .
  ?room rdfs:label ?name ;
        rec:area/rec:netArea ?area .
  OPTIONAL { ?room rec:capacity/rec:seatingCapacity ?seats }
  OPTIONAL { ?room rec:hasPoint ?point }
}
ORDER BY ?room
```

<!-- ci: eng/ci.cs learn — output samples/learn/04-commits/expected/before.txt -->
```text
room       | name             | area             | seats | point
-----------+------------------+------------------+-------+-----------------
:room-3-01 | 3.01 Harbour     | 18.0^^xsd:float  | 6     | :point-temp-3-01
:room-3-02 | 3.02 Open office | 290.0^^xsd:float | 36    | :point-temp-3-02
(2 rows)
```

## One request, several operations

SPARQL Update's `DELETE { … } INSERT { … } WHERE { … }` is `UPDATE … WHERE`:
the `WHERE` finds the values, `DELETE` removes the statements built from
them, and `INSERT` adds new ones. Values can be computed with `BIND`, as
`SET net_area = net_area / 2` computes them. Operations are separated by
`;`, and a request is applied as one unit (SPARQL 1.1 Update §3): all of it,
or none of it.

Here is the split, in three operations: halve 3.02 and make the other half
3.04; then move the temperature sensor, which is on the west wall:

<!-- ci: eng/ci.cs learn — source samples/learn/04-commits/queries/split-unchecked.ru -->
```sparql
PREFIX rec:  <https://w3id.org/rec#>
PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>
PREFIX xsd:  <http://www.w3.org/2001/XMLSchema#>
PREFIX :     <https://example.org/learn/>

# 1. Halve room 3.02, and make the other half room 3.04.
DELETE {
  :room-3-02 rdfs:label ?name .
  :room-3-02-area rec:netArea ?area .
  :room-3-02-capacity rec:seatingCapacity ?seats .
}
INSERT {
  :room-3-02 rdfs:label "3.02 Open office, east" .
  :room-3-02-area rec:netArea ?half .
  :room-3-02-capacity rec:seatingCapacity ?halfSeats .

  :level-3 rec:hasPart :room-3-04 .
  :room-3-04 a rec:Room ;
    rdfs:label "3.04 Open office, west" ;
    rec:area :room-3-04-area ;
    rec:capacity :room-3-04-capacity .
  :room-3-04-area a rec:ArchitectureArea ;
    rec:netArea ?half .
  :room-3-04-capacity a rec:ArchitectureCapacity ;
    rec:seatingCapacity ?halfSeats .
}
WHERE {
  :room-3-02 rdfs:label ?name .
  :room-3-02-area rec:netArea ?area .
  :room-3-02-capacity rec:seatingCapacity ?seats .
  BIND (?area / 2 AS ?half)
  BIND (?seats / 2 AS ?halfSeats)
} ;

# 2. The temperature sensor is on the west wall: it moves to 3.04.
DELETE DATA { :room-3-02 rec:hasPoint :point-temp-3-02 } ;
INSERT DATA { :room-3-04 rec:hasPoint :point-temp-3-02 }
```

There is no `BEGIN` and no `COMMIT`: the request is the transaction.

## A request that breaks a shape lands not at all

The sample runs that request, and it is refused:

<!-- ci: eng/ci.cs learn — output samples/learn/04-commits/expected/split-unchecked.txt -->
```text
Rejected, the head still at position 3
focusNode           | path                | value                                            | component
--------------------+---------------------+--------------------------------------------------+-------------------------------
:room-3-02-capacity | rec:seatingCapacity | "18"^^<http://www.w3.org/2001/XMLSchema#decimal> | sh:DatatypeConstraintComponent
:room-3-04-capacity | rec:seatingCapacity | "18"^^<http://www.w3.org/2001/XMLSchema#decimal> | sh:DatatypeConstraintComponent
(2 rows)
room       | name             | area             | seats | point
-----------+------------------+------------------+-------+-----------------
:room-3-01 | 3.01 Harbour     | 18.0^^xsd:float  | 6     | :point-temp-3-01
:room-3-02 | 3.02 Open office | 290.0^^xsd:float | 36    | :point-temp-3-02
(2 rows)
```

In SPARQL, an integer divided by an integer is an `xsd:decimal`: 36 / 2 is
`18.0`, not `18`, and REC says a seating capacity is an `xsd:integer`. Both
halves would break that. (The report writes the value as N-Triples does, with
its datatype's full IRI, and in canonical form: `"18"` is the canonical
`xsd:decimal` for 18.0.) The refusal is of the
whole request: level 3 is as it was, the label and the areas unchanged, and
the sensor still in 3.02, although neither the label nor the move broke
anything.

The fix is a cast, `xsd:integer(?seats / 2)`, the one line in which
[`split.ru`](../../samples/learn/04-commits/queries/split.ru) differs:

<!-- ci: eng/ci.cs learn — output samples/learn/04-commits/expected/split.txt -->
```text
Committed at position 4
room       | name                   | area              | seats | point
-----------+------------------------+-------------------+-------+-----------------
:room-3-01 | 3.01 Harbour           | 18.0^^xsd:float   | 6     | :point-temp-3-01
:room-3-02 | 3.02 Open office, east | 1.45E2^^xsd:float | 18    |
:room-3-04 | 3.04 Open office, west | 1.45E2^^xsd:float | 18    | :point-temp-3-02
(3 rows)
```

`1.45E2` is 145.0, written in `xsd:float`'s canonical form: the division
made a new value, and a computed value is written canonically.

## Two writers

Two people work on level 3 from the same reading. The first moves the
display out of 3.01, into the new room 3.04. The second, seeing a display in
3.01, renames the room for it. Each read the same thing:

<!-- ci: eng/ci.cs learn — source samples/learn/04-commits/queries/displays.rq -->
```sparql
PREFIX rec:  <https://w3id.org/rec#>
PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>
PREFIX :     <https://example.org/learn/>

SELECT ?asset ?room ?name
WHERE {
  VALUES ?asset { :asset-display-2-01 :asset-display-3-01 }
  ?asset rec:locatedIn ?room .
  ?room rdfs:label ?name .
}
ORDER BY ?asset
```

<!-- ci: eng/ci.cs learn — output samples/learn/04-commits/expected/both-read.txt -->
```text
position 4
asset               | room       | name
--------------------+------------+-------------
:asset-display-2-01 | :room-2-01 | 2.01 Fjord
:asset-display-3-01 | :room-3-01 | 3.01 Harbour
(2 rows)
```

Their updates:

<!-- ci: eng/ci.cs learn — source samples/learn/04-commits/queries/move-display.ru -->
```sparql
PREFIX rec: <https://w3id.org/rec#>
PREFIX :    <https://example.org/learn/>

DELETE { :asset-display-3-01 rec:locatedIn ?room }
INSERT { :asset-display-3-01 rec:locatedIn :room-3-04 }
WHERE  { :asset-display-3-01 rec:locatedIn ?room }
```

<!-- ci: eng/ci.cs learn — source samples/learn/04-commits/queries/rename-3-01.ru -->
```sparql
PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>
PREFIX :     <https://example.org/learn/>

DELETE { :room-3-01 rdfs:label ?name }
INSERT { :room-3-01 rdfs:label "3.01 Harbour, with display" }
WHERE  { :room-3-01 rdfs:label ?name }
```

Run one after the other, both succeed, and 3.01 is named for a display it no
longer has. Neither update is wrong; the second was decided on a reading
that is out of date. SQL's usual answer is optimistic concurrency: a version
column, and `UPDATE … WHERE version = :read`, which matches no row if
someone wrote in between. SPARQL has no standard for it. The protocol leaves
concurrency to the store.

## In Varve

### One request is one commit

`SparqlUpdate.ExecuteAsync` evaluates the whole request against one pinned
position and commits its net effect as one commit, or none:

<!-- ci: eng/ci.cs learn — source samples/learn/04-commits/Program.cs#split-unchecked -->
```csharp
CommitResult refused = await SparqlUpdate.ExecuteAsync(
    dataset, SparqlParser.ParseUpdate(Show.File("split-unchecked.ru")), new UpdateOptions());
```

- The commit's validators, here chapter 3's shapes gate, see the request's
  whole effect, and refuse it whole
  ([`docs/spec/sparql-update-store.md`](../spec/sparql-update-store.md) §3).
- A request whose net effect is empty commits nothing and moves no
  position: its outcome is `NoChange`. Chapter 5 shows one.
- Every commit gets the next position, a number. The refused split left the
  head at 3: REC, the rooms and the topology. The split itself is 4.

### Optimistic concurrency is an expected position

Each writer says which position it read, as `UpdateOptions.ExpectedPosition`.
If the head has moved since, the request is not evaluated, and the answer is
`Conflict` with the head it met:

<!-- ci: eng/ci.cs learn — source samples/learn/04-commits/Program.cs#read -->
```csharp
Position read = dataset.Head;
```

<!-- ci: eng/ci.cs learn — source samples/learn/04-commits/Program.cs#writers -->
```csharp
CommitResult first = await SparqlUpdate.ExecuteAsync(
    dataset,
    SparqlParser.ParseUpdate(Show.File("move-display.ru")),
    new UpdateOptions { ExpectedPosition = read });
CommitResult second = await SparqlUpdate.ExecuteAsync(
    dataset,
    SparqlParser.ParseUpdate(Show.File("rename-3-01.ru")),
    new UpdateOptions { ExpectedPosition = read });
```

<!-- ci: eng/ci.cs learn — output samples/learn/04-commits/expected/conflict.txt -->
```text
move-display: Committed at position 5
rename-3-01:  Conflict, the head at position 5
```

The second writer reads again at the head it was given, and sees why its
reading no longer holds:

<!-- ci: eng/ci.cs learn — output samples/learn/04-commits/expected/re-read.txt -->
```text
asset               | room       | name
--------------------+------------+-----------------------
:asset-display-2-01 | :room-2-01 | 2.01 Fjord
:asset-display-3-01 | :room-3-04 | 3.04 Open office, west
(2 rows)
```

Whether to retry is the writer's decision, and here the answer is not to:
3.01 has no display to be named for. `UpdateOptions.ConflictRetries` would
run the request again automatically, which is right only when the request
means the same against any head
([ADR 0057](../adr/0057-sparql-update-one-request-one-commit.md)).

### Where Varve does less than a SQL transaction

- **A transaction is one request.** There is no `BEGIN` held open across
  calls, and no lock. A read and a write are two calls, and the expected
  position is what joins them. A long piece of work reads, decides, and
  writes once, prepared to be told `Conflict`.
- **The expected position is the whole dataset's.** Any commit in between,
  to any room, is a conflict here. Chapter 9 narrows it to the graphs a
  writer read ([ADR 0125](../adr/0125-per-graph-optimistic-concurrency.md)).

To run the sample, from a clone of the repository:

<!-- ci: eng/ci.cs learn — eng/learn.cs --run runs every sample this way and compares its output with the page -->
```bash
dotnet run --project samples/learn/04-commits
```

## Next

Chapter 5, deletion and history, retires a sensor: what `DELETE` removes,
what it leaves pointing at nothing, and what Varve still remembers.
