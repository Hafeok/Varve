// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Chapter 3 of the learning track: schema without migrations (#94, #411).
// docs/learn/03-schema-without-migrations.md quotes the queries under
// queries/, the Varve calls by their regions here, and the output by section.

using System;
using System.Text;
using Varve.Learn;
using Varve.Rdf;
using Varve.Sparql;
using Varve.Sparql.Store;
using Varve.Store;
using Varve.Store.Log;

// region: create
await using Dataset dataset = await Gated.CreateAsync(TimeProvider.System);
await Gated.LoadAsync(dataset, "01-rooms.trig", "02-topology.trig", "03-portfolio.trig");
// endregion: create

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
Findings.Show(refused);

Show.Section("untyped");
CommitResult untyped = await SparqlUpdate.ExecuteAsync(dataset, SparqlParser.ParseUpdate(Show.File("untyped.ru")), new UpdateOptions());
Console.WriteLine(untyped.Outcome);
Findings.Show(untyped);

Show.Section("open-shapes");
CommitResult open = await SparqlUpdate.ExecuteAsync(dataset, SparqlParser.ParseUpdate(Show.File("open-shapes.ru")), new UpdateOptions());
Console.WriteLine(open.Outcome);

Show.StateIfAsked(args, dataset, vocabulary: Gated.Rec);

// A term as the store returned it: an IRI in angle brackets, a literal's lexical form and its datatype.
static string Raw(RdfTerm term)
{
    string lexical = Encoding.UTF8.GetString(term.Lexical);
    return term.Kind == RdfTermKind.Iri
        ? "<" + lexical + ">"
        : "a literal of datatype <" + Encoding.UTF8.GetString(term.Datatype!.Lexical) + ">, whose lexical form is:\n" + lexical;
}
