// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Varve.Rdf;
using Varve.Shacl.Shapes;
using Varve.Shacl.Validation;
using Varve.Xsd;

namespace Varve.Shacl.Model;

/// <summary>
/// A validation report (SHACL 1.2 Core §6.8): an immutable value, read as a
/// summary, as typed results in a stated order, or as RDF whose bytes are the
/// same for the same shapes, data and labels (ADR 0154).
/// </summary>
public sealed class ValidationReport
{
    private readonly ImmutableArray<ShapePath?> _paths;
    private readonly bool _shapesAreData;
    private byte[]? _canonical;
    private ReportGraph? _graph;

    private ValidationReport(
        bool conforms,
        ValidationSummary summary,
        IReadOnlyList<ShaclResult> results,
        ImmutableArray<ShapePath?> paths,
        IReadOnlyList<RdfTerm> importsNotFollowed,
        IReadOnlyList<RdfTerm>? conformanceDisallows,
        bool shapesAreData,
        CanonicalisationOptions canonicalisation)
    {
        Conforms = conforms;
        Summary = summary;
        Results = results;
        _paths = paths;
        ImportsNotFollowed = importsNotFollowed;
        ConformanceDisallows = conformanceDisallows;
        _shapesAreData = shapesAreData;
        Canonicalisation = canonicalisation;
    }

    /// <summary><c>sh:conforms</c>: no result has a disallowed severity (§6.8.1.2).</summary>
    public bool Conforms { get; }

    /// <summary>The results counted by severity.</summary>
    public ValidationSummary Summary { get; }

    /// <summary>The results in ADR 0154's order, two equal results kept as two.</summary>
    public IReadOnlyList<ShaclResult> Results { get; }

    /// <summary>The IRIs of <c>owl:imports</c> in the shapes graph, none followed (ADR 0151).</summary>
    public IReadOnlyList<RdfTerm> ImportsNotFollowed { get; }

    /// <summary>The conformance-disallow set, when it is not the default; null for the default.</summary>
    public IReadOnlyList<RdfTerm>? ConformanceDisallows { get; }

    internal CanonicalisationOptions Canonicalisation { get; }

    /// <summary>The report graph, its own blank nodes labelled by RDFC-1.0 and the data's as their source gives them (ADR 0154).</summary>
    public InMemoryDataset ToDataset() => Graph().ToDataset();

    /// <summary>
    /// The report graph as canonical N-Quads, lines in code point order: the
    /// same bytes for the same shapes graph, data and labels.
    /// </summary>
    /// <exception cref="CanonicalisationLimitException">The report's own blank nodes exceed the RDFC-1.0 work limit.</exception>
    public ReadOnlyMemory<byte> ToCanonicalNQuads() => _canonical ??= Graph().ToNQuads();

    private ReportGraph Graph() => _graph ??= ReportGraph.Build(this, _paths, _shapesAreData);

    internal static ValidationReport Create(ValidationRun run, List<ResultRecord> records)
    {
        DataGraph data = run.Data;
        HashSet<RdfTerm> disallowed = new(run.Options.ConformanceDisallows ?? ShaclOptions.DefaultConformanceDisallows, RdfTerm.Comparer);

        List<(ShaclResult Result, ShapePath? Path, string[] Key)> keyed = [];
        foreach (ResultRecord record in records)
        {
            RdfTerm focus = data.Term(record.Focus);
            RdfTerm? value = record.Value is { } v ? data.Term(v) : null;
            ShaclResult result = new(
                focus,
                record.Path is PredicatePath p ? p.Predicate : null,
                record.Path is not null,
                value,
                record.Shape.Node,
                record.Component,
                record.SourceConstraint,
                record.Severity,
                record.Messages.IsDefault ? [] : record.Messages);

            List<string> messages = [];
            foreach (RdfTerm message in result.Messages)
            {
                messages.Add(Strings.NTriples(message));
            }

            messages.Sort(string.CompareOrdinal);
            string[] key =
            [
                Strings.NTriples(focus),
                record.Path?.Sparql ?? string.Empty,
                value is null ? string.Empty : Strings.NTriples(value),
                Strings.NTriples(record.Shape.Node),
                record.SourceConstraint is null ? string.Empty : Strings.NTriples(record.SourceConstraint),
                Strings.NTriples(record.Component),
                Strings.NTriples(record.Severity),
                string.Join(" ", messages),
            ];

            // Every result is a new result node (SHACL 1.2 Core §7): two equal results are two (ADR 0154).
            keyed.Add((result, record.Path, key));
        }

        keyed.Sort(static (a, b) =>
        {
            for (int i = 0; i < a.Key.Length; i++)
            {
                int c = XsdString.CompareCodePoints(a.Key[i], b.Key[i]);
                if (c != 0)
                {
                    return c;
                }
            }

            return 0;
        });

        int violations = 0, warnings = 0, infos = 0, traces = 0, debugs = 0, others = 0;
        bool conforms = true;
        foreach ((ShaclResult result, ShapePath? _, string[] _) in keyed)
        {
            RdfTerm severity = result.Severity;
            conforms &= !disallowed.Contains(severity);
            if (severity.Equals(Vocabulary.Violation))
            {
                violations++;
            }
            else if (severity.Equals(Vocabulary.Warning))
            {
                warnings++;
            }
            else if (severity.Equals(Vocabulary.Info))
            {
                infos++;
            }
            else if (severity.Equals(Vocabulary.Trace))
            {
                traces++;
            }
            else if (severity.Equals(Vocabulary.Debug))
            {
                debugs++;
            }
            else
            {
                others++;
            }
        }

        ImmutableArray<ShapePath?>.Builder paths = ImmutableArray.CreateBuilder<ShapePath?>(keyed.Count);
        foreach ((ShaclResult _, ShapePath? path, string[] _) in keyed)
        {
            paths.Add(path);
        }

        return new ValidationReport(
            conforms,
            new ValidationSummary(violations, warnings, infos, traces, debugs, others),
            keyed.ConvertAll(k => k.Result),
            paths.MoveToImmutable(),
            run.Shapes.Imports,
            run.Options.ConformanceDisallows,
            run.Data.Source == run.Shapes.Source.Source,
            run.Options.Canonicalisation);
    }
}
