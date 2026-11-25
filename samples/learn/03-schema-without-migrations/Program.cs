// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Chapter 3 of the learning track: schema without migrations (#94, #411).
// docs/learn/03-schema-without-migrations.md quotes the queries under
// queries/, the Varve calls by their regions here, and the output by section.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Varve.Learn;
using Varve.Rdf;
using Varve.Shacl.Store;
using Varve.Shacl.Store.Model;
using Varve.Sparql;
using Varve.Sparql.Store;
using Varve.Store;
using Varve.Store.Log;

// region: gate
RdfTerm rec = RdfTerm.Iri("https://example.org/learn/rec"u8);
ShapesBinding binding = new(
    RdfTerm.Iri("https://example.org/learn/rec-binding"u8),
    rec,
    GraphScope.Of([], [], DefaultGraphAccess.Included),
    ValidationMode.Reject)
{
    SubClassOfInShapesGraph = true,
};
ShapesGate gate = new([binding]);

await using Dataset dataset = await Dataset.CreateAsync(
    new MemoryStorage(),
    new DatasetId(Guid.NewGuid()),
    new DatasetOptions { Clock = TimeProvider.System, Validators = [gate] });
// endregion: gate

// region: load-rec
string[] recFiles = ["rec.ttl", "Brick+patches.ttl", "recimports.ttl", "composition.ttl"];
CommitResult vocabulary = await Load.TurtleIntoAsync(
    dataset, rec, [.. recFiles.Select(file => Path.Combine(AppContext.BaseDirectory, "rec", file))]);
// endregion: load-rec

if (vocabulary.Outcome != CommitOutcome.Committed)
{
    throw new InvalidDataException("REC 4: " + vocabulary.Outcome + " " + vocabulary.Reason);
}

foreach (string file in (string[])["01-rooms.trig", "02-topology.trig", "03-portfolio.trig"])
{
    CommitResult loaded = await Load.TriGAsync(dataset, Path.Combine(AppContext.BaseDirectory, "data", file));
    if (loaded.Outcome != CommitOutcome.Committed)
    {
        throw new InvalidDataException(file + ": " + loaded.Outcome + " " + loaded.Reason);
    }
}

using (DatasetView view = dataset.Pin())
{
    foreach (string query in (string[])["types", "point-types", "parents", "sensors"])
    {
        Show.Section(query);
        Show.Query(view, query + ".rq");
    }
}

Show.Section("new-type");
CommitResult added = await SparqlUpdate.ExecuteAsync(dataset, SparqlParser.ParseUpdate(Show.File("new-type.ru")), new UpdateOptions());
Console.WriteLine(added.Outcome);
using (DatasetView view = dataset.Pin())
{
    Show.Query(view, "sensors.rq");
}

// region: refused
CommitResult refused = await SparqlUpdate.ExecuteAsync(
    dataset, SparqlParser.ParseUpdate(Show.File("refused.ru")), new UpdateOptions());
// endregion: refused

Show.Section("refused");
Console.WriteLine("Outcome: " + refused.Outcome);
Console.WriteLine("Reason:  " + (refused.Reason ?? "(none)"));
Console.WriteLine("Report:  " + refused.Report.Count + " terms");
foreach (RdfTerm term in refused.Report)
{
    Console.WriteLine();
    Console.WriteLine(Raw(term));
}

Show.Section("findings");
Findings(refused);

Show.Section("untyped");
CommitResult untyped = await SparqlUpdate.ExecuteAsync(dataset, SparqlParser.ParseUpdate(Show.File("untyped.ru")), new UpdateOptions());
Console.WriteLine(untyped.Outcome);
Findings(untyped);

Show.Section("open-shapes");
CommitResult open = await SparqlUpdate.ExecuteAsync(dataset, SparqlParser.ParseUpdate(Show.File("open-shapes.ru")), new UpdateOptions());
Console.WriteLine(open.Outcome);

Show.StateIfAsked(args, dataset, vocabulary: rec);

// A term as the store returned it: an IRI in angle brackets, a literal's lexical form and its datatype.
static string Raw(RdfTerm term)
{
    string lexical = Encoding.UTF8.GetString(term.Lexical);
    return term.Kind == RdfTermKind.Iri
        ? "<" + lexical + ">"
        : "a literal of datatype <" + Encoding.UTF8.GetString(term.Datatype!.Lexical) + ">, whose lexical form is:\n" + lexical;
}

// The report's findings as a table: the sample's own formatting, not the store's.
static void Findings(CommitResult result)
{
    using JsonDocument report = JsonDocument.Parse(result.Report[1].Lexical.ToArray());
    List<string[]> rows = [];
    foreach (JsonElement finding in report.RootElement.GetProperty("findings").EnumerateArray())
    {
        rows.Add([.. ((string[])["focusNode", "path", "value", "component"]).Select(m => Show.Written(Member(finding, m)))]);
    }

    Show.Table(["focusNode", "path", "value", "component"], rows);
}

static string Member(JsonElement finding, string name) =>
    finding.TryGetProperty(name, out JsonElement value) ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText() : "";
