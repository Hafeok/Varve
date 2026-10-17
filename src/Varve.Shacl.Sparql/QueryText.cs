// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Text;
using Varve.Rdf;

namespace Varve.Shacl.Sparql;

/// <summary>
/// The text of a SHACL-SPARQL query before it is parsed: its PREFIX lines
/// (SHACL 1.2 SPARQL-Related Features §2) and <c>$PATH</c> replaced by the
/// property shape's path in SPARQL syntax (§3.3).
/// </summary>
internal static class QueryText
{
    /// <summary>
    /// The prefix declarations for a constraint or validator: the values of
    /// <c>sh:prefixes/sh:declare</c> (<c>owl:imports</c> not followed, ADR
    /// 0151), or, with no <c>sh:prefixes</c>, every <c>sh:declare</c> at an
    /// ontology, data graph or shapes graph node. Null when one prefix is
    /// declared with two namespaces, which makes the shapes graph ill-formed.
    /// </summary>
    internal static string? Prefixes(ShapesView view, TermHandle node, out string? problem)
    {
        problem = null;
        List<TermHandle> declarers = [];
        List<TermHandle> prefixes = view.Objects(node, Vocabulary.Prefixes);
        if (prefixes.Count > 0)
        {
            // sh:prefixes/(^owl:versionIRI)?/(owl:imports/(^owl:versionIRI)?)*/sh:declare: a path over the
            // shapes graph's own triples. Nothing is fetched (ADR 0151); an import the graph does not hold adds nothing.
            HashSet<TermHandle> visited = new(view.Source.TermComparer);
            Queue<TermHandle> pending = new(prefixes);
            while (pending.Count > 0)
            {
                TermHandle current = pending.Dequeue();
                List<TermHandle> step = [current, .. view.Subjects(Vocabulary.OwlVersionIri, current)];
                foreach (TermHandle graph in step)
                {
                    if (!visited.Add(graph))
                    {
                        continue;
                    }

                    declarers.Add(graph);
                    foreach (TermHandle imported in view.Objects(graph, Vocabulary.OwlImports))
                    {
                        pending.Enqueue(imported);
                    }
                }
            }
        }
        else
        {
            foreach (RdfTerm type in (RdfTerm[])[Vocabulary.OwlOntology, Vocabulary.ShapesGraph, Vocabulary.DataGraph, Vocabulary.RulesGraph])
            {
                if (view.TryHandle(type, out TermHandle t))
                {
                    declarers.AddRange(view.Subjects(Vocabulary.RdfType, t));
                }
            }
        }

        Dictionary<string, string> mappings = new(System.StringComparer.Ordinal);
        foreach (TermHandle declarer in declarers)
        {
            foreach (TermHandle declaration in view.Objects(declarer, Vocabulary.Declare))
            {
                RdfTerm? prefix = view.Object(declaration, Vocabulary.Prefix, out int prefixCount);
                RdfTerm? ns = view.Object(declaration, Vocabulary.Namespace, out int namespaceCount);
                if (prefixCount != 1 || namespaceCount != 1 || prefix!.Kind != RdfTermKind.Literal || ns!.Kind != RdfTermKind.Literal)
                {
                    problem = "a prefix declaration has exactly one sh:prefix and one sh:namespace, both literals (SHACL 1.2 SPARQL-Related Features §2)";
                    return null;
                }

                string p = Encoding.UTF8.GetString(prefix.Lexical), n = Encoding.UTF8.GetString(ns.Lexical);
                if (mappings.TryGetValue(p, out string? existing) && existing != n)
                {
                    problem = "the prefix \"" + p + "\" is declared with two namespaces (SHACL 1.2 SPARQL-Related Features §2)";
                    return null;
                }

                mappings[p] = n;
            }
        }

        List<string> keys = [.. mappings.Keys];
        keys.Sort(System.StringComparer.Ordinal);
        StringBuilder text = new();
        foreach (string key in keys)
        {
            text.Append("PREFIX ").Append(key).Append(": <").Append(mappings[key]).Append(">\n");
        }

        return text.ToString();
    }

    /// <summary>The SHACL path at <paramref name="path"/> in SPARQL property path syntax, or null when it is not a path.</summary>
    internal static string? Path(ShapesView view, TermHandle path)
    {
        RdfTerm term = view.Term(path);
        if (term.Kind == RdfTermKind.Iri)
        {
            return "<" + Encoding.UTF8.GetString(term.Lexical) + ">";
        }

        if (view.List(path) is { Count: >= 2 } sequence)
        {
            return Join(view, sequence, "/");
        }

        foreach ((RdfTerm predicate, string format) in ((RdfTerm, string)[])
        [
            (Vocabulary.InversePath, "^({0})"),
            (Vocabulary.ZeroOrMorePath, "({0})*"),
            (Vocabulary.OneOrMorePath, "({0})+"),
            (Vocabulary.ZeroOrOnePath, "({0})?"),
        ])
        {
            List<TermHandle> inner = view.Objects(path, predicate);
            if (inner.Count == 1 && Path(view, inner[0]) is { } text)
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, format, text);
            }
        }

        List<TermHandle> alternative = view.Objects(path, Vocabulary.AlternativePath);
        return alternative.Count == 1 && view.List(alternative[0]) is { Count: >= 2 } alternatives ? Join(view, alternatives, "|") : null;
    }

    /// <summary>
    /// Replaces the variable <c>PATH</c> (<c>$PATH</c> or <c>?PATH</c>) with
    /// <paramref name="path"/>. The only legal use of <c>PATH</c> is the
    /// predicate of a triple pattern (§3.2); anywhere else, the substituted text
    /// does not parse, which refuses the shapes graph.
    /// </summary>
    internal static string SubstitutePath(string query, string path)
    {
        StringBuilder text = new(query.Length + path.Length);
        for (int i = 0; i < query.Length; i++)
        {
            if (query[i] is '$' or '?' && string.CompareOrdinal(query, i + 1, "PATH", 0, 4) == 0
                && (i + 5 >= query.Length || !IsNameChar(query[i + 5])))
            {
                text.Append('(').Append(path).Append(')');
                i += 4;
            }
            else
            {
                text.Append(query[i]);
            }
        }

        return text.ToString();

        static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '·';
    }

    private static string? Join(ShapesView view, List<TermHandle> members, string separator)
    {
        StringBuilder text = new();
        for (int i = 0; i < members.Count; i++)
        {
            if (Path(view, members[i]) is not { } member)
            {
                return null;
            }

            text.Append(i == 0 ? string.Empty : separator).Append('(').Append(member).Append(')');
        }

        return text.ToString();
    }
}
