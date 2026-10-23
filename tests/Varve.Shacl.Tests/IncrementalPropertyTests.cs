// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Varve.Rdf;
using Varve.Shacl.Model;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Shacl.Tests;

/// <summary>
/// ADR 0171 as a property: over generated shapes graphs that use every
/// construct the classification covers, and generated histories of commits
/// with assertions and retractions, the whole report before a commit,
/// without the comparison's results before and with its results after, is
/// a full validation after the commit, byte for byte; and the results the
/// comparison says it introduced are exactly the identities the full report
/// after has and the full report before does not.
/// </summary>
public class IncrementalPropertyTests
{
    /// <summary>Generated histories per run on the land gate, as reported in the pull request.</summary>
    internal static readonly int Iterations = Environment.GetEnvironmentVariable("VARVE_LONG_PROPERTIES") is null ? 500 : 5_000;

    [Fact]
    public async Task The_incremental_report_is_a_full_validation_at_every_position()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        int histories = 0, refused = 0, positions = 0;
        await Model.Generator.SampleAsync(async model =>
        {
            ShapesLoad load = ShapesGraph.Load(Support.Dataset(model.Shapes), GraphPattern.DefaultGraph, null, ct);
            if (load.Shapes is not { } shapes)
            {
                // An unstratifiable or ill-formed graph is refused before any data (ADR 0150, 0151): nothing to maintain.
                Interlocked.Increment(ref refused);
                return;
            }

            Recorder recorder = new(shapes);
            await using Dataset dataset = await Dataset.CreateAsync(
                new MemoryStorage(),
                new DatasetId(Guid.NewGuid()),
                new DatasetOptions { Clock = TimeProvider.System, Validators = [recorder] },
                ct);
            HashSet<Triple> state = [];
            ValidationReport before = Full(shapes, dataset);
            foreach (Triple[] toggles in model.History)
            {
                CommitRequest request = new();
                foreach (Triple triple in toggles.Distinct())
                {
                    (RequestTerm s, RequestTerm p, RequestTerm o) = triple.Request();
                    if (state.Remove(triple))
                    {
                        request.Retract(s, p, o);
                    }
                    else
                    {
                        state.Add(triple);
                        request.Assert(s, p, o);
                    }
                }

                recorder.Last = null;
                CommitResult result = await dataset.CommitAsync(request, ct);
                Assert.Equal(CommitOutcome.Committed, result.Outcome);
                IncrementalComparison comparison = recorder.Last ?? throw new InvalidOperationException("The validator did not run.");

                ValidationReport after = Full(shapes, dataset);
                ValidationReport combined = ValidationReport.Combine(before, comparison.Before, comparison.After);
                Assert.True(
                    after.ToCanonicalNQuads().Span.SequenceEqual(combined.ToCanonicalNQuads().Span),
                    "The incremental report differs from a full validation.\n" + model + "\n# expected\n"
                        + Encoding.UTF8.GetString(after.ToCanonicalNQuads().Span) + "\n# incremental\n" + Encoding.UTF8.GetString(combined.ToCanonicalNQuads().Span));

                HashSet<string> introduced = [.. comparison.After.Identities().Where((_, i) => comparison.Introduced.Contains(comparison.After.Results[i]))];
                HashSet<string> expected = [.. after.Identities().Except(before.Identities())];
                Assert.True(expected.SetEquals(introduced), "Introduced differs from the full reports' difference.\n" + model);

                before = after;
                Interlocked.Increment(ref positions);
            }

            Interlocked.Increment(ref histories);
        }, iter: Iterations);

        TestContext.Current.SendDiagnosticMessage($"{histories} histories, {positions} positions, {refused} shapes graphs refused as unstratifiable");
    }

    private static ValidationReport Full(ShapesGraph shapes, Dataset dataset)
    {
        using DatasetView view = dataset.Pin();
        ValidationOutcome outcome = ShaclValidator.Validate(shapes, view, GraphPattern.DefaultGraph);
        Assert.True(outcome.Succeeded, string.Join("; ", outcome.Problems));
        return outcome.Report!;
    }

    /// <summary>Runs the incremental validation inside the sequencer, where the store's gate runs it (ADR 0169).</summary>
    private sealed class Recorder(ShapesGraph shapes) : ICommitValidator
    {
        internal IncrementalComparison? Last { get; set; }

        public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta)
        {
            IncrementalOutcome outcome = IncrementalValidation.Compare(shapes, proposed, GraphPattern.DefaultGraph, delta);
            Last = outcome.Comparison ?? throw new InvalidOperationException(string.Join("; ", outcome.Problems));
            return ValidationVerdict.Accept();
        }
    }

    /// <summary>A triple of the generated data, its terms in Turtle.</summary>
    internal readonly record struct Triple(string S, string P, string O)
    {
        internal (RequestTerm, RequestTerm, RequestTerm) Request() => (Term(S), Term(P), Term(O));

        private static RequestTerm Term(string text)
        {
            InMemoryDataset parsed = Support.Dataset("ex:s ex:p " + text + " .");
            using IQuadCursor cursor = parsed.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);
            cursor.MoveNext();
            parsed.TryExternalise(cursor.Current.Object, out RdfTerm? term);
            return RequestTerm.FromTerm(term!);
        }

        public override string ToString() => S + " " + P + " " + O + " .";
    }

    /// <summary>A shapes graph and a history of commits, each a set of triples toggled.</summary>
    internal sealed record Model(string Shapes, Triple[][] History)
    {
        private static readonly string[] Nodes = ["ex:n0", "ex:n1", "ex:n2", "ex:n3"];
        private static readonly string[] Classes = ["ex:C0", "ex:C1", "ex:C2"];
        private static readonly string[] Predicates = ["ex:p0", "ex:p1", "ex:p2"];
        private static readonly string[] Literals = ["1", "2", "\"a\"@en", "\"b\"@fr", "\"ab\""];
        private const int ShapeCount = 3;

        private static readonly Gen<string> Path = Gen.Recursive<string>((depth, self) =>
            depth >= 2
                ? Gen.OneOfConst(Predicates)
                : Gen.Frequency(
                    (5, Gen.OneOfConst(Predicates)),
                    (1, self.Select(p => "[ sh:inversePath " + p + " ]")),
                    (1, Gen.Select(self, self).Select((a, b) => "( " + a + " " + b + " )")),
                    (1, Gen.Select(self, self).Select((a, b) => "[ sh:alternativePath ( " + a + " " + b + " ) ]")),
                    (1, self.Select(p => "[ sh:zeroOrMorePath " + p + " ]")),
                    (1, self.Select(p => "[ sh:oneOrMorePath " + p + " ]")),
                    (1, self.Select(p => "[ sh:zeroOrOnePath " + p + " ]"))));

        private static readonly Gen<string> Shape = Gen.Int[0, ShapeCount - 1].Select(i => "ex:S" + i);

        /// <summary>
        /// A shape a negative or target reference of shape <paramref name="i"/>
        /// may name: a later one, so that no cycle runs through it (ADR 0150);
        /// the last shape names none.
        /// </summary>
        private static Gen<string>? Later(int i) => i < ShapeCount - 1 ? Gen.Int[i + 1, ShapeCount - 1].Select(j => "ex:S" + j) : null;

        /// <summary>A constraint that applies at a shape's value nodes, node or property shape alike.</summary>
        private static Gen<string> ValueConstraint(int i)
        {
            List<Gen<string>> choices =
            [
                .. Positive,
            ];
            if (Later(i) is { } later)
            {
                choices.Add(later.Select(s => "sh:not " + s));
                choices.Add(Gen.Select(later, later).Select((a, b) => "sh:xone ( " + a + " " + b + " )"));
            }

            return Gen.OneOf([.. choices]);
        }

        private static readonly Gen<string>[] Positive =
        [
            Gen.OneOfConst(Classes).Select(c => "sh:class " + c),
            Gen.Const("sh:datatype xsd:integer"),
            Gen.Const("sh:nodeKind sh:IRI"),
            Gen.Const("sh:in ( ex:n0 1 \"a\"@en )"),
            Gen.Const("sh:pattern \"^a\""),
            Gen.Const("sh:languageIn ( \"en\" )"),
            Gen.Const("sh:minInclusive 2"),
            Shape.Select(s => "sh:node " + s),
            Gen.Select(Shape, Shape).Select((a, b) => "sh:or ( " + a + " " + b + " )"),
            Gen.Select(Shape, Shape).Select((a, b) => "sh:and ( " + a + " " + b + " )"),
            Gen.OneOfConst(Classes).Select(c => "sh:rootClass " + c),
            Shape.Select(s => "sh:memberShape " + s),
            Gen.Const("sh:minListLength 1"),
            Gen.OneOfConst(Predicates).Select(p => "sh:uniqueValuesFor ( " + p + " )"),
        ];

        /// <summary>A constraint only a property shape has.</summary>
        private static Gen<string> PropertyConstraint(int i)
        {
            List<Gen<string>> choices =
            [
                Gen.Int[0, 2].Select(k => "sh:minCount " + k.ToString(CultureInfo.InvariantCulture)),
                Gen.Int[0, 2].Select(k => "sh:maxCount " + k.ToString(CultureInfo.InvariantCulture)),
                Gen.Const("sh:uniqueLang true"),
                Path.Select(p => "sh:equals " + p),
                Path.Select(p => "sh:disjoint " + p),
                Gen.OneOfConst(Predicates).Select(p => "sh:lessThan " + p),
                Shape.Select(s => "sh:qualifiedValueShape " + s + " ; sh:qualifiedMinCount 1"),
                Shape.Select(s => "sh:someValue " + s),
            ];
            if (Later(i) is { } later)
            {
                choices.Add(later.Select(s => "sh:qualifiedValueShape " + s + " ; sh:qualifiedMaxCount 1"));
            }

            return Gen.OneOf([.. choices]);
        }

        private static Gen<string> Property(int i) =>
            from path in Path
            from constraints in Gen.Frequency((1, ValueConstraint(i)), (2, PropertyConstraint(i))).Array[1, 2]
            from severity in Gen.Frequency((4, Gen.Const(string.Empty)), (1, Gen.Const(" ; sh:severity sh:Warning")))
            select "sh:property [ sh:path " + path + " ; " + string.Join(" ; ", constraints) + severity + " ]";

        private static Gen<string> Target(int i)
        {
            List<Gen<string>> choices =
            [
                Gen.OneOfConst(Nodes).Select(n => "sh:targetNode " + n),
                Gen.OneOfConst(Classes).Select(c => "sh:targetClass " + c),
                Gen.OneOfConst(Predicates).Select(p => "sh:targetSubjectsOf " + p),
                Gen.OneOfConst(Predicates).Select(p => "sh:targetObjectsOf " + p),
                Gen.Const(string.Empty),
            ];
            if (Later(i) is { } later)
            {
                choices.Add(later.Select(s => "sh:targetWhere " + s));
            }

            return Gen.OneOf([.. choices]);
        }

        private static Gen<string> Body(int i) => Gen.Frequency(
            (2, Gen.Const("sh:property ex:P0")),
            (3, Property(i)),
            (2, ValueConstraint(i)),
            (1, Gen.Const("sh:closed true ; sh:ignoredProperties ( rdf:type )")));

        private static readonly Gen<string> ShapesGraphs =
            Gen.Select(
                Gen.Select(Gen.Select(Target(0), Body(0).Array[1, 3]), Gen.Select(Target(1), Body(1).Array[1, 3]), Gen.Select(Target(2), Body(2).Array[1, 3])),
                Gen.Select(Path, PropertyConstraint(ShapeCount - 1)))
            .Select((nodes, shared) => (Shapes: new[] { nodes.Item1, nodes.Item2, nodes.Item3 }, Shared: shared))
            .Select(model =>
            {
                (string, string[])[] shapes = model.Shapes;

                // A property shape several node shapes share: one result may come from two pairs (ADR 0169's R1).
                StringBuilder text = new("ex:P0 a sh:PropertyShape ; sh:path " + model.Shared.Item1 + " ; " + model.Shared.Item2 + " .\n");
                for (int i = 0; i < shapes.Length; i++)
                {
                    (string target, string[] body) = shapes[i];
                    text.Append("ex:S").Append(i).Append(" a sh:NodeShape");
                    if (target.Length > 0)
                    {
                        text.Append(" ; ").Append(target);
                    }

                    foreach (string part in body.Distinct())
                    {
                        text.Append(" ; ").Append(part);
                    }

                    text.Append(" .\n");
                }

                return text.ToString();
            });

        private static readonly Gen<Triple> Triples = Gen.Frequency(
            (6, Gen.Select(Gen.OneOfConst(Nodes), Gen.OneOfConst(Predicates), Gen.OneOfConst([.. Nodes, .. Literals])).Select((s, p, o) => new Triple(s, p, o))),
            (3, Gen.Select(Gen.OneOfConst(Nodes), Gen.OneOfConst(Classes)).Select((s, c) => new Triple(s, "rdf:type", c))),
            (1, Gen.Select(Gen.OneOfConst(Classes), Gen.OneOfConst(Classes)).Select((a, b) => new Triple(a, "rdfs:subClassOf", b))),
            (1, Gen.Select(Gen.OneOfConst(Nodes), Gen.OneOfConst([.. Nodes, "1", "\"a\"@en"])).Select((s, o) => new Triple(s, "rdf:first", o))),
            (1, Gen.Select(Gen.OneOfConst(Nodes), Gen.OneOfConst([.. Nodes, "rdf:nil"])).Select((s, o) => new Triple(s, "rdf:rest", o))),
            (1, Gen.Select(Gen.OneOfConst(Nodes), Shape).Select((n, s) => new Triple(n, "sh:shape", s))));

        internal static readonly Gen<Model> Generator =
            from shapes in ShapesGraphs
            from history in Triples.Array[1, 4].Array[1, 8]
            select new Model(shapes, history);

        public override string ToString() =>
            "# shapes\n" + Shapes + "# history\n" + string.Join("# commit\n", History.Select(c => string.Join("\n", c.Select(t => t.ToString())) + "\n"));
    }
}
