// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>
/// A region of a derived blob read in pages of 4 KiB, each checked against
/// its check the first time any read touches it (ADR 0213): a version-6
/// run's term section — its entries, offsets, hash index and filter. A page
/// that does not match is <see cref="DerivedDamageException"/>, before a
/// byte of it is used. Reads outside the region pass through unchecked; the
/// blob's owner disposes the blob.
/// </summary>
[DesignDecision(typeof(VerifiedDerivedData.VerifiedBeforeFirstUse), Scope = ExceptionScope.Boundary)]
internal sealed class CheckedPages : IReadableBlob
{
    /// <summary>The bytes of a page; the region's last page may be shorter.</summary>
    internal const int PageBytes = 4096;

    private readonly IReadableBlob _blob;
    private readonly long _from;
    private readonly long _to;
    private readonly Chunked<ulong> _checks;
    // A page's flag is set only after it verified. A read racing the write
    // at worst verifies the page again.
    private readonly bool[] _verified;

    internal CheckedPages(IReadableBlob blob, long from, long to, Chunked<ulong> checks)
    {
        if (to < from || checks.Count != (to - from + PageBytes - 1) / PageBytes)
        {
            throw new ArgumentException("A checked region has one check a page.", nameof(checks));
        }

        _blob = blob;
        _from = from;
        _to = to;
        _checks = checks;
        _verified = new bool[checks.Count];
    }

    public ByteCount Length => _blob.Length;

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public int Read(ByteOffset offset, Span<byte> destination)
    {
        long start = Math.Max(offset.Value, _from);
        long end = Math.Min(offset.Value + destination.Length, _to);

        for (long page = start < end ? (start - _from) / PageBytes : 0, last = start < end ? (end - 1 - _from) / PageBytes : -1; page <= last; page++)
        {
            Verify(page);
        }

        return _blob.Read(offset, destination);
    }

    public void Dispose()
    {
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private void Verify(long page)
    {
        if (_verified[page])
        {
            return;
        }

        long at = _from + (page * PageBytes);
        int length = (int)Math.Min(PageBytes, _to - at);
        Span<byte> bytes = stackalloc byte[PageBytes];

        if (_blob.Read(new ByteOffset(at), bytes[..length]) != length)
        {
            throw new DerivedDamageException("A derived run's term section is shorter than its directory says.");
        }

        if (BlockCheck.Of(bytes[..length]) != _checks[page])
        {
            throw new DerivedDamageException("A page of a derived run's term section does not match its check; the run is damaged.");
        }

        _verified[page] = true;
    }
}
