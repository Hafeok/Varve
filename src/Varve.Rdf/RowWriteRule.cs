// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Rdf;

/// <summary>
/// The row-level write rule (ADR 0236), over one write's state before and
/// after: a row-restricted writer writes only rows already wholly its own,
/// creates rows only at blank nodes or under its <c>createUnder</c>
/// prefixes, and links only to rows it reads. Every breach is the same
/// <see cref="RowNotWritableException"/>, so a write's outcome depends only on
/// what its writer reads.
/// </summary>
/// <remarks>
/// <c>before</c> and <c>after</c> share one handle space, as a staging view
/// and its overlay do, and each classification is in those handles. A
/// retraction of a quad the writer does not see is a no-op the executor
/// drops (W5), and is ignored here.
/// </remarks>
[Contract(typeof(TheRowLevelWriteRule.RowRestrictedWriterWritesOnlyItsRows), Role = "the write rule over a write's two states")]
public sealed class RowWriteRule
{
    private readonly IQuadSource _before;
    private readonly IRowClassification _beforeRows;
    private readonly IQuadSource _after;
    private readonly IRowClassification _afterRows;
    private readonly RowScope _writable;
    private readonly RowScopedQuadSource _visibleBefore;
    private readonly SubjectDecider?[] _writeBefore;
    private readonly SubjectDecider?[] _writeAfter;
    private readonly SubjectDecider?[] _readAfter;

    /// <summary>The rule for a writer with <paramref name="readable"/> and <paramref name="writable"/> rows, over the write's two states.</summary>
    public RowWriteRule(
        IQuadSource before,
        IRowClassification beforeRows,
        IQuadSource after,
        IRowClassification afterRows,
        RowScope readable,
        RowScope writable)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(beforeRows);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(afterRows);
        ArgumentNullException.ThrowIfNull(readable);
        ArgumentNullException.ThrowIfNull(writable);
        _before = before;
        _beforeRows = beforeRows;
        _after = after;
        _afterRows = afterRows;
        _writable = writable;
        _visibleBefore = new RowScopedQuadSource(before, beforeRows, readable);
        _writeBefore = Deciders(before, beforeRows, writable);
        _writeAfter = Deciders(after, afterRows, writable);
        _readAfter = Deciders(after, afterRows, readable);
    }

    /// <summary>Whether <paramref name="quad"/> is there before the write and the writer sees it: a retraction of any other is a no-op (W5).</summary>
    public bool SeesBefore(in Quad quad) => _visibleBefore.Contains(in quad);

    /// <summary>Refuses the write when one of <paramref name="asserted"/> or <paramref name="retracted"/> breaks the rule.</summary>
    /// <exception cref="RowNotWritableException">A quad breaks W1 to W4.</exception>
    public void Check(ReadOnlySpan<Quad> asserted, ReadOnlySpan<Quad> retracted)
    {
        if (_writable.IsAll)
        {
            return;
        }

        foreach (Quad quad in retracted)
        {
            if (SeesBefore(in quad) && !AllowsRetraction(in quad))
            {
                throw new RowNotWritableException();
            }
        }

        foreach (Quad quad in asserted)
        {
            if (!AllowsAssertion(in quad))
            {
                throw new RowNotWritableException();
            }
        }
    }

    private bool AllowsRetraction(in Quad quad)
    {
        BindingIndex binding = _beforeRows.BindingOf(quad.Graph);
        SubjectDecider? before = binding.IsNone ? null : _writeBefore[binding.Value];

        if (before is null)
        {
            return true;
        }

        // The quad is about its subject's subjects and, unless its
        // predicate is public, its object's: all of them written by W.
        return SubjectRowWritable(binding, quad.Subject)
            && (_beforeRows.IsPublicLink(binding, quad.Predicate) || before.Allows(_beforeRows.SubjectsOf(binding, quad.Object)));
    }

    private bool AllowsAssertion(in Quad quad)
    {
        BindingIndex binding = _afterRows.BindingOf(quad.Graph);
        SubjectDecider? after = binding.IsNone ? null : _writeAfter[binding.Value];

        if (after is null)
        {
            return true;
        }

        if (!SubjectRowWritable(binding, quad.Subject))
        {
            return false;
        }

        if (_afterRows.IsPublicLink(binding, quad.Predicate))
        {
            return true;
        }

        return ObjectReadable(binding, quad.Object) && after.Allows(_afterRows.SubjectsOf(binding, quad.Object));
    }

    // W1 to W3 for the quad's subject node.
    private bool SubjectRowWritable(BindingIndex binding, TermHandle subject)
    {
        SubjectDecider before = _writeBefore[binding.Value]!;
        SubjectDecider after = _writeAfter[binding.Value]!;

        if (HasRow(_before, _beforeRows, binding, subject))
        {
            SubjectSetId set = _beforeRows.SubjectsOf(binding, subject);

            if (set.IsNone || !before.Allows(set))
            {
                return false;
            }
        }
        else if (!MayCreate(subject, before.Scope))
        {
            return false;
        }

        if (HasRow(_after, _afterRows, binding, subject))
        {
            SubjectSetId set = _afterRows.SubjectsOf(binding, subject);
            return !set.IsNone && after.Allows(set);
        }

        return true;
    }

    // W4: an object node has a row the writer reads, or is a blank node
    // without one; an IRI without a row is refused as a hidden one is. A
    // literal is no node.
    private bool ObjectReadable(BindingIndex binding, TermHandle @object)
    {
        if (!_after.TryExternalise(@object, out RdfTerm? term))
        {
            return false;
        }

        switch (term.Kind)
        {
            case RdfTermKind.Literal:
                return true;
            case RdfTermKind.TripleTerm:
                return _after.TryGetTripleTermComponents(@object, out TermHandle s, out _, out TermHandle o)
                    && ObjectReadable(binding, s) && ObjectReadable(binding, o);
            default:
                if (HasRow(_after, _afterRows, binding, @object))
                {
                    return _readAfter[binding.Value] is not { } reader || reader.Allows(_afterRows.SubjectsOf(binding, @object));
                }

                return term.Kind == RdfTermKind.BlankNode;
        }
    }

    private bool MayCreate(TermHandle subject, SubjectScope scope) =>
        _after.TryExternalise(subject, out RdfTerm? term)
        && (term.Kind == RdfTermKind.BlankNode || scope.MayCreate(term));

    private static bool HasRow(IQuadSource source, IRowClassification rows, BindingIndex binding, TermHandle node)
    {
        using IQuadCursor cursor = source.Match(node, TermHandle.None, TermHandle.None, GraphPattern.Any);

        while (cursor.MoveNext())
        {
            if (rows.BindingOf(cursor.Current.Graph) == binding)
            {
                return true;
            }
        }

        return false;
    }

    private static SubjectDecider?[] Deciders(IQuadSource source, IRowClassification rows, RowScope scope)
    {
        SubjectDecider?[] deciders = new SubjectDecider?[rows.Bindings.Count];

        for (int i = 0; i < deciders.Length; i++)
        {
            BindingIndex index = new(i);
            SubjectScope subjects = scope.For(rows.Bindings[i]);
            deciders[i] = subjects.IsAll ? null : new SubjectDecider(source, rows, index, subjects);
        }

        return deciders;
    }
}
