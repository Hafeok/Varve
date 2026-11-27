// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Chapter 6 of the learning track: temporal and audit (#94, #411).
// docs/learn/06-temporal-and-audit.md quotes the updates under queries/, the
// Varve calls by their regions here, and the output by section.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Varve.Learn;
using Varve.Rdf;
using Varve.Sparql;
using Varve.Sparql.Store;
using Varve.Store;
using Varve.Store.Log;

// region: dataset
SetClock clock = new(At("2026-01-05T09:00:00Z"));
await using Dataset dataset = await Dataset.CreateAsync(
    new MemoryStorage(),
    new DatasetId(Guid.NewGuid()),
    new DatasetOptions { Clock = clock });
// endregion: dataset

await Load.TriGAsync(dataset, Path.Combine(AppContext.BaseDirectory, "data", "01-rooms.trig"));
clock.Set(At("2026-01-05T09:05:00Z"));
await Load.TriGAsync(dataset, Path.Combine(AppContext.BaseDirectory, "data", "02-topology.trig"));

RdfTerm facilities = RdfTerm.Iri("mailto:facilities@example.org"u8);
RdfTerm it = RdfTerm.Iri("mailto:it@example.org"u8);

// region: renovation
clock.Set(At("2026-03-02T10:00:00Z"));
CommitResult renovation = await SparqlUpdate.ExecuteAsync(
    dataset,
    SparqlParser.ParseUpdate(Show.File("renovation.ru")),
    new UpdateOptions { Metadata = new CommitMetadata { Agent = facilities } });
// endregion: renovation

clock.Set(At("2026-03-09T14:30:00Z"));
await UpdateAsync("move-to-3-04.ru", it);
clock.Set(At("2026-04-14T08:15:00Z"));
await UpdateAsync("move-to-3-02.ru", facilities);

Show.Section("today");
using (DatasetView head = dataset.Pin())
{
    Console.WriteLine("position " + head.Position.Value);
    Show.Query(head, "level-3.rq");
}

// region: as-of-timestamp
using (DatasetView february = await dataset.AsOfTimestampAsync(new CommitTimestamp(At("2026-02-01T00:00:00Z"))))
{
    Show.Section("before-the-renovation");
    Console.WriteLine("position " + february.Position.Value);
    Show.Query(february, "level-3.rq");
}
// endregion: as-of-timestamp

// region: diff
Position after = renovation.Position;
QuadDelta changed = await dataset.DiffAsync(new Position(after.Value - 1), after);
// endregion: diff

Show.Section("what-the-renovation-changed");
using (DatasetView head = dataset.Pin())
{
    Delta(head, changed);
}

// region: audit
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
// endregion: audit

Show.StateIfAsked(args, dataset);

async Task UpdateAsync(string file, RdfTerm agent)
{
    CommitResult result = await SparqlUpdate.ExecuteAsync(
        dataset,
        SparqlParser.ParseUpdate(Show.File(file)),
        new UpdateOptions { Metadata = new CommitMetadata { Agent = agent } });
    if (result.Outcome != CommitOutcome.Committed)
    {
        throw new InvalidOperationException(file + ": " + result.Outcome);
    }
}

static DateTimeOffset At(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

// The room a quad places the display in, when it is the display's rec:locatedIn.
static string? LocationOfDisplay(DatasetView view, Quad quad) =>
    view.TryExternalise(quad.Subject, out RdfTerm? s) && Show.Text(s) == ":asset-display-3-01"
    && view.TryExternalise(quad.Predicate, out RdfTerm? p) && Show.Text(p) == "rec:locatedIn"
    && view.TryExternalise(quad.Object, out RdfTerm? o)
        ? Show.Text(o)
        : null;

// A delta as two lists of statements, the removed and the added, in the track's prefixes.
static void Delta(DatasetView view, QuadDelta delta)
{
    List<string[]> rows = [];
    foreach (Quad quad in delta.Retracted)
    {
        rows.Add(["-", .. Statement(view, quad)]);
    }

    foreach (Quad quad in delta.Asserted)
    {
        rows.Add(["+", .. Statement(view, quad)]);
    }

    rows.Sort((a, b) => string.CompareOrdinal(Key(a), Key(b)));

    static string Key(string[] row) => row[1] + " " + row[2] + " " + (row[0] == "-" ? "0" : "1");
    Show.Table(["", "subject", "predicate", "object"], rows);
}

static string[] Statement(DatasetView view, Quad quad)
{
    view.TryExternalise(quad.Subject, out RdfTerm? s);
    view.TryExternalise(quad.Predicate, out RdfTerm? p);
    view.TryExternalise(quad.Object, out RdfTerm? o);
    return [Show.Text(s!), Show.Text(p!), Show.Text(o!)];
}
