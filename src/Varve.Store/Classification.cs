// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Store;

/// <summary>
/// ADR 0023's classifier, as ADR 0231 fixes it: from a pinned source and a
/// pending delta to the classification of their overlay, assigning term
/// occurrences to data subjects by ADR 0230's rule. Free of SPARQL and
/// SHACL; implementations derived from shapes are layer 5. Usable with
/// erasure mode off: row-level access reads the classification without
/// encryption.
/// </summary>
[Contract(typeof(TheClassifierAndSelectorContracts.ClassifierClassifiesTheOverlay), Role = "assigns occurrences to data subjects")]
public interface IClassifier
{
    /// <summary>
    /// The classification of <c>Overlay(<paramref name="pinned"/>,
    /// <paramref name="pending"/>)</c>, in <paramref name="pinned"/>'s
    /// handles: a pure function of that state and of the classifier's
    /// configuration. <paramref name="pending"/> may be empty.
    /// </summary>
    IRowClassification Classify(IQuadSource pinned, QuadDelta pending);
}

/// <summary>
/// ADR 0023's selector, as ADR 0231 fixes it: the quads of a source about
/// one data subject, by its classification. One definition of "the
/// subject's data", serving access without erasure mode now and erasure's
/// cleanup by key id later.
/// </summary>
[Contract(typeof(TheClassifierAndSelectorContracts.SelectorIsTheClassification), Role = "a subject's data")]
public interface ISelector
{
    /// <summary>Every quad of <paramref name="source"/> whose about-set, by <paramref name="classification"/>, contains <paramref name="subject"/>.</summary>
    IQuadCursor Select(IQuadSource source, IRowClassification classification, RdfTerm subject);
}

/// <summary>
/// The selector, over the classification alone (ADR 0231 point 3): a quad of
/// a classified graph is selected when its subject node belongs to the
/// subject, or its object does under a predicate that is not a public link.
/// </summary>
[Contract(typeof(TheClassifierAndSelectorContracts.SelectorIsTheClassification), Role = "the one selector")]
public sealed class ClassificationSelector : ISelector
{
    /// <summary>The selector.</summary>
    public static ClassificationSelector Instance { get; } = new();

    /// <inheritdoc />
    public IQuadCursor Select(IQuadSource source, IRowClassification classification, RdfTerm subject)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentNullException.ThrowIfNull(subject);

        return source.TryInternalise(subject, out TermHandle handle)
            ? new Cursor(source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any), classification, handle, source)
            : new Cursor(null, classification, TermHandle.None, source);
    }

    private sealed class Cursor(IQuadCursor? inner, IRowClassification rows, TermHandle subject, IQuadSource source) : IQuadCursor
    {
        public Quad Current => inner!.Current;

        public bool MoveNext()
        {
            if (inner is null)
            {
                return false;
            }

            while (inner.MoveNext())
            {
                Quad quad = inner.Current;
                BindingIndex binding = rows.BindingOf(quad.Graph);

                if (!binding.IsNone
                    && (Holds(rows.SubjectsOf(binding, quad.Subject), binding)
                        || (!rows.IsPublicLink(binding, quad.Predicate) && Holds(rows.SubjectsOf(binding, quad.Object), binding))))
                {
                    return true;
                }
            }

            return false;
        }

        public void Dispose() => inner?.Dispose();

        // ADR 0022: handles compare through the source, by value when a
        // dictionary holds private terms.
        [DesignDecision(typeof(TheClassifierAndSelectorContracts.SelectorComparesThroughTheSourceComparer), Scope = ExceptionScope.HotPath)]
        private bool Holds(SubjectSetId set, BindingIndex binding)
        {
            foreach (TermHandle member in rows.Members(binding, set))
            {
                if (source.TermComparer.Equals(member, subject))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
