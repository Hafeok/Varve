// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using CsCheck;
using Varve.Events.Model;
using Varve.Rdf;
using Varve.Shacl;
using Varve.Turtle;
using Xunit;
using ShaclReport = Varve.Shacl.Model.ValidationReport;
using ShaclResult = Varve.Shacl.Model.ShaclResult;

namespace Varve.Events.Tests;

/// <summary>
/// ADR 0157's agreement property: over generated catalogue graphs, valid and
/// with one fault each, <see cref="EventDeclarationShapes"/> and
/// <see cref="EventDeclarations"/> agree on everything the shapes state, and
/// the faults only the code can see leave the shapes conforming.
/// </summary>
public class ShapesAgreementTests
{
    /// <summary>The number of generated cases each run checks, as reported in the pull request.</summary>
    internal const int Iterations = 2_000;

    private const string Varve = "https://w3id.org/varve/ns#";
    private const string Rdfs = "http://www.w3.org/2000/01/rdf-schema#";
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    private static readonly RdfTerm CatalogueGraph = Terms.Iri(Varve + "events-catalogue/default");

    private static readonly ShapesGraph Shapes = LoadShapes();

    /// <summary>Faults the shapes state (ADR 0157 point 2): both refuse.</summary>
    private enum Seen
    {
        NameBadPattern,
        NameMissing,
        NameTwice,
        NameTagged,
        ClassLiteral,
        VersionZero,
        VersionTooLarge,
        VersionDecimal,
        SelectMissing,
        SubjectBadPattern,
        RetiredNotBoolean,
        CommentTwice,
        FieldLinkToNonField,
        ExtraType,
        TypeAndField,
        ClassTaken,
        FieldNamedEvent,
        FieldNameBadPattern,
        FieldDatatypeLiteral,
        FieldRequiredMissing,
        FieldAsNumber,
        FieldKeepLanguageNotBoolean,
        FieldOfNoType,
        FieldOfTwoTypes,
    }

    /// <summary>Faults only the code sees (ADR 0157 point 3): the code refuses, the shapes conform.</summary>
    private enum Unseen
    {
        UnknownVarveTerm,
        FieldNameTwiceInType,
        SubjectIsAField,
        SelectDoesNotParse,
        SelectProjectsAnotherVariable,
        BlankNodeElsewhere,
        StrayNode,
    }

    private sealed record Declaration(int Fields, bool Subject, bool Comment, bool Retired);

    [Fact]
    public void The_shapes_and_the_code_agree_on_what_the_shapes_state()
    {
        Gen<Declaration> declaration = Gen.Select(Gen.Int[1, 3], Gen.Bool, Gen.Bool, Gen.Bool, (f, s, c, r) => new Declaration(f, s, c, r));
        Gen.Select(declaration.Array[1, 3], Gen.Int[0, 2], Gen.Int[0, Enum.GetValues<Seen>().Length - 1], Gen.Int[0, Enum.GetValues<Unseen>().Length - 1])
            .Sample((types, mode, seen, unseen) => { Check(types, mode == 1 ? (Seen)seen : null, mode == 2 ? (Unseen)unseen : null); }, iter: Iterations);
    }

    /// <summary>Every fault, on declarations every fault applies to, is seen by the side it should be.</summary>
    [Theory]
    [MemberData(nameof(EveryFault))]
    public void Each_fault_is_refused_by_the_side_that_can_see_it(string fault)
    {
        Declaration[] types = [new Declaration(2, true, true, true), new Declaration(2, true, false, false)];
        Assert.True(Check(types, Enum.TryParse(fault, out Seen seen) ? seen : null, Enum.TryParse(fault, out Unseen unseen) ? unseen : null));
    }

    public static TheoryData<string> EveryFault()
    {
        TheoryData<string> data = [];
        foreach (Seen fault in Enum.GetValues<Seen>())
        {
            data.Add(fault.ToString());
        }

        foreach (Unseen fault in Enum.GetValues<Unseen>())
        {
            data.Add(fault.ToString());
        }

        return data;
    }

    /// <summary>Builds the graph, applies at most one fault, and checks both sides; true when a fault applied.</summary>
    private static bool Check(Declaration[] types, Seen? seen, Unseen? unseen)
    {
        List<(RdfTerm S, RdfTerm P, RdfTerm O)> triples = Build(types);
        RdfTerm? faulty = null;
        string fault = "none";
        if (seen is { } s && Apply(s, types, triples, out faulty))
        {
            fault = s.ToString();
        }
        else if (unseen is { } u && Apply(u, types, triples))
        {
            fault = u.ToString();
        }

        List<EventTypeDeclaration> read = [];
        List<DeclarationProblem> problems = [];
        EventDeclarations.ReadGraph(CatalogueGraph, triples, read, problems);
        ShaclReport report = Validate(triples);
        string shown = fault + "\ncode: " + string.Join("; ", problems.ConvertAll(p => p.ToString())) + "\nshapes: " + Results(report);

        if (fault == "none")
        {
            Assert.True(problems.Count == 0 && read.Count == types.Length, shown);
            Assert.True(report.Conforms, shown);
        }
        else if (seen is not null)
        {
            Assert.True(problems.Count > 0, shown);
            Assert.False(report.Conforms, shown);

            // The shapes name the node the fault is in, and so does the code, or the code names the field's type.
            Assert.True(Names(report, faulty!), "the shapes do not name " + faulty + "\n" + shown);
            Assert.True(problems.Exists(p => p.Declaration.Equals(faulty)) || (Owner(triples, faulty!) is { } owner && problems.Exists(p => p.Declaration.Equals(owner))), "the code does not name " + faulty + "\n" + shown);
        }
        else
        {
            Assert.True(problems.Count > 0, shown);
            Assert.True(report.Conforms, shown);
        }

        // Whatever the fault: what the code accepts conforms, and what does not conform the code refuses.
        Assert.True(problems.Count > 0 || report.Conforms, shown);
        return fault != "none";
    }

    [Fact]
    public void The_shapes_are_well_formed_and_a_spec_example_conforms()
    {
        ShapesLoad load = ShapesGraph.Load(Parse(EventDeclarationShapes.Turtle), GraphPattern.DefaultGraph, null, TestContext.Current.CancellationToken);
        Assert.True(load.Succeeded, string.Join("; ", load.Problems));
        Assert.True(Validate(Build([new Declaration(2, true, true, false)])).Conforms);
    }

    private static List<(RdfTerm, RdfTerm, RdfTerm)> Build(Declaration[] types)
    {
        List<(RdfTerm, RdfTerm, RdfTerm)> triples = [];
        for (int t = 0; t < types.Length; t++)
        {
            Declaration d = types[t];
            RdfTerm node = TypeNode(t);
            triples.Add((node, Terms.Iri(Terms.RdfType), Terms.Iri(Varve + "EventType")));
            triples.Add((node, V("eventName"), Terms.Plain("Type" + t)));
            triples.Add((node, V("eventClass"), Terms.Iri(Terms.Ex + "Class" + t)));
            triples.Add((node, V("eventVersion"), Terms.Literal("1", Terms.Xsd + "integer")));
            StringBuilder select = new("SELECT");
            for (int f = 0; f < d.Fields; f++)
            {
                select.Append(" ?f").Append(f);
            }

            if (d.Subject)
            {
                select.Append(" ?subject");
                triples.Add((node, V("eventSubject"), Terms.Plain("subject")));
            }

            select.Append(" WHERE { ?event ?p ?o }");
            triples.Add((node, V("eventSelect"), Terms.Plain(select.ToString())));
            if (d.Comment)
            {
                triples.Add((node, Terms.Iri(Rdfs + "comment"), Terms.Plain("A type.")));
            }

            if (d.Retired)
            {
                triples.Add((node, V("eventRetired"), Terms.Literal("true", Terms.Xsd + "boolean")));
            }

            for (int f = 0; f < d.Fields; f++)
            {
                RdfTerm field = FieldNode(t, f);
                triples.Add((node, V("eventField"), field));
                triples.Add((field, Terms.Iri(Terms.RdfType), Terms.Iri(Varve + "EventField")));
                triples.Add((field, V("fieldName"), Terms.Plain("f" + f)));
                triples.Add((field, V("fieldDatatype"), Terms.Iri(Terms.Xsd + (f % 2 == 0 ? "integer" : "string"))));
                triples.Add((field, V("fieldRequired"), Terms.Literal(f == 0 ? "true" : "false", Terms.Xsd + "boolean")));
            }
        }

        return triples;
    }

    /// <summary>Applies a fault the shapes state; false when the graph has nothing it applies to.</summary>
    private static bool Apply(Seen fault, Declaration[] types, List<(RdfTerm S, RdfTerm P, RdfTerm O)> triples, out RdfTerm? faulty)
    {
        RdfTerm type = TypeNode(0), field = FieldNode(0, 0);
        faulty = type;
        switch (fault)
        {
            case Seen.NameBadPattern: Replace(triples, type, V("eventName"), Terms.Plain("1Bad")); return true;
            case Seen.NameMissing: Remove(triples, type, V("eventName")); return true;
            case Seen.NameTwice: triples.Add((type, V("eventName"), Terms.Plain("Other"))); return true;
            case Seen.NameTagged: Replace(triples, type, V("eventName"), Terms.Lang("Type0", "en")); return true;
            case Seen.ClassLiteral: Replace(triples, type, V("eventClass"), Terms.Plain("Class0")); return true;
            case Seen.VersionZero: Replace(triples, type, V("eventVersion"), Terms.Literal("0", Terms.Xsd + "integer")); return true;
            case Seen.VersionTooLarge: Replace(triples, type, V("eventVersion"), Terms.Literal("3000000000", Terms.Xsd + "integer")); return true;
            case Seen.VersionDecimal: Replace(triples, type, V("eventVersion"), Terms.Literal("1.0", Terms.Xsd + "decimal")); return true;
            case Seen.SelectMissing: Remove(triples, type, V("eventSelect")); return true;
            case Seen.SubjectBadPattern:
                if (!types[0].Subject)
                {
                    return false;
                }

                Replace(triples, type, V("eventSubject"), Terms.Plain("9subject"));
                return true;
            case Seen.RetiredNotBoolean:
                Remove(triples, type, V("eventRetired"));
                triples.Add((type, V("eventRetired"), Terms.Plain("yes")));
                return true;
            case Seen.CommentTwice:
                triples.Add((type, Terms.Iri(Rdfs + "comment"), Terms.Plain("One.")));
                triples.Add((type, Terms.Iri(Rdfs + "comment"), Terms.Plain("Two.")));
                return true;
            case Seen.FieldLinkToNonField: triples.Add((type, V("eventField"), Terms.Iri(Terms.Types + "nowhere"))); return true;
            case Seen.ExtraType: triples.Add((type, Terms.Iri(Terms.RdfType), Terms.Iri(Terms.Ex + "Other"))); return true;
            case Seen.TypeAndField: triples.Add((type, Terms.Iri(Terms.RdfType), Terms.Iri(Varve + "EventField"))); return true;
            case Seen.ClassTaken:
                if (types.Length < 2)
                {
                    return false;
                }

                // The code keeps the first type of the two, in its order, and refuses the second; the shapes name both.
                faulty = TypeNode(1);
                Replace(triples, faulty, V("eventClass"), Terms.Iri(Terms.Ex + "Class0"));
                return true;
        }

        faulty = field;
        switch (fault)
        {
            case Seen.FieldNamedEvent: Replace(triples, field, V("fieldName"), Terms.Plain("event")); return true;
            case Seen.FieldNameBadPattern: Replace(triples, field, V("fieldName"), Terms.Plain("no-dashes")); return true;
            case Seen.FieldDatatypeLiteral: Replace(triples, field, V("fieldDatatype"), Terms.Plain("integer")); return true;
            case Seen.FieldRequiredMissing: Remove(triples, field, V("fieldRequired")); return true;
            case Seen.FieldAsNumber: triples.Add((field, V("fieldAs"), Terms.Plain("number"))); return true;
            case Seen.FieldKeepLanguageNotBoolean: triples.Add((field, V("fieldKeepLanguage"), Terms.Plain("true"))); return true;
            case Seen.FieldOfNoType: triples.RemoveAll(t => t.P.Equals(V("eventField")) && t.O.Equals(field)); return true;
            case Seen.FieldOfTwoTypes:
                if (types.Length < 2)
                {
                    return false;
                }

                triples.Add((TypeNode(1), V("eventField"), field));
                return true;
            default:
                throw new InvalidOperationException(fault.ToString());
        }
    }

    /// <summary>Applies a fault only the code sees; false when the graph has nothing it applies to.</summary>
    private static bool Apply(Unseen fault, Declaration[] types, List<(RdfTerm S, RdfTerm P, RdfTerm O)> triples)
    {
        RdfTerm type = TypeNode(0);
        switch (fault)
        {
            case Unseen.UnknownVarveTerm: triples.Add((type, V("eventColour"), Terms.Plain("red"))); return true;
            case Unseen.FieldNameTwiceInType:
                if (types[0].Fields < 2)
                {
                    return false;
                }

                Replace(triples, FieldNode(0, 1), V("fieldName"), Terms.Plain("f0"));
                return true;
            case Unseen.SubjectIsAField:
                if (!types[0].Subject)
                {
                    return false;
                }

                Replace(triples, type, V("eventSubject"), Terms.Plain("f0"));
                return true;
            case Unseen.SelectDoesNotParse: Replace(triples, type, V("eventSelect"), Terms.Plain("SELECT nonsense {")); return true;
            case Unseen.SelectProjectsAnotherVariable: Replace(triples, type, V("eventSelect"), Terms.Plain("SELECT ?f0 ?other WHERE { ?event ?p ?o }")); return true;
            case Unseen.BlankNodeElsewhere: triples.Add((type, Terms.Iri(Terms.Ex + "note"), RdfTerm.BlankNode("b"u8))); return true;
            case Unseen.StrayNode: triples.Add((Terms.Iri(Terms.Ex + "stray"), Terms.Iri(Terms.Ex + "p"), Terms.Plain("x"))); return true;
            default:
                throw new InvalidOperationException(fault.ToString());
        }
    }

    private static RdfTerm V(string local) => Terms.Iri(Varve + local);

    private static RdfTerm TypeNode(int t) => Terms.Iri(Terms.Types + "T" + t);

    private static RdfTerm FieldNode(int t, int f) => Terms.Iri(Terms.Types + "T" + t + "/f" + f);

    private static void Replace(List<(RdfTerm S, RdfTerm P, RdfTerm O)> triples, RdfTerm subject, RdfTerm predicate, RdfTerm value)
    {
        Remove(triples, subject, predicate);
        triples.Add((subject, predicate, value));
    }

    private static void Remove(List<(RdfTerm S, RdfTerm P, RdfTerm O)> triples, RdfTerm subject, RdfTerm predicate) =>
        triples.RemoveAll(t => t.S.Equals(subject) && t.P.Equals(predicate));

    /// <summary>The type a field belongs to, when the code names the type instead of the field.</summary>
    private static RdfTerm? Owner(List<(RdfTerm S, RdfTerm P, RdfTerm O)> triples, RdfTerm field)
    {
        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in triples)
        {
            if (p.Equals(V("eventField")) && o.Equals(field))
            {
                return s;
            }
        }

        return null;
    }

    private static bool Names(ShaclReport report, RdfTerm node)
    {
        foreach (ShaclResult result in report.Results)
        {
            if (result.FocusNode.Equals(node))
            {
                return true;
            }
        }

        return false;
    }

    private static string Results(ShaclReport report)
    {
        StringBuilder text = new();
        foreach (ShaclResult result in report.Results)
        {
            text.Append(Encoding.UTF8.GetString(result.FocusNode.Lexical)).Append(' ')
                .Append(Encoding.UTF8.GetString(result.SourceConstraintComponent.Lexical)).Append("; ");
        }

        return text.ToString();
    }

    private static ShaclReport Validate(List<(RdfTerm S, RdfTerm P, RdfTerm O)> triples)
    {
        InMemoryDatasetBuilder builder = new();
        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in triples)
        {
            builder.Add(s, p, o);
        }

        ValidationOutcome outcome = ShaclValidator.Validate(Shapes, builder.ToDataset(), GraphPattern.DefaultGraph);
        Assert.True(outcome.Succeeded, string.Join("; ", outcome.Problems));
        return outcome.Report!;
    }

    private static ShapesGraph LoadShapes() => ShapesGraph.Load(Parse(EventDeclarationShapes.Turtle), GraphPattern.DefaultGraph).Shapes!;

    private static InMemoryDataset Parse(string turtle)
    {
        InMemoryDatasetBuilder builder = new();
        ParseResult result = TurtleParser.Parse(
            Encoding.UTF8.GetBytes(turtle),
            (in QuadView quad) => builder.Add(quad.Subject.Materialise(), quad.Predicate.Materialise(), quad.Object.Materialise()),
            new TurtleOptions { Syntax = RdfSyntax.Turtle });
        Assert.True(result.Succeeded, result.FirstError.ToString());
        return builder.ToDataset();
    }
}
