// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Shacl.Model;
using Varve.Shacl.Sparql;
using Varve.Shacl.Store.Model;
using Varve.Store;
using Varve.Store.Log;
using Varve.Turtle;
using Xunit;

namespace Varve.Shacl.Store.Tests;

/// <summary>A store with a gate, Turtle in, commits as toggles.</summary>
internal static class Support
{
    internal const string Prefixes = """
        @prefix sh: <http://www.w3.org/ns/shacl#> .
        @prefix ex: <http://example.com/ns#> .
        @prefix rdf: <http://www.w3.org/1999/02/22-rdf-syntax-ns#> .
        @prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .
        @prefix xsd: <http://www.w3.org/2001/XMLSchema#> .

        """;

    internal static readonly RdfTerm ShapesGraph = Ex("shapes");

    internal static readonly RdfTerm DataGraph = Ex("data");

    internal static RdfTerm Ex(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes("http://example.com/ns#" + local));

    internal static ShapesBinding Binding(ValidationMode mode, bool allowFull = false) =>
        new(Ex("binding"), ShapesGraph, GraphScope.All, mode) { AllowFullEvaluation = allowFull };

    internal static Task<Dataset> CreateAsync(ShapesGate gate, CancellationToken ct) =>
        Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System, Validators = [gate] }, ct).AsTask();

    /// <summary>The triples of a Turtle text, as terms.</summary>
    internal static List<(RdfTerm S, RdfTerm P, RdfTerm O)> Triples(string turtle)
    {
        List<(RdfTerm, RdfTerm, RdfTerm)> triples = [];
        ParseResult result = TurtleParser.Parse(
            Encoding.UTF8.GetBytes(Prefixes + turtle),
            (in QuadView quad) => triples.Add((quad.Subject.Materialise(), quad.Predicate.Materialise(), quad.Object.Materialise())),
            new TurtleOptions { Syntax = RdfSyntax.Turtle });
        Assert.True(result.Succeeded, result.FirstError.ToString());
        return triples;
    }

    /// <summary>A request replacing the shapes graph with <paramref name="shapes"/>, and toggling data triples.</summary>
    internal static CommitRequestBuilder Request(Dataset dataset, string? shapes, IEnumerable<(RdfTerm S, RdfTerm P, RdfTerm O, bool Assert)> data)
    {
        CommitRequestBuilder request = new();
        if (shapes is not null)
        {
            using DatasetView view = dataset.Pin();
            if (view.TryInternalise(ShapesGraph, out TermHandle graph))
            {
                using IQuadCursor cursor = view.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Named(graph));
                while (cursor.MoveNext())
                {
                    Quad quad = cursor.Current;
                    request.Retract(RequestTerm.Existing(quad.Subject), RequestTerm.Existing(quad.Predicate), RequestTerm.Existing(quad.Object), RequestTerm.Existing(graph));
                }
            }

            foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in Triples(shapes))
            {
                request.Assert(RequestTerm.FromTerm(s), RequestTerm.FromTerm(p), RequestTerm.FromTerm(o), RequestTerm.FromTerm(ShapesGraph));
            }
        }

        foreach ((RdfTerm s, RdfTerm p, RdfTerm o, bool assert) in data)
        {
            if (assert)
            {
                request.Assert(RequestTerm.FromTerm(s), RequestTerm.FromTerm(p), RequestTerm.FromTerm(o), RequestTerm.FromTerm(DataGraph));
            }
            else
            {
                request.Retract(RequestTerm.FromTerm(s), RequestTerm.FromTerm(p), RequestTerm.FromTerm(o), RequestTerm.FromTerm(DataGraph));
            }
        }

        return request;
    }

    /// <summary>A full validation of the data graph against the shapes graph at the dataset's head, or the shapes given.</summary>
    internal static ValidationOutcome Full(Dataset dataset, string? shapesText = null)
    {
        using DatasetView view = dataset.Pin();
        InMemoryDatasetBuilder builder = new();
        if (shapesText is not null)
        {
            foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in Triples(shapesText))
            {
                builder.Add(s, p, o);
            }
        }
        else if (view.TryInternalise(ShapesGraph, out TermHandle graph))
        {
            using IQuadCursor cursor = view.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Named(graph));
            while (cursor.MoveNext())
            {
                Quad q = cursor.Current;
                view.TryExternalise(q.Subject, out RdfTerm? s);
                view.TryExternalise(q.Predicate, out RdfTerm? p);
                view.TryExternalise(q.Object, out RdfTerm? o);
                builder.Add(s!, p!, o!);
            }
        }

        ShaclOptions options = new() { Extensions = [SparqlConstraints.Extension()] };
        ShapesLoad load = Shacl.ShapesGraph.Load(builder.ToDataset(), GraphPattern.DefaultGraph, options);
        if (!view.TryInternalise(DataGraph, out TermHandle data))
        {
            return ShaclValidator.Validate(load.Shapes!, new InMemoryDatasetBuilder().ToDataset(), GraphPattern.DefaultGraph, options);
        }

        return ShaclValidator.Validate(load.Shapes!, view, GraphPattern.Named(data), options);
    }

    /// <summary>A result's identity by its public fields: focus node, source shape, component, path, value.</summary>
    internal static HashSet<string> Violations(ValidationReport report) =>
        [.. report.Results.Where(r => r.Severity.Equals(RdfTerm.Iri("http://www.w3.org/ns/shacl#Violation"u8)))
            .Select(r => string.Join("|", Show(r.FocusNode), Show(r.SourceShape), Show(r.SourceConstraintComponent), r.HasPath + Show(r.Path), Show(r.Value)))];

    internal static string Show(RdfTerm? term) =>
        term is null ? "-" : term.Kind + ":" + Encoding.UTF8.GetString(term.Lexical) + "^" + Encoding.UTF8.GetString(term.DatatypeIri) + "@" + Encoding.UTF8.GetString(term.Language);

    /// <summary>The attachments of the commit at the head.</summary>
    internal static async Task<List<RdfTerm>> HeadAttachmentsAsync(Dataset dataset, CancellationToken ct)
    {
        // Subscribe delivers the commits after its position: the head's is after the one before it.
        await foreach (Commit commit in dataset.Subscribe(new Position(dataset.Head.Value - 1), SubscriptionFilter.All, ct))
        {
            List<RdfTerm> terms = [];
            foreach (TermHandle handle in commit.Attachments.Span)
            {
                Assert.True(commit.TryExternalise(handle, out RdfTerm? term));
                terms.Add(term!);
            }

            return terms;
        }

        return [];
    }
}
