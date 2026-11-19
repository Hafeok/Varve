// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Chapter 1 of the learning track: rows as resources (#94).
// docs/learn/01-rows-as-resources.md quotes this file by its regions, and
// its output by section.

using System;
using System.IO;
using Varve.Learn;
using Varve.Sparql;
using Varve.Sparql.Store;
using Varve.Store;
using Varve.Store.Log;

// region: create
await using Dataset dataset = await Dataset.CreateAsync(
    new MemoryStorage(),
    new DatasetId(Guid.NewGuid()),
    new DatasetOptions { Clock = TimeProvider.System });
// endregion: create

CommitResult loaded = await Load.TriGAsync(dataset, Path.Combine(AppContext.BaseDirectory, "data", "01-rooms.trig"));
Show.Section("loaded");
Console.WriteLine(loaded.Outcome + " at position " + loaded.Position.Value);

using (DatasetView view = dataset.Pin())
{
    Show.Section("one-room");
    // region: one-room
    Show.Query(view, """
        PREFIX : <https://example.org/learn/>
        SELECT ?property ?value
        WHERE { :room-2-01 ?property ?value }
        ORDER BY ?property
        """);
    // endregion: one-room

    Show.Section("rooms-seating-ten");
    // region: rooms-seating-ten
    Show.Query(view, """
        PREFIX rec:  <https://w3id.org/rec#>
        PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>
        SELECT ?room ?name ?seats
        WHERE {
          ?room a rec:Room ;
                rdfs:label ?name ;
                rec:capacity ?capacity .
          ?capacity rec:seatingCapacity ?seats .
          FILTER (?seats >= 10)
        }
        ORDER BY ?name
        """);
    // endregion: rooms-seating-ten

    Show.Section("every-room");
    // region: every-room
    Show.Query(view, """
        PREFIX rec:  <https://w3id.org/rec#>
        PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>
        SELECT ?name ?area ?seats
        WHERE {
          ?room a rec:Room ;
                rdfs:label ?name ;
                rec:area ?a .
          ?a rec:netArea ?area .
          OPTIONAL { ?room rec:capacity ?c . ?c rec:seatingCapacity ?seats }
        }
        ORDER BY ?name
        """);
    // endregion: every-room
}

Show.Section("insert");
// region: insert
CommitResult inserted = await SparqlUpdate.ExecuteAsync(dataset, SparqlParser.ParseUpdate("""
    PREFIX rec:  <https://w3id.org/rec#>
    PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>
    PREFIX xsd:  <http://www.w3.org/2001/XMLSchema#>
    PREFIX :     <https://example.org/learn/>
    INSERT DATA {
      :room-3-03 a rec:Room ;
        rdfs:label "3.03 Phone booth" ;
        rec:area :room-3-03-area ;
        rec:capacity :room-3-03-capacity .
      :room-3-03-area a rec:ArchitectureArea ;
        rec:netArea "3.0"^^xsd:float .
      :room-3-03-capacity a rec:ArchitectureCapacity ;
        rec:seatingCapacity 1 .
    }
    """), new UpdateOptions());
// endregion: insert
Console.WriteLine(inserted.Outcome + " at position " + inserted.Position.Value);
using (DatasetView view = dataset.Pin())
{
    Show.Query(view, """
        PREFIX rec: <https://w3id.org/rec#>
        SELECT (COUNT(?room) AS ?rooms) WHERE { ?room a rec:Room }
        """);
}

Show.Section("nothing-checks-a-value");
// region: nothing-checks-a-value
await using (Dataset scratch = await Dataset.CreateAsync(
    new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }))
{
    CommitResult accepted = await SparqlUpdate.ExecuteAsync(scratch, SparqlParser.ParseUpdate("""
        PREFIX rec:  <https://w3id.org/rec#>
        PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>
        PREFIX :     <https://example.org/learn/>
        INSERT DATA {
          :room-9-99 a rec:Room ;
            rdfs:label "9.99 Nowhere" ;
            rec:capacity :room-9-99-capacity .
          :room-9-99-capacity rec:seatingCapacity "ten" .
        }
        """), new UpdateOptions());
    Console.WriteLine(accepted.Outcome + " at position " + accepted.Position.Value);
    using DatasetView view = scratch.Pin();
    Show.Query(view, """
        PREFIX rec:  <https://w3id.org/rec#>
        SELECT ?seats ?tenOrMore
        WHERE {
          ?c rec:seatingCapacity ?seats .
          BIND (?seats >= 10 AS ?tenOrMore)
        }
        """);
}
// endregion: nothing-checks-a-value

Show.StateIfAsked(args, dataset);
