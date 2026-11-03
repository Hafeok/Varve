// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Varve.Delta.Model;
using Varve.Events.Model;
using Xunit;

namespace Varve.Events.Tests;

/// <summary>
/// The CloudEvents rendering and the event-schema generator's output as
/// v0.1.0-preview.7 and v0.1.0-preview.13 wrote them, from their published
/// packages (testdata/formats/events-preview.*, ADR 0216). Both are read
/// forever by consumers, so current code renders the same inputs to the same
/// bytes, every schema revision and every event.
/// </summary>
public sealed class FormatFixtureTests
{
    public static TheoryData<string> Releases => ["events-preview.7", "events-preview.13"];

    [Theory]
    [MemberData(nameof(Releases))]
    public void every_schema_revision_is_the_release_s_bytes(string name)
    {
        string fixture = Fixture(name);
        EventEndpoints endpoints = new("https://varve.example.org/", "golden");
        string inputs = Path.Combine(fixture, "generator", "inputs", "schemas");
        string[] cases = [.. Directory.GetDirectories(inputs).Select(d => Path.GetFileName(d)!).Order(StringComparer.Ordinal)];
        Assert.NotEmpty(cases);

        foreach (string folder in cases.Select(c => Path.Combine(inputs, c)))
        {
            History history = new();
            List<Q> previous = [];

            foreach (string file in Directory.GetFiles(folder, "*.trig").Where(f => char.IsAsciiDigit(Path.GetFileName(f)[0])).Order(StringComparer.Ordinal))
            {
                List<Q> state = Trig.Quads(File.ReadAllText(file));
                Assert.NotNull(history.Commit(previous.Except(state), state.Except(previous)));
                previous = state;
            }

            EventCatalogueHistory dataset = history.Catalogue(history.Head);
            EventCatalogueHistory catalogue = dataset;
            string[] moved = [.. Directory.GetFiles(folder, "m*.trig").Order(StringComparer.Ordinal)];

            if (moved.Length > 0)
            {
                catalogue = EventCatalogueHistory.ContinueFrom(dataset.Current);
                List<Q> managed = [];

                foreach (string file in moved)
                {
                    List<Q> state = Trig.Quads(File.ReadAllText(file));
                    List<FeedChange> changes = [
                        .. managed.Except(state).Select(q => new FeedChange(FeedChangeKind.Retract, q.S, q.P, q.O, q.G)),
                        .. state.Except(managed).Select(q => new FeedChange(FeedChangeKind.Assert, q.S, q.P, q.O, q.G))];
                    catalogue.Apply(new FeedPosition(long.Parse(Path.GetFileNameWithoutExtension(file)[1..], System.Globalization.CultureInfo.InvariantCulture)), changes);
                    managed = state;
                }
            }

            string expected = Path.Combine(fixture, "schemas", Path.GetFileName(folder));
            SortedDictionary<string, byte[]> generated = new(StringComparer.Ordinal);

            foreach (CataloguedType type in catalogue.Current.Types)
            {
                foreach (SchemaRevision revision in type.Revisions)
                {
                    Assert.True((revision.Revision.IsManagement ? catalogue : dataset).At(revision.Revision).TryGet(type.Declaration.Name, out CataloguedType? then));
                    string id = endpoints.Schema(type.Declaration.Name, revision.Version, revision.Revision);
                    generated[type.Declaration.Name + "/v" + revision.Version + "/r" + revision.Revision + ".json"] = EventSchemas.Generate(then.Declaration, id);
                }
            }

            Assert.Equal(
                Directory.GetFiles(expected, "*.json", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(expected, f).Replace('\\', '/')).Order(StringComparer.Ordinal),
                generated.Keys);

            foreach ((string file, byte[] bytes) in generated)
            {
                Assert.True(File.ReadAllBytes(Path.Combine(expected, file)).AsSpan().SequenceEqual(bytes), name + ": " + file + " is not the release's bytes.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Releases))]
    public void every_event_renders_as_the_release_rendered_it(string name)
    {
        string fixture = Fixture(name);
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

        foreach ((string caseName, TypeSpec[] types, string events) in cases)
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

            Assert.True(File.ReadAllBytes(Path.Combine(fixture, "cloudevents", caseName + ".jsonl")).AsSpan().SequenceEqual([.. lines]), name + ": " + caseName + " does not render as the release rendered it.");
        }
    }

    private static string Fixture(string name)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Varve.slnx")))
        {
            directory = directory.Parent;
        }

        string fixture = Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("No repository root above " + AppContext.BaseDirectory + "."), "testdata", "formats", name);

        foreach (string line in File.ReadAllLines(Path.Combine(fixture, "SHA256SUMS")).Where(l => l.Length > 0))
        {
            Assert.Equal(line[..64], Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(fixture, line[66..])))));
        }

        return fixture;
    }
}
