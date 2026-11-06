// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Inference;

/// <summary>
/// What a closure needs from the source whose handles it is over: a handle
/// for each term a rule names, and what a handle stands for where a rule
/// tests it.
/// </summary>
/// <remarks>
/// The rules invent no terms (ADR 0197 point 2), so these are the only terms a
/// closure ever names beyond the source's own. A source that lacks one stages
/// a handle for it (ADR 0058), never allocates it.
/// </remarks>
[Contract(typeof(TheInferenceProjection.RowsAreImplicitQuadsOverHandles), Role = "the source's handles, as a closure sees them")]
public interface IInferenceTerms
{
    /// <summary>The handle of a term a rule names: the source's, or one staged for it.</summary>
    TermHandle Handle(RdfTerm term);

    /// <summary>Whether a handle names a literal.</summary>
    bool IsLiteral(TermHandle handle);

    /// <summary>The handle of a literal's datatype: its own, or <c>rdf:langString</c> for a language-tagged one.</summary>
    bool TryDatatype(TermHandle literal, out TermHandle datatype);

    /// <summary>Whether a handle names a container membership property, <c>rdf:_n</c>.</summary>
    bool IsContainerMembership(TermHandle handle);
}
