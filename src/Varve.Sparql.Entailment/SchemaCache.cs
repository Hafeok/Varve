// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Threading;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Sparql.Entailment.Model;

namespace Varve.Sparql.Entailment;

/// <summary>
/// Saturated schemas kept across requests, one per scoping graph as of the
/// versions of the graphs it merges (ADRs 0125, 0193): a query whose scoping
/// graph has not changed since one before it reuses that one's schema, and
/// pays no probe for it. Bounded by count and by size, the least recently
/// used dropped first.
/// </summary>
/// <remarks>
/// One cache serves one dataset: a key names graphs and their versions, not
/// the dataset they are in. A source that cannot name a graph's version
/// (<see cref="Varve.Rdf.IGraphVersionSource"/>) has its schemas saturated
/// per request and counted as misses. Safe for concurrent requests.
/// </remarks>
[Contract(typeof(RdfsRdfAndDByRewriting.SchemaCachedPerGraphVersion), Role = "the saturated schemas kept across requests")]
public sealed class SchemaCache
{
    private readonly int _capacity;
    private readonly long _budget;
    private long _weight;
    private readonly Lock _lock = new();
    private readonly Dictionary<SchemaKey, LinkedListNode<(SchemaKey Key, Schema Schema)>> _entries = [];
    private readonly LinkedList<(SchemaKey Key, Schema Schema)> _recent = new();
    private long _hits;
    private long _misses;

    /// <summary>
    /// A cache of at most 256 schemas holding at most 500,000 closure triples
    /// between them. A schema read whole holds its scoping graph's closure,
    /// and a graph written to makes a new one per commit, so the size is
    /// bounded as well as the count; one larger than the whole budget is
    /// saturated per request and not kept.
    /// </summary>
    public SchemaCache()
        : this(256, 500_000)
    {
    }

    internal SchemaCache(int capacity, long budget = 500_000)
    {
        _capacity = capacity;
        _budget = budget;
    }

    /// <summary>The lookups answered from the cache.</summary>
    public SchemaLookups Hits
    {
        get
        {
            lock (_lock)
            {
                return new SchemaLookups(_hits);
            }
        }
    }

    /// <summary>The lookups saturated: absent from the cache, or with a graph whose version the source could not name.</summary>
    public SchemaLookups Misses
    {
        get
        {
            lock (_lock)
            {
                return new SchemaLookups(_misses);
            }
        }
    }

    // The schema kept under a key, made the most recent; a miss is counted.
    internal bool TryGet(SchemaKey key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Schema? schema)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out LinkedListNode<(SchemaKey Key, Schema Schema)>? node))
            {
                _recent.Remove(node);
                _recent.AddFirst(node);
                _hits++;
                schema = node.Value.Schema;
                return true;
            }

            _misses++;
            schema = null;
            return false;
        }
    }

    // A saturation that could not be keyed.
    internal void Missed()
    {
        lock (_lock)
        {
            _misses++;
        }
    }

    internal void Add(SchemaKey key, Schema schema)
    {
        long weight = Weight(schema);
        lock (_lock)
        {
            if (_entries.ContainsKey(key) || weight > _budget)
            {
                return;
            }

            _entries[key] = _recent.AddFirst((key, schema));
            _weight += weight;
            while (_entries.Count > _capacity || _weight > _budget)
            {
                LinkedListNode<(SchemaKey Key, Schema Schema)> last = _recent.Last!;
                _recent.RemoveLast();
                _entries.Remove(last.Value.Key);
                _weight -= Weight(last.Value.Schema);
            }
        }
    }

    // A schema's size: the triples its closure holds beyond the axioms'.
    private static long Weight(Schema schema) => 1 + schema.Closure.Own.Count;
}

// What a scoping graph's schema depends on, as text: the rules, the rdf:_n
// the query names, and each graph merged, by name and version.
internal readonly record struct SchemaKey(string Value);
