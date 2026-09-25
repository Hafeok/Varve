// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Varve.Rdf;

/// <summary>
/// RDF Dataset Canonicalization, RDFC-1.0 (W3C Recommendation, 21 May 2024),
/// over any quad source (<c>rdf-canon.md</c>, ADR 0059).
/// </summary>
/// <remarks>
/// <para>
/// Equal canonical forms mean isomorphic datasets. The converse holds when
/// every graph name is an IRI; with blank nodes as graph names RDFC-1.0 can
/// give two isomorphic datasets different forms (<c>rdf-canon.md</c> §6).
/// The cost is bounded by <see cref="CanonicalisationOptions.WorkLimit"/>:
/// Hash N-Degree Quads is exponential in the worst case, and a dataset that
/// would need more than the limit fails with
/// <see cref="CanonicalisationLimitException"/> rather than running without
/// end (§4.4.3, §7.1).
/// </para>
/// <para>
/// RDFC-1.0 is defined over RDF 1.1 datasets. A triple term with no blank
/// node inside is a ground term; one with a blank node inside is refused
/// (<c>rdf-canon.md</c> §3.1).
/// </para>
/// </remarks>
public static class RdfCanonicaliser
{
    /// <summary>Canonicalises every quad the source holds, in every graph.</summary>
    /// <exception cref="CanonicalisationLimitException">The work limit was met.</exception>
    /// <exception cref="ArgumentException">A triple term holds a blank node, or two blank nodes of the source share a label.</exception>
    public static CanonicalDataset Canonicalise(IQuadSource source, CanonicalisationOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new Run(source, options ?? CanonicalisationOptions.Default, cancellationToken).Canonicalise();
    }

    private readonly record struct CQuad(int S, int P, int O, int G);

    /// <summary>An identifier issuer (§4.3): a prefix, a counter, and what it issued, in order.</summary>
    private sealed class Issuer
    {
        private readonly string _prefix;
        private readonly Dictionary<int, string> _issued;
        private readonly List<int> _order;

        internal Issuer(string prefix)
            : this(prefix, [], [])
        {
        }

        private Issuer(string prefix, Dictionary<int, string> issued, List<int> order)
        {
            _prefix = prefix;
            _issued = issued;
            _order = order;
        }

        internal IReadOnlyList<int> Order => _order;

        internal bool TryGet(int blank, out string identifier) => _issued.TryGetValue(blank, out identifier!);

        // §4.5.2.
        internal string Issue(int blank)
        {
            if (!_issued.TryGetValue(blank, out string? identifier))
            {
                identifier = _prefix + _order.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                _issued[blank] = identifier;
                _order.Add(blank);
            }

            return identifier;
        }

        internal Issuer Copy() => new(_prefix, new Dictionary<int, string>(_issued), [.. _order]);
    }

    private sealed class Run
    {
        private readonly IQuadSource _source;
        private readonly CanonicalisationOptions _options;
        private readonly CancellationToken _cancellationToken;
        private readonly IncrementalHash _hash;

        // Terms by index: a ground term's canonical bytes, or a blank node's index.
        private readonly List<byte[]?> _ground = [];
        private readonly List<int> _blankOf = [];
        private readonly List<string> _labels = [];
        private readonly HashSet<string> _labelSet = new(StringComparer.Ordinal);
        private readonly List<CQuad> _quads = [];
        private readonly List<List<int>> _mentions = [];
        private readonly Dictionary<int, string> _firstDegree = [];
        private readonly Issuer _canonical = new("c14n");
        private long _steps;
        private long _limit;

        internal Run(IQuadSource source, CanonicalisationOptions options, CancellationToken cancellationToken)
        {
            _source = source;
            _options = options;
            _cancellationToken = cancellationToken;
            _hash = IncrementalHash.CreateHash(options.HashAlgorithm);
        }

        internal CanonicalDataset Canonicalise()
        {
            using (_hash)
            {
                Read();

                // §4.4.3 step 3: first-degree hashes, grouped.
                SortedDictionary<string, List<int>> byHash = new(StringComparer.Ordinal);

                for (int n = 0; n < _labels.Count; n++)
                {
                    string hash = HashFirstDegree(n);

                    if (!byHash.TryGetValue(hash, out List<int>? list))
                    {
                        byHash[hash] = list = [];
                    }

                    list.Add(n);
                }

                // Step 4: unique hashes get canonical identifiers at once.
                List<List<int>> shared = [];

                foreach (List<int> list in byHash.Values)
                {
                    if (list.Count == 1)
                    {
                        _canonical.Issue(list[0]);
                    }
                    else
                    {
                        shared.Add(list);
                    }
                }

                int sharedCount = 0;
                foreach (List<int> list in shared)
                {
                    sharedCount += list.Count;
                }

                _limit = (long)_options.WorkLimit * Math.Max(1, sharedCount);

                // Step 5: the rest by Hash N-Degree Quads.
                foreach (List<int> list in shared)
                {
                    List<(string Hash, Issuer Issuer)> paths = [];

                    foreach (int n in list)
                    {
                        if (_canonical.TryGet(n, out _))
                        {
                            continue;
                        }

                        Issuer temporary = new("b");
                        temporary.Issue(n);
                        paths.Add(HashNDegree(n, temporary));
                    }

                    paths.Sort(static (a, b) => string.CompareOrdinal(a.Hash, b.Hash));

                    foreach ((_, Issuer issuer) in paths)
                    {
                        foreach (int existing in issuer.Order)
                        {
                            _canonical.Issue(existing);
                        }
                    }
                }

                return new CanonicalDataset(Serialise(), Issued());
            }
        }

        private void Read()
        {
            Dictionary<TermHandle, int> index = new(_source.TermComparer);
            HashSet<CQuad> seen = [];

            using IQuadCursor cursor = _source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);

            while (cursor.MoveNext())
            {
                Quad quad = cursor.Current;
                CQuad q = new(
                    Term(quad.Subject, index),
                    Term(quad.Predicate, index),
                    Term(quad.Object, index),
                    quad.Graph.IsNone ? -1 : Term(quad.Graph, index));

                // A dataset is a set (test076: a duplicate ground triple in the input).
                if (!seen.Add(q))
                {
                    continue;
                }

                int position = _quads.Count;
                _quads.Add(q);
                Mention(q.S, position);
                Mention(q.O, position);
                Mention(q.G, position);
            }
        }

        private void Mention(int term, int quad)
        {
            if (term >= 0 && _blankOf[term] >= 0)
            {
                List<int> mentions = _mentions[_blankOf[term]];

                if (mentions.Count == 0 || mentions[^1] != quad)
                {
                    mentions.Add(quad);
                }
            }
        }

        private int Term(TermHandle handle, Dictionary<TermHandle, int> index)
        {
            if (index.TryGetValue(handle, out int existing))
            {
                return existing;
            }

            if (!_source.TryExternalise(handle, out RdfTerm? term))
            {
                throw new ArgumentException("The source holds a term it cannot externalise, which cannot be canonicalised.", nameof(handle));
            }

            int position = _ground.Count;
            index[handle] = position;

            if (term.Kind == RdfTermKind.BlankNode)
            {
                string label = Encoding.UTF8.GetString(term.Lexical);

                if (!_labelSet.Add(label))
                {
                    throw new ArgumentException("Two blank nodes of the source share the label '" + label + "'.", nameof(handle));
                }

                _ground.Add(null);
                _blankOf.Add(_labels.Count);
                _labels.Add(label);
                _mentions.Add([]);
                return position;
            }

            if (HasBlank(term))
            {
                throw new ArgumentException(
                    "A triple term with a blank node inside cannot be canonicalised: RDFC-1.0 is defined over RDF 1.1 datasets (rdf-canon.md §3.1).",
                    nameof(handle));
            }

            List<byte> bytes = [];
            CanonicalNQuads.Write(term, bytes);
            _ground.Add([.. bytes]);
            _blankOf.Add(-1);
            return position;
        }

        private static bool HasBlank(RdfTerm term) => term.Kind switch
        {
            RdfTermKind.BlankNode => true,
            RdfTermKind.TripleTerm => HasBlank(term.Subject!) || HasBlank(term.Predicate!) || HasBlank(term.Object!),
            _ => false,
        };

        // §4.6.3.
        private string HashFirstDegree(int reference)
        {
            if (_firstDegree.TryGetValue(reference, out string? cached))
            {
                return cached;
            }

            List<byte[]> lines = [];

            foreach (int q in _mentions[reference])
            {
                lines.Add(Line(_quads[q], blank => blank == reference ? "a" : "z"));
            }

            string hash = HashOf(lines);
            _firstDegree[reference] = hash;
            return hash;
        }

        // §4.7.3.
        private string HashRelated(int related, CQuad quad, Issuer issuer, char position)
        {
            StringBuilder input = new();
            input.Append(position);

            if (position != 'g')
            {
                input.Append('<').Append(Encoding.UTF8.GetString(_ground[quad.P]!.AsSpan(1, _ground[quad.P]!.Length - 2))).Append('>');
            }

            if (_canonical.TryGet(related, out string? identifier) || issuer.TryGet(related, out identifier))
            {
                input.Append("_:").Append(identifier);
            }
            else
            {
                input.Append(HashFirstDegree(related));
            }

            return HashOf(input.ToString());
        }

        // §4.8.3.
        private (string Hash, Issuer Issuer) HashNDegree(int identifier, Issuer issuer)
        {
            Step();
            SortedDictionary<string, List<int>> related = new(StringComparer.Ordinal);

            foreach (int q in _mentions[identifier])
            {
                CQuad quad = _quads[q];
                Relate(quad.S, 's');
                Relate(quad.O, 'o');
                Relate(quad.G, 'g');

                void Relate(int term, char position)
                {
                    if (term < 0 || _blankOf[term] < 0 || _blankOf[term] == identifier)
                    {
                        return;
                    }

                    int blank = _blankOf[term];
                    string hash = HashRelated(blank, quad, issuer, position);

                    if (!related.TryGetValue(hash, out List<int>? list))
                    {
                        related[hash] = list = [];
                    }

                    list.Add(blank);
                }
            }

            StringBuilder data = new();

            foreach ((string relatedHash, List<int> blanks) in related)
            {
                data.Append(relatedHash);
                string? chosenPath = null;
                Issuer? chosenIssuer = null;

                foreach (int[] permutation in Permutations(blanks))
                {
                    Step();
                    Issuer copy = issuer.Copy();
                    StringBuilder path = new();
                    List<int> recursion = [];
                    bool skip = false;

                    foreach (int node in permutation)
                    {
                        if (_canonical.TryGet(node, out string? canonical))
                        {
                            path.Append("_:").Append(canonical);
                        }
                        else
                        {
                            if (!copy.TryGet(node, out _))
                            {
                                recursion.Add(node);
                            }

                            path.Append("_:").Append(copy.Issue(node));
                        }

                        if (Worse(path, chosenPath))
                        {
                            skip = true;
                            break;
                        }
                    }

                    if (skip)
                    {
                        continue;
                    }

                    foreach (int node in recursion)
                    {
                        (string hash, Issuer result) = HashNDegree(node, copy);
                        path.Append("_:").Append(copy.Issue(node));
                        path.Append('<').Append(hash).Append('>');
                        copy = result;

                        if (Worse(path, chosenPath))
                        {
                            skip = true;
                            break;
                        }
                    }

                    if (skip)
                    {
                        continue;
                    }

                    string candidate = path.ToString();

                    if (chosenPath is null || string.CompareOrdinal(candidate, chosenPath) < 0)
                    {
                        chosenPath = candidate;
                        chosenIssuer = copy;
                    }
                }

                data.Append(chosenPath);
                issuer = chosenIssuer!;
            }

            return (HashOf(data.ToString()), issuer);
        }

        // "the length of path is greater than or equal to the length of chosen
        // path and path is greater than chosen path" (§4.8.3 steps 5.4.4.2, 5.4.5.4).
        private static bool Worse(StringBuilder path, string? chosen) =>
            chosen is not null && path.Length >= chosen.Length && string.CompareOrdinal(path.ToString(), chosen) > 0;

        private void Step()
        {
            _cancellationToken.ThrowIfCancellationRequested();

            if (++_steps > _limit)
            {
                throw new CanonicalisationLimitException(_steps, _limit);
            }
        }

        // Every permutation, lexicographic in the list's order; the order is
        // immaterial to the result, which is the least path over all of them.
        private static IEnumerable<int[]> Permutations(List<int> items)
        {
            int[] current = [.. items];
            Array.Sort(current);
            yield return (int[])current.Clone();

            while (true)
            {
                int i = current.Length - 2;

                while (i >= 0 && current[i] >= current[i + 1])
                {
                    i--;
                }

                if (i < 0)
                {
                    yield break;
                }

                int j = current.Length - 1;

                while (current[j] <= current[i])
                {
                    j--;
                }

                (current[i], current[j]) = (current[j], current[i]);
                Array.Reverse(current, i + 1, current.Length - i - 1);
                yield return (int[])current.Clone();
            }
        }

        // One quad as a canonical N-Quads line, its blank nodes named by `name`.
        private byte[] Line(CQuad quad, Func<int, string> name)
        {
            List<byte> line = [];
            Component(quad.S, name, line);
            line.Add((byte)' ');
            Component(quad.P, name, line);
            line.Add((byte)' ');
            Component(quad.O, name, line);

            if (quad.G >= 0)
            {
                line.Add((byte)' ');
                Component(quad.G, name, line);
            }

            line.AddRange(" .\n"u8);
            return [.. line];
        }

        private void Component(int term, Func<int, string> name, List<byte> line)
        {
            if (_blankOf[term] >= 0)
            {
                line.AddRange("_:"u8);
                line.AddRange(Encoding.UTF8.GetBytes(name(_blankOf[term])));
            }
            else
            {
                line.AddRange(_ground[term]!);
            }
        }

        // Lines sorted in code point order — UTF-8 byte order is code point order — and joined.
        private string HashOf(List<byte[]> lines)
        {
            lines.Sort(static (a, b) => a.AsSpan().SequenceCompareTo(b));

            foreach (byte[] line in lines)
            {
                _hash.AppendData(line);
            }

            return Convert.ToHexStringLower(_hash.GetHashAndReset());
        }

        private string HashOf(string text)
        {
            _hash.AppendData(Encoding.UTF8.GetBytes(text));
            return Convert.ToHexStringLower(_hash.GetHashAndReset());
        }

        // §4.4.3 final steps: every quad with canonical identifiers, sorted, once each.
        private ReadOnlyMemory<byte> Serialise()
        {
            List<byte[]> lines = [];

            foreach (CQuad quad in _quads)
            {
                lines.Add(Line(quad, blank => _canonical.Issue(blank)));
            }

            lines.Sort(static (a, b) => a.AsSpan().SequenceCompareTo(b));
            List<byte> output = [];
            byte[]? previous = null;

            foreach (byte[] line in lines)
            {
                if (previous is null || !line.AsSpan().SequenceEqual(previous))
                {
                    output.AddRange(line);
                }

                previous = line;
            }

            return output.ToArray();
        }

        private Dictionary<string, string> Issued()
        {
            Dictionary<string, string> issued = new(StringComparer.Ordinal);

            foreach (int blank in _canonical.Order)
            {
                _canonical.TryGet(blank, out string identifier);
                issued[_labels[blank]] = identifier;
            }

            return issued;
        }
    }
}
