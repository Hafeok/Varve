// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store;

namespace Varve.Conformance.Tests;

/// <summary>
/// A quad source the evaluation cases run over: loaded from the case's data,
/// one load per file so that blank nodes stay distinct across files, as RDF
/// merge requires (<c>sparql-evaluation.md</c> §12.1).
/// </summary>
internal interface IEvaluationSubject
{
    string Name { get; }

    /// <summary>Loads the graphs, each a list of quads with its graph name (null: the default graph).</summary>
    ValueTask<LoadedSource> LoadAsync(IReadOnlyList<(IReadOnlyList<DataQuad> Quads, RdfTerm? Graph)> files);
}

/// <summary>A loaded source and whatever must be released after the query.</summary>
internal sealed class LoadedSource(IQuadSource source, Func<ValueTask>? release) : IAsyncDisposable
{
    internal IQuadSource Source { get; } = source;

    public ValueTask DisposeAsync() => release?.Invoke() ?? ValueTask.CompletedTask;
}

internal static class EvaluationSubjects
{
    internal static IEvaluationSubject Dataset { get; } = new DatasetSubject();

    internal static IEvaluationSubject Store { get; } = new StoreSubject();

    internal static IEvaluationSubject ByName(string name) => name switch
    {
        "dataset" => Dataset,
        "store" => Store,
        _ => throw new ArgumentException("Unknown subject " + name, nameof(name)),
    };

    /// <summary>A blank node's label, prefixed by the file it came from, so two files' _:a are two nodes.</summary>
    internal static RdfTerm Scope(RdfTerm term, int file) =>
        term.Kind == RdfTermKind.BlankNode
            ? RdfTerm.BlankNode(Encoding.UTF8.GetBytes("f" + file.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." + Encoding.UTF8.GetString(term.Lexical)))
            : term.Kind == RdfTermKind.TripleTerm
                ? RdfTerm.TripleTerm(Scope(term.Subject!, file), Scope(term.Predicate!, file), Scope(term.Object!, file))
                : term;

    /// <summary><see cref="InMemoryDataset"/>, which interns labels: each file's blank nodes are renamed apart.</summary>
    private sealed class DatasetSubject : IEvaluationSubject
    {
        public string Name => "dataset";

        public ValueTask<LoadedSource> LoadAsync(IReadOnlyList<(IReadOnlyList<DataQuad> Quads, RdfTerm? Graph)> files)
        {
            InMemoryDatasetBuilder builder = new();
            for (int f = 0; f < files.Count; f++)
            {
                (IReadOnlyList<DataQuad> quads, RdfTerm? graph) = files[f];
                foreach (DataQuad quad in quads)
                {
                    builder.Add(Scope(quad.Subject, f), quad.Predicate, Scope(quad.Object, f), graph ?? quad.Graph);
                }
            }

            return ValueTask.FromResult(new LoadedSource(builder.ToDataset(), null));
        }
    }

    /// <summary>
    /// The store's default projection, one commit per file (a blank node label
    /// in a request is fresh per request, ADR 0044), read through a pinned view
    /// released after the query (ADR 0052).
    /// </summary>
    private sealed class StoreSubject : IEvaluationSubject
    {
        public string Name => "store";

        public async ValueTask<LoadedSource> LoadAsync(IReadOnlyList<(IReadOnlyList<DataQuad> Quads, RdfTerm? Graph)> files)
        {
            Dataset dataset = await Varve.Store.Dataset.OpenAsync(new MemoryStorage(), new DatasetOptions { Clock = FixedClock.Instance });
            foreach ((IReadOnlyList<DataQuad> quads, RdfTerm? graph) in files)
            {
                if (quads.Count == 0)
                {
                    continue;
                }

                CommitRequest request = new();
                foreach (DataQuad quad in quads)
                {
                    RdfTerm? g = graph ?? quad.Graph;
                    if (g is null)
                    {
                        request.Assert(RequestTerm.FromTerm(quad.Subject), RequestTerm.FromTerm(quad.Predicate), RequestTerm.FromTerm(quad.Object));
                    }
                    else
                    {
                        request.Assert(RequestTerm.FromTerm(quad.Subject), RequestTerm.FromTerm(quad.Predicate), RequestTerm.FromTerm(quad.Object), RequestTerm.FromTerm(g));
                    }
                }

                CommitResult result = await dataset.CommitAsync(request);
                if (result.Outcome != CommitOutcome.Committed && result.Outcome != CommitOutcome.NoChange)
                {
                    throw new InvalidOperationException("The store refused the test data: " + result.Outcome);
                }
            }

            DatasetView view = dataset.Pin();
            return new LoadedSource(view, async () =>
            {
                view.Dispose();
                await dataset.DisposeAsync();
            });
        }
    }
}

/// <summary>A clock that always reads the same instant: the harness's NOW() and the store's commit times.</summary>
internal sealed class FixedClock : TimeProvider
{
    internal static FixedClock Instance { get; } = new();

    public override DateTimeOffset GetUtcNow() => new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

/// <summary>A seeded random source: RAND(), UUID() and STRUUID() are tested for properties, never values.</summary>
internal sealed class SeededRandom : Varve.Sparql.Evaluation.IRandomSource
{
    private readonly Random _random = new(20260925);

    public void NextBytes(Span<byte> destination)
    {
        lock (_random)
        {
            _random.NextBytes(destination);
        }
    }
}
