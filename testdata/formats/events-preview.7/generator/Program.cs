// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Writes the CloudEvents rendering and the event-schema generator's output as
// v0.1.0-preview.7 writes them, from that release's published Varve.Events and
// Varve.Turtle packages (docs/formats/, ADR 0216). Support.cs is that
// release's own test support (tests/Varve.Events.Tests/Support.cs at the tag),
// less what needs a test package.
//
//   schemas/<case>/<type>/v<version>/r<revision>.json: each revision of each
//   type of the inputs' histories, as the release's tests generate them.
//   cloudevents/<case>.json: each event the release renders for the inputs'
//   declarations and commits, one per line, in its own bytes.
using System.Text;
using Fixture;
using Varve.Events;
using Varve.Events.Model;

string inputs = args[0];
string root = args[1];
EventEndpoints schemaEndpoints = new("https://varve.example.org/", "golden");

foreach (string folder in Directory.GetDirectories(Path.Combine(inputs, "schemas")).Order(StringComparer.Ordinal))
{
    History history = new();
    List<Q> previous = [];

    foreach (string file in Directory.GetFiles(folder, "*.trig").Order(StringComparer.Ordinal))
    {
        List<Q> state = Trig.Quads(File.ReadAllText(file));
        history.Commit(previous.Except(state), state.Except(previous));
        previous = state;
    }

    EventCatalogueHistory catalogue = history.Catalogue(history.Head);

    foreach (CataloguedType type in catalogue.Current.Types)
    {
        foreach (SchemaRevision revision in type.Revisions)
        {
            catalogue.At(revision.Revision).TryGet(type.Declaration.Name, out CataloguedType? then);
            string id = schemaEndpoints.Schema(type.Declaration.Name, revision.Version, revision.Revision);
            string path = Path.Combine(root, "schemas", Path.GetFileName(folder), type.Declaration.Name, "v" + revision.Version, "r" + revision.Revision.Value + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, EventSchemas.Generate(then!.Declaration, id));
        }
    }
}

// The rendering: each case declares its types, then commits its events;
// every event of the history, in the order the release renders them, one line
// each. The cases are the release's own rendering tests'.
EventEndpoints endpoints = new("https://varve.example.org/", "orders");
TypeSpec orderPlaced = new(
    "OrderPlaced", Terms.Ex + "OrderPlaced", 1,
    [
        new FieldSpec("order", Terms.Xsd + "anyURI", Required: true),
        new FieldSpec("total", Terms.Xsd + "decimal", Required: true),
        new FieldSpec("placedAt", Terms.Xsd + "dateTime", Required: true),
        new FieldSpec("note", Terms.Xsd + "string", Required: false),
    ],
    Subject: "subject",
    Description: "An order was placed.");
TypeSpec shipped = new("Shipped", Terms.Ex + "Shipped", 1, [new FieldSpec("carrier", Terms.Xsd + "string", Required: true)],
    Select: "PREFIX ex: <https://example.org/ns#> SELECT ?carrier WHERE { ?event ex:via [ ex:carrier ?carrier ] }");
TypeSpec ping = new("Ping", Terms.Ex + "Ping", 1, [new FieldSpec("n", Terms.Xsd + "integer", Required: true)]);
TypeSpec greeted = new("Greeted", Terms.Ex + "Greeted", 1,
[
    new FieldSpec("plain", "http://www.w3.org/1999/02/22-rdf-syntax-ns#langString", Required: true),
    new FieldSpec("kept", "http://www.w3.org/1999/02/22-rdf-syntax-ns#langString", Required: true, KeepLanguage: true),
]);
TypeSpec big = new("Big", Terms.Ex + "Big", 1, [new FieldSpec("value", Terms.Xsd + "integer", Required: true, AsString: true)]);

(string Name, TypeSpec[] Types, string Events)[] cases =
[
    ("order-placed", [orderPlaced], """
        <https://example.org/orders/1/placed> a ex:OrderPlaced ;
          ex:order <https://example.org/orders/1> ;
          ex:total 19.99 ;
          ex:placedAt "2026-10-15T09:30:00Z"^^xsd:dateTime .
        """),
    ("order-not-rendered", [orderPlaced], "<https://example.org/orders/2/placed> a ex:OrderPlaced ; ex:total 19.99 ."),
    ("closure", [shipped], "<https://example.org/s/1> a ex:Shipped ; ex:via [ ex:carrier \"Post\" ] ."),
    ("ordered", [ping], "<https://example.org/p/b> a ex:Ping ; ex:n 2 . <https://example.org/p/a> a ex:Ping ; ex:n 1 ."),
    ("language", [greeted], "<https://example.org/g/1> a ex:Greeted ; ex:plain \"hej\"@da ; ex:kept \"hello\"@en ."),
    ("as-string", [big], "<https://example.org/b/1> a ex:Big ; ex:value 9007199254740993 ."),
];

foreach ((string name, TypeSpec[] types, string events) in cases)
{
    History history = new();
    history.Commit([], types.SelectMany(t => t.Quads(Terms.EventsGraph)).ToList());
    history.Commit([], Trig.Quads("<https://example.org/events> { " + events + " }"));
    List<byte> lines = [];

    foreach (RenderedEvent rendered in history.RenderAll(endpoints, history.Head))
    {
        lines.AddRange(rendered.Json.ToArray());
        lines.Add((byte)'\n');
    }

    string path = Path.Combine(root, "cloudevents", name + ".jsonl");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, [.. lines]);
}

Console.WriteLine("written under " + root);
