// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Rdf;

namespace Varve.Inference.Model;

/// <summary>What a fact is in a closure (ADR 0199 point 2).</summary>
public enum ProofStatus
{
    /// <summary>Asserted: a leaf of every proof.</summary>
    Explicit,

    /// <summary>An axiom of the profile: a leaf of every proof.</summary>
    Axiom,

    /// <summary>Concluded by a rule from premises proved before it.</summary>
    Implicit,
}

/// <summary>
/// A well-founded proof of a fact of a closure (ADR 0199): the fact, and for
/// an implicit one the rule that concludes it and a proof of each premise. No
/// fact's proof uses that fact, so the tree is finite and each node is proved
/// from below.
/// </summary>
public sealed class Proof
{
    /// <summary>A proof of <paramref name="fact"/>.</summary>
    public Proof(Fact fact, ProofStatus status, RdfTerm? rule, IReadOnlyList<Proof> premises)
    {
        Fact = fact;
        Status = status;
        Rule = rule;
        Premises = premises;
    }

    /// <summary>The fact proved.</summary>
    public Fact Fact { get; }

    /// <summary>Whether it is asserted, an axiom, or concluded.</summary>
    public ProofStatus Status { get; }

    /// <summary>The rule's IRI, for an implicit fact: <c>https://w3id.org/varve/ns#rule/rdfs/rdfs9</c>, <c>…/owl2rl/cax-sco</c>.</summary>
    public RdfTerm? Rule { get; }

    /// <summary>A proof of each of the rule's premises, in the rule's order; none for a leaf.</summary>
    public IReadOnlyList<Proof> Premises { get; }
}
