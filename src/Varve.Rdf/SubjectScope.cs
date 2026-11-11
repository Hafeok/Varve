// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Rdf;

/// <summary>
/// A rule form of a subject set (ADR 0234): the data subjects with a quad
/// <c>(s <see cref="Predicate"/> <see cref="Target"/>)</c> in the binding's
/// graphs, such as the subjects whose caseworker is the caller.
/// </summary>
/// <param name="Predicate">The link's predicate, an IRI.</param>
/// <param name="Target">The link's target, an IRI: the caller's agent, or a template's expansion.</param>
public readonly record struct SubjectLink(RdfTerm Predicate, RdfTerm Target);

/// <summary>
/// A caller's subject set for one classifier binding (ADRs 0230 and 0234):
/// every subject, or the subjects named explicitly, by IRI prefix, by class
/// or by a link to the caller, with the prefixes it may create rows under.
/// An immutable value; the rule forms are decided against data by
/// <see cref="RowScopedQuadSource"/> and <see cref="RowWriteRule"/>.
/// </summary>
[Contract(typeof(TheRowScopedQuadSource.SubjectScopeAndRowScope), Role = "a subject set as a value")]
public sealed class SubjectScope
{
    private readonly RdfTerm[] _subjects;
    private readonly byte[][] _prefixes;
    private readonly RdfTerm[] _classes;
    private readonly SubjectLink[] _links;
    private readonly byte[][] _createUnder;

    private SubjectScope(RdfTerm[] subjects, byte[][] prefixes, RdfTerm[] classes, SubjectLink[] links, byte[][] createUnder, bool all)
    {
        _subjects = subjects;
        _prefixes = prefixes;
        _classes = classes;
        _links = links;
        _createUnder = createUnder;
        IsAll = all;
    }

    /// <summary>Every subject: a grant without subject terms.</summary>
    public static SubjectScope All { get; } = new([], [], [], [], [], all: true);

    /// <summary>No subject at all: a row-restricted caller whose terms name nobody.</summary>
    public static SubjectScope None { get; } = new([], [], [], [], [], all: false);

    /// <summary>Whether this is every subject.</summary>
    public bool IsAll { get; }

    /// <summary>The subjects named by IRI.</summary>
    public IReadOnlyList<RdfTerm> Subjects => _subjects;

    /// <summary>The classes whose instances are in the set.</summary>
    public IReadOnlyList<RdfTerm> Classes => _classes;

    /// <summary>The links whose sources are in the set.</summary>
    public IReadOnlyList<SubjectLink> Links => _links;

    /// <summary>Whether a decision needs the data: the set has a class or a link form.</summary>
    public bool HasRules => _classes.Length > 0 || _links.Length > 0;

    /// <summary>The set of the given forms; none of them names nobody.</summary>
    /// <exception cref="ArgumentException">A subject, class or link term is not an IRI, or a prefix is empty.</exception>
    public static SubjectScope Of(
        ReadOnlySpan<RdfTerm> subjects,
        ReadOnlySpan<string> prefixes,
        ReadOnlySpan<RdfTerm> classes,
        ReadOnlySpan<SubjectLink> links,
        ReadOnlySpan<string> createUnder)
    {
        RdfTerm[] named = Iris(subjects, nameof(subjects));
        RdfTerm[] typed = Iris(classes, nameof(classes));
        SubjectLink[] linked = links.ToArray();

        foreach (SubjectLink link in linked)
        {
            if (link.Predicate is not { Kind: RdfTermKind.Iri } || link.Target is not { Kind: RdfTermKind.Iri })
            {
                throw new ArgumentException("A subject link's predicate and target are IRIs.", nameof(links));
            }
        }

        return new SubjectScope(named, Utf8(prefixes, nameof(prefixes)), typed, linked, Utf8(createUnder, nameof(createUnder)), all: false);
    }

    /// <summary>The union of this set and <paramref name="other"/>: every subject when either is.</summary>
    public SubjectScope Union(SubjectScope other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (IsAll || other.IsAll)
        {
            return All;
        }

        return new SubjectScope(
            Concat(_subjects, other._subjects),
            Concat(_prefixes, other._prefixes),
            Concat(_classes, other._classes),
            Concat(_links, other._links),
            Concat(_createUnder, other._createUnder),
            all: false);
    }

    /// <summary>Whether <paramref name="subject"/> is in the set by name: an explicit subject or a prefix. The rule forms need the data, and are not asked here.</summary>
    public bool AllowsByName(RdfTerm subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        if (IsAll)
        {
            return true;
        }

        if (subject.Kind != RdfTermKind.Iri)
        {
            return false;
        }

        foreach (RdfTerm named in _subjects)
        {
            if (named.Equals(subject))
            {
                return true;
            }
        }

        return StartsWithAny(_prefixes, subject.Lexical);
    }

    /// <summary>Whether a writer of this set may create a row at <paramref name="iri"/> (ADR 0236, W3).</summary>
    public bool MayCreate(RdfTerm iri)
    {
        ArgumentNullException.ThrowIfNull(iri);
        return iri.Kind == RdfTermKind.Iri && StartsWithAny(_createUnder, iri.Lexical);
    }

    private static bool StartsWithAny(byte[][] prefixes, ReadOnlySpan<byte> iri)
    {
        foreach (byte[] prefix in prefixes)
        {
            if (iri.StartsWith(prefix))
            {
                return true;
            }
        }

        return false;
    }

    private static RdfTerm[] Iris(ReadOnlySpan<RdfTerm> terms, string parameter)
    {
        RdfTerm[] iris = terms.ToArray();

        foreach (RdfTerm term in iris)
        {
            if (term is not { Kind: RdfTermKind.Iri })
            {
                throw new ArgumentException("A subject or class in a subject set is an IRI.", parameter);
            }
        }

        return iris;
    }

    private static byte[][] Utf8(ReadOnlySpan<string> prefixes, string parameter)
    {
        byte[][] starts = new byte[prefixes.Length][];

        for (int i = 0; i < prefixes.Length; i++)
        {
            if (string.IsNullOrEmpty(prefixes[i]))
            {
                throw new ArgumentException("A prefix in a subject set is not empty.", parameter);
            }

            starts[i] = Encoding.UTF8.GetBytes(prefixes[i]);
        }

        return starts;
    }

    private static T[] Concat<T>(T[] first, T[] second)
    {
        if (second.Length == 0)
        {
            return first;
        }

        if (first.Length == 0)
        {
            return second;
        }

        T[] both = new T[first.Length + second.Length];
        first.CopyTo(both, 0);
        second.CopyTo(both, first.Length);
        return both;
    }
}
