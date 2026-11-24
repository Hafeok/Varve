// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.IO;
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
}
