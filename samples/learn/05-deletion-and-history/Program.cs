// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Chapter 5 of the learning track: deletion and history (#94, #411).
// docs/learn/05-deletion-and-history.md quotes the updates under queries/,
// the Varve calls by their regions here, and the output by section.

using System;
using System.IO;
using System.Threading.Tasks;
using Varve.Learn;
using Varve.Sparql;
using Varve.Sparql.Store;
using Varve.Store;
using Varve.Store.Log;

await using Dataset dataset = await Dataset.CreateAsync(
    new MemoryStorage(),
    new DatasetId(Guid.NewGuid()),
    new DatasetOptions { Clock = TimeProvider.System });
await Load.TriGAsync(dataset, Path.Combine(AppContext.BaseDirectory, "data", "01-rooms.trig"));
await Load.TriGAsync(dataset, Path.Combine(AppContext.BaseDirectory, "data", "02-topology.trig"));

// region: before
Position before = dataset.Head;
// endregion: before

Show.Section("before");
Console.WriteLine("position " + before.Value);
Queries("about.rq", "links-to.rq");

Show.Section("retire-statements");
await UpdateAsync("retire-statements.ru");
Queries("about.rq", "links-to.rq", "points-of-3-01.rq");

Show.Section("retire-links");
await UpdateAsync("retire-links.ru");
Queries("links-to.rq", "points-of-3-01.rq");

// region: again
CommitResult again = await SparqlUpdate.ExecuteAsync(
    dataset, SparqlParser.ParseUpdate(Show.File("retire.ru")), new UpdateOptions());
// endregion: again

Show.Section("again");
Console.WriteLine(again.Outcome + ", the head still at position " + dataset.Head.Value);

// region: as-of
using (DatasetView then = await dataset.AsOfAsync(before))
{
    Show.Section("as-of");
    Console.WriteLine("as of position " + then.Position.Value);
    Show.Query(then, "about.rq");
    Show.Query(then, "links-to.rq");
}
// endregion: as-of

Show.StateIfAsked(args, dataset);

async Task UpdateAsync(string file)
{
    CommitResult result = await SparqlUpdate.ExecuteAsync(
        dataset, SparqlParser.ParseUpdate(Show.File(file)), new UpdateOptions());
    Console.WriteLine(result.Outcome + " at position " + result.Position.Value);
}

void Queries(params string[] files)
{
    using DatasetView view = dataset.Pin();
    foreach (string file in files)
    {
        Show.Query(view, file);
    }
}
