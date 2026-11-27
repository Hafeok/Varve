// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using Varve.Turtle;

namespace Varve.Learn;

/// <summary>Reading a TriG file into a dataset, as one commit.</summary>
internal static class Load
{
    // region: load
    /// <summary>Parses <paramref name="path"/> and commits every quad in it as one commit.</summary>
    internal static async ValueTask<CommitResult> TriGAsync(Dataset dataset, string path)
    {
        CommitRequestBuilder commit = new();
        ParseResult parsed = TurtleParser.Parse(
            File.ReadAllBytes(path),
            (in QuadView quad) => commit.Assert(
                quad.Subject.Materialise(),
                quad.Predicate.Materialise(),
                quad.Object.Materialise()),
            new TurtleOptions { Syntax = RdfSyntax.TriG });
        if (!parsed.Succeeded)
        {
            throw new InvalidDataException(path + ": " + parsed.FirstError);
        }

        return await dataset.CommitAsync(commit);
    }
    // endregion: load

    // region: load-graph
    /// <summary>
    /// Parses the Turtle files <paramref name="paths"/> and commits every
    /// triple in them into the named graph <paramref name="graph"/>, as one
    /// commit: their RDF merge, each file's blank nodes kept its own.
    /// </summary>
    internal static async ValueTask<CommitResult> TurtleIntoAsync(Dataset dataset, RdfTerm graph, params string[] paths)
    {
        CommitRequestBuilder commit = new();
        for (int file = 0; file < paths.Length; file++)
        {
            string own = "f" + file.ToString(CultureInfo.InvariantCulture) + "-";
            RdfTerm Own(RdfTerm term) => term.Kind == RdfTermKind.BlankNode
                ? RdfTerm.BlankNode(Encoding.UTF8.GetBytes(own + Encoding.UTF8.GetString(term.Lexical)))
                : term;
            ParseResult parsed = TurtleParser.Parse(
                File.ReadAllBytes(paths[file]),
                (in QuadView quad) => commit.Assert(
                    Own(quad.Subject.Materialise()),
                    quad.Predicate.Materialise(),
                    Own(quad.Object.Materialise()),
                    graph),
                new TurtleOptions { Syntax = RdfSyntax.Turtle });
            if (!parsed.Succeeded)
            {
                throw new InvalidDataException(paths[file] + ": " + parsed.FirstError);
            }
        }

        return await dataset.CommitAsync(commit);
    }
    // endregion: load-graph
}
