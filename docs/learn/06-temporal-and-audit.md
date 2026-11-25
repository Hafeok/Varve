# 6. Temporal and audit

Level 3 was renovated in March: room 3.02 became two rooms, as in chapter 4.
Since then, the display from 3.01 has been moved twice. This chapter asks
two questions a facilities team asks: "what did level 3 look like before
the renovation?" and "who moved this display, and when?" In SQL the
answers come from audit tables filled by triggers, or from SQL:2011's
system-versioned tables, `FOR SYSTEM_TIME AS OF`.

RDF and SPARQL have nothing to say here. A dataset is a set of statements
as they are now, and the standards define no history. So this chapter is
Varve throughout, and the queries are the ones you already know, asked of
the past. The sample,
[`samples/learn/06-temporal-and-audit`](../../samples/learn/06-temporal-and-audit/Program.cs),
runs it on chapters 1 and 2's slice, and every answer on this page is what
it printed.

## What you would build in SQL

<!-- ci: example — the SQL this chapter is set against, for comparison; no SQL runs -->
```sql
CREATE TABLE rooms (
  id        text PRIMARY KEY,
  name      text NOT NULL,
  net_area  real,
  valid_from timestamp GENERATED ALWAYS AS ROW START,
  valid_to   timestamp GENERATED ALWAYS AS ROW END,
  PERIOD FOR SYSTEM_TIME (valid_from, valid_to)
) WITH SYSTEM VERSIONING;

SELECT * FROM rooms FOR SYSTEM_TIME AS OF TIMESTAMP '2026-02-01 00:00:00';
```

Every table you want history for is declared so, and "who" is a column the
application must fill in on every write.

## A clock you can read twice

Every commit records the time the store's clock gave it. So that this page
can quote the times, the sample runs on a clock it sets by hand, a
`TimeProvider` of its own:

<!-- ci: eng/ci.cs learn — source samples/learn/06-temporal-and-audit/SetClock.cs#clock -->
```csharp
/// <summary>A clock the sample sets by hand, so that every run records the same commit times.</summary>
internal sealed class SetClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    /// <summary>Moves the clock to <paramref name="now"/>.</summary>
    internal void Set(DateTimeOffset now) => _now = now;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;
}
```

<!-- ci: eng/ci.cs learn — source samples/learn/06-temporal-and-audit/Program.cs#dataset -->
```csharp
SetClock clock = new(At("2026-01-05T09:00:00Z"));
await using Dataset dataset = await Dataset.CreateAsync(
    new MemoryStorage(),
    new DatasetId(Guid.NewGuid()),
    new DatasetOptions { Clock = clock });
```

An application passes `TimeProvider.System`, as the other samples do. The
store reads the clock and never the machine's own time, which is also what
makes a commit's bytes the same on every machine given the same requests.

## Who made a commit

The sample loads the slice in January, then commits the renovation as
facilities, on 2 March, with chapter 4's split as the update. An update
names its agent in its metadata, which the commit records beside the
timestamp:

<!-- ci: eng/ci.cs learn — source samples/learn/06-temporal-and-audit/Program.cs#renovation -->
```csharp
clock.Set(At("2026-03-02T10:00:00Z"));
CommitResult renovation = await SparqlUpdate.ExecuteAsync(
    dataset,
    SparqlParser.ParseUpdate(Show.File("renovation.ru")),
    new UpdateOptions { Metadata = new CommitMetadata { Agent = facilities } });
```

Then IT moves the display from 3.01 to the new room 3.04 on 9 March, and
facilities move it on to 3.02 on 14 April. Level 3 today:

<!-- ci: eng/ci.cs learn — output samples/learn/06-temporal-and-audit/expected/today.txt -->
```text
position 5
room       | name                   | area              | seats | point
-----------+------------------------+-------------------+-------+-----------------
:room-3-01 | 3.01 Harbour           | 18.0^^xsd:float   | 6     | :point-temp-3-01
:room-3-02 | 3.02 Open office, east | 1.45E2^^xsd:float | 18    |
:room-3-04 | 3.04 Open office, west | 1.45E2^^xsd:float | 18    | :point-temp-3-02
(3 rows)
```

## Before the renovation

`AsOfTimestampAsync` gives a read-only view of the dataset as it was at a
time: the last commit at or before it. Chapter 4's level 3 query runs on it
unchanged:

<!-- ci: eng/ci.cs learn — source samples/learn/06-temporal-and-audit/Program.cs#as-of-timestamp -->
```csharp
using (DatasetView february = await dataset.AsOfTimestampAsync(new CommitTimestamp(At("2026-02-01T00:00:00Z"))))
{
    Show.Section("before-the-renovation");
    Console.WriteLine("position " + february.Position.Value);
    Show.Query(february, "level-3.rq");
}
```

<!-- ci: eng/ci.cs learn — output samples/learn/06-temporal-and-audit/expected/before-the-renovation.txt -->
```text
position 2
room       | name             | area             | seats | point
-----------+------------------+------------------+-------+-----------------
:room-3-01 | 3.01 Harbour     | 18.0^^xsd:float  | 6     | :point-temp-3-01
:room-3-02 | 3.02 Open office | 290.0^^xsd:float | 36    | :point-temp-3-02
(2 rows)
```

On 1 February the head was position 2, the slice as loaded. `AsOfAsync`,
which chapter 5 used, does the same by position. Nothing was declared in
advance: every statement in the dataset has its history, and every query
can be asked of any time.

## What the renovation changed

`DiffAsync(from, to)` gives the statements added and removed between two
positions. The renovation is one commit, so its change is the diff from
the position before it:

<!-- ci: eng/ci.cs learn — source samples/learn/06-temporal-and-audit/Program.cs#diff -->
```csharp
Position after = renovation.Position;
QuadDelta changed = await dataset.DiffAsync(new Position(after.Value - 1), after);
```

<!-- ci: eng/ci.cs learn — output samples/learn/06-temporal-and-audit/expected/what-the-renovation-changed.txt -->
```text
  | subject             | predicate           | object
--+---------------------+---------------------+-------------------------
+ | :level-3            | rec:hasPart         | :room-3-04
- | :room-3-02          | rdfs:label          | 3.02 Open office
+ | :room-3-02          | rdfs:label          | 3.02 Open office, east
- | :room-3-02          | rec:hasPoint        | :point-temp-3-02
- | :room-3-02-area     | rec:netArea         | 290.0^^xsd:float
+ | :room-3-02-area     | rec:netArea         | 1.45E2^^xsd:float
- | :room-3-02-capacity | rec:seatingCapacity | 36
+ | :room-3-02-capacity | rec:seatingCapacity | 18
+ | :room-3-04          | rdf:type            | rec:Room
+ | :room-3-04          | rdfs:label          | 3.04 Open office, west
+ | :room-3-04          | rec:area            | :room-3-04-area
+ | :room-3-04          | rec:capacity        | :room-3-04-capacity
+ | :room-3-04          | rec:hasPoint        | :point-temp-3-02
+ | :room-3-04-area     | rdf:type            | rec:ArchitectureArea
+ | :room-3-04-area     | rec:netArea         | 1.45E2^^xsd:float
+ | :room-3-04-capacity | rdf:type            | rec:ArchitectureCapacity
+ | :room-3-04-capacity | rec:seatingCapacity | 18
(17 rows)
```

`-` is a statement removed, `+` one added; the table is the sample's
formatting. A changed value is a removal and an addition: RDF has no
"update", only statements that are there or not.

## Who moved the display, and when

Each commit's metadata is read by position with `ReadMetadataAsync`: its
timestamp, its agent, and what caused it. The sample walks the commits,
keeps those whose change touches the display's `rec:locatedIn`, and reads
who made each:

<!-- ci: eng/ci.cs learn — source samples/learn/06-temporal-and-audit/Program.cs#audit -->
```csharp
Show.Section("who-moved-the-display");
List<string[]> rows = [];
using (DatasetView head = dataset.Pin())
{
    for (long p = 1; p <= head.Position.Value; p++)
    {
        QuadDelta delta = await dataset.DiffAsync(new Position(p - 1), new Position(p));
        string? from = null, to = null;
        foreach (Quad quad in delta.Retracted)
        {
            from ??= LocationOfDisplay(head, quad);
        }

        foreach (Quad quad in delta.Asserted)
        {
            to ??= LocationOfDisplay(head, quad);
        }

        if (from is null && to is null)
        {
            continue;
        }

        CommitHeaderTerms header = await dataset.ReadMetadataAsync(new Position(p));
        rows.Add([
            p.ToString(CultureInfo.InvariantCulture),
            header.Timestamp.ToString(),
            header.Agent is { } agent ? Show.Text(agent) : "(none)",
            from ?? "",
            to ?? "",
        ]);
    }
}

Show.Table(["position", "time", "agent", "from", "to"], rows);
```

<!-- ci: eng/ci.cs learn — output samples/learn/06-temporal-and-audit/expected/who-moved-the-display.txt -->
```text
position | time                              | agent                           | from       | to
---------+-----------------------------------+---------------------------------+------------+-----------
2        | 2026-01-05T09:05:00.0000000+00:00 | (none)                          |            | :room-3-01
4        | 2026-03-09T14:30:00.0000000+00:00 | <mailto:it@example.org>         | :room-3-01 | :room-3-04
5        | 2026-04-14T08:15:00.0000000+00:00 | <mailto:facilities@example.org> | :room-3-04 | :room-3-02
(3 rows)
```

Position 2 is the slice's load, which named no agent. The audit trail is
not a table someone remembered to fill: it is the log the store writes
anyway, and the agent is whatever the writer gave. A server sets it from
the caller's identity; chapter 8 is about who may write.

## Where Varve does less than SQL:2011

- **Time is the commit's time only.** Varve answers "what did the dataset
  say at time T", transaction time in SQL:2011's terms. It has no
  application time: SQL:2011's `PERIOD FOR` a business period, "the lease
  runs from May to April", recorded whenever it is entered. In RDF, that
  is data: the lease carries its start and its end as statements, and you
  query them as values, like any other.
- **History is per dataset, not per table.** That is less to declare, and
  also nothing to opt out of: what was written stays in the log (chapter 5
  says what that means for personal data).
- **The walk is the sample's.** Varve has no query language over history:
  the sample asks the log commit by commit. The same log, read as it
  grows, is a change feed (chapter 7).

To run the sample, from a clone of the repository:

<!-- ci: eng/ci.cs learn — eng/learn.cs --run runs every sample this way and compares its output with the page -->
```bash
dotnet run --project samples/learn/06-temporal-and-audit
```

## Next

Chapter 7, CDC and the outbox, reads the same log as it grows: a change
feed of every commit, for the systems that must react to a building's
alarms and send commands to its equipment.
