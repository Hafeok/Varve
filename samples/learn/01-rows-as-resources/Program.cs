// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Chapter 1 of the learning track: rows as resources (#94).
// docs/learn/01-rows-as-resources.md quotes the data, the queries under
// queries/, the Varve calls by their regions here, and the output by section.

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

CommitResult loaded = await Load.TriGAsync(dataset, Path.Combine(AppContext.BaseDirectory, "data", "01-rooms.trig"));
// endregion: create

using (DatasetView view = dataset.Pin())
{
    Show.Section("one-room");
    Show.Query(view, "one-room.rq");

    Show.Section("rooms-seating-ten");
    Show.Query(view, "rooms-seating-ten.rq");

    Show.Section("every-room");
    Show.Query(view, "every-room.rq");
}

// region: update
CommitResult inserted = await SparqlUpdate.ExecuteAsync(
    dataset, SparqlParser.ParseUpdate(Show.File("insert.ru")), new UpdateOptions());
// endregion: update

Show.Section("insert");
using (DatasetView view = dataset.Pin())
{
    Show.Query(view, "count-rooms.rq");
}

Show.Section("nothing-checks-a-value");
await using (Dataset scratch = await Dataset.CreateAsync(
    new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }))
{
    await SparqlUpdate.ExecuteAsync(scratch, SparqlParser.ParseUpdate(Show.File("unchecked.ru")), new UpdateOptions());
    using DatasetView view = scratch.Pin();
    Show.Query(view, "unchecked.rq");
}

Show.Section("in-varve");
Console.WriteLine("loaded:   " + loaded.Outcome + " at position " + loaded.Position.Value);
Console.WriteLine("inserted: " + inserted.Outcome + " at position " + inserted.Position.Value);

Show.StateIfAsked(args, dataset);
