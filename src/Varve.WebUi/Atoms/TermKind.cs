// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.WebUi.Atoms;

/// <summary>An RDF term's kind, shown by form (docs/ui/components/TermChip).</summary>
public enum TermKind
{
    /// <summary>An IRI: link colour, no frame.</summary>
    Iri,

    /// <summary>A literal: quoted on a fill, its datatype or language as a caption.</summary>
    Literal,

    /// <summary>A blank node: a dashed outline.</summary>
    BlankNode,

    /// <summary>An RDF 1.2 triple term: a frame holding three chips.</summary>
    Triple,
}

/// <summary>Whether a term or a statement is part of a change (docs/ui/README.md, change).</summary>
public enum Change
{
    /// <summary>Not part of a change.</summary>
    None,

    /// <summary>Asserted by the change: <c>added</c>, with a +.</summary>
    Added,

    /// <summary>Retracted by the change: <c>removed</c>, with a − and struck through.</summary>
    Removed,
}
