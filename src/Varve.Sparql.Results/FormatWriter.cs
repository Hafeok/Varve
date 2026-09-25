// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Text;

namespace Varve.Sparql.Results;

/// <summary>
/// One format's writing. <see cref="SparqlResultsWriter"/> checks the call
/// order; a format writer only writes.
/// </summary>
internal abstract class FormatWriter
{
    protected FormatWriter(ResultsOutput output) => Output = output;

    protected ResultsOutput Output { get; }

    /// <summary>The variables' names as UTF-8, encoded once at the head.</summary>
    protected byte[][] Names { get; private set; } = [];

    internal virtual void WriteHead(IReadOnlyList<string> variables)
    {
        byte[][] names = new byte[variables.Count][];

        for (int i = 0; i < names.Length; i++)
        {
            names[i] = Encoding.UTF8.GetBytes(variables[i]);
        }

        Names = names;
    }

    internal abstract void WriteBoolean(bool value);

    internal abstract void StartSolution();

    internal abstract void WriteBinding(int variable, scoped TermInput term);

    internal abstract void EndSolution();

    internal abstract void WriteEnd(bool isBoolean);
}
