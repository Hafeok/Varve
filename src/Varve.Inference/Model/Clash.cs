// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Rdf;

namespace Varve.Inference.Model;

/// <summary>
/// One firing of a check, a rule whose head is <c>false</c> (ADR 0197 point
/// 5): the check, named by its IRI, and the facts of the closure it matched.
/// </summary>
public sealed class Clash
{
    /// <summary>A clash of <paramref name="rule"/> over <paramref name="premises"/>.</summary>
    public Clash(RdfTerm rule, IReadOnlyList<Fact> premises)
    {
        Rule = rule;
        Premises = premises;
    }

    /// <summary>The check that fired: <c>https://w3id.org/varve/ns#rule/owl2rl/cax-dw</c>, <c>…/rdfs/dt-not-type</c>.</summary>
    public RdfTerm Rule { get; }

    /// <summary>The facts it matched, in the order of its body.</summary>
    public IReadOnlyList<Fact> Premises { get; }
}
