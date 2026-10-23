// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Shacl.Model;
using Varve.Shacl.Sparql;
using Varve.Shacl.Store.Model;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Shacl.Store;

/// <summary>
/// A dataset's shapes bindings at commit time (ADR 0169): the pre-commit
/// validator that refuses, under a <c>reject</c> binding, a commit whose
/// affected focus nodes gain a violation the pinned position does not have,
/// and attaches to every accepted commit under a <c>report</c> or
/// <c>reject</c> binding a digest of its affected report (ADR 0173). It
/// reads the commit's overlay and delta, and the pinned position as the
/// overlay's base, and nothing else (ADR 0017).
/// </summary>
[Contract(typeof(CommitTimeGatingUnderAShapesBinding.ShaclStoreAtLayerFive), Role = "the shapes bindings' pre-commit validator")]
public sealed class ShapesGate : ICommitValidator
{
    private readonly List<ShapesBinding> _bindings;
    private readonly ShaclOptions _options;
    private readonly Dictionary<RdfTerm, ShapesGraph> _loaded = new(RdfTerm.Comparer);

    /// <summary>
    /// A gate over <paramref name="bindings"/>. <paramref name="options"/>
    /// give the limits and the extensions, SHACL-SPARQL when none is given;
    /// non-deterministic functions are always refused (ADR 0169).
    /// </summary>
    public ShapesGate(IEnumerable<ShapesBinding> bindings, ShaclOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        _bindings = [.. bindings];
        _bindings.Sort(static (a, b) => string.CompareOrdinal(Encoding.UTF8.GetString(a.Binding.Lexical), Encoding.UTF8.GetString(b.Binding.Lexical)));
        options ??= ShaclOptions.Default;
        IReadOnlyList<IShaclExtension> extensions = options.Extensions.Count > 0 ? options.Extensions : [SparqlConstraints.Extension()];
        _options = new ShaclOptions
        {
            Extensions = extensions,
            RegexTimeout = options.RegexTimeout,
            ConformanceDisallows = options.ConformanceDisallows,
            Canonicalisation = options.Canonicalisation,
            RefuseNonDeterministicFunctions = true,
        };
    }

    /// <summary>The bindings, in the order of their IRIs.</summary>
    public IReadOnlyList<ShapesBinding> Bindings => _bindings;

    /// <inheritdoc />
    public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        List<RdfTerm> attachments = [];
        foreach (ShapesBinding binding in _bindings)
        {
            if (binding.Mode == ValidationMode.Off)
            {
                continue;
            }

            if (!Decide(binding, proposed, delta, attachments, out ValidationVerdict refused))
            {
                return refused;
            }
        }

        return attachments.Count > 0 ? ValidationVerdict.Accept(attachments) : ValidationVerdict.Accept();
    }

    /// <summary>
    /// A bulk commit is validated as any other, and refused alike; it carries
    /// no attachment, since a bulk commit's attachment must be a term the
    /// proposed state already holds (ADR 0081).
    /// </summary>
    public ValidationVerdict Validate(IQuadSource proposed, BulkDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);
        ValidationVerdict verdict = Validate(proposed, delta.Materialise());
        return verdict.IsAccepted ? ValidationVerdict.Accept() : verdict;
    }

    /// <summary>The classification of a binding's shapes as of <paramref name="state"/>, or the problems that refuse them: what the apply path checks (R4).</summary>
    public BindingCheck Check(ShapesBinding binding, IQuadSource state)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(state);
        ShapesLoad load = Load(binding, state);
        if (load.Shapes is not { } shapes)
        {
            return new BindingCheck(null, load.Problems, Report.ShapesInvalid(binding, load.Problems));
        }

        ShapesClassification classification = shapes.Classify();
        IReadOnlyList<RdfTerm>? refusal = binding.Mode == ValidationMode.Reject && !binding.AllowFullEvaluation && classification.Worst == Maintainability.Full
            ? Report.BindingNotIncremental(binding, classification)
            : null;
        return new BindingCheck(classification, [], refusal);
    }

    private bool Decide(ShapesBinding binding, IQuadSource proposed, QuadDelta delta, List<RdfTerm> attachments, out ValidationVerdict refused)
    {
        refused = default;
        bool touched = Touches(proposed, delta, binding.ShapesGraph);
        if (touched || !_loaded.TryGetValue(binding.Binding, out ShapesGraph? shapes))
        {
            ShapesLoad load = Load(binding, proposed);
            if (load.Shapes is null)
            {
                // A bound shapes graph that does not load refuses the commit, whichever made it so (R4, R5).
                refused = ValidationVerdict.Reject(Report.ShapesInvalid(binding, load.Problems));
                return false;
            }

            shapes = load.Shapes;
            if (touched)
            {
                // A commit that changes the shapes may still be refused: the next commit loads them from its own base.
                _loaded.Remove(binding.Binding);
            }
            else
            {
                _loaded[binding.Binding] = shapes;
            }
        }

        if (binding.Mode == ValidationMode.Reject && !binding.AllowFullEvaluation && shapes.Classify().Worst == Maintainability.Full)
        {
            // R6: failing closed, until the binding or its shapes are fixed.
            refused = ValidationVerdict.Reject(Report.BindingNotIncremental(binding, shapes.Classify()));
            return false;
        }

        DataGraphSource data = new(proposed, binding);
        QuadDelta changes = InDataGraph(data, delta);
        IncrementalOutcome outcome = IncrementalValidation.Compare(shapes, data, GraphPattern.Any, changes, Options(binding));
        if (outcome.Comparison is not { } comparison)
        {
            if (binding.Mode == ValidationMode.Reject)
            {
                refused = ValidationVerdict.Reject(Report.ShapesInvalid(binding, outcome.Problems));
                return false;
            }

            // Under report, a validation that fails decides nothing and attaches nothing.
            return true;
        }

        if (binding.Mode == ValidationMode.Reject)
        {
            List<ShaclResult> violations = [];
            foreach (ShaclResult result in comparison.Introduced)
            {
                if (result.Severity.Equals(Violation))
                {
                    violations.Add(result);
                }
            }
            if (violations.Count > 0)
            {
                refused = ValidationVerdict.Reject(Report.ShapesViolated(binding, violations, comparison.After, touched ? ShapesChange.Changed : ShapesChange.Unchanged));
                return false;
            }
        }

        attachments.Add(Attachment(binding, comparison.After));
        return true;
    }

    private static readonly RdfTerm Violation = RdfTerm.Iri("http://www.w3.org/ns/shacl#Violation"u8);

    private static readonly RdfTerm AffectedReport = RdfTerm.Iri("https://w3id.org/varve/ns#affectedReport"u8);

    /// <summary>ADR 0173's attachment: the binding, and the SHA-256 of the affected report's canonical N-Quads.</summary>
    [DesignDecision(typeof(TheValidationReportOnTheCommit.AffectedReportAttachment), Scope = ExceptionScope.Boundary)]
    internal static RdfTerm Attachment(ShapesBinding binding, ValidationReport affected)
    {
        byte[] digest = SHA256.HashData(affected.ToCanonicalNQuads().Span);
        string literal = "sha256:" + Convert.ToHexStringLower(digest);
        return RdfTerm.TripleTerm(binding.Binding, AffectedReport, RdfTerm.Literal(Encoding.UTF8.GetBytes(literal)));
    }

    private ShaclOptions Options(ShapesBinding binding) => binding.SubClassOfInShapesGraph
        ? new ShaclOptions
        {
            Extensions = _options.Extensions,
            RegexTimeout = _options.RegexTimeout,
            ConformanceDisallows = _options.ConformanceDisallows,
            Canonicalisation = _options.Canonicalisation,
            RefuseNonDeterministicFunctions = true,
            SubClassOfInShapesGraph = true,
        }
        : _options;

    /// <summary>The shapes graph as of <paramref name="state"/>, copied out so that it outlives the view, and loaded.</summary>
    private ShapesLoad Load(ShapesBinding binding, IQuadSource state)
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

        return ShapesGraph.Load(builder.ToDataset(), GraphPattern.DefaultGraph, Options(binding));
    }

    private static bool Touches(IQuadSource proposed, QuadDelta delta, RdfTerm shapesGraph)
    {
        if (!proposed.TryInternalise(shapesGraph, out TermHandle graph))
        {
            return false;
        }

        foreach (Quad quad in delta.Asserted)
        {
            if (proposed.TermComparer.Equals(quad.Graph, graph))
            {
                return true;
            }
        }

        foreach (Quad quad in delta.Retracted)
        {
            if (proposed.TermComparer.Equals(quad.Graph, graph))
            {
                return true;
            }
        }

        return false;
    }

    private static QuadDelta InDataGraph(DataGraphSource data, QuadDelta delta)
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
