// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Shacl.Sparql;
using Varve.Shacl.Store.Model;

namespace Varve.Shacl.Store;

/// <summary>
/// A binding's validation as of one state, in full (ADRs 0169, 0174): the
/// shapes graph as of the state, against the binding's data graph — the union
/// of the graphs it validates, without the shapes graph and the
/// <c>varve:</c> graphs. What the gate decides a commit against, what the
/// validation projection's report at a position equals, and REC's standalone
/// validation.
/// </summary>
[Contract(typeof(TheValidationProjection.ReportAtEqualsFullValidation), Role = "a binding's full validation as of one state")]
public static class BindingValidation
{
    /// <summary>
    /// Validates <paramref name="state"/> under <paramref name="binding"/>:
    /// the report, or the problems of a shapes graph that does not load or a
    /// validation that fails. <paramref name="options"/> give the limits and
    /// the extensions, SHACL-SPARQL when none is given; non-deterministic
    /// functions are always refused, and the binding's
    /// <c>varve:subClassOfInShapesGraph</c> is applied.
    /// </summary>
    public static ValidationOutcome Validate(ShapesBinding binding, IQuadSource state, ShaclOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(state);
        ShaclOptions effective = Options(binding, options);
        ShapesLoad load = Load(binding, state, effective);
        if (load.Shapes is not { } shapes)
        {
            return ValidationOutcome.Failed(load.Problems);
        }

        return ShaclValidator.Validate(shapes, new DataGraphSource(state, binding), GraphPattern.Any, effective, cancellationToken);
    }

    /// <summary>The options a binding validates under: <paramref name="options"/>'s limits and extensions, SHACL-SPARQL by default, non-deterministic functions refused.</summary>
    internal static ShaclOptions Options(ShapesBinding binding, ShaclOptions? options)
    {
        options ??= ShaclOptions.Default;
        IReadOnlyList<IShaclExtension> extensions = options.Extensions.Count > 0 ? options.Extensions : [SparqlConstraints.Extension()];
        return new ShaclOptions
        {
            Extensions = extensions,
            RegexTimeout = options.RegexTimeout,
            ConformanceDisallows = options.ConformanceDisallows,
            Canonicalisation = options.Canonicalisation,
            RefuseNonDeterministicFunctions = true,
            SubClassOfInShapesGraph = binding.SubClassOfInShapesGraph,
        };
    }

    /// <summary>The shapes graph as of <paramref name="state"/>, copied out so that it outlives the view, and loaded.</summary>
    internal static ShapesLoad Load(ShapesBinding binding, IQuadSource state, ShaclOptions options)
    {
        InMemoryDatasetBuilder builder = new();
        if (state.TryInternalise(binding.ShapesGraph, out TermHandle graph))
        {
            using IQuadCursor cursor = state.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Named(graph));
            while (cursor.MoveNext())
            {
                Quad quad = cursor.Current;
                if (state.TryExternalise(quad.Subject, out RdfTerm? s) && state.TryExternalise(quad.Predicate, out RdfTerm? p) && state.TryExternalise(quad.Object, out RdfTerm? o))
                {
                    builder.Add(s, p, o);
                }
            }
        }

        return ShapesGraph.Load(builder.ToDataset(), GraphPattern.DefaultGraph, options);
    }

    /// <summary>Whether <paramref name="delta"/> changes the shapes graph.</summary>
    internal static bool Touches(IQuadSource state, QuadDelta delta, RdfTerm shapesGraph)
    {
        if (!state.TryInternalise(shapesGraph, out TermHandle graph))
        {
            return false;
        }

        foreach (Quad quad in delta.Asserted)
        {
            if (state.TermComparer.Equals(quad.Graph, graph))
            {
                return true;
            }
        }

        foreach (Quad quad in delta.Retracted)
        {
            if (state.TermComparer.Equals(quad.Graph, graph))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The delta without the quads outside the binding's data graph.</summary>
    internal static QuadDelta InDataGraph(DataGraphSource data, QuadDelta delta)
    {
        List<Quad> asserted = [], retracted = [];
        foreach (Quad quad in delta.Asserted)
        {
            if (data.Allows(quad.Graph))
            {
                asserted.Add(quad);
            }
        }

        foreach (Quad quad in delta.Retracted)
        {
            if (data.Allows(quad.Graph))
            {
                retracted.Add(quad);
            }
        }

        return asserted.Count == delta.Asserted.Length && retracted.Count == delta.Retracted.Length
            ? delta
            : QuadDelta.Create([.. asserted], [.. retracted]);
    }
}
