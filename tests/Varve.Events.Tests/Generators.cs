// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CsCheck;
using Varve.Rdf;

namespace Varve.Events.Tests;

/// <summary>Generated declarations, events and histories.</summary>
internal static class Generators
{
    internal static readonly string[] Datatypes =
    [
        Terms.Xsd + "integer", Terms.Xsd + "decimal", Terms.Xsd + "double", Terms.Xsd + "boolean", Terms.Xsd + "string",
        Terms.Xsd + "dateTime", Terms.Xsd + "date", Terms.Xsd + "anyURI", "http://www.w3.org/1999/02/22-rdf-syntax-ns#langString",
    ];

    internal static readonly string[] FieldNames = ["a", "b", "c", "d", "e"];

    internal static readonly Gen<FieldSpec> Field =
        Gen.Select(Gen.OneOfConst(FieldNames), Gen.OneOfConst(Datatypes), Gen.Bool, Gen.Int[0, 9],
            (name, datatype, required, flag) => new FieldSpec(
                name, datatype, required,
                AsString: flag == 0,
                KeepLanguage: flag == 1 && datatype.EndsWith("langString", System.StringComparison.Ordinal)));

    internal static readonly Gen<List<FieldSpec>> Fields =
        Field.List[1, 4].Select(list => list.GroupBy(f => f.Name).Select(g => g.First()).ToList());

    /// <summary>A value of a field's datatype, sometimes at the boundaries the mapping refuses.</summary>
    internal static Gen<RdfTerm> Value(string datatype) => datatype switch
    {
        _ when datatype.EndsWith("#integer", System.StringComparison.Ordinal) => Gen.OneOf(
            Gen.Long.Select(v => Terms.Literal(v.ToString(CultureInfo.InvariantCulture), datatype)),
            Gen.OneOfConst("9007199254740993", "-9007199254740993", "18446744073709551617").Select(v => Terms.Literal(v, datatype))),
        _ when datatype.EndsWith("#decimal", System.StringComparison.Ordinal) => Gen.OneOf(
            Gen.Select(Gen.Int, Gen.Int[0, 4], (v, scale) => Terms.Literal((v / System.Math.Pow(10, scale)).ToString("0.####", CultureInfo.InvariantCulture), datatype)),
            Gen.OneOfConst("19.99", "0.1", "12345678901234567890.123").Select(v => Terms.Literal(v, datatype))),
        _ when datatype.EndsWith("#double", System.StringComparison.Ordinal) => Gen.OneOf(
            Gen.Double.Where(double.IsFinite).Select(v => Terms.Literal(v.ToString("R", CultureInfo.InvariantCulture), datatype)),
            Gen.OneOfConst("INF", "NaN").Select(v => Terms.Literal(v, datatype))),
        _ when datatype.EndsWith("#boolean", System.StringComparison.Ordinal) => Gen.Bool.Select(b => Terms.Literal(b ? "true" : "false", datatype)),
        _ when datatype.EndsWith("#dateTime", System.StringComparison.Ordinal) => Gen.Select(Gen.Int[0, 100_000_000], Gen.Bool,
            (seconds, zoned) => Terms.Literal(new System.DateTime(2020, 1, 1).AddSeconds(seconds).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + (zoned ? "Z" : ""), datatype)),
        _ when datatype.EndsWith("#date", System.StringComparison.Ordinal) => Gen.Int[0, 5000].Select(days =>
            Terms.Literal(new System.DateTime(2020, 1, 1).AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), datatype)),
        _ when datatype.EndsWith("#anyURI", System.StringComparison.Ordinal) => Gen.Int[0, 1000].Select(n => Terms.Iri("https://example.org/r/" + n)),
        _ when datatype.EndsWith("langString", System.StringComparison.Ordinal) => Gen.Select(Gen.String.AlphaNumeric, Gen.OneOfConst("en", "da", "ar"),
            (text, language) => Terms.Lang(text, language)),
        _ => Gen.String.AlphaNumeric.Select(text => Terms.Plain(text)),
    };

    /// <summary>One step of a history.</summary>
    internal abstract record Step;

    /// <summary>Declares or redeclares type <paramref name="Index"/> with these fields, at a version.</summary>
    internal sealed record Declare(int Index, List<FieldSpec> Fields, int Version, bool Retired) : Step;

    /// <summary>Writes an event of type <paramref name="Index"/> with a value per field, a field sometimes missing or doubled.</summary>
    internal sealed record Emit(int Index, List<(string Field, RdfTerm Value)> Values) : Step;

    /// <summary>Writes something no event declares.</summary>
    internal sealed record Noise(int N) : Step;

    internal static readonly Gen<Step> Steps = Gen.Frequency(
        (2, Gen.Select(Gen.Int[0, 2], Fields, Gen.Int[1, 3], Gen.Int[0, 9], (i, f, v, r) => (Step)new Declare(i, f, v, r == 0))),
        (5, Gen.Select(Gen.Int[0, 2], Gen.Select(Gen.OneOfConst(FieldNames), Gen.OneOfConst(Datatypes)).SelectMany(p => Value(p.Item2).Select(v => (p.Item1, v))).List[0, 5],
            (i, values) => (Step)new Emit(i, values))),
        (1, Gen.Int[0, 1000].Select(n => (Step)new Noise(n))));

    internal static readonly Gen<List<Step>> Histories = Steps.List[1, 12];

    internal static TypeSpec TypeOf(int index, List<FieldSpec> fields, int version, bool retired) =>
        new("T" + index, Terms.Ex + "C" + index, version, fields, Retired: retired);

    /// <summary>Plays a generated history into a <see cref="History"/>, replacing a type's declaration whole on each redeclaration.</summary>
    internal static History Play(List<Step> steps)
    {
        History history = new();
        Dictionary<int, List<Q>> declared = [];
        int events = 0;

        foreach (Step step in steps)
        {
            switch (step)
            {
                case Declare d:
                    List<Q> quads = TypeOf(d.Index, d.Fields, d.Version, d.Retired).Quads(Terms.EventsGraph);
                    history.Commit(declared.TryGetValue(d.Index, out List<Q>? old) ? old : [], quads);
                    declared[d.Index] = quads;
                    break;
                case Emit e:
                    RdfTerm node = Terms.Iri("https://example.org/e/" + events++);
                    List<Q> assert = [new Q(node, Terms.Iri(Terms.RdfType), Terms.Iri(Terms.Ex + "C" + e.Index), Terms.EventsGraph)];
                    assert.AddRange(e.Values.Select(v => new Q(node, Terms.Iri(Terms.Ex + v.Field), v.Value, Terms.EventsGraph)));
                    history.Commit([], assert);
                    break;
                case Noise n:
                    history.Commit([], [new Q(Terms.Iri("https://example.org/n/" + n.N), Terms.Iri(Terms.Ex + "noise"), Terms.Plain("x"), null)]);
                    break;
            }
        }

        return history;
    }
}
