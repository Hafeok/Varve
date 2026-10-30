// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Management.Model;
using Varve.Rdf;

namespace Varve.Management;

/// <summary>
/// ADR 0234 point 4's apply-time preconditions over one dataset's grants and
/// bindings, which compare declarations and so stay in code (ADR 0175): a
/// grant with subject terms covers the whole of every classifier binding it
/// meets and meets one; classifier bindings do not overlap; and no gate
/// whose refusal could read another row runs over a classified graph a
/// row-restricted writer writes.
/// </summary>
[Contract(typeof(SubjectScopedGrantsAndTheClassifierBinding.RowGrantCoversTheWholeBinding), Role = "the row preconditions over a dataset's declarations")]
internal static class RowPreconditions
{
    internal static IEnumerable<DeclarationFinding> Check(RdfTerm graph, DatasetDeclaration dataset)
    {
        List<BindingDeclaration> classifiers = [];
        List<BindingDeclaration> gates = [];

        foreach (BindingDeclaration binding in dataset.Bindings)
        {
            if (binding.Kind == BindingKind.Classifier && binding.Classifies is not null)
            {
                classifiers.Add(binding);
            }
            else if (binding.Kind == BindingKind.Shapes && binding.Mode is ShapesMode.Reject or ShapesMode.Report && binding.Validates is not null)
            {
                gates.Add(binding);
            }
        }

        for (int i = 0; i < classifiers.Count; i++)
        {
            for (int j = i + 1; j < classifiers.Count; j++)
            {
                if (Meets(classifiers[i].Classifies!, classifiers[j].Classifies!))
                {
                    yield return new DeclarationFinding(graph, classifiers[j].Node, ManagementVocabulary.Graph, $"two classifier bindings classify a graph in common, {Name(classifiers[i].Node)} and {Name(classifiers[j].Node)}: a graph has at most one classification (ADR 0234, P4)");
                }
            }
        }

        foreach (GrantDeclaration grant in dataset.Grants)
        {
            if (grant.Subjects.All)
            {
                continue;
            }

            bool meetsOne = false;

            foreach (BindingDeclaration binding in classifiers)
            {
                if (!Meets(grant.Graphs, binding.Classifies!))
                {
                    continue;
                }

                meetsOne = true;

                if (!Contains(grant.Graphs, binding.Classifies!))
                {
                    yield return new DeclarationFinding(graph, grant.Node, ManagementVocabulary.Graph, $"a grant with subject terms covers every graph of the classifier binding {Name(binding.Node)} it meets, so that every input of a classification it can see is in a graph it reads (ADR 0234, P1)");
                }
            }

            if (!meetsOne)
            {
                yield return new DeclarationFinding(graph, grant.Node, ManagementVocabulary.Subject, "a grant with subject terms covers the graphs of a classifier binding; this one meets none, so its subject terms would mean nothing (ADR 0234, P3)");
            }
        }

        foreach (BindingDeclaration shapes in gates)
        {
            foreach (BindingDeclaration classifier in classifiers)
            {
                if (!Meets(shapes.Validates!, classifier.Classifies!))
                {
                    continue;
                }

                foreach (GrantDeclaration grant in dataset.Grants)
                {
                    if (grant.Access == GrantAccess.Write && !grant.Subjects.All && Meets(grant.Graphs, shapes.Validates!))
                    {
                        yield return new DeclarationFinding(graph, shapes.Node, ManagementVocabulary.ValidationMode, $"the shapes binding {Name(shapes.Node)} gates commits to graphs the classifier binding {Name(classifier.Node)} classifies, which the row-restricted write grant {Name(grant.Node)} writes: its refusal could depend on a row the writer cannot read (ADR 0234, P5)");
                    }
                }
            }
        }
    }

    /// <summary>Whether two graph sets share a graph, or could: set intersection over every graph, the default graph, explicit graphs, prefixes and templates.</summary>
    internal static bool Meets(GraphSet a, GraphSet b)
    {
        if (a.IsEmpty || b.IsEmpty)
        {
            return false;
        }

        if (a.All || b.All || (a.DefaultGraph && b.DefaultGraph) || a.Templates.Count > 0 || b.Templates.Count > 0)
        {
            return true;
        }

        foreach (RdfTerm graph in a.Graphs)
        {
            if (Holds(b, graph))
            {
                return true;
            }
        }

        foreach (RdfTerm graph in b.Graphs)
        {
            if (Holds(a, graph))
            {
                return true;
            }
        }

        foreach (string p in a.Prefixes)
        {
            foreach (string q in b.Prefixes)
            {
                if (p.StartsWith(q, StringComparison.Ordinal) || q.StartsWith(p, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether <paramref name="outer"/> holds every graph <paramref name="inner"/> can name.</summary>
    internal static bool Contains(GraphSet outer, GraphSet inner)
    {
        if (outer.All)
        {
            return true;
        }

        if (inner.All || inner.Templates.Count > 0 || outer.Templates.Count > 0 || (inner.DefaultGraph && !outer.DefaultGraph))
        {
            return false;
        }

        foreach (RdfTerm graph in inner.Graphs)
        {
            if (!Holds(outer, graph))
            {
                return false;
            }
        }

        foreach (string p in inner.Prefixes)
        {
            if (!StartsWithAny(outer.Prefixes, p))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Holds(GraphSet set, RdfTerm graph)
    {
        if (set.All)
        {
            return true;
        }

        foreach (RdfTerm named in set.Graphs)
        {
            if (named.Equals(graph))
            {
                return true;
            }
        }

        return StartsWithAny(set.Prefixes, Encoding.UTF8.GetString(graph.Lexical));
    }

    private static bool StartsWithAny(IReadOnlyList<string> prefixes, string value)
    {
        foreach (string prefix in prefixes)
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    [DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    private static string Name(RdfTerm node) => node.Kind == RdfTermKind.Iri ? "<" + Encoding.UTF8.GetString(node.Lexical) + ">" : "a blank node";
}
