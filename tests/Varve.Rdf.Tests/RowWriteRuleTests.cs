// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Xunit;
using static Varve.Rdf.Tests.RowFixture;

namespace Varve.Rdf.Tests;

/// <summary>
/// <see cref="RowWriteRule"/> (ADR 0236): a row-restricted writer writes only
/// rows already wholly its own, creates only at blank nodes or under
/// <c>createUnder</c>, links only to rows it reads, and its outcome never
/// depends on a quad it does not see.
/// </summary>
public class RowWriteRuleTests
{
    private const string Mine = "http://ex/a";
    private static readonly string[] Universe = ["http://ex/a", "http://ex/b", "http://ex/x", "http://ex/new/1", "http://ex/absent"];

    [Fact]
    public void a_writer_writes_its_own_row_and_nothing_else()
    {
        List<(string, string, string, string?)> data =
        [
            (Mine, TypeIri, Person, Classified[0]),
            ("http://ex/b", TypeIri, Person, Classified[0]),
            ("http://ex/x", Knows, "http://ex/a", Classified[0]),
        ];

        Assert.True(Accepts(data, [(Mine, Knows, "lit", Classified[1])], []));
        Assert.False(Accepts(data, [("http://ex/b", Knows, "lit", Classified[1])], []), "a hidden row");
        Assert.False(Accepts(data, [("http://ex/x", Knows, "lit", Classified[1])], []), "an unclassified row");
        Assert.False(Accepts(data, [(Mine, Knows, "http://ex/b", Classified[1])], []), "a link into a hidden row");
        Assert.False(Accepts(data, [(Mine, Knows, "http://ex/absent", Classified[1])], []), "a link to an IRI with no row");
        Assert.True(Accepts(data, [(Mine, Label, "http://ex/absent", Classified[1])], []), "a public link");
        Assert.True(Accepts(data, [(Mine, Knows, "http://ex/x", Classified[1])], []), "a link to an unclassified row it reads");
        Assert.False(Accepts(data, [("http://ex/absent", Owns, Mine, Classified[1])], []), "creation outside createUnder");
        Assert.True(Accepts(data, [("http://ex/new/1", Owns, Mine, Classified[1])], []), "creation under createUnder");
        Assert.False(Accepts(data, [("http://ex/new/1", Knows, "lit", Classified[1])], []), "a created row must be its own");
        Assert.True(Accepts(data, [("http://ex/b", Knows, "lit", Outside)], []), "a graph outside every binding");
        Assert.True(Accepts(data, [], [("http://ex/b", TypeIri, Person, Classified[0])]), "a retraction it does not see is a no-op");
        Assert.False(Accepts(data, [(Mine, "http://ex/caseworker", "http://ex/me", Classified[0]), ("http://ex/b", "http://ex/caseworker", "http://ex/me", Classified[0])], []), "an escalation into a hidden row");
    }

    /// <summary>
    /// ADR 0236's property at the rule: over generated datasets and requests,
    /// the dataset and the dataset without every quad the writer does not see
    /// give the same outcome.
    /// </summary>
    [Fact]
    public void a_writes_outcome_never_depends_on_a_quad_the_writer_does_not_see()
    {
        Gen<(string, string, string, string?)> request = Gen.Select(
            Gen.OneOfConst(Universe),
            Gen.OneOfConst(TypeIri, Owns, Knows, Label),
            Gen.OneOfConst([.. Universe, Person, "lit"]),
            Gen.OneOfConst<string?>(Classified[0], Classified[1]),
            (s, p, o, g) => (s, p, p == TypeIri ? Person : o, g));

        Gen.Select(RowScopedSourceTests.Quads.List[0, 14], request.List[0, 3], request.List[0, 2], (data, asserted, retracted) => (data, asserted, retracted)).Sample(sample =>
        {
            (List<(string, string, string, string?)> data, List<(string, string, string, string?)> asserted, List<(string, string, string, string?)> retracted) = sample;
            InMemoryDataset whole = RowScopedSourceTests.Dataset(data);
            IQuadSource visible = new RowScopedQuadSource(whole, new RowFixture(whole), Scope);
            List<(string, string, string, string?)> seen = [.. Quads(visible).Select(q => (Text(q.S), Text(q.P), q.O.Kind == RdfTermKind.Literal ? "lit" : Text(q.O), q.G is null ? null : Text(q.G)))];

            Assert.Equal(Accepts(data, asserted, retracted), Accepts(seen, asserted, retracted));
        }, iter: RowScopedSourceTests.Iterations);
    }

    private static SubjectScope Subjects { get; } = SubjectScope.Of([IriOf(Mine)], [], [], [], ["http://ex/new/"]);

    private static RowScope Scope { get; } = RowScopedSourceTests.Restricted(Subjects);

    // The write over one handle space: the state before, the request, and
    // the state after with every addition and every retraction the writer
    // sees (W5).
    private static bool Accepts(
        IEnumerable<(string S, string P, string O, string? G)> data,
        IEnumerable<(string S, string P, string O, string? G)> asserted,
        IEnumerable<(string S, string P, string O, string? G)> retracted)
    {
        InMemoryDatasetBuilder builder = new();

        foreach (string term in Universe.Concat([TypeIri, Owns, Knows, Label, Person, Outside, .. Classified]))
        {
            builder.Internalise(IriOf(term));
        }

        builder.Internalise(RdfTerm.Literal("lit"u8));

        foreach ((string s, string p, string o, string? g) in data)
        {
            builder.Add(Quad(builder, s, p, o, g));
        }

        InMemoryDataset before = builder.ToDataset();
        RowFixture beforeRows = new(before);
        Quad[] adds = [.. asserted.Select(q => Quad(builder, q.S, q.P, q.O, q.G))];
        Quad[] removes = [.. retracted.Select(q => Quad(builder, q.S, q.P, q.O, q.G))];
        RowScopedQuadSource seen = new(before, beforeRows, Scope);

        foreach (Quad quad in removes.Where(q => seen.Allows(in q)))
        {
            builder.Remove(in quad);
        }

        foreach (Quad quad in adds)
        {
            builder.Add(in quad);
        }

        InMemoryDataset after = builder.ToDataset();
        RowWriteRule rule = new(before, beforeRows, after, new RowFixture(after), Scope, Scope);

        try
        {
            rule.Check(adds, removes);
            return true;
        }
        catch (RowNotWritableException)
        {
            return false;
        }
    }

    private static Quad Quad(InMemoryDatasetBuilder builder, string s, string p, string o, string? g) => new(
        builder.Internalise(IriOf(s)),
        builder.Internalise(IriOf(p)),
        builder.Internalise(o == "lit" ? RdfTerm.Literal("lit"u8) : IriOf(o)),
        g is null ? TermHandle.None : builder.Internalise(IriOf(g)));
}
