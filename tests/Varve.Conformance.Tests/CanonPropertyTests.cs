// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using CsCheck;
using Varve.Rdf;
using Varve.Turtle;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// RDFC-1.0 as properties (<c>rdf-canon.md</c> §6, §7, ADR 0059):
/// <c>iso(A, B) ⇔ canon(A) = canon(B)</c> against the harness's backtracking
/// check — which makes each the other's differential test — idempotence, and
/// the canonical form reading back as the dataset it came from.
/// </summary>
/// <remarks>
/// The equivalence holds in both directions for datasets whose graph names
/// are IRIs. With blank nodes as graph names only one direction holds —
/// equal canonical forms mean isomorphic datasets — because RDFC-1.0 can give
/// two isomorphic datasets different canonical forms there; pyoxigraph's
/// RDFC-1.0 gives the same two forms as this one on the pinned
/// counterexample. That is a finding about the algorithm (<c>rdf-canon.md</c>
/// §6), found by the first run of this property.
/// </remarks>
public class CanonPropertyTests
{
    /// <summary>The iterations each property runs; the report states them.</summary>
    internal const int Iterations = 20_000;

    private static byte[] U(string s) => Encoding.UTF8.GetBytes(s);

    // Few distinct terms and blank nodes, so that pairs are often isomorphic
    // and symmetric structures — which make first-degree hashes collide and
    // send the algorithm to Hash N-Degree Quads — are common.
    private static readonly Gen<RdfTerm> Blank = Gen.Int[0, 4].Select(i => RdfTerm.BlankNode(U("n" + i.ToString(CultureInfo.InvariantCulture))));

    private static readonly Gen<RdfTerm> Iri = Gen.OneOfConst("a", "b", "é").Select(l => RdfTerm.Iri(U("http://example.org/" + l)));

    private static readonly Gen<RdfTerm> Literal = Gen.OneOf(
        Gen.OneOfConst("x", "tab\there", "nl\nq\"\\", "\u0001\u007F\b\f\r", "\uFFFE", "😀").Select(t => RdfTerm.Literal(U(t))),
        Gen.OneOfConst("en", "EN").Select(l => RdfTerm.Literal(U("x"), U(l))),
        Gen.Const(RdfTerm.Literal(U("x"), U("ar"), TextDirection.RightToLeft)),
        Gen.Const(RdfTerm.Literal(U("1"), RdfTerm.Iri(U("http://www.w3.org/2001/XMLSchema#integer")))),
        Gen.Const(RdfTerm.TripleTerm(RdfTerm.Iri(U("http://example.org/s")), RdfTerm.Iri(U("http://example.org/p")), RdfTerm.Literal(U("o")))));

    private static Gen<DataQuad> QuadOf(Gen<RdfTerm?> graph) => Gen.Select(
        Gen.OneOf(Blank, Blank, Iri),
        Gen.OneOfConst("p", "q").Select(l => RdfTerm.Iri(U("http://example.org/" + l))),
        Gen.OneOf(Blank, Blank, Iri, Literal),
        graph,
        (s, p, o, g) => new DataQuad(s, p, o, g));

    // Graph names that are IRIs or the default graph.
    private static readonly Gen<DataQuad> Quad =
        QuadOf(Gen.OneOf(Gen.Const((RdfTerm?)null), Gen.Const((RdfTerm?)null), Iri.Select(t => (RdfTerm?)t)));

    // Blank nodes as graph names as well.
    private static readonly Gen<DataQuad> AnyQuad =
        QuadOf(Gen.OneOf(Gen.Const((RdfTerm?)null), Gen.Const((RdfTerm?)null), Iri.Select(t => (RdfTerm?)t), Blank.Select(t => (RdfTerm?)t)));

    private static readonly Gen<List<DataQuad>> Dataset = Quad.List[0, 10];

    private static readonly Gen<List<DataQuad>> AnyDataset = AnyQuad.List[0, 10];

    /// <summary>
    /// A pair: a dataset and either a relabelled, reordered copy of it
    /// (isomorphic by construction) or that copy with one quad changed or
    /// removed (usually not, and the backtracking check says which).
    /// </summary>
    private static readonly Gen<(List<DataQuad> A, List<DataQuad> B)> Pair = PairOf(Dataset, Quad);

    private static readonly Gen<(List<DataQuad> A, List<DataQuad> B)> AnyPair = PairOf(AnyDataset, AnyQuad);

    private static Gen<(List<DataQuad> A, List<DataQuad> B)> PairOf(Gen<List<DataQuad>> datasets, Gen<DataQuad> quads) =>
        Gen.Select(datasets, Gen.Int[0, 1_000_000], Gen.Int[0, 2], quads, (a, seed, edit, replacement) =>
        {
            Random random = new(seed);
            Dictionary<string, string> relabel = [];
            List<DataQuad> b = [.. a.Select(q => Relabel(q, relabel, random)).OrderBy(_ => random.Next())];

            if (b.Count > 0 && edit == 1)
            {
                b[random.Next(b.Count)] = replacement;
            }
            else if (b.Count > 0 && edit == 2)
            {
                b.RemoveAt(random.Next(b.Count));
            }

            return (a, b);
        });

    private static DataQuad Relabel(DataQuad quad, Dictionary<string, string> map, Random random)
    {
        RdfTerm Node(RdfTerm term)
        {
            if (term.Kind != RdfTermKind.BlankNode)
            {
                return term;
            }

            string label = Encoding.UTF8.GetString(term.Lexical);

            if (!map.TryGetValue(label, out string? renamed))
            {
                renamed = "r" + random.Next(1_000_000).ToString(CultureInfo.InvariantCulture) + "_" + map.Count.ToString(CultureInfo.InvariantCulture);
                map[label] = renamed;
            }

            return RdfTerm.BlankNode(U(renamed));
        }

        return new DataQuad(Node(quad.Subject), quad.Predicate, Node(quad.Object), quad.Graph is null ? null : Node(quad.Graph));
    }

    [Fact]
    public void Without_blank_graph_names_isomorphism_holds_exactly_when_the_canonical_forms_are_equal()
    {
        int same = 0, different = 0, inconclusive = 0;

        Pair.Sample(
            pair =>
            {
                IsomorphismResult verdict = Isomorphism.Compare(Parsed(pair.A), Parsed(pair.B));
                bool equal = Canonical(pair.A).AsSpan().SequenceEqual(Canonical(pair.B));

                switch (verdict.Verdict)
                {
                    case IsomorphismVerdict.Inconclusive:
                        Interlocked.Increment(ref inconclusive);
                        return;
                    case IsomorphismVerdict.Same:
                        Interlocked.Increment(ref same);
                        break;
                    default:
                        Interlocked.Increment(ref different);
                        break;
                }

                Assert.True(
                    verdict.IsSame == equal,
                    "backtracking says " + verdict.Verdict + ", canonical forms " + (equal ? "equal" : "differ") + "\nA:\n" + Text(pair.A) + "B:\n" + Text(pair.B));
            },
            iter: Iterations);

        TestContext.Current.TestOutputHelper?.WriteLine($"isomorphic {same}, not {different}, inconclusive {inconclusive}");

        // Both directions of the equivalence were exercised, many times each.
        Assert.True(same > Iterations / 10 && different > Iterations / 10, $"isomorphic {same}, not {different}");
    }

    /// <summary>
    /// With blank graph names: equal canonical forms still mean isomorphic
    /// datasets, always; isomorphic datasets with different canonical forms
    /// are counted and reported, as RDFC-1.0's, not this implementation's.
    /// </summary>
    [Fact]
    public void With_blank_graph_names_equal_canonical_forms_mean_isomorphic_datasets()
    {
        int separatedWrongly = 0, isomorphic = 0;

        AnyPair.Sample(
            pair =>
            {
                IsomorphismResult verdict = Isomorphism.Compare(Parsed(pair.A), Parsed(pair.B));
                bool equal = Canonical(pair.A).AsSpan().SequenceEqual(Canonical(pair.B));

                if (equal)
                {
                    Assert.True(verdict.Verdict != IsomorphismVerdict.Different, "equal canonical forms of datasets that are not isomorphic\nA:\n" + Text(pair.A) + "B:\n" + Text(pair.B));
                }

                if (verdict.IsSame)
                {
                    Interlocked.Increment(ref isomorphic);

                    if (!equal)
                    {
                        Interlocked.Increment(ref separatedWrongly);
                    }
                }
            },
            iter: Iterations);

        TestContext.Current.TestOutputHelper?.WriteLine($"isomorphic pairs {isomorphic}, of which RDFC-1.0 gave different forms to {separatedWrongly}");
    }

    /// <summary>
    /// The counterexample the first run found, minimised by hand: _:n0 and
    /// _:n2 are not automorphic — swapping them needs _:n3 and _:n4 swapped,
    /// which the third quad of the first dataset forbids — but their
    /// first-degree hashes are equal and so are their N-degree hashes, because
    /// a related hash records where the related node stands in the quad
    /// (§4.7.3) and not where the reference node does: object here, graph name
    /// there. The tie falls to input order, and the two relabellings get
    /// different forms. pyoxigraph 0.5.11's <c>Dataset.canonicalize(RDFC_1_0)</c>
    /// produces the same pair of forms across relabellings, though not
    /// necessarily for these two inputs, since its tie falls to its own
    /// order (<c>rdf-canon.md</c> §6 has the counts).
    /// </summary>
    [Fact]
    public void Rdfc_10_gives_these_isomorphic_datasets_different_forms()
    {
        const string a = """
            <http://example.org/a> <http://example.org/p> _:n1 _:n1 .
            _:n4 <http://example.org/p> _:n0 _:n2 .
            _:n4 <http://example.org/q> _:n3 _:n4 .
            _:n3 <http://example.org/p> _:n2 _:n0 .
            """;
        const string b = """
            <http://example.org/a> <http://example.org/p> _:x0 _:x0 .
            _:x4 <http://example.org/p> _:x3 _:x2 .
            _:x1 <http://example.org/p> _:x2 _:x3 .
            _:x1 <http://example.org/q> _:x4 _:x1 .
            """;
        List<DataQuad> first = Read(U(a));
        List<DataQuad> second = Read(U(b));

        Assert.True(Isomorphism.Compare(Parsed(first), Parsed(second)).IsSame);
        Assert.Equal(
            "<http://example.org/a> <http://example.org/p> _:c14n2 _:c14n2 .\n"
            + "_:c14n0 <http://example.org/p> _:c14n4 _:c14n3 .\n"
            + "_:c14n1 <http://example.org/p> _:c14n3 _:c14n4 .\n"
            + "_:c14n1 <http://example.org/q> _:c14n0 _:c14n1 .\n",
            Encoding.UTF8.GetString(Canonical(first)));
        Assert.Equal(
            "<http://example.org/a> <http://example.org/p> _:c14n2 _:c14n2 .\n"
            + "_:c14n0 <http://example.org/p> _:c14n3 _:c14n4 .\n"
            + "_:c14n1 <http://example.org/p> _:c14n4 _:c14n3 .\n"
            + "_:c14n1 <http://example.org/q> _:c14n0 _:c14n1 .\n",
            Encoding.UTF8.GetString(Canonical(second)));
    }

    [Fact]
    public void Canonicalising_the_canonical_form_gives_it_back()
    {
        Dataset.Sample(
            quads =>
            {
                byte[] once = Canonical(quads);
                byte[] twice = Canonical(Read(once));
                Assert.True(once.AsSpan().SequenceEqual(twice), "not idempotent:\n" + Encoding.UTF8.GetString(once) + "---\n" + Encoding.UTF8.GetString(twice));
            },
            iter: Iterations);
    }

    /// <remarks>
    /// And it is the same bytes <c>Varve.Turtle</c>'s canonical N-Quads writer
    /// produces for it. The two canonical writers are separate code, one per
    /// layer (ADR 0003), and ADR 0061 makes them one form: RDF 1.2 N-Triples
    /// §3's, which is RDFC-1.0 Appendix A's. Reading the canonical document
    /// and writing each quad back out canonically must give it byte for byte.
    /// </remarks>
    [Fact]
    public void The_canonical_form_reads_back_as_the_dataset_it_came_from()
    {
        AnyDataset.Sample(
            quads =>
            {
                byte[] canonical = Canonical(quads);
                List<DataQuad> back = Read(canonical);
                IsomorphismResult verdict = Isomorphism.Compare(Parsed(back), Parsed(quads));
                Assert.True(verdict.Verdict != IsomorphismVerdict.Different, verdict.Reason + "\n" + Text(quads));

                byte[] rewritten = Rewrite(canonical);
                Assert.True(
                    rewritten.AsSpan().SequenceEqual(canonical),
                    "Varve.Turtle's canonical writer and RDFC-1.0's disagree:\n" + Encoding.UTF8.GetString(canonical)
                    + "---\n" + Encoding.UTF8.GetString(rewritten));
            },
            iter: Iterations);
    }

    private static byte[] Rewrite(byte[] nquads)
    {
        ArrayBufferWriter output = new();
        WriteOptions options = new() { Syntax = RdfSyntax.NQuads };
        ParseResult result = NQuadsParser.Parse(
            nquads,
            (in QuadView q) => NQuadsWriter.Write(output, in q, in options),
            new ParseOptions { Syntax = RdfSyntax.NQuads });
        Assert.True(result.Succeeded, "the canonical form does not parse: " + result.FirstError);
        return output.Written.ToArray();
    }

    private static byte[] Canonical(List<DataQuad> quads)
    {
        InMemoryDatasetBuilder builder = new();

        foreach (DataQuad quad in quads)
        {
            builder.Add(quad.Subject, quad.Predicate, quad.Object, quad.Graph);
        }

        return RdfCanonicaliser.Canonicalise(builder.ToDataset()).NQuads.ToArray();
    }

    private static List<DataQuad> Read(byte[] nquads)
    {
        List<DataQuad> quads = [];
        ParseResult result = NQuadsParser.Parse(
            nquads,
            (in QuadView q) => quads.Add(new DataQuad(q.Subject.Materialise(), q.Predicate.Materialise(), q.Object.Materialise(), q.HasGraph ? q.Graph.Materialise() : null)),
            new ParseOptions { Syntax = RdfSyntax.NQuads });
        Assert.True(result.Succeeded, "the canonical form does not parse: " + result.FirstError + "\n" + Encoding.UTF8.GetString(nquads));
        return quads;
    }

    private static List<ParsedQuad> Parsed(List<DataQuad> quads) =>
        [.. quads.Select(q => new ParsedQuad(EvaluationData.Text(q.Subject), EvaluationData.Text(q.Predicate), EvaluationData.Text(q.Object), q.Graph is null ? null : EvaluationData.Text(q.Graph))).Distinct()];

    private static string Text(List<DataQuad> quads) => string.Concat(Parsed(quads).Select(q => q + "\n"));
}
