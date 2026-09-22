// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Turtle;

/// <summary>
/// The prefixes a writer was given, and the longest-match lookup over them.
/// </summary>
/// <remarks>
/// A linear scan over a handful of entries. A trie would win on a document with
/// hundreds of prefixes and nobody writes one; the array costs nothing to build
/// and nothing to keep correct.
/// </remarks>
internal sealed class PrefixTable
{
    private readonly System.Collections.Generic.List<byte[]> _names = [];
    private readonly System.Collections.Generic.List<byte[]> _iris = [];

    internal int Count => _names.Count;

    /// <summary>
    /// Records a binding, replacing an earlier one of the same name.
    /// </summary>
    /// <remarks>
    /// Replacing rather than appending, because a writer emits each declaration
    /// as it is given and the reader of the output takes the last one. Keeping
    /// both would make the table disagree with the document it just wrote.
    /// </remarks>
    internal void Bind(ReadOnlySpan<byte> name, ReadOnlySpan<byte> iri)
    {
        for (int i = 0; i < _names.Count; i++)
        {
            if (name.SequenceEqual(_names[i]))
            {
                _iris[i] = iri.ToArray();
                return;
            }
        }

        _names.Add(name.ToArray());
        _iris.Add(iri.ToArray());
    }

    /// <summary>
    /// The longest bound IRI that <paramref name="iri"/> starts with, or -1.
    /// </summary>
    /// <remarks>
    /// Longest wins so that a document binding both <c>ex:</c> to
    /// <c>http://e/</c> and <c>exv:</c> to <c>http://e/v/</c> compacts
    /// <c>http://e/v/x</c> with the second. Picking the first match instead
    /// would be stable but would produce <c>ex:v/x</c>, whose local part is not
    /// a <c>PN_LOCAL</c>, so the compaction would then be rejected and the
    /// shorter form lost for no reason.
    /// </remarks>
    internal int LongestMatch(ReadOnlySpan<byte> iri)
    {
        int best = -1;
        int bestLength = -1;

        for (int i = 0; i < _iris.Count; i++)
        {
            byte[] candidate = _iris[i];

            if (candidate.Length > bestLength && iri.StartsWith(candidate))
            {
                best = i;
                bestLength = candidate.Length;
            }
        }

        return best;
    }

    internal ReadOnlySpan<byte> NameAt(int index) => _names[index];

    internal ReadOnlySpan<byte> IriAt(int index) => _iris[index];
}
