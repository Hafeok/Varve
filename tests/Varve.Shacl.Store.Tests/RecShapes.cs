// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Varve.Rdf;
using Varve.Rec.Tests;
using Varve.Turtle;
using Xunit;

namespace Varve.Shacl.Store.Tests;

/// <summary>
/// REC 4.1.0's shapes and the generated portfolio, as the REC tests read
/// them (ADRs 0141, 0176): the three vendored files as one RDF merge, each
/// file's blank nodes its own, with the gate's composition or without it.
/// </summary>
internal static class RecShapes
{
    internal static readonly RdfTerm Sparql = RdfTerm.Iri("http://www.w3.org/ns/shacl#sparql"u8);

    /// <summary>
    /// The shapes graph's triples: the three files, Brick's SPARQL
    /// constraints when <paramref name="withSparql"/>, and the composition
    /// file when <paramref name="composed"/>; without it, only the
    /// <c>owl:Class</c> triple, as part 2a's standalone check read it.
    /// </summary>
    internal static List<(RdfTerm S, RdfTerm P, RdfTerm O)> Triples(bool withSparql, bool composed)
    {
        List<(RdfTerm, RdfTerm, RdfTerm)> triples = [];
        string directory = Path.Combine(Root(), "testdata", "rec", "v4.1.0");
        List<string> files = [Path.Combine(directory, "rec.ttl"), Path.Combine(directory, "Brick+patches.ttl"), Path.Combine(directory, "recimports.ttl")];
        if (composed)
        {
            files.Add(Path.Combine(Root(), "testdata", "rec", "gate", "composition.ttl"));
        }

        for (int f = 0; f < files.Count; f++)
        {
            string prefix = "f" + f + "_";
            RdfTerm Own(RdfTerm term) => term.Kind == RdfTermKind.BlankNode ? RdfTerm.BlankNode(Encoding.UTF8.GetBytes(prefix + Encoding.UTF8.GetString(term.Lexical))) : term;
            ParseResult parsed = TurtleParser.Parse(File.ReadAllBytes(files[f]), (in QuadView q) =>
            {
                RdfTerm predicate = q.Predicate.Materialise();
                if (withSparql || !predicate.Equals(Sparql))
                {
                    triples.Add((Own(q.Subject.Materialise()), predicate, Own(q.Object.Materialise())));
                }
            }, new TurtleOptions { Syntax = RdfSyntax.Turtle });
            Assert.True(parsed.Succeeded, files[f] + ": " + parsed.FirstError);
        }

        if (!composed)
        {
            triples.Add((RdfTerm.Iri("http://www.w3.org/2002/07/owl#Class"u8), RdfTerm.Iri("http://www.w3.org/2000/01/rdf-schema#subClassOf"u8), RdfTerm.Iri("http://www.w3.org/2000/01/rdf-schema#Class"u8)));
        }

        return triples;
    }

    /// <summary>The shapes graph as a dataset, the triples in its default graph.</summary>
    internal static InMemoryDataset Dataset(bool withSparql, bool composed)
    {
        InMemoryDatasetBuilder shapes = new();
        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in Triples(withSparql, composed))
        {
            shapes.Add(s, p, o);
        }

        return shapes.ToDataset();
    }

    /// <summary>The portfolio's head but its last <paramref name="held"/> commits, as terms, and those commits.</summary>
    internal static (List<(RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm G)> Quads, List<RecCommit> Held) Head(RecSize size, int held)
    {
        int keep = RecPortfolio.Shape(size).Commits - held;
        HashSet<RecQuad> head = [];
        List<RecCommit> rest = [];
        int n = 0;
        foreach (RecCommit commit in RecPortfolio.Generate(size, 20261015))
        {
            if (n++ >= keep)
            {
                rest.Add(commit);
                continue;
            }

            foreach (RecQuad q in commit.Retracted)
            {
                head.Remove(q);
            }

            foreach (RecQuad q in commit.Asserted)
            {
                head.Add(q);
            }
        }

        List<(RdfTerm, RdfTerm, RdfTerm, RdfTerm)> quads = [];
        foreach (RecQuad q in head)
        {
            quads.Add(Terms(q));
        }

        return (quads, rest);
    }

    internal static (RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm G) Terms(RecQuad q) => (Term(q.Subject), Term(q.Predicate), Term(q.Object), Term(q.Graph));

    internal static RdfTerm Term(string text) => text[0] == '<'
        ? RdfTerm.Iri(Encoding.UTF8.GetBytes(text[1..^1]))
        : RdfTerm.Literal(Encoding.UTF8.GetBytes(text[1..^1]));

    internal static string Root()
    {
        DirectoryInfo? d = new(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Varve.slnx")))
        {
            d = d.Parent;
        }

        return d?.FullName ?? throw new InvalidOperationException("The repository root is not above " + AppContext.BaseDirectory + ".");
    }
}
