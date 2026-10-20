// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Rdf;
using Varve.Shacl.Model;

namespace Varve.Shacl.Store.Model;

/// <summary>A binding checked against its dataset at apply time (ADR 0169's R4): its classification, or why it is refused.</summary>
public sealed class BindingCheck
{
    internal BindingCheck(ShapesClassification? classification, IReadOnlyList<ShaclProblem> problems, IReadOnlyList<RdfTerm>? refusal)
    {
        Classification = classification;
        Problems = problems;
        Refusal = refusal;
    }

    /// <summary>The shapes' classification, served with the binding; null when the shapes graph is refused.</summary>
    public ShapesClassification? Classification { get; }

    /// <summary>Why the shapes graph is refused; empty when it loads.</summary>
    public IReadOnlyList<ShaclProblem> Problems { get; }

    /// <summary>The refusal as a validator's report — a problem type and its members (ADR 0145's form) — or null when the binding may be applied.</summary>
    public IReadOnlyList<RdfTerm>? Refusal { get; }

    /// <summary>Whether the binding may be applied.</summary>
    public bool Accepted => Refusal is null;
}
