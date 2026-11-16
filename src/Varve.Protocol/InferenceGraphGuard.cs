// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Json.Canonical;
using Varve.Protocol.Endpoints;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Protocol;

/// <summary>
/// The closure's views and reserved graphs are computed, never written (ADR
/// 0198 point 4; ADR 0130 point 5, amended): a commit with a quad in
/// <c>varve:explicit</c>, <c>varve:implicit</c> or a graph under
/// <c>varve:inferred/</c> is refused as <c>graph-not-writable</c>, on every
/// write path and whoever writes it. The twin of
/// <see cref="ReportGraphGuard"/>.
/// </summary>
[Contract(typeof(TheViewsAndTheReservedGraphs.InferenceGraphsNotWritable), Role = "the dataset validator that keeps the closure's graphs computed")]
public sealed class InferenceGraphGuard : ICommitValidator
{
    private static ReadOnlySpan<byte> Explicit => "https://w3id.org/varve/ns#explicit"u8;

    private static ReadOnlySpan<byte> Implicit => "https://w3id.org/varve/ns#implicit"u8;

    private static ReadOnlySpan<byte> Inferred => "https://w3id.org/varve/ns#inferred/"u8;

    /// <summary>Whether a graph is one of the closure's: a view, or under <c>varve:inferred/</c>.</summary>
    public static bool IsClosureGraph(RdfTerm graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return graph.Kind == RdfTermKind.Iri
            && (graph.Lexical.SequenceEqual(Explicit) || graph.Lexical.SequenceEqual(Implicit) || graph.Lexical.StartsWith(Inferred));
    }

    /// <inheritdoc />
    public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        Dictionary<TermHandle, RdfTerm?> decided = new(proposed.TermComparer);
        RdfTerm? refused = Find(proposed, delta.Asserted, decided) ?? Find(proposed, delta.Retracted, decided);
        return refused is null ? ValidationVerdict.Accept() : ValidationVerdict.Reject(Report(refused));
    }

    private static RdfTerm? Find(IQuadSource proposed, ReadOnlySpan<Quad> quads, Dictionary<TermHandle, RdfTerm?> decided)
    {
        foreach (Quad quad in quads)
        {
            if (quad.IsDefaultGraph)
            {
                continue;
            }

            if (!decided.TryGetValue(quad.Graph, out RdfTerm? graph))
            {
                graph = proposed.TryExternalise(quad.Graph, out RdfTerm? term) && IsClosureGraph(term) ? term : null;
                decided[quad.Graph] = graph;
            }

            if (graph is not null)
            {
                return graph;
            }
        }

        return null;
    }

    private static IReadOnlyList<RdfTerm> Report(RdfTerm graph)
    {
        ArrayBufferWriter<byte> json = new();
        CanonicalJsonWriter writer = new(json);
        writer.StartObject();
        writer.WriteName("detail"u8);
        writer.WriteString("Nothing was committed; the graph is part of a materialised closure, computed by its projection and never written (ADR 0198).");
        writer.WriteName("graph"u8);
        writer.WriteString(graph.Lexical);
        writer.EndObject();
        return ValidatorProblem.Report(ProblemType.GraphNotWritable, json.WrittenSpan.ToArray());
    }
}
