// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Shacl.Store;
using Varve.Shacl.Store.Model;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Learn;

/// <summary>
/// A dataset whose schema is REC 4 (chapter 3): REC in the graph
/// <c>:rec</c>, bound as a reject shapes binding over the default graph.
/// Compiled into the samples that set <c>VarveLearnRec</c>.
/// </summary>
internal static class Gated
{
    /// <summary>The graph REC 4 is loaded into.</summary>
    internal static RdfTerm Rec { get; } = RdfTerm.Iri("https://example.org/learn/rec"u8);

    /// <summary>A new dataset in memory, gated by REC 4's shapes, with REC 4 loaded into <see cref="Rec"/>.</summary>
    internal static async ValueTask<Dataset> CreateAsync(TimeProvider clock)
    {
        RdfTerm rec = Rec;

        // region: gate
        ShapesBinding binding = new(
            RdfTerm.Iri("https://example.org/learn/rec-binding"u8),
            rec,
            GraphScope.Of([], [], DefaultGraphAccess.Included),
            ValidationMode.Reject)
        {
            SubClassOfInShapesGraph = true,
        };
        ShapesGate gate = new([binding]);

        Dataset dataset = await Dataset.CreateAsync(
            new MemoryStorage(),
            new DatasetId(Guid.NewGuid()),
            new DatasetOptions { Clock = clock, Validators = [gate] });
        // endregion: gate

        // region: load-rec
        string[] recFiles = ["rec.ttl", "Brick+patches.ttl", "recimports.ttl", "composition.ttl"];
        CommitResult vocabulary = await Load.TurtleIntoAsync(
            dataset, rec, [.. recFiles.Select(file => Path.Combine(AppContext.BaseDirectory, "rec", file))]);
        // endregion: load-rec

        if (vocabulary.Outcome != CommitOutcome.Committed)
        {
            await dataset.DisposeAsync();
            throw new InvalidDataException("REC 4: " + vocabulary.Outcome + " " + vocabulary.Reason);
        }

        return dataset;
    }

    /// <summary>Loads the slice's or the portfolio's TriG files, each as one commit through the gate, and throws on any refused.</summary>
    internal static async ValueTask LoadAsync(Dataset dataset, params string[] files)
    {
        foreach (string file in files)
        {
            CommitResult loaded = await Load.TriGAsync(dataset, Path.Combine(AppContext.BaseDirectory, "data", file));
            if (loaded.Outcome != CommitOutcome.Committed)
            {
                throw new InvalidDataException(file + ": " + loaded.Outcome + " " + loaded.Reason);
            }
        }
    }
}
