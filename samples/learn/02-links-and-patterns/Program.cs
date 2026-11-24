// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Chapter 2 of the learning track: links and patterns (#94).
// docs/learn/02-links-and-patterns.md quotes the data, the queries under
// queries/, the Varve calls by their regions here, and the output by section.

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

// region: load
CommitResult rooms = await Load.TriGAsync(dataset, Path.Combine(AppContext.BaseDirectory, "data", "01-rooms.trig"));
CommitResult topology = await Load.TriGAsync(dataset, Path.Combine(AppContext.BaseDirectory, "data", "02-topology.trig"));
// endregion: load

using (DatasetView view = dataset.Pin())
{
    foreach (string query in (string[])["rooms-in-the-building", "assets-on-level-3", "points-in-the-building", "where-is-3-01", "no-inverse"])
    {
        Show.Section(query);
        Show.Query(view, query + ".rq");
    }
}

Show.Section("no-foreign-key");
await using (Dataset scratch = await Dataset.CreateAsync(
    new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }))
{
    await SparqlUpdate.ExecuteAsync(scratch, SparqlParser.ParseUpdate(Show.File("no-foreign-key.ru")), new UpdateOptions());
    using DatasetView view = scratch.Pin();
    Show.Query(view, "lost-asset.rq");
}

Show.Section("in-varve");
Console.WriteLine("rooms:    " + rooms.Outcome + " at position " + rooms.Position.Value);
Console.WriteLine("topology: " + topology.Outcome + " at position " + topology.Position.Value);

Show.StateIfAsked(args, dataset);
