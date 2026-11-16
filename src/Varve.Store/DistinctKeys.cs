// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Store;

/// <summary>
/// The distinct values of one component of the keys in a range, one seek past
/// each (ADR 0193): the first key at or after the seek gives the next value,
/// and the seek after it is to that value plus one, every later component 0.
/// The merged scan under it decides each key across the runs, so a value
/// whose every key a newer run retracted is never given.
/// </summary>
[Contract(typeof(RdfsRdfAndDByRewriting.DistinctTermsBySeek), Role = "distinct terms of an index, by seeking past each")]
internal sealed class DistinctKeyCursor : ITermCursor
{
    private readonly RunCursor? _runs;
    private readonly ExtentSeekScan? _extents;
    private readonly IndexOrder _order;
    private readonly int _component;
    private bool _ended;

    /// <summary>Over a scan of the runs.</summary>
    internal DistinctKeyCursor(RunCursor runs, IndexOrder order, int component)
    {
        _runs = runs;
        _order = order;
        _component = component;
    }

    /// <summary>Over a scan of a version whose newest member is a sought set.</summary>
    internal DistinctKeyCursor(ExtentSeekScan extents, IndexOrder order, int component)
    {
        _extents = extents;
        _order = order;
        _component = component;
    }

    public TermHandle Current { get; private set; }

    public bool MoveNext()
    {
        if (_ended || !(_runs?.MoveNext() ?? _extents!.MoveNext()))
        {
            _ended = true;
            return false;
        }

        Quad quad = _runs?.Current ?? _extents!.Current;
        QuadKey key = Orders.Key(_order, in quad);
        ulong value = Component(in key, _component);
        Current = new TermHandle(value);

        // The last value an id can take has nothing after it.
        if (value == ulong.MaxValue)
        {
            _ended = true;
            return true;
        }

        QuadKey next = Next(in key, value + 1);
        if (_runs is not null)
        {
            _runs.SeekTo(in next);
        }
        else
        {
            _extents!.SeekTo(in next);
        }
        return true;
    }

    public void Dispose()
    {
        _runs?.Dispose();
        _extents?.Dispose();
    }

    // The key with the enumerated component set and every later one 0; the earlier ones are the range's.
    private QuadKey Next(in QuadKey key, ulong value) => _component switch
    {
        0 => new QuadKey(value, 0, 0, 0),
        1 => new QuadKey(key.K0, value, 0, 0),
        2 => new QuadKey(key.K0, key.K1, value, 0),
        _ => new QuadKey(key.K0, key.K1, key.K2, value),
    };

    private static ulong Component(in QuadKey key, int component) => component switch
    {
        0 => key.K0,
        1 => key.K1,
        2 => key.K2,
        _ => key.K3,
    };
}
