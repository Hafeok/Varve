# 3. Schema without migrations

Chapters 1 and 2 used a handful of rooms written by hand. From here, the
track works on a generated portfolio: a region, a site and two buildings of
eight levels each, with 400 rooms, 1,200 assets and 3,215 points. This
chapter asks what happens to the schema as a portfolio like that grows new
kinds of thing. In a relational database, a new kind of thing is a
migration: a column, a lookup row or an enum value, and a `CHECK` that
keeps the data in line. In RDF, a new kind of thing is one more statement,
and the vocabulary's class hierarchy is data you can query. The schema, the
part that refuses bad data, is a separate layer: SHACL shapes.

As in chapters 1 and 2, everything up to [In Varve](#in-varve) is standard
RDF, SPARQL and SHACL. The sample,
[`samples/learn/03-schema-without-migrations`](../../samples/learn/03-schema-without-migrations/Program.cs),
runs it in Varve, and every answer on this page is what it printed.

## The schema you would write

A table per kind of thing, a column per kind of fact, and a lookup table
for the kinds of sensor:

<!-- ci: example — the relational schema this chapter is set against, for comparison; no SQL runs -->
```sql
CREATE TABLE point_types (
  id    text PRIMARY KEY,           -- 'temperature', 'co2', ...
  is_sensor boolean NOT NULL
);

CREATE TABLE points (
  id       integer PRIMARY KEY,
  space_id integer REFERENCES spaces (id),
  type_id  text NOT NULL REFERENCES point_types (id)
);

ALTER TABLE capacities
  ADD CONSTRAINT seats_is_a_number CHECK (seating_capacity >= 0);
```

Adding a kind of sensor is an `INSERT` into `point_types`, if someone
thought to make it a table, or an `ALTER TYPE … ADD VALUE` if it was an
enum. Asking "which points are sensors?" needs `is_sensor`, kept by hand.

## The portfolio

The portfolio is
[`samples/learn/data/03-portfolio.trig`](../../samples/learn/data/03-portfolio.trig),
generated and never edited by hand. Every entity is a resource with a type
and a label, linked as in chapter 2. Its first room:

<!-- ci: example — an excerpt of samples/learn/data/03-portfolio.trig, which the sample loads whole -->
```trig
room:0 a rec:Pantry ;
  rdfs:label "Room 0" ;
  rec:hasPoint point:6 ;
  rec:hasPoint point:7 ;
  rec:isLocationOf asset:0 ;
  rec:isLocationOf asset:1 ;
  rec:isLocationOf asset:2 ;
  rec:isPartOf level:0 .
```

The room is a `rec:Pantry`, not a `rec:Room`. REC has a class for each kind
of room, and the generator uses them. Which kinds are there? In SQL you
would read the catalog. In RDF the types are data, so you ask the data:

<!-- ci: eng/ci.cs learn — source samples/learn/03-schema-without-migrations/queries/types.rq -->
```sparql
SELECT ?type (COUNT(?thing) AS ?things)
WHERE { ?thing a ?type }
GROUP BY ?type
ORDER BY ?type
```

<!-- ci: eng/ci.cs learn — output samples/learn/03-schema-without-migrations/expected/types.txt -->
```text
type                           | things
-------------------------------+-------
brick:Air_Temperature_Setpoint | 619
brick:CO2_Sensor               | 651
brick:Humidity_Sensor          | 641
brick:Occupancy_Count_Sensor   | 636
brick:Temperature_Sensor       | 672
rec:ArchitectureArea           | 5
rec:ArchitectureCapacity       | 4
rec:Asset                      | 3
rec:Bathroom                   | 43
rec:Building                   | 3
rec:Cabinet                    | 195
rec:ConferenceRoom             | 55
rec:Desk                       | 206
rec:Hallway                    | 41
rec:IAQSensorEquipment         | 188
rec:Kitchenette                | 53
rec:Lamp                       | 204
rec:Level                      | 18
rec:OccupancySensorEquipment   | 194
rec:Office                     | 71
rec:OfficeChair                | 213
rec:OfficeLandscape            | 38
rec:Pantry                     | 61
rec:PhoneBooth                 | 47
rec:Region                     | 1
rec:Room                       | 5
rec:Site                       | 2
(27 rows)
```

The `rec:Room`, `rec:Asset` and two `rec:Building`s too many are chapter 1
and 2's slice, loaded beside the portfolio. Nothing declared these 27
types before the data used them: a type is whatever a statement's object
says it is.

## Which sensors does building 1 have?

The points of building 1, by type, are a chapter 2 query: walk down from the
building with `rec:hasPart+`, then to the points of each room and of each
asset in it:

<!-- ci: eng/ci.cs learn — source samples/learn/03-schema-without-migrations/queries/point-types.rq -->
```sparql
PREFIX rec:      <https://w3id.org/rec#>
PREFIX building: <https://example.org/rec/building/>

SELECT ?type (COUNT(?point) AS ?points)
WHERE {
  building:1 rec:hasPart+ ?space .
  ?space (rec:hasPoint | rec:isLocationOf/rec:hasPoint) ?point .
  ?point a ?type .
}
GROUP BY ?type
ORDER BY ?type
```

<!-- ci: eng/ci.cs learn — output samples/learn/03-schema-without-migrations/expected/point-types.txt -->
```text
type                           | points
-------------------------------+-------
brick:Air_Temperature_Setpoint | 313
brick:CO2_Sensor               | 342
brick:Humidity_Sensor          | 298
brick:Occupancy_Count_Sensor   | 320
brick:Temperature_Sensor       | 334
(5 rows)
```

A setpoint is not a sensor: it is the value a controller aims for. To ask
for sensors only, you could list the four sensor types in a `VALUES`
clause, which is the `is_sensor` column kept by hand again. RDF has a
better answer: the vocabulary says which types are sensors.

### The class hierarchy is data

REC and Brick are RDF too. Each class is a resource, and
`rdfs:subClassOf` says which class it is a kind of. The sample loads REC 4,
Brick included, into a graph of its own, named `:rec`, beside the data.
(Chapter 9 is about graphs; for now, `GRAPH :rec { … }` reads that graph,
and a pattern outside it reads the data.) The parents of a class are a
property path away, `rdfs:subClassOf*`, zero or more steps up:

<!-- ci: eng/ci.cs learn — source samples/learn/03-schema-without-migrations/queries/parents.rq -->
```sparql
PREFIX brick: <https://brickschema.org/schema/Brick#>
PREFIX rdfs:  <http://www.w3.org/2000/01/rdf-schema#>
PREFIX :      <https://example.org/learn/>

SELECT ?parent
WHERE {
  GRAPH :rec { brick:CO2_Level_Sensor rdfs:subClassOf* ?parent }
  FILTER isIRI(?parent)
}
ORDER BY ?parent
```

<!-- ci: eng/ci.cs learn — output samples/learn/03-schema-without-migrations/expected/parents.txt -->
```text
parent
------------------------
brick:Air_Quality_Sensor
brick:CO2_Level_Sensor
brick:CO2_Sensor
brick:Class
brick:Entity
brick:Point
brick:Sensor
(7 rows)
```

`brick:CO2_Level_Sensor`, a type the portfolio doesn't use yet, is a
`brick:CO2_Sensor`, which is a `brick:Air_Quality_Sensor`, which is a
`brick:Sensor`, which is a `brick:Point`. (`FILTER isIRI` drops the blank
nodes Brick also uses as superclasses, for its OWL restrictions.) So the
sensors of building 1 are its points whose type is, by zero or more steps,
a `brick:Sensor`:

<!-- ci: eng/ci.cs learn — source samples/learn/03-schema-without-migrations/queries/sensors.rq -->
```sparql
PREFIX rec:      <https://w3id.org/rec#>
PREFIX brick:    <https://brickschema.org/schema/Brick#>
PREFIX rdfs:     <http://www.w3.org/2000/01/rdf-schema#>
PREFIX :         <https://example.org/learn/>
PREFIX building: <https://example.org/rec/building/>

SELECT ?type (COUNT(?point) AS ?sensors)
WHERE {
  building:1 rec:hasPart+ ?space .
  ?space (rec:hasPoint | rec:isLocationOf/rec:hasPoint) ?point .
  ?point a ?type .
  GRAPH :rec { ?type rdfs:subClassOf* brick:Sensor }
}
GROUP BY ?type
ORDER BY ?type
```

<!-- ci: eng/ci.cs learn — output samples/learn/03-schema-without-migrations/expected/sensors.txt -->
```text
type                         | sensors
-----------------------------+--------
brick:CO2_Sensor             | 342
brick:Humidity_Sensor        | 298
brick:Occupancy_Count_Sensor | 320
brick:Temperature_Sensor     | 334
(4 rows)
```

The setpoints are gone, and the query names no sensor type.

## A new kind of sensor, without a migration

Building 1 gets its first CO2 level sensor, in room 200. In SQL, that is a
row in `point_types` first, and a migration if the type was an enum. In RDF
it is the statements themselves:

<!-- ci: eng/ci.cs learn — source samples/learn/03-schema-without-migrations/queries/new-type.ru -->
```sparql
PREFIX brick: <https://brickschema.org/schema/Brick#>
PREFIX rec:   <https://w3id.org/rec#>
PREFIX rdfs:  <http://www.w3.org/2000/01/rdf-schema#>
PREFIX room:  <https://example.org/rec/room/>
PREFIX point: <https://example.org/rec/point/>

INSERT DATA {
  point:co2-level-200 a brick:CO2_Level_Sensor ;
    rdfs:label "CO2 level, room 200" .
  room:200 rec:hasPoint point:co2-level-200 .
}
```

The same sensors query, unchanged, finds it, because the vocabulary already
says what a `brick:CO2_Level_Sensor` is:

<!-- ci: eng/ci.cs learn — output samples/learn/03-schema-without-migrations/expected/new-type.txt -->
```text
Committed
type                         | sensors
-----------------------------+--------
brick:CO2_Level_Sensor       | 1
brick:CO2_Sensor             | 342
brick:Humidity_Sensor        | 298
brick:Occupancy_Count_Sensor | 320
brick:Temperature_Sensor     | 334
(5 rows)
```

Nothing changed shape: no table, column or type was altered, and no other
query is affected. A type that no vocabulary defines would be stored just
the same, and then the hierarchy query would not count it. That freedom is
what the next section limits.

## Shapes are the schema

In chapter 1, `rec:seatingCapacity "ten"` was stored without complaint:
RDF has no columns, so nothing checks a value. What does the checking is
**SHACL**, the W3C's shapes language. A shape is itself RDF: it says which
nodes it targets, usually by class, and what must hold of them. REC ships
its shapes in the same files as its classes. For a
`rec:ArchitectureCapacity`, REC says:

<!-- ci: example — an excerpt of REC 4.1.0's testdata/rec/v4.1.0/rec.ttl, the class rec:ArchitectureCapacity with its comment, label, superclass and other property shape left out -->
```turtle
rec:ArchitectureCapacity
  rdf:type rdfs:Class ;
  rdf:type sh:NodeShape ;
  sh:property [
      rdf:type sh:PropertyShape ;
      sh:path rec:seatingCapacity ;
      sh:datatype xsd:integer ;
      sh:maxCount 1 ;
      sh:name "seating capacity" ;
    ] ;
.
```

A shapes validator reads the data and the shapes, and writes a validation
report: the data conforms, or a list of results, each naming the focus node,
the path, the value and the constraint broken. On its own, SHACL reports.
Whether a report refuses a write is the store's choice, and is where
[In Varve](#in-varve) picks up.

Three things a SQL developer would expect from a schema do not follow from
SHACL itself:

- **Shapes constrain statements, never a table's shape.** Nothing has a
  column to add or drop. A shape only says what must be true of the
  statements about a node.
- **A shape targets nodes by what the data says they are.** A capacity with
  no `rdf:type` is no `rec:ArchitectureCapacity`, so its shape does not
  apply to it. The refusal of chapter 1's update, below, shows this.
- **REC's shapes are open.** None of them is `sh:closed`, so a property no
  shape mentions is allowed. A misspelt predicate is not refused: it is
  stored as one more fact that no query asks for.

## In Varve

### Binding the shapes

The sample loads REC 4 into the graph `:rec`, and binds it as the
dataset's shapes: a **shapes binding** in `reject` mode, validating the
default graph, and the **shapes gate** that enforces it at commit time.
`rec` is the graph's IRI, `https://example.org/learn/rec`.

<!-- ci: eng/ci.cs learn — source samples/learn/Shared/Rec/Gated.cs#gate -->
```csharp
ShapesBinding binding = new(
    RdfTerm.Iri("https://example.org/learn/rec-binding"u8),
    rec,
    GraphScope.Of([], [], DefaultGraphAccess.Included),
    ValidationMode.Reject)
{
    SubClassOfInShapesGraph = true,
};
ShapesGate gate = new([binding]);

Dataset dataset = await Dataset.CreateAsync(
    new MemoryStorage(),
    new DatasetId(Guid.NewGuid()),
    new DatasetOptions { Clock = clock, Validators = [gate] });
```

- The shapes graph is a named graph of the same dataset
  ([ADR 0170](../adr/0170-the-shapes-binding-vocabulary.md)),
  so the shapes are versioned with the data: a change to them is a commit
  like any other.
- `SubClassOfInShapesGraph` reads `rdfs:subClassOf` from the shapes graph
  as well as the data, SHACL 1.2's §6.4 parameter. Without it, a point
  typed `brick:Temperature_Sensor` would not count as the `brick:Point`
  that REC's `rec:hasPoint` shape asks for, because the data does not say
  so; the vocabulary does.
- With no options, the gate validates with SHACL-SPARQL, which Brick's
  shapes use ([ADR 0176](../adr/0176-the-rec-gate.md)).

REC is loaded as one commit, the four files as the REC gate composes them
([ADR 0176](../adr/0176-the-rec-gate.md)):
[`rec.ttl`](../../testdata/rec/v4.1.0/rec.ttl),
`Brick+patches.ttl`, `recimports.ttl` and the gate's
[`composition.ttl`](../../testdata/rec/gate/composition.ttl). One commit,
because the shapes must load as a whole: Brick's SPARQL constraints need
the prefixes the composition declares, and a bound shapes graph that does
not load refuses the commit that made it so.

<!-- ci: eng/ci.cs learn — source samples/learn/Shared/Rec/Gated.cs#load-rec -->
```csharp
string[] recFiles = ["rec.ttl", "Brick+patches.ttl", "recimports.ttl", "composition.ttl"];
CommitResult vocabulary = await Load.TurtleIntoAsync(
    dataset, rec, [.. recFiles.Select(file => Path.Combine(AppContext.BaseDirectory, "rec", file))]);
```

<!-- ci: eng/ci.cs learn — source samples/learn/Shared/Load.cs#load-graph -->
```csharp
/// <summary>
/// Parses the Turtle files <paramref name="paths"/> and commits every
/// triple in them into the named graph <paramref name="graph"/>, as one
/// commit: their RDF merge, each file's blank nodes kept its own.
/// </summary>
internal static async ValueTask<CommitResult> TurtleIntoAsync(Dataset dataset, RdfTerm graph, params string[] paths)
{
    CommitRequestBuilder commit = new();
    for (int file = 0; file < paths.Length; file++)
    {
        string own = "f" + file.ToString(CultureInfo.InvariantCulture) + "-";
        RdfTerm Own(RdfTerm term) => term.Kind == RdfTermKind.BlankNode
            ? RdfTerm.BlankNode(Encoding.UTF8.GetBytes(own + Encoding.UTF8.GetString(term.Lexical)))
            : term;
        ParseResult parsed = TurtleParser.Parse(
            File.ReadAllBytes(paths[file]),
            (in QuadView quad) => commit.Assert(
                Own(quad.Subject.Materialise()),
                quad.Predicate.Materialise(),
                Own(quad.Object.Materialise()),
                graph),
            new TurtleOptions { Syntax = RdfSyntax.Turtle });
        if (!parsed.Succeeded)
        {
            throw new InvalidDataException(paths[file] + ": " + parsed.FirstError);
        }
    }

    return await dataset.CommitAsync(commit);
}
```

Both are in [`Shared/Rec/Gated.cs`](../../samples/learn/Shared/Rec/Gated.cs),
which chapter 4 uses too. The sample creates its dataset with it, then loads
the slice and the portfolio through the gate, each file as one commit:

<!-- ci: eng/ci.cs learn — source samples/learn/03-schema-without-migrations/Program.cs#create -->
```csharp
await using Dataset dataset = await Gated.CreateAsync(TimeProvider.System);
await Gated.LoadAsync(dataset, "01-rooms.trig", "02-topology.trig", "03-portfolio.trig");
```

They conform. Every query above ran on that dataset, and the new sensor was
committed through it.

### A refused commit

Now chapter 1's mistake, on room 2.01's capacity, which is typed:

<!-- ci: eng/ci.cs learn — source samples/learn/03-schema-without-migrations/queries/refused.ru -->
```sparql
PREFIX rec:  <https://w3id.org/rec#>
PREFIX :     <https://example.org/learn/>

INSERT DATA { :room-2-01-capacity rec:seatingCapacity "ten" . }
```

<!-- ci: eng/ci.cs learn — source samples/learn/03-schema-without-migrations/Program.cs#refused -->
```csharp
CommitResult refused = await SparqlUpdate.ExecuteAsync(
    dataset, SparqlParser.ParseUpdate(Show.File("refused.ru")), new UpdateOptions());
```

Nothing is committed. The sample prints the result's outcome, its reason
and each term of its report, as the store returned them:

<!-- ci: eng/ci.cs learn — output samples/learn/03-schema-without-migrations/expected/refused.txt -->
```text
Outcome: Rejected
Reason:  (none)
Report:  2 terms

<https://w3id.org/varve/problems/shapes-violated>

a literal of datatype <http://www.w3.org/1999/02/22-rdf-syntax-ns#JSON>, whose lexical form is:
{"binding":"<https://example.org/learn/rec-binding>","detail":"2 violations the pinned position does not have, among the focus nodes this commit affects (ADR 0169).","findings":[{"component":"<http://www.w3.org/ns/shacl#MaxCountConstraintComponent>","focusNode":"<https://example.org/learn/room-2-01-capacity>","messages":[],"path":"<https://w3id.org/rec#seatingCapacity>","severity":"<http://www.w3.org/ns/shacl#Violation>","sourceShape":"_:b28"},{"component":"<http://www.w3.org/ns/shacl#DatatypeConstraintComponent>","focusNode":"<https://example.org/learn/room-2-01-capacity>","messages":[],"path":"<https://w3id.org/rec#seatingCapacity>","severity":"<http://www.w3.org/ns/shacl#Violation>","sourceShape":"_:b28","value":"\"ten\""}],"report":"_:c14n0 <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <http://www.w3.org/ns/shacl#ValidationReport> .\n_:c14n0 <http://www.w3.org/ns/shacl#conforms> \"false\"^^<http://www.w3.org/2001/XMLSchema#boolean> .\n_:c14n0 <http://www.w3.org/ns/shacl#result> _:c14n2 .\n_:c14n0 <http://www.w3.org/ns/shacl#result> _:c14n3 .\n_:c14n0 <https://w3id.org/varve/ns#importNotFollowed> <http://datashapes.org/dash> .\n_:c14n0 <https://w3id.org/varve/ns#importNotFollowed> <https://brickschema.org/schema/1.3/Brick> .\n_:c14n0 <https://w3id.org/varve/ns#importNotFollowed> <https://w3id.org/rec/brickpatches> .\n_:c14n0 <https://w3id.org/varve/ns#importNotFollowed> <https://w3id.org/rec> .\n_:c14n2 <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <http://www.w3.org/ns/shacl#ValidationResult> .\n_:c14n2 <http://www.w3.org/ns/shacl#focusNode> <https://example.org/learn/room-2-01-capacity> .\n_:c14n2 <http://www.w3.org/ns/shacl#resultPath> <https://w3id.org/rec#seatingCapacity> .\n_:c14n2 <http://www.w3.org/ns/shacl#resultSeverity> <http://www.w3.org/ns/shacl#Violation> .\n_:c14n2 <http://www.w3.org/ns/shacl#sourceConstraintComponent> <http://www.w3.org/ns/shacl#DatatypeConstraintComponent> .\n_:c14n2 <http://www.w3.org/ns/shacl#sourceShape> _:c14n1 .\n_:c14n2 <http://www.w3.org/ns/shacl#value> \"ten\" .\n_:c14n3 <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <http://www.w3.org/ns/shacl#ValidationResult> .\n_:c14n3 <http://www.w3.org/ns/shacl#focusNode> <https://example.org/learn/room-2-01-capacity> .\n_:c14n3 <http://www.w3.org/ns/shacl#resultPath> <https://w3id.org/rec#seatingCapacity> .\n_:c14n3 <http://www.w3.org/ns/shacl#resultSeverity> <http://www.w3.org/ns/shacl#Violation> .\n_:c14n3 <http://www.w3.org/ns/shacl#sourceConstraintComponent> <http://www.w3.org/ns/shacl#MaxCountConstraintComponent> .\n_:c14n3 <http://www.w3.org/ns/shacl#sourceShape> _:c14n1 .\n","shapesChanged":false,"shapesGraph":"<https://example.org/learn/rec>"}
```

- `Outcome` is `Rejected`: a validator refused the commit, and the head
  did not move. `Reason` is empty; the report says why.
- The report's first term is the problem type,
  `https://w3id.org/varve/problems/shapes-violated`. The second is one
  `rdf:JSON` literal, in canonical form, with these members:
  - `binding` and `shapesGraph`: which binding refused, and its shapes.
  - `detail`: how many violations the commit adds. "The pinned position" is
    the head the commit was made against.
  - `findings`: one per violation, with the constraint component, the focus
    node, the path, the value when there is one, the severity, the shape
    (here a blank node of REC's), and its messages (REC gives none).
  - `report`: the SHACL validation report itself, as canonical N-Quads.
    Its four `varve:importNotFollowed` lines are REC's `owl:imports`: the
    gate reports them and never fetches them
    ([ADR 0151](../adr/0151-shacl-inputs-failures-and-instances.md)).
  - `shapesChanged`: whether this commit changed the shapes. It did not.

The same findings, as a table the sample formats itself:

<!-- ci: eng/ci.cs learn — output samples/learn/03-schema-without-migrations/expected/findings.txt -->
```text
focusNode           | path                | value | component
--------------------+---------------------+-------+-------------------------------
:room-2-01-capacity | rec:seatingCapacity |       | sh:MaxCountConstraintComponent
:room-2-01-capacity | rec:seatingCapacity | "ten" | sh:DatatypeConstraintComponent
(2 rows)
```

Two violations: `"ten"` is not an `xsd:integer`, and the capacity now has
two seating capacities, 10 and `"ten"`, where REC allows one. The commit
that chapter 1 stored without a schema is refused with one.

Chapter 1's own update, the new room 9.99 whose capacity has no type, is
refused too, but not for `"ten"`:

<!-- ci: eng/ci.cs learn — output samples/learn/03-schema-without-migrations/expected/untyped.txt -->
```text
Rejected
focusNode  | path         | value               | component
-----------+--------------+---------------------+----------------------------
:room-9-99 | rec:capacity | :room-9-99-capacity | sh:ClassConstraintComponent
(1 row)
```

`rec:capacity` must point to a `rec:ArchitectureCapacity`, and nothing says
`:room-9-99-capacity` is one. Since it is not one, the capacity's own shape
never looks at `"ten"`. Typing the node is what brings it under its shape.

And a property no shape mentions, in the sample's own namespace, is
accepted, because REC's shapes are open:

<!-- ci: eng/ci.cs learn — source samples/learn/03-schema-without-migrations/queries/open-shapes.ru -->
```sparql
PREFIX :     <https://example.org/learn/>

INSERT DATA { :room-2-01 :seats 10 . }
```

<!-- ci: eng/ci.cs learn — output samples/learn/03-schema-without-migrations/expected/open-shapes.txt -->
```text
Committed
```

### Where Varve does less than a schema

- **Only what a commit touches is checked.** The gate validates the focus
  nodes the commit affects, and refuses only violations the head did not
  already have ([ADR 0169](../adr/0169-commit-time-gating-under-a-shapes-binding.md)).
  `ALTER TABLE … ADD
  CONSTRAINT` checks every existing row; binding shapes, or changing them,
  is never refused for the data already there.
- **So violations can stay.** In `reject` mode, a commit never adds a
  violation, but one that was there when the shapes were bound stays until
  someone fixes it. A server keeps a validation report of the whole
  dataset; chapter 10 shows it.
- **Open shapes accept what they don't mention.** That is REC's choice,
  not Varve's: a closed shape would refuse `:seats`.

To run the sample, from a clone of the repository:

<!-- ci: eng/ci.cs learn — eng/learn.cs --run runs every sample this way and compares its output with the page -->
```bash
dotnet run --project samples/learn/03-schema-without-migrations
```

## Next

Chapter 4, commits, changes several things at once: it splits a room in
two and moves its assets and points in one request, which lands whole or
not at all, and shows what happens when two writers change the same thing.
