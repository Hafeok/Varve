// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CsCheck;
using Varve.Classification;
using Varve.Classification.Model;
using Varve.Rdf;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Xunit;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// ADR 0237 point 11, part 1's replication gate: classification is derived,
/// so a tier-1 follower (ADR 0164) keeps its own classification projection
/// from its log and, at its reported position and every position before it,
/// shows a row-restricted caller exactly the quads its writer shows.
/// </summary>
[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public class FollowerRowsTests
{
    /// <summary>Generated histories per run on the land gate; ten times as many in the nightly <c>long</c> job.</summary>
    private static readonly int Iterations = Environment.GetEnvironmentVariable("VARVE_LONG_PROPERTIES") is null ? 30 : 300;

    private static readonly RdfTerm Shapes = Iri("http://ex/shapes");
    private static readonly ClassifierBinding Binding = new(Iri("http://ex/binding"), Shapes, GraphScope.Of([], ["http://ex/g/"], DefaultGraphAccess.Excluded));
    private static readonly string[] Nodes = ["http://ex/a", "http://ex/b", "http://ex/c", "http://ex/x", "http://ex/y"];
    private static readonly string[] Predicates = ["http://www.w3.org/1999/02/22-rdf-syntax-ns#type", "http://ex/ownedBy", "http://ex/knows", "http://ex/label"];

    [Fact]
    public async Task A_tier_1_follower_shows_the_rows_its_writer_shows_at_its_position()
    {
        Gen<(string S, string P, string O, string G)> quad = Gen.Select(Gen.OneOfConst(Nodes), Gen.OneOfConst(Predicates), Gen.OneOfConst(Nodes), Gen.OneOfConst("http://ex/g/1", "http://ex/g/2", "http://ex/other"), (s, p, o, g) => (s, p, p.EndsWith("#type", StringComparison.Ordinal) ? "http://ex/Person" : o, g));

        await Gen.Select(quad.Array[1, 4].Array[1, 6], Gen.Int[0, 7], Gen.Int[0, 6]).SampleAsync(async (commits, set, advanceAt) =>
        {
            await using ObjectHarness objects = new();
            await using Dataset writer = await T.OpenOrCreate(await objects.OpenAsync(), T.Options());
            await using Dataset follower = await Dataset.OpenFollowerAsync(await objects.OpenAsync(ObjectStorageAccess.ReadOnly), T.Options(), T.Ct);
            await writer.CommitAsync(ShapesRequest(), T.Ct);

            for (int i = 0; i < commits.Length; i++)
            {
                CommitRequestBuilder request = new();
                foreach ((string s, string p, string o, string g) in commits[i])
                {
                    request.Assert(RequestTerm.FromTerm(Iri(s)), RequestTerm.FromTerm(Iri(p)), RequestTerm.FromTerm(Iri(o)), RequestTerm.FromTerm(Iri(g)));
                }

                await writer.CommitAsync(request, T.Ct);

                // The follower advances at some point mid-history, and at the end.
                if (i == advanceAt)
                {
                    await follower.AdvanceAsync(T.Ct);
                }
            }

            Position reported = await follower.AdvanceAsync(T.Ct);
            Assert.Equal(writer.Head, reported);
            ClassificationProjection onWriter = new(writer, Binding);
            ClassificationProjection onFollower = new(follower, Binding);
            await writer.CatchUpAsync(onWriter, T.Ct);
            await follower.CatchUpAsync(onFollower, T.Ct);
            RowScope scope = RowScope.Of([new(Binding.Binding, SubjectScope.Of([.. Nodes.Take(3).Where((_, i) => (set & (1 << i)) != 0).Select(Iri)], [], [], [], []))]);

            for (long p = 0; p <= reported.Value; p++)
            {
                using DatasetView w = await writer.AsOfAsync(new Position(p), T.Ct);
                using DatasetView f = await follower.AsOfAsync(new Position(p), T.Ct);
                Assert.True(onWriter.TryAt(new Position(p), w, out IRowClassification wr));
                Assert.True(onFollower.TryAt(new Position(p), f, out IRowClassification fr));
                Assert.Equal(Seen(RowScopedQuadSource.Wrap(w, wr, scope)), Seen(RowScopedQuadSource.Wrap(f, fr, scope)));
            }
        }, iter: Iterations);
    }

    private static CommitRequestBuilder ShapesRequest()
    {
        CommitRequestBuilder request = new();
        RdfTerm yes = RdfTerm.Literal("true"u8, Iri("http://www.w3.org/2001/XMLSchema#boolean"));
        (string, string, RdfTerm)[] shapes =
        [
            ("http://ex/S", "https://w3id.org/varve/ns#dataSubject", yes),
            ("http://ex/S", "http://www.w3.org/ns/shacl#targetClass", Iri("http://ex/Person")),
            ("http://ex/L", "http://www.w3.org/ns/shacl#path", Iri("http://ex/ownedBy")),
            ("http://ex/L", "https://w3id.org/varve/ns#subjectLink", yes),
            ("http://ex/P", "http://www.w3.org/ns/shacl#path", Iri("http://ex/label")),
            ("http://ex/P", "https://w3id.org/varve/ns#publicLink", yes),
        ];

        foreach ((string s, string p, RdfTerm o) in shapes)
        {
            request.Assert(RequestTerm.FromTerm(Iri(s)), RequestTerm.FromTerm(Iri(p)), RequestTerm.FromTerm(o), RequestTerm.FromTerm(Shapes));
        }

        return request;
    }

    private static SortedSet<string> Seen(IQuadSource source)
    {
        SortedSet<string> seen = new(StringComparer.Ordinal);
        using IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);

        while (cursor.MoveNext())
        {
            Quad q = cursor.Current;
            seen.Add(Name(source, q.Subject) + " " + Name(source, q.Predicate) + " " + Name(source, q.Object) + " " + Name(source, q.Graph));
        }

        return seen;
    }

    private static string Name(IQuadSource source, TermHandle handle) =>
        handle.IsNone ? "default" : source.TryExternalise(handle, out RdfTerm? term) ? Encoding.UTF8.GetString(term.Lexical) : "?";

    private static RdfTerm Iri(string iri) => RdfTerm.Iri(Encoding.UTF8.GetBytes(iri));
}
