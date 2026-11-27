// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Chapter 4 of the learning track: commits (#94, #411).
// docs/learn/04-commits.md quotes the updates under queries/, the Varve calls
// by their regions here, and the output by section.

using System;
using System.Threading.Tasks;
using Varve.Learn;
using Varve.Sparql;
using Varve.Sparql.Store;
using Varve.Store;
using Varve.Store.Log;

await using Dataset dataset = await Gated.CreateAsync(TimeProvider.System);
await Gated.LoadAsync(dataset, "01-rooms.trig", "02-topology.trig");

Show.Section("before");
Level3();

// region: split-unchecked
CommitResult refused = await SparqlUpdate.ExecuteAsync(
    dataset, SparqlParser.ParseUpdate(Show.File("split-unchecked.ru")), new UpdateOptions());
// endregion: split-unchecked

Show.Section("split-unchecked");
Console.WriteLine(refused.Outcome + ", the head still at position " + dataset.Head.Value);
Findings.Show(refused);
Level3();

Show.Section("split");
CommitResult split = await SparqlUpdate.ExecuteAsync(
    dataset, SparqlParser.ParseUpdate(Show.File("split.ru")), new UpdateOptions());
Console.WriteLine(split.Outcome + " at position " + split.Position.Value);
Level3();

// Two writers read the same position. Each decides what to write from what it read.
// region: read
Position read = dataset.Head;
// endregion: read

Show.Section("both-read");
Console.WriteLine("position " + read.Value);
await DisplaysAsync(read);

// region: writers
CommitResult first = await SparqlUpdate.ExecuteAsync(
    dataset,
    SparqlParser.ParseUpdate(Show.File("move-display.ru")),
    new UpdateOptions { ExpectedPosition = read });
CommitResult second = await SparqlUpdate.ExecuteAsync(
    dataset,
    SparqlParser.ParseUpdate(Show.File("rename-3-01.ru")),
    new UpdateOptions { ExpectedPosition = read });
// endregion: writers

Show.Section("conflict");
Console.WriteLine("move-display: " + first.Outcome + " at position " + first.Position.Value);
Console.WriteLine("rename-3-01:  " + second.Outcome + ", the head at position " + second.Position.Value);

Show.Section("re-read");
await DisplaysAsync(second.Position);

Show.StateIfAsked(args, dataset, vocabulary: Gated.Rec);

void Level3()
{
    using DatasetView view = dataset.Pin();
    Show.Query(view, "level-3.rq");
}

async Task DisplaysAsync(Position at)
{
    using DatasetView view = await dataset.AsOfAsync(at);
    Show.Query(view, "displays.rq");
}
