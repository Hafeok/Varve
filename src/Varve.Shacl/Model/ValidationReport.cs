// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using DecisionDriven;
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
    [Memoized]
    private ImmutableArray<byte> _canonical;
    [Memoized]
    private ReportGraph? _graph;

    private ValidationReport(
        bool conforms,
        ValidationSummary summary,
        IReadOnlyList<ShaclResult> results,
        ImmutableArray<ShapePath?> paths,
        ImmutableArray<int> sources,
        IReadOnlyList<RdfTerm> importsNotFollowed,
        IReadOnlyList<RdfTerm>? conformanceDisallows,
        bool shapesAreData,
        CanonicalisationOptions canonicalisation)
    {
        Conforms = conforms;
        Summary = summary;
        Results = results;
        _paths = paths;
        Sources = sources;
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

    /// <summary>For each result, in order, the index of the record it was made from in the list given to <see cref="Create"/>.</summary>
    internal ImmutableArray<int> Sources { get; }

    /// <summary>The report graph, its own blank nodes labelled by RDFC-1.0 and the data's as their source gives them (ADR 0154).</summary>
    /// <remarks>A dataset of its own on every call, built from the memoised triples, so no caller sees another's.</remarks>
    public InMemoryDataset ToDataset() => Graph().ToDataset();

    /// <summary>
    /// The report graph as canonical N-Quads, lines in code point order: the
    /// same bytes for the same shapes graph, data and labels.
    /// </summary>
    /// <exception cref="CanonicalisationLimitException">The report's own blank nodes exceed the RDFC-1.0 work limit.</exception>
    public ReadOnlyMemory<byte> ToCanonicalNQuads()
    {
        if (_canonical == default)
        {
            _canonical = ImmutableCollectionsMarshal.AsImmutableArray(Graph().ToNQuads());
        }

        return _canonical.AsMemory();
    }

    private ReportGraph Graph() =>
        _graph ??= ReportGraph.Build(Conforms, ConformanceDisallows, ImportsNotFollowed, Results, _paths, _shapesAreData, Canonicalisation);

    internal static ValidationReport Create(ValidationRun run, List<ResultRecord> records)
    {
        DataGraph data = run.Data;
        HashSet<RdfTerm> disallowed = new(run.Options.ConformanceDisallows ?? ShaclOptions.DefaultConformanceDisallows, RdfTerm.Comparer);

        List<(ShaclResult Result, ShapePath? Path, string[] Key, int Source)> keyed = [];
        for (int index = 0; index < records.Count; index++)
        {
            ResultRecord record = records[index];
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

            string[] key = Key(result, record.Path);

            // Every result is a new result node (SHACL 1.2 Core §7): two equal results are two (ADR 0154).
            keyed.Add((result, record.Path, key, index));
        }

        keyed.Sort(static (a, b) => Compare(a.Key, b.Key));
        List<ShaclResult> results = keyed.ConvertAll(k => k.Result);
        (bool conforms, ValidationSummary summary) = Summarise(results, disallowed);

        ImmutableArray<ShapePath?>.Builder paths = ImmutableArray.CreateBuilder<ShapePath?>(keyed.Count);
        ImmutableArray<int>.Builder sources = ImmutableArray.CreateBuilder<int>(keyed.Count);
        foreach ((ShaclResult _, ShapePath? path, string[] _, int source) in keyed)
        {
            paths.Add(path);
            sources.Add(source);
        }

        return new ValidationReport(
            conforms,
            summary,
            results,
            paths.MoveToImmutable(),
            sources.MoveToImmutable(),
            run.Shapes.Imports,
            run.Options.ConformanceDisallows,
            run.Data.Source == run.Shapes.Source.Source,
            run.Options.Canonicalisation);
    }

    /// <summary>A result's order key (ADR 0154 point 3): each field's canonical N-Triples form, a path by its SPARQL form, an absent field empty.</summary>
    private static string[] Key(ShaclResult result, ShapePath? path)
    {
        List<string> messages = [];
        foreach (RdfTerm message in result.Messages)
        {
            messages.Add(Strings.NTriples(message));
        }

        messages.Sort(string.CompareOrdinal);
        return
        [
            Strings.NTriples(result.FocusNode),
            path?.Sparql ?? string.Empty,
            result.Value is null ? string.Empty : Strings.NTriples(result.Value),
            Strings.NTriples(result.SourceShape),
            result.SourceConstraint is null ? string.Empty : Strings.NTriples(result.SourceConstraint),
            Strings.NTriples(result.SourceConstraintComponent),
            Strings.NTriples(result.Severity),
            string.Join(" ", messages),
        ];
    }

    private static int Compare(string[] a, string[] b)
    {
        for (int i = 0; i < a.Length; i++)
        {
            int c = XsdString.CompareCodePoints(a[i], b[i]);
            if (c != 0)
            {
                return c;
            }
        }

        return 0;
    }

    /// <summary>
    /// <paramref name="whole"/> without the results of <paramref name="removed"/>
    /// and with those of <paramref name="added"/>, in ADR 0154's order: the
    /// whole report after a delta from the one before it and the delta's
    /// <c>IncrementalComparison</c> (ADR 0171). Equal results are
    /// interchangeable, so a removed result takes away one equal to it.
    /// </summary>
    internal static ValidationReport Combine(ValidationReport whole, ValidationReport removed, ValidationReport added)
    {
        Dictionary<string, int> taken = new(StringComparer.Ordinal);
        for (int i = 0; i < removed.Results.Count; i++)
        {
            string joined = string.Join("\u0000", Key(removed.Results[i], removed._paths[i]));
            taken[joined] = taken.GetValueOrDefault(joined) + 1;
        }

        List<(ShaclResult Result, ShapePath? Path, string[] Key)> kept = [];
        for (int i = 0; i < whole.Results.Count; i++)
        {
            string[] key = Key(whole.Results[i], whole._paths[i]);
            string joined = string.Join("\u0000", key);
            if (taken.TryGetValue(joined, out int count) && count > 0)
            {
                taken[joined] = count - 1;
                continue;
            }

            kept.Add((whole.Results[i], whole._paths[i], key));
        }

        for (int i = 0; i < added.Results.Count; i++)
        {
            kept.Add((added.Results[i], added._paths[i], Key(added.Results[i], added._paths[i])));
        }

        kept.Sort(static (a, b) => Compare(a.Key, b.Key));
        HashSet<RdfTerm> disallowed = new(whole.ConformanceDisallows ?? ShaclOptions.DefaultConformanceDisallows, RdfTerm.Comparer);
        List<ShaclResult> results = kept.ConvertAll(k => k.Result);
        (bool conforms, ValidationSummary summary) = Summarise(results, disallowed);
        ImmutableArray<ShapePath?>.Builder paths = ImmutableArray.CreateBuilder<ShapePath?>(kept.Count);
        ImmutableArray<int>.Builder sources = ImmutableArray.CreateBuilder<int>(kept.Count);
        for (int i = 0; i < kept.Count; i++)
        {
            paths.Add(kept[i].Path);
            sources.Add(i);
        }

        return new ValidationReport(
            conforms,
            summary,
            results,
            paths.MoveToImmutable(),
            sources.MoveToImmutable(),
            whole.ImportsNotFollowed,
            whole.ConformanceDisallows,
            whole._shapesAreData,
            whole.Canonicalisation);
    }

    /// <summary>
    /// One report of the results of <paramref name="parts"/>, in ADR 0154's
    /// order, with <paramref name="header"/>'s imports, conformance-disallow
    /// set and canonicalisation: how a validation projection serves the report
    /// at a position from its rows (ADR 0174). Every part and the header come
    /// from validations of the same shapes graph with the same options; the
    /// header's own results are not included.
    /// </summary>
    public static ValidationReport Compose(ValidationReport header, IEnumerable<ValidationReport> parts)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(parts);
        List<(ShaclResult Result, ShapePath? Path, string[] Key)> kept = [];
        foreach (ValidationReport part in parts)
        {
            for (int i = 0; i < part.Results.Count; i++)
            {
                kept.Add((part.Results[i], part._paths[i], Key(part.Results[i], part._paths[i])));
            }
        }

        kept.Sort(static (a, b) => Compare(a.Key, b.Key));
        HashSet<RdfTerm> disallowed = new(header.ConformanceDisallows ?? ShaclOptions.DefaultConformanceDisallows, RdfTerm.Comparer);
        List<ShaclResult> results = kept.ConvertAll(k => k.Result);
        (bool conforms, ValidationSummary summary) = Summarise(results, disallowed);
        ImmutableArray<ShapePath?>.Builder paths = ImmutableArray.CreateBuilder<ShapePath?>(kept.Count);
        ImmutableArray<int>.Builder sources = ImmutableArray.CreateBuilder<int>(kept.Count);
        for (int i = 0; i < kept.Count; i++)
        {
            paths.Add(kept[i].Path);
            sources.Add(i);
        }

        return new ValidationReport(
            conforms,
            summary,
            results,
            paths.MoveToImmutable(),
            sources.MoveToImmutable(),
            header.ImportsNotFollowed,
            header.ConformanceDisallows,
            header._shapesAreData,
            header.Canonicalisation);
    }

    /// <summary>
    /// Writes the report as bytes <see cref="ReadFrom"/> reads back to an
    /// equal report, the same canonical N-Quads included: what a validation
    /// projection keeps of a row in the local cache (ADR 0174).
    /// </summary>
    public void WriteTo(IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ReportCodec.Write(writer, _shapesAreData, ConformanceDisallows, ImportsNotFollowed, Results, _paths);
    }

    /// <summary>
    /// Reads a report <see cref="WriteTo"/> wrote, with
    /// <paramref name="options"/>' canonicalisation limits.
    /// </summary>
    /// <exception cref="System.IO.InvalidDataException">The bytes are not a report this version reads.</exception>
    public static ValidationReport ReadFrom(ReadOnlySpan<byte> bytes, ShaclOptions? options = null)
    {
        (bool shapesAreData, IReadOnlyList<RdfTerm>? disallows, IReadOnlyList<RdfTerm> imports, List<(ShaclResult Result, ShapePath? Path)> read) = ReportCodec.Read(bytes);
        List<(ShaclResult Result, ShapePath? Path, string[] Key)> kept = read.ConvertAll(r => (r.Result, r.Path, Key(r.Result, r.Path)));
        kept.Sort(static (a, b) => Compare(a.Key, b.Key));
        HashSet<RdfTerm> disallowed = new(disallows ?? ShaclOptions.DefaultConformanceDisallows, RdfTerm.Comparer);
        List<ShaclResult> results = kept.ConvertAll(k => k.Result);
        (bool conforms, ValidationSummary summary) = Summarise(results, disallowed);
        ImmutableArray<ShapePath?>.Builder paths = ImmutableArray.CreateBuilder<ShapePath?>(kept.Count);
        ImmutableArray<int>.Builder sources = ImmutableArray.CreateBuilder<int>(kept.Count);
        for (int i = 0; i < kept.Count; i++)
        {
            paths.Add(kept[i].Path);
            sources.Add(i);
        }

        return new ValidationReport(
            conforms,
            summary,
            results,
            paths.MoveToImmutable(),
            sources.MoveToImmutable(),
            imports,
            disallows,
            shapesAreData,
            (options ?? ShaclOptions.Default).Canonicalisation);
    }

    /// <summary>Each result's identity for ADR 0169's R1 — focus node, source shape, component, path, value — as one string.</summary>
    internal List<string> Identities()
    {
        List<string> identities = new(Results.Count);
        for (int i = 0; i < Results.Count; i++)
        {
            string[] key = Key(Results[i], _paths[i]);
            identities.Add(key[0] + " " + key[3] + " " + key[5] + " " + key[1] + " " + key[2]);
        }

        return identities;
    }

    /// <summary>§6.8.1.1 and the counts: whether no result has a disallowed severity, and the results by severity.</summary>
    private static (bool Conforms, ValidationSummary Summary) Summarise(List<ShaclResult> results, HashSet<RdfTerm> disallowed)
    {
        int violations = 0, warnings = 0, infos = 0, traces = 0, debugs = 0, others = 0;
        bool conforms = true;
        foreach (ShaclResult result in results)
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

        return (conforms, new ValidationSummary(violations, warnings, infos, traces, debugs, others));
    }
}
