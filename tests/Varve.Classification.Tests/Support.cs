// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Classification.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Classification.Tests;

/// <summary>A memory dataset with one classifier binding over two graphs, and the requests that change it.</summary>
internal static class Support
{
    internal const string Ex = "http://ex/";
    internal const string Varve = "https://w3id.org/varve/ns#";
    internal const string Sh = "http://www.w3.org/ns/shacl#";
    internal const string TypeIri = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    internal static readonly RdfTerm ShapesGraph = IriOf(Ex + "shapes");
    internal static readonly RdfTerm[] Classified = [IriOf(Ex + "g/1"), IriOf(Ex + "g/2")];
    internal static readonly RdfTerm Outside = IriOf(Ex + "g/3");
    internal static readonly RdfTerm True = RdfTerm.Literal("true"u8, IriOf("http://www.w3.org/2001/XMLSchema#boolean"));

    internal static ClassifierBinding Binding { get; } = new(IriOf(Ex + "binding"), ShapesGraph, GraphScope.Of(Classified, [], DefaultGraphAccess.Excluded));

    internal static RdfTerm IriOf(string iri) => RdfTerm.Iri(Encoding.UTF8.GetBytes(iri));

    internal static Task<Dataset> CreateAsync(CancellationToken ct) =>
        Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }, ct).AsTask();

    /// <summary>Annotated shapes: people by class, optionally by a predicate; ownership along ownedBy and optionally partOf; label public; optionally a refused target.</summary>
    internal static List<(RdfTerm S, RdfTerm P, RdfTerm O)> Shapes(bool bySsn, bool partOf, bool broken)
    {
        List<(RdfTerm, RdfTerm, RdfTerm)> shapes =
        [
            (IriOf(Ex + "PersonShape"), IriOf(Varve + "dataSubject"), True),
            (IriOf(Ex + "PersonShape"), IriOf(Sh + "targetClass"), IriOf(Ex + "Person")),
            (IriOf(Ex + "Owner"), IriOf(Sh + "path"), IriOf(Ex + "ownedBy")),
            (IriOf(Ex + "Owner"), IriOf(Varve + "subjectLink"), True),
            (IriOf(Ex + "Label"), IriOf(Sh + "path"), IriOf(Ex + "label")),
            (IriOf(Ex + "Label"), IriOf(Varve + "publicLink"), True),
        ];

        if (bySsn)
        {
            shapes.Add((IriOf(Ex + "SsnShape"), IriOf(Varve + "dataSubject"), True));
            shapes.Add((IriOf(Ex + "SsnShape"), IriOf(Sh + "targetSubjectsOf"), IriOf(Ex + "ssn")));
        }

        if (partOf)
        {
            shapes.Add((IriOf(Ex + "Part"), IriOf(Sh + "path"), IriOf(Ex + "partOf")));
            shapes.Add((IriOf(Ex + "Part"), IriOf(Varve + "subjectLink"), True));
        }

        if (broken)
        {
            shapes.Add((IriOf(Ex + "PersonShape"), IriOf(Sh + "targetObjectsOf"), IriOf(Ex + "knows")));
        }

        return shapes;
    }

    /// <summary>A request replacing the shapes graph, when given, and toggling data quads.</summary>
    internal static CommitRequestBuilder Request(Dataset dataset, List<(RdfTerm S, RdfTerm P, RdfTerm O)>? shapes, IEnumerable<(RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm G, bool Assert)> data)
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

            foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in shapes)
            {
                request.Assert(RequestTerm.FromTerm(s), RequestTerm.FromTerm(p), RequestTerm.FromTerm(o), RequestTerm.FromTerm(ShapesGraph));
            }
        }

        foreach ((RdfTerm s, RdfTerm p, RdfTerm o, RdfTerm g, bool assert) in data)
        {
            if (assert)
            {
                request.Assert(RequestTerm.FromTerm(s), RequestTerm.FromTerm(p), RequestTerm.FromTerm(o), RequestTerm.FromTerm(g));
            }
            else
            {
                request.Retract(RequestTerm.FromTerm(s), RequestTerm.FromTerm(p), RequestTerm.FromTerm(o), RequestTerm.FromTerm(g));
            }
        }

        return request;
    }

    /// <summary>What a classification says of every term of a state, as text: each node's subjects, each graph's binding and each predicate's publicity.</summary>
    internal static SortedSet<string> Signature(IRowClassification classification, IQuadSource state)
    {
        SortedSet<string> said = [];
        using IQuadCursor cursor = state.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);

        while (cursor.MoveNext())
        {
            Quad quad = cursor.Current;
            BindingIndex binding = classification.BindingOf(quad.Graph);
            said.Add($"graph {Name(state, quad.Graph)} {binding.Value}");

            if (binding.IsNone)
            {
                continue;
            }

            said.Add($"public {Name(state, quad.Predicate)} {classification.IsPublicLink(binding, quad.Predicate)}");

            foreach (TermHandle node in new[] { quad.Subject, quad.Object })
            {
                SubjectSetId set = classification.SubjectsOf(binding, node);
                string members = string.Join(",", classification.Members(binding, set).ToArray().Select(m => Name(state, m)).Order(StringComparer.Ordinal));
                said.Add($"node {Name(state, node)} [{members}]");
            }
        }

        return said;
    }

    internal static string Name(IQuadSource state, TermHandle handle) =>
        handle.IsNone ? "default" : state.TryExternalise(handle, out RdfTerm? term) ? Encoding.UTF8.GetString(term.Lexical) : "?" + handle.Value;
}
