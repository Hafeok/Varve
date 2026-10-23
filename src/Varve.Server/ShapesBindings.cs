// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Varve.Management.Model;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Shacl.Store;
using Varve.Shacl.Store.Model;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Server;

/// <summary>
/// The fleet's shapes bindings (ADRs 0169, 0170), as the server enforces
/// them: each declared binding mapped to <c>Varve.Shacl.Store</c>'s value,
/// one gate per dataset kept while its bindings stay the same — so its
/// shapes stay loaded between commits — and the check an apply makes before
/// it commits.
/// </summary>
internal sealed class ShapesBindings
{
    private readonly ConcurrentDictionary<string, (string Bindings, ShapesGate Gate)> _gates = new(StringComparer.Ordinal);

    /// <summary>The gate of <paramref name="dataset"/> under the bindings it declares, or null when it declares none that acts.</summary>
    internal ShapesGate? GateOf(DatasetName dataset, Declarations declarations)
    {
        if (!declarations.Datasets.TryGetValue(dataset.Value, out DatasetDeclaration? declared))
        {
            _gates.TryRemove(dataset.Value, out _);
            return null;
        }

        List<ShapesBinding> bindings = Acting(declared);
        if (bindings.Count == 0)
        {
            _gates.TryRemove(dataset.Value, out _);
            return null;
        }

        string key = Key(declared);
        (string Bindings, ShapesGate Gate) entry = _gates.AddOrUpdate(
            dataset.Value,
            _ => (key, new ShapesGate(bindings)),
            (_, existing) => existing.Bindings == key ? existing : (key, new ShapesGate(bindings)));
        return entry.Gate;
    }

    /// <summary>R4: each binding of the proposed declarations checked against its dataset, where this instance holds it; the first refusal, or null.</summary>
    internal static IReadOnlyList<RdfTerm>? Check(Declarations proposed, Func<DatasetName, Dataset?> held)
    {
        foreach (DatasetDeclaration declared in proposed.Datasets.Values)
        {
            if (held(new DatasetName(declared.Name)) is not { } dataset)
            {
                // Not held here: its writer fails closed at commit time (R6).
                continue;
            }

            foreach (ShapesBinding binding in Acting(declared))
            {
                using DatasetView view = dataset.Pin();
                if (new ShapesGate([]).Check(binding, view) is { Refusal: { } refusal })
                {
                    return refusal;
                }
            }
        }

        return null;
    }

    /// <summary>A binding of <paramref name="dataset"/> at <paramref name="position"/>, classified where the dataset is held here.</summary>
    internal static BindingDescription? Describe(DatasetName dataset, RdfTerm node, Declarations declarations, ManagementPosition position, Dataset? held)
    {
        if (!declarations.Datasets.TryGetValue(dataset.Value, out DatasetDeclaration? declared))
        {
            return null;
        }

        foreach (BindingDeclaration binding in declared.Bindings)
        {
            if (binding.Kind != BindingKind.Shapes || !binding.Node.Equals(node))
            {
                continue;
            }

            if (held is null)
            {
                return new BindingDescription(binding, position, null, []);
            }

            using DatasetView view = held.Pin();
            BindingCheck check = new ShapesGate([]).Check(Map(binding), view);
            return new BindingDescription(binding, position, check.Classification, check.Problems);
        }

        return null;
    }

    /// <summary>The bindings that act: shapes bindings not <c>off</c>.</summary>
    internal static List<ShapesBinding> Acting(DatasetDeclaration declared)
    {
        List<ShapesBinding> acting = [];
        foreach (BindingDeclaration binding in declared.Bindings)
        {
            if (binding.Kind == BindingKind.Shapes && binding.Mode != ShapesMode.Off && binding.Validates is not null)
            {
                acting.Add(Map(binding));
            }
        }

        return acting;
    }

    private static ShapesBinding Map(BindingDeclaration binding) =>
        new(binding.Node, binding.BoundGraph, Scope(binding.Validates), binding.Mode switch
        {
            ShapesMode.Reject => ValidationMode.Reject,
            ShapesMode.Report => ValidationMode.Report,
            _ => ValidationMode.Off,
        })
        {
            AllowFullEvaluation = binding.AllowFullEvaluation,
            SubClassOfInShapesGraph = binding.SubClassOfInShapesGraph,
        };

    /// <summary>A binding's graph set as a scope: it names no template, which the declaration validator refuses (ADR 0170).</summary>
    private static GraphScope Scope(GraphSet? graphs)
    {
        if (graphs is null || graphs.All)
        {
            return GraphScope.All;
        }

        RdfTerm[] named = [.. graphs.Graphs];
        string[] prefixes = [.. graphs.Prefixes];
        return GraphScope.Of(named, prefixes, graphs.DefaultGraph ? DefaultGraphAccess.Included : DefaultGraphAccess.Excluded);
    }

    /// <summary>What makes two declarations the same bindings: every field of every binding, in order.</summary>
    private static string Key(DatasetDeclaration declared)
    {
        StringBuilder key = new();
        foreach (BindingDeclaration binding in declared.Bindings)
        {
            key.Append(Encoding.UTF8.GetString(binding.Node.Lexical)).Append('|')
                .Append(binding.Kind).Append('|')
                .Append(Encoding.UTF8.GetString(binding.BoundGraph.Lexical)).Append('|')
                .Append(binding.Mode).Append('|')
                .Append(binding.AllowFullEvaluation).Append('|')
                .Append(binding.SubClassOfInShapesGraph).Append('|');
            if (binding.Validates is { } graphs)
            {
                key.Append(graphs.All).Append(graphs.DefaultGraph);
                foreach (RdfTerm graph in graphs.Graphs)
                {
                    key.Append('<').Append(Encoding.UTF8.GetString(graph.Lexical)).Append('>');
                }

                foreach (string prefix in graphs.Prefixes)
                {
                    key.Append('"').Append(prefix).Append('"');
                }
            }

            key.Append('\n');
        }

        return key.ToString();
    }
}

/// <summary>A data write's two validators at its management position: the order of positions (ADR 0165), then the dataset's shapes (ADR 0169).</summary>
internal sealed class OrderAndShapes(ICommitValidator order, ICommitValidator shapes) : ICommitValidator
{
    public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta) => Both(order.Validate(proposed, delta), () => shapes.Validate(proposed, delta));

    public ValidationVerdict Validate(IQuadSource proposed, BulkDelta delta) => Both(order.Validate(proposed, delta), () => shapes.Validate(proposed, delta));

    private static ValidationVerdict Both(ValidationVerdict first, Func<ValidationVerdict> then)
    {
        if (!first.IsAccepted)
        {
            return first;
        }

        ValidationVerdict second = then();
        if (!second.IsAccepted || first.Attachments.Count == 0)
        {
            return second;
        }

        return ValidationVerdict.Accept([.. first.Attachments, .. second.Attachments]);
    }
}
