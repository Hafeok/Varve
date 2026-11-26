// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Varve.Inference.Model;
using Varve.Inference.Store.Model;
using Varve.Rdf;
using Varve.Store;
using Xunit;
using static Varve.Inference.Store.Tests.Support;

namespace Varve.Inference.Store.Tests;

/// <summary>
/// ADR 0197, amended 2026-11-26 (#413): a cross-graph binding reads each data
/// graph's lists from that graph's statements and the TBox's alone. One
/// graph's statement never makes another graph's list unread; a list that
/// goes on in another graph is unread where it starts, as
/// <c>spans-graphs</c>; and each unread cell is reported under the graph
/// that leaves it unread.
/// </summary>
public class ListsPerGraphTests
{
    private static readonly RdfTerm First = IriOf(RdfNs + "first");
    private static readonly RdfTerm Rest = IriOf(RdfNs + "rest");
    private static readonly RdfTerm Nil = IriOf(RdfNs + "nil");
    private static readonly RdfTerm Type = IriOf(RdfNs + "type");
    private static readonly RdfTerm UnionOf = IriOf(OwlNs + "unionOf");

    [Fact]
    public async Task One_graphs_statements_never_make_another_graphs_list_unread()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using Dataset dataset = await CreateAsync(ct);
        InferenceProjection projection = new(dataset, Binding(InferenceProfile.Owl2Rl, crossGraph: true), new InferenceOptions());
        RdfTerm g1 = Ex("g1"), g2 = Ex("g2");
        await CommitAsync(
            dataset,
            [
                (Ex("U"), UnionOf, Ex("l1"), TBox, true),

                // g1 makes l1, and l6 on its own.
                (Ex("l1"), First, Ex("C1"), g1, true), (Ex("l1"), Rest, Nil, g1, true),
                (Ex("l6"), First, Ex("C3"), g1, true), (Ex("l6"), Rest, Nil, g1, true),
                (Ex("a"), Type, Ex("C1"), g1, true),

                // g2 gives l1 a second rest, and starts a chain into l6, which only g1 makes.
                (Ex("l1"), Rest, Ex("l9"), g2, true),
                (Ex("l5"), First, Ex("C2"), g2, true), (Ex("l5"), Rest, Ex("l6"), g2, true),
            ],
            ct);
        await dataset.CatchUpAsync(projection, ct);

        // g1's list is read: the union's member is an instance of it, placed in g1.
        Assert.Equal(InferenceReadOutcome.Held, projection.TryRead(dataset.Head, out InferredRows? rows));
        using (DatasetView view = await dataset.AsOfAsync(dataset.Head, ct))
        {
            Assert.Contains("<http://ex/g1> <http://ex/a> <" + RdfNs + "type> <http://ex/U>", Text(rows!, view));
        }

        // Each unread cell under the graph that leaves it unread; none under g1.
        Assert.Equal(
            [("http://ex/g2", "http://ex/l1", UnreadListReason.SpansGraphs), ("http://ex/g2", "http://ex/l5", UnreadListReason.SpansGraphs)],
            projection.UnreadLists().Select(u => (Iri(u.Graph), Iri(u.Cell), u.Reason)).OrderBy(u => u.Item2, System.StringComparer.Ordinal));
    }

    private static string Iri(RdfTerm term) => System.Text.Encoding.UTF8.GetString(term.Lexical);
}
