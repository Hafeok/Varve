// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Rdf;

namespace Varve.Inference.Model;

/// <summary>
/// A node of an explained proof, in terms (ADR 0199 point 2): a quad, what it
/// is, and for an implicit one the rule and a proof of each premise.
/// </summary>
public sealed class ExplainedQuad
{
    /// <summary>A node for the quad <paramref name="subject"/> <paramref name="predicate"/> <paramref name="object"/> in <paramref name="graph"/>.</summary>
    public ExplainedQuad(RdfTerm subject, RdfTerm predicate, RdfTerm @object, RdfTerm? graph, ProofStatus status, RdfTerm? rule, IReadOnlyList<ExplainedQuad> premises)
    {
        Subject = subject;
        Predicate = predicate;
        Object = @object;
        Graph = graph;
        Status = status;
        Rule = rule;
        Premises = premises;
    }

    /// <summary>The subject.</summary>
    public RdfTerm Subject { get; }

    /// <summary>The predicate.</summary>
    public RdfTerm Predicate { get; }

    /// <summary>The object.</summary>
    public RdfTerm Object { get; }

    /// <summary>The graph the quad is in: a data graph, a TBox graph an explicit premise is asserted in, the binding's TBox conclusions; null for an axiom and for the default graph.</summary>
    public RdfTerm? Graph { get; }

    /// <summary>Whether it is asserted, an axiom, or concluded.</summary>
    public ProofStatus Status { get; }

    /// <summary>The rule's IRI, for an implicit quad.</summary>
    public RdfTerm? Rule { get; }

    /// <summary>A proof of each premise, for an implicit quad.</summary>
    public IReadOnlyList<ExplainedQuad> Premises { get; }
}

/// <summary>A clash in the closure an explanation is drawn from: the check that fired, and a proof of each premise it matched.</summary>
public sealed class ExplainedClash
{
    /// <summary>A clash of <paramref name="rule"/> over <paramref name="premises"/>.</summary>
    public ExplainedClash(RdfTerm rule, IReadOnlyList<ExplainedQuad> premises)
    {
        Rule = rule;
        Premises = premises;
    }

    /// <summary>The check's IRI.</summary>
    public RdfTerm Rule { get; }

    /// <summary>A proof of each premise.</summary>
    public IReadOnlyList<ExplainedQuad> Premises { get; }
}

/// <summary>
/// What explain answers for a quad at a position (ADR 0199): its proof when
/// it is in the closure, none when it is not, and the clashes of the closure
/// it was looked up in.
/// </summary>
public sealed class Explanation
{
    /// <summary>An explanation: <paramref name="proof"/>, or none for a quad not in the closure; and <paramref name="clashes"/>.</summary>
    public Explanation(ExplainedQuad? proof, IReadOnlyList<ExplainedClash> clashes)
    {
        Proof = proof;
        Clashes = clashes;
    }

    /// <summary>The quad's proof; null when the closure does not hold it there, and it is <c>absent</c>.</summary>
    public ExplainedQuad? Proof { get; }

    /// <summary>The clashes of the closure the quad was looked up in.</summary>
    public IReadOnlyList<ExplainedClash> Clashes { get; }
}
