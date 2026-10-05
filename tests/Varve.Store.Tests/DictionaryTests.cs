// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using CsCheck;
using Varve.Rdf;
using Varve.Store.Log;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// The dictionary on disk (ADR 0079): term sections carried by runs and
/// checkpoints, read by id and by term through the synchronous blob read.
/// </summary>
public class DictionaryTests
{
    private static DatasetOptions Options(long memtable) => new()
    {
        Clock = ManualClock.Epoch(),
        MemtableLimit = new QuadCount(memtable),
        Maintenance = MaintenanceMode.Off,
    };

    // A spread of terms: IRIs, plain, typed and language-tagged literals in
    // either case, with and without a base direction, and triple terms.
    private static RdfTerm Term(int i) => (i % 7) switch
    {
        0 => T.Iri("term/" + i.ToString(CultureInfo.InvariantCulture)),
        1 => T.Literal("lexical " + i.ToString(CultureInfo.InvariantCulture)),
        2 => T.Lang("tagged " + i.ToString(CultureInfo.InvariantCulture), i % 2 == 0 ? "en-GB" : "da"),
        3 => RdfTerm.Literal(Encoding.UTF8.GetBytes("directed " + i.ToString(CultureInfo.InvariantCulture)), "ar"u8, TextDirection.RightToLeft),
        4 => RdfTerm.Literal(Encoding.UTF8.GetBytes("2026-10-0" + (i % 9 + 1).ToString(CultureInfo.InvariantCulture) + "/" + i.ToString(CultureInfo.InvariantCulture)), T.Iri("datatype")),
        5 => RdfTerm.TripleTerm(T.Iri("ts/" + i.ToString(CultureInfo.InvariantCulture)), T.Iri("tp"), T.Literal("to " + i.ToString(CultureInfo.InvariantCulture))),
        _ => T.Iri("long/" + new string('x', i % 600) + i.ToString(CultureInfo.InvariantCulture)),
    };

    private static async Task<Dataset> Load(IStorage storage, int terms, int batch, long memtable)
    {
        Dataset dataset = await Dataset.CreateAsync(storage, T.Id, Options(memtable), T.Ct);

        for (int start = 0; start < terms; start += batch)
        {
            CommitRequest request = new();

            for (int i = start; i < Math.Min(terms, start + batch); i++)
            {
                request.Assert(T.Iri("s"), T.Iri("p"), Term(i));
            }

            await dataset.CommitAsync(request, T.Ct);
            await dataset.MaintainAsync(T.Ct);
        }

        return dataset;
    }

    private static void EveryTermResolves(DatasetView view, int terms)
    {
        for (int i = 0; i < terms; i++)
        {
            RdfTerm term = Term(i);
            Assert.True(view.TryInternalise(term, out TermHandle handle), "term " + i + " is not found");
            Assert.True(view.TryExternalise(handle, out RdfTerm? back));
            Assert.Equal(term, back);
        }

        Assert.False(view.TryInternalise(T.Iri("never/allocated"), out _));
        Assert.False(view.TryInternalise(T.Lang("tagged 1", "en-GB"), out _));
    }

    [Theory]
    [InlineData(20_000, 1_000, 2_500)]
    [InlineData(3_000, 7, 1)]
    public async Task every_term_resolves_both_ways_from_runs_on_disk_and_after_reopening(int terms, int batch, long memtable)
    {
        MemoryStorage storage = new();

        await using (Dataset dataset = await Load(storage, terms, batch, memtable))
        {
            using DatasetView view = dataset.Pin();
            EveryTermResolves(view, terms);
        }

        await using Dataset reopened = await Dataset.OpenAsync(storage, Options(memtable), T.Ct);
        using DatasetView again = reopened.Pin();
        EveryTermResolves(again, terms);
    }

    [Fact]
    public async Task a_checkpoint_is_the_dictionary_and_a_dataset_opens_from_it_alone()
    {
        MemoryStorage storage = new();
        const int Terms = 5_000;

        await using (Dataset dataset = await Load(storage, Terms, 500, 1_000_000))
        {
            await dataset.CheckpointAsync(dataset.Head, T.Ct);
        }

        // No projection state: the base is the checkpoint, and its term
        // section is the dictionary up to it.
        Assert.True(await storage.Derived.DeleteAsync(new BlobName("index/state"), T.Ct) || true);
        await using Dataset reopened = await Dataset.OpenAsync(storage, Options(1_000_000), T.Ct);
        using DatasetView view = reopened.Pin();
        EveryTermResolves(view, Terms);
    }

    /// <summary>
    /// Defect 1 of milestone 6c, found by the model property (seed
    /// <c>0TyDyZl1BdDa</c>, <c>a_checkpoint_plus_the_tail_equals_full_replay</c>):
    /// a term key compared language tags byte by byte, where RDF 1.1 Concepts
    /// §3.3 compares them ignoring case, so <c>"hello"@EN</c> was not found
    /// as <c>"hello"@en</c> and a retraction of it did nothing.
    /// </summary>
    [Fact]
    public async Task regression_a_language_tag_is_found_in_either_case()
    {
        MemoryStorage storage = new();
        await using Dataset dataset = await Dataset.CreateAsync(storage, T.Id, Options(1), T.Ct);
        await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Lang("hello", "en")), T.Ct);
        await dataset.MaintainAsync(T.Ct);

        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(new CommitRequest().Retract(T.Iri("s"), T.Iri("p"), T.Lang("hello", "EN")), T.Ct)).Outcome);
        using DatasetView view = dataset.Pin();
        Assert.True(view.TryInternalise(T.Lang("hello", "EN"), out _));
        Assert.Equal(0, view.Estimate(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any).Count.Value);
    }

    /// <summary>
    /// The interpolation search finds every key of a section built from
    /// arbitrary terms, on a blob, and no key it does not hold.
    /// </summary>
    [Fact]
    public async Task a_section_on_a_blob_finds_every_key_and_no_other()
    {
        await Gen.Int[1, 4_000].SampleAsync(
            async count =>
            {
                Allocation[] allocations = new Allocation[count];

                for (int i = 0; i < count; i++)
                {
                    allocations[i] = new Allocation(TermIds.Canonical(i + 1), T.Iri("k" + i.ToString(CultureInfo.InvariantCulture)));
                }

                MemoryStorage storage = new();
                Run run = Run.FromDelta([], [], TermSection.Of(allocations, 0), 0, 1);
                await DerivedFormat.WriteRunAsync(storage.Derived, new BlobName("r"), DerivedFormat.KindRun, T.Id, 0, 1, new byte[32], DerivedFormat.MergeOf([run], dropRetractions: false), [run.Terms], 0, T.Ct);
                LoadedRun loaded = (await DerivedFormat.TryLoadAsync(storage.Derived, new BlobName("r"), T.Id, DerivedFormat.KindRun, T.Ct))!;
                TermDictionary dictionary = new();
                Run[] runs = [loaded.Run];

                for (int i = 0; i < count; i++)
                {
                    Assert.True(dictionary.TryFind(runs, allocations[i].Term!, count, out ulong id));
                    Assert.Equal(allocations[i].Id, id);
                    Assert.Equal(allocations[i].Term, dictionary.Term(runs, id));
                }

                Assert.False(dictionary.TryFind(runs, T.Iri("absent"), count, out _));
                loaded.Run.Blob!.Release();
            },
            iter: 40);
    }
}
