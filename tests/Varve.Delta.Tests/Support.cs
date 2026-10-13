// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers;
using System.Text;
using Varve.Delta.Model;
using Varve.Rdf;

namespace Varve.Delta.Tests;

/// <summary>Terms and changes as the lines that name them.</summary>
internal static class P
{
    internal static string TermText(RdfTerm term)
    {
        ArrayBufferWriter<byte> buffer = new();
        DeltaWriter.WriteTerm(buffer, term);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>A change as the line that names its quad.</summary>
    internal static string Line(FeedChange change)
    {
        StringBuilder text = new();
        text.Append(TermText(change.Subject)).Append(' ').Append(TermText(change.Predicate)).Append(' ').Append(TermText(change.Object));

        if (change.Graph is not null)
        {
            text.Append(' ').Append(TermText(change.Graph));
        }

        return text.ToString();
    }
}
