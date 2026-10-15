// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using Varve.Store.Log;

namespace Varve.Store.ObjectStorage;

/// <summary>
/// Where each of a dataset's objects lives (ADR 0135):
/// <c>writer</c>, <c>log/MANIFEST</c>, <c>log/NNNNNNNN.seg</c> for a
/// consolidated segment and <c>log/NNNNNNNN.chunks/CCCCCCCC</c> for a chunk,
/// all under the dataset's prefix.
/// </summary>
/// <remarks>
/// A segment's chunks are under <c>.chunks/</c>, not under the segment's own
/// name: a store backed by a file system refuses an object whose name is also
/// a prefix of others, and a container downloaded to a disk would too.
/// </remarks>
internal sealed class ObjectNames(ObjectName? prefix)
{
    private const string LogPart = "log/";
    private const string ChunksPart = ".chunks/";
    private const string SegmentSuffix = ".seg";

    private readonly string _prefix = prefix?.Value ?? string.Empty;

    public ObjectName Writer => new(_prefix + "writer");

    public ObjectName Log => new(_prefix + LogPart);

    public ObjectName Manifest => new(_prefix + LogPart + "MANIFEST");

    /// <summary>A blob of <c>derived/</c> the store holds: an extent, or the catalog (ADR 0139).</summary>
    public ObjectName Derived(string blob) => new(_prefix + "derived/" + blob);

    public ObjectName Segment(int segment) => new(_prefix + LogPart + Number(segment) + SegmentSuffix);

    public ObjectName Chunk(int segment, int chunk) => new(_prefix + LogPart + Slot(segment, chunk));

    /// <summary>A slot as the writer record names it: <c>NNNNNNNN.chunks/CCCCCCCC</c>.</summary>
    public static string Slot(int segment, int chunk) => Number(segment) + ChunksPart + Number(chunk);

    /// <summary>What a name under <c>log/</c> is: a consolidated segment, a chunk, or neither.</summary>
    public bool TryParse(ObjectName name, out int segment, out int chunk)
    {
        segment = -1;
        chunk = -1;
        string start = _prefix + LogPart;

        if (!name.Value.StartsWith(start, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> rest = name.Value.AsSpan(start.Length);

        if (rest.Length == 8 + SegmentSuffix.Length && rest.EndsWith(SegmentSuffix, StringComparison.Ordinal))
        {
            return TryNumber(rest[..8], out segment);
        }

        if (rest.Length == 8 + ChunksPart.Length + 8 && rest[8..(8 + ChunksPart.Length)].SequenceEqual(ChunksPart))
        {
            return TryNumber(rest[..8], out segment) && TryNumber(rest[^8..], out chunk);
        }

        return false;
    }

    private static string Number(int value) => value.ToString("D8", CultureInfo.InvariantCulture);

    private static bool TryNumber(ReadOnlySpan<char> digits, out int value)
    {
        value = -1;

        foreach (char c in digits)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
