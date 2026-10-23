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
/// The report graphs are computed, never written (ADR 0174; ADR 0130 point
/// 5, amended): a commit with a quad in a graph under
/// <c>https://w3id.org/varve/ns#validation/</c> is refused as
/// <c>graph-not-writable</c>, on every write path and whoever writes it. A
/// commit that touches no such graph costs one decision per distinct graph
/// in its delta.
/// </summary>
[Contract(typeof(TheValidationProjection.ReportGraphNotWritable), Role = "the dataset validator that keeps the report graphs computed")]
public sealed class ReportGraphGuard : ICommitValidator
{
    private static ReadOnlySpan<byte> Namespace => "https://w3id.org/varve/ns#validation/"u8;

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
                graph = proposed.TryExternalise(quad.Graph, out RdfTerm? term) && term.Kind == RdfTermKind.Iri && term.Lexical.StartsWith(Namespace) ? term : null;
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
        writer.WriteString("Nothing was committed; the graph is a validation report, computed by its projection and never written (ADR 0174).");
        writer.WriteName("graph"u8);
        writer.WriteString(graph.Lexical);
        writer.EndObject();
        return ValidatorProblem.Report(ProblemType.GraphNotWritable, json.WrittenSpan.ToArray());
    }
}
