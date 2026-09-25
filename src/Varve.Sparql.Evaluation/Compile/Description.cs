// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation.Execution;
using Varve.Sparql.Evaluation.Operators;

namespace Varve.Sparql.Evaluation.Compile;

/// <summary>
/// <c>DESCRIBE</c> (§16.4, informative; <c>sparql-evaluation.md</c> §9.3): a
/// minimal description — each resource's triples in the default graph with it
/// as subject, closed over blank-node objects. The concise bounded description
/// (its reification clause and the inverse direction) is out of scope.
/// </summary>
internal static class Description
{
    internal static IEnumerator<(RdfTerm, RdfTerm, RdfTerm)> Describe(Exec exec, IEnumerator<ulong[]> solutions, AlgebraList<PatternTerm> resources, int[] slots)
    {
        List<TermRef> targets = [];
        HashSet<TermRef> known = new(new TermRefComparer(exec));
        using (solutions)
        {
            while (solutions.MoveNext())
            {
                for (int i = 0; i < resources.Count; i++)
                {
                    TermRef value = slots[i] >= 0
                        ? Rows.Get(solutions.Current, exec.Width, slots[i])
                        : resources[i] is TermPattern constant ? exec.Intern(constant.Term) : TermRef.Unbound;
                    if (value.IsBound && known.Add(value))
                    {
                        targets.Add(value);
                    }
                }
            }
        }

        HashSet<(RdfTerm, RdfTerm, RdfTerm)> seen = [];
        HashSet<TermHandle> described = new(exec.Comparer);
        Stack<TermHandle> pending = new();
        foreach (TermRef target in targets)
        {
            if (exec.TryGetSourceHandle(target, out TermHandle handle) && described.Add(handle))
            {
                pending.Push(handle);
            }
        }

        ScanCursor scan = new();
        while (pending.Count > 0)
        {
            exec.Check();
            TermHandle subject = pending.Pop();
            if (!scan.Open(exec, subject, default, default, ActiveGraph.Default, []))
            {
                continue;
            }

            List<Quad> quads = [];
            while (scan.MoveNext())
            {
                quads.Add(scan.Current);
            }

            scan.Close();
            foreach (Quad quad in quads)
            {
                RdfTerm s = exec.Materialise(new TermRef(quad.Subject.Value, false));
                RdfTerm p = exec.Materialise(new TermRef(quad.Predicate.Value, false));
                RdfTerm o = exec.Materialise(new TermRef(quad.Object.Value, false));
                if (seen.Add((s, p, o)))
                {
                    yield return (s, p, o);
                }

                if (o.Kind == RdfTermKind.BlankNode && described.Add(quad.Object))
                {
                    pending.Push(quad.Object);
                }
            }
        }
    }
}
