// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Chapter 2 of the learning track: links and patterns (#94).
// docs/learn/02-links-and-patterns.md quotes this file by its regions, and
// its output by section.

using System;
using System.IO;
using Varve.Learn;
using Varve.Sparql;
using Varve.Sparql.Store;
using Varve.Store;
using Varve.Store.Log;

await using Dataset dataset = await Dataset.CreateAsync(
    new MemoryStorage(),
    new DatasetId(Guid.NewGuid()),
    new DatasetOptions { Clock = TimeProvider.System });

Show.Section("loaded");
// region: load
CommitResult rooms = await Load.TriGAsync(dataset, Path.Combine(AppContext.BaseDirectory, "data", "01-rooms.trig"));
CommitResult topology = await Load.TriGAsync(dataset, Path.Combine(AppContext.BaseDirectory, "data", "02-topology.trig"));
// endregion: load
Console.WriteLine("rooms:    " + rooms.Outcome + " at position " + rooms.Position.Value);
Console.WriteLine("topology: " + topology.Outcome + " at position " + topology.Position.Value);

using (DatasetView view = dataset.Pin())
{
    Show.Section("rooms-in-the-building");
    // region: rooms-in-the-building
    Show.Query(view, """
        PREFIX rec:  <https://w3id.org/rec#>
        PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>
        PREFIX :     <https://example.org/learn/>
        SELECT ?level ?name
        WHERE {
          :building-hq rec:hasPart+ ?room .
          ?room a rec:Room ;
                rdfs:label ?name .
          ?level rec:hasPart ?room .
        }
        ORDER BY ?name
        """);
    // endregion: rooms-in-the-building

    Show.Section("assets-on-level-3");
    // region: assets-on-level-3
    Show.Query(view, """
        PREFIX rec:  <https://w3id.org/rec#>
        PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>
        PREFIX :     <https://example.org/learn/>
        SELECT ?asset ?name ?room
        WHERE {
          :level-3 rec:hasPart+ ?room .
          ?asset rec:locatedIn ?room ;
                 rdfs:label ?name .
        }
        ORDER BY ?name
        """);
    // endregion: assets-on-level-3

    Show.Section("points-in-the-building");
    // region: points-in-the-building
    Show.Query(view, """
        PREFIX rec:  <https://w3id.org/rec#>
        PREFIX :     <https://example.org/learn/>
        SELECT ?space ?point ?type
        WHERE {
          :building-hq rec:hasPart+ ?space .
          ?space (rec:hasPoint | ^rec:locatedIn/rec:hasPoint) ?point .
          ?point a ?type .
        }
        ORDER BY ?space ?point
        """);
    // endregion: points-in-the-building

    Show.Section("where-is-3-01");
    // region: where-is-3-01
    Show.Query(view, """
        PREFIX rec:  <https://w3id.org/rec#>
        PREFIX :     <https://example.org/learn/>
        SELECT ?container ?type
        WHERE {
          :room-3-01 ^rec:hasPart+ ?container .
          ?container a ?type .
        }
        ORDER BY ?container
        """);
    // endregion: where-is-3-01

    Show.Section("no-inverse");
    // region: no-inverse
    Show.Query(view, """
        PREFIX rec:  <https://w3id.org/rec#>
        PREFIX :     <https://example.org/learn/>
        SELECT (COUNT(?container) AS ?stated)
        WHERE { :room-3-01 rec:isPartOf ?container }
        """);
    // endregion: no-inverse
}

Show.Section("no-foreign-key");
// region: no-foreign-key
await using (Dataset scratch = await Dataset.CreateAsync(
    new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }))
{
    CommitResult accepted = await SparqlUpdate.ExecuteAsync(scratch, SparqlParser.ParseUpdate("""
        PREFIX rec:  <https://w3id.org/rec#>
        PREFIX :     <https://example.org/learn/>
        INSERT DATA { :asset-lost a rec:Asset ; rec:locatedIn :room-9-99 . }
        """), new UpdateOptions());
    Console.WriteLine(accepted.Outcome + " at position " + accepted.Position.Value);
}
// endregion: no-foreign-key

Show.StateIfAsked(args, dataset);
