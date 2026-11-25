// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Inference.Model;
using Varve.Inference.Store.Model;
using Varve.Rdf;

namespace Varve.Inference.Store;

/// <summary>
/// Explain over a binding's closures at one position (ADR 0199): a quad in a
/// data graph or in the binding's TBox conclusions, its proof from the
/// closure its row is drawn from, in terms, with the graph of each node, and
/// that closure's clashes.
/// </summary>
[Contract(typeof(Explain.WellFoundedProof), Role = "a quad's proof, in terms")]
internal sealed class Explainer(Materialisation state, IQuadSource view, InferenceBinding binding)
{
    /// <summary>The explanation of <paramref name="subject"/> <paramref name="predicate"/> <paramref name="object"/> in <paramref name="graph"/>, the TBox conclusions' graph or a data graph, null the default graph.</summary>
    internal Explanation Explain(RdfTerm? graph, RdfTerm subject, RdfTerm predicate, RdfTerm @object)
    {
        TermHandle placed;
        if (graph is null)
        {
            placed = TermHandle.None;
        }
        else if (graph.Equals(InferenceProjection.InferredTBox(binding.Binding.Lexical)))
        {
            placed = state.TBoxGraph;
        }
        else if (!view.TryInternalise(graph, out placed))
        {
            return new Explanation(null, []);
        }

        Closure? closure = state.ClosureOf(placed);
        if (closure is null || !state.Terms.TryHandle(subject, out TermHandle s) || !state.Terms.TryHandle(predicate, out TermHandle p) || !state.Terms.TryHandle(@object, out TermHandle o))
        {
            return new Explanation(null, closure is null ? [] : Clashes(closure, placed));
        }

        Fact fact = new(s, p, o);
        ExplainedQuad? proof = null;
        if (placed != state.TBoxGraph && view.Contains(new Quad(s, p, o, placed)))
        {
            proof = new ExplainedQuad(subject, predicate, @object, graph, ProofStatus.Explicit, null, []);
        }
        else if (state.IsRow(placed, fact) && closure.Explain(fact) is { } found)
        {
            proof = Node(found, placed);
        }

        return new Explanation(proof, Clashes(closure, placed));
    }

    private List<ExplainedClash> Clashes(Closure closure, TermHandle placed)
    {
        List<ExplainedClash> clashes = [];
        foreach (Clash clash in closure.Clashes())
        {
            List<ExplainedQuad> premises = [];
            foreach (Fact premise in clash.Premises)
            {
                if (closure.Explain(premise) is { } proof)
                {
                    premises.Add(Node(proof, placed));
                }
            }

            clashes.Add(new ExplainedClash(clash.Rule, premises));
        }

        return clashes;
    }

    /// <summary>A proof's node in terms: an asserted premise in the graph that asserts it, a TBox conclusion in the binding's conclusions' graph, any other in the graph explained.</summary>
    private ExplainedQuad Node(Proof proof, TermHandle placed)
    {
        Fact fact = proof.Fact;
        RdfTerm subject = Name(fact.Subject), predicate = Name(fact.Predicate), @object = Name(fact.Object);
        List<ExplainedQuad> premises = [];
        foreach (Proof premise in proof.Premises)
        {
            premises.Add(Node(premise, placed));
        }

        RdfTerm? graph = proof.Status switch
        {
            ProofStatus.Axiom => null,
            ProofStatus.Explicit => Asserting(fact, placed),
            _ => placed == state.TBoxGraph || state.InTBox(fact) ? InferenceProjection.InferredTBox(binding.Binding.Lexical) : NameOrNull(placed),
        };

        return new ExplainedQuad(subject, predicate, @object, graph, proof.Status, proof.Rule, premises);
    }

    /// <summary>The graph that asserts an explicit premise: the graph explained, or else one of the TBox graphs.</summary>
    private RdfTerm? Asserting(Fact fact, TermHandle placed)
    {
        if (placed != state.TBoxGraph && view.Contains(new Quad(fact.Subject, fact.Predicate, fact.Object, placed)))
        {
            return placed.IsNone ? null : Name(placed);
        }

        foreach (RdfTerm tbox in binding.TBoxGraphs)
        {
            if (view.TryInternalise(tbox, out TermHandle handle) && view.Contains(new Quad(fact.Subject, fact.Predicate, fact.Object, handle)))
            {
                return tbox;
            }
        }

        return null;
    }

    private RdfTerm? NameOrNull(TermHandle handle) => handle.IsNone ? null : Name(handle);

    private RdfTerm Name(TermHandle handle) =>
        state.Terms.TryExternalise(handle, out RdfTerm? term) ? term : throw new System.InvalidOperationException("A proof names a handle neither the dataset nor the projection knows.");
}
