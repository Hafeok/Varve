// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text;
using System.Threading.Tasks;
using Varve.Delta.Model;
using Varve.Events.Model;
using Varve.Management;
using Varve.Protocol.Endpoints;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>
/// ADR 0185 decision 8: the management position a copy's commit records is a
/// position of the fleet that wrote it. The event declarations in force for
/// such a commit are read from that fleet's system dataset, shipped beside
/// the copy, under the name the dataset had there, and never from the
/// serving fleet's.
/// </summary>
public sealed class ForeignFleetEventsTests
{
    private const string Ns = "https://w3id.org/varve/ns#";

    [Fact]
    public async Task a_copys_old_commits_take_their_event_declarations_from_the_fleet_that_wrote_them()
    {
        DatasetOptions options = new() { Clock = TimeProvider.System };
        await using Dataset theirs = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), options, P.Ct);
        await using Dataset ours = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), options, P.Ct);
        await using Dataset copy = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), options, P.Ct);
        await copy.CommitAsync(new CommitRequestBuilder().Assert(RdfTerm.Iri("https://example.org/e/1"u8), RdfTerm.Iri("https://example.org/p"u8), RdfTerm.Iri("https://example.org/o"u8)), P.Ct);

        // The source fleet called the dataset "orders", and moved its event declarations to the management plane.
        await DeclareAsync(theirs,
            "<" + Ns + "manage/dataset/orders> { "
            + "<urn:src:orders> a <" + Ns + "Dataset> ; <" + Ns + "datasetName> \"orders\" ; <" + Ns + "eventDeclarations> <" + Ns + "Moved> . "
            + "<https://example.org/types/Paid> a <" + Ns + "EventType> ; <" + Ns + "sourceGraph> <" + Ns + "defaultGraph> ; <" + Ns + "eventName> \"Paid\" ; "
            + "<" + Ns + "eventClass> <https://example.org/ns#Paid> ; <" + Ns + "eventVersion> 1 ; "
            + "<" + Ns + "eventField> <https://example.org/types/Paid/total> ; "
            + "<" + Ns + "eventSelect> \"SELECT ?total WHERE { ?event <https://example.org/ns#total> ?total }\" . "
            + "<https://example.org/types/Paid/total> a <" + Ns + "EventField> ; <" + Ns + "fieldName> \"total\" ; "
            + "<" + Ns + "fieldDatatype> <http://www.w3.org/2001/XMLSchema#decimal> ; <" + Ns + "fieldRequired> true . }");

        // The fleet that serves the copy calls it "copy", and declares no event type for it.
        await DeclareAsync(ours, "<" + Ns + "manage/dataset/copy> { <urn:dst:copy> a <" + Ns + "Dataset> ; <" + Ns + "datasetName> \"copy\" . }");

        EventCatalogues catalogues = new();
        DatasetName served = new("copy");

        // In the source fleet's system dataset, under the source's name: the declarations as they were.
        EventCatalogues.Moved? shipped = await catalogues.MovedAsync(theirs, new DatasetName("orders"), copy, served, theirs.Head.Value, P.Ct);
        Assert.NotNull(shipped);
        Assert.Equal(theirs.Head.Value, shipped.MovedAt);
        EventCatalogueSnapshot then = shipped.History.At(new FeedPosition(theirs.Head.Value));
        Assert.Equal("Paid", Assert.Single(then.Types).Declaration.Name);

        // In the serving fleet's own, at the same number: nothing moved, and nothing of the source's is seen.
        Assert.Null(await catalogues.MovedAsync(ours, served, copy, served, ours.Head.Value, P.Ct));

        // The two fleets of one dataset are kept apart: asking one does not forget the other.
        EventCatalogues.Moved? again = await catalogues.MovedAsync(theirs, new DatasetName("orders"), copy, served, theirs.Head.Value, P.Ct);
        Assert.Same(shipped.History, again!.History);

        // Under the name the serving fleet uses, the source fleet's system dataset declares nothing.
        Assert.Null(await new EventCatalogues().MovedAsync(theirs, served, copy, served, theirs.Head.Value, P.Ct));
    }

    private static async Task DeclareAsync(Dataset system, string trig)
    {
        InMemoryDataset desired = ManagementTrig.TryParse(Encoding.UTF8.GetBytes(trig), null, out string? error) ?? throw new InvalidOperationException(error);
        CommitRequestBuilder request = new();

        using (IQuadCursor cursor = desired.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any))
        {
            while (cursor.MoveNext())
            {
                Quad quad = cursor.Current;
                desired.TryExternalise(quad.Subject, out RdfTerm? s);
                desired.TryExternalise(quad.Predicate, out RdfTerm? p);
                desired.TryExternalise(quad.Object, out RdfTerm? o);
                desired.TryExternalise(quad.Graph, out RdfTerm? g);
                request = request.Assert(s!, p!, o!, g!);
            }
        }

        Assert.Equal(CommitOutcome.Committed, (await system.CommitAsync(request, P.Ct)).Outcome);
    }
}
