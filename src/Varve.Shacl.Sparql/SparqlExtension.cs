// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using Varve.Rdf;
using Varve.Shacl.Model;
using Varve.Sparql;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation;

namespace Varve.Shacl.Sparql;

/// <summary>SHACL-SPARQL as a <c>Varve.Shacl</c> extension (ADR 0148).</summary>
internal sealed class SparqlExtension(EvaluationOptions options) : IShaclExtension
{
    public IShaclExtensionScope Begin(ShapesGraphContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ShapesView view = new(context.Source, context.Graph);
        context.DeclareShapeParameter(Vocabulary.Sparql);

        List<Component> components = [];
        if (view.TryHandle(Vocabulary.ConstraintComponent, out TermHandle componentClass))
        {
            // §4.2: a SHACL instance of sh:ConstraintComponent, its subclasses read from the shapes graph.
            List<TermHandle> classes = [componentClass];
            for (int i = 0; i < classes.Count; i++)
            {
                foreach (TermHandle sub in view.Subjects(Vocabulary.RdfsSubClassOf, classes[i]))
                {
                    if (!classes.Exists(c => view.Source.TermComparer.Equals(c, sub)))
                    {
                        classes.Add(sub);
                    }
                }
            }

            List<TermHandle> declared = [];
            foreach (TermHandle cls in classes)
            {
                foreach (TermHandle instance in view.Subjects(Vocabulary.RdfType, cls))
                {
                    if (!declared.Exists(d => view.Source.TermComparer.Equals(d, instance)))
                    {
                        declared.Add(instance);
                    }
                }
            }

            foreach (TermHandle node in declared)
            {
                if (ReadComponent(view, node, context) is { } component)
                {
                    components.Add(component);
                    foreach (Parameter parameter in component.Parameters)
                    {
                        context.DeclareShapeParameter(parameter.Path);
                    }
                }
            }
        }

        return new Scope(view, components, options);
    }

    /// <summary>§4.2: a SPARQL-based constraint component, its parameters and validators.</summary>
    private static Component? ReadComponent(ShapesView view, TermHandle node, ShapesGraphContext context)
    {
        RdfTerm term = view.Term(node);
        List<Parameter> parameters = [];
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (TermHandle declaration in view.Objects(node, Vocabulary.Parameter))
        {
            RdfTerm? path = view.Object(declaration, Vocabulary.Path, out int count);
            if (count != 1 || path!.Kind != RdfTermKind.Iri)
            {
                context.Refuse(Problem(term, "each parameter declaration has exactly one value for sh:path, an IRI (§4.2.1)"));
                return null;
            }

            string name = LocalName(Encoding.UTF8.GetString(path.Lexical));
            if (name.Length == 0 || IsReserved(name) || !names.Add(name))
            {
                context.Refuse(Problem(term, "the parameter name \"" + name + "\" is not a usable SPARQL variable name, or is used twice (§4.2.1)"));
                return null;
            }

            RdfTerm? optional = view.Object(declaration, Vocabulary.Optional, out _);
            parameters.Add(new Parameter(path, name, optional is { Kind: RdfTermKind.Literal } && optional.Lexical.SequenceEqual("true"u8)));
        }

        if (!parameters.Exists(p => !p.Optional))
        {
            context.Refuse(Problem(term, "every constraint component has at least one non-optional parameter (§4.2.1)"));
            return null;
        }

        return new Component(node, term, parameters);
    }

    /// <summary>§4.2.1: the names a parameter cannot have.</summary>
    private static bool IsReserved(string name) => name is "this" or "path" or "PATH" or "value" or "message" or "failure";

    /// <summary>§4.2.1: the longest NCName at the end of the IRI.</summary>
    private static string LocalName(string iri)
    {
        int start = iri.Length;
        while (start > 0 && (char.IsLetterOrDigit(iri[start - 1]) || iri[start - 1] is '_' or '-' or '.' or '·'))
        {
            start--;
        }

        while (start < iri.Length && !(char.IsLetter(iri[start]) || iri[start] == '_'))
        {
            start++;
        }

        return iri[start..];
    }

    private static ShaclProblem Problem(RdfTerm node, string rule) =>
        new(ShaclProblemKind.ExtensionFailure, node, null, "SHACL-SPARQL: " + rule + ".");

    private sealed record Parameter(RdfTerm Path, string Name, bool Optional);

    private sealed record Component(TermHandle Node, RdfTerm Term, List<Parameter> Parameters);

    private sealed class Scope(ShapesView view, List<Component> components, EvaluationOptions options) : IShaclExtensionScope
    {
        public void Load(ShapeContext shape)
        {
            if (!shape.TryGetHandle(shape.Shape, out TermHandle node))
            {
                return;
            }

            string? path = null;
            if (shape.Path is { } pathTerm && shape.TryGetHandle(pathTerm, out TermHandle pathHandle))
            {
                path = QueryText.Path(view, pathHandle);
            }

            foreach (TermHandle constraint in view.Objects(node, Vocabulary.Sparql))
            {
                LoadSparqlConstraint(shape, constraint, path);
            }

            foreach (Component component in components)
            {
                LoadComponent(shape, node, component, path);
            }
        }

        /// <summary>§3.2: a SPARQL-based constraint.</summary>
        private void LoadSparqlConstraint(ShapeContext shape, TermHandle node, string? path)
        {
            RdfTerm term = view.Term(node);
            RdfTerm? select = view.Object(node, Vocabulary.Select, out int selects);
            if (selects != 1 || select!.Kind != RdfTermKind.Literal)
            {
                shape.Refuse(Problem(term, "a SPARQL-based constraint has exactly one value for sh:select, a string (§3.2)"));
                return;
            }

            if (view.Object(node, Vocabulary.Deactivated, out _) is { Kind: RdfTermKind.Literal } deactivated && deactivated.Lexical.SequenceEqual("true"u8))
            {
                return;
            }

            RdfTerm? severity = view.Object(node, Vocabulary.Severity, out int severities);
            if (severities > 1 || severity is { Kind: not RdfTermKind.Iri })
            {
                shape.Refuse(Problem(term, "a SPARQL-based constraint has at most one value for sh:severity, an IRI (§3.2)"));
                return;
            }

            List<RdfTerm> messages = Terms(view.Objects(node, Vocabulary.Message));
            if (Parse(shape, node, term, Encoding.UTF8.GetString(select.Lexical), path, ["this"], expectSelect: true) is { } query)
            {
                shape.Add(new SparqlConstraint(Vocabulary.SparqlConstraintComponent, term, severity, messages, query, [], ask: false, nodeShape: path is null, options));
            }
        }

        /// <summary>§4.3: a constraint of a SPARQL-based constraint component the shape uses.</summary>
        private void LoadComponent(ShapeContext shape, TermHandle node, Component component, string? path)
        {
            List<(Parameter Parameter, List<TermHandle> Values)> values = [];
            foreach (Parameter parameter in component.Parameters)
            {
                List<TermHandle> found = view.Objects(node, parameter.Path);
                if (found.Count == 0 && !parameter.Optional)
                {
                    return;
                }

                values.Add((parameter, found));
            }

            // §4.2.3: the validator for the kind of shape, else sh:validator; none means the constraint is ignored.
            List<TermHandle> select = view.Objects(component.Node, shape.Path is null ? Vocabulary.NodeValidator : Vocabulary.PropertyValidator);
            List<TermHandle> askValidators = view.Objects(component.Node, Vocabulary.Validator);
            bool ask = select.Count == 0;
            TermHandle? validator = select.Count > 0 ? select[0] : askValidators.Count > 0 ? askValidators[0] : null;
            if (validator is not TermHandle v)
            {
                return;
            }

            RdfTerm? text = view.Object(v, ask ? Vocabulary.Ask : Vocabulary.Select, out int count);
            if (count != 1 || text!.Kind != RdfTermKind.Literal)
            {
                shape.Refuse(Problem(component.Term, "a validator has exactly one value for sh:" + (ask ? "ask" : "select") + ", a string (§4.2.3)"));
                return;
            }

            List<RdfTerm> messages = Terms(view.Objects(v, Vocabulary.Message));
            if (messages.Count == 0)
            {
                messages = Terms(view.Objects(component.Node, Vocabulary.Message));
            }

            // Each value of a single parameter is one constraint; several parameters with several values are one constraint per combination.
            foreach (List<(string Name, RdfTerm Value)> binding in Combinations(values))
            {
                List<string> prebound = ["this"];
                foreach ((string name, RdfTerm _) in binding)
                {
                    prebound.Add(name);
                }

                if (ask)
                {
                    prebound.Add("value");
                }

                if (Parse(shape, v, component.Term, Encoding.UTF8.GetString(text.Lexical), path, prebound, expectSelect: !ask) is { } query)
                {
                    shape.Add(new SparqlConstraint(component.Term, null, null, messages, query, binding, ask, nodeShape: shape.Path is null, options));
                }
            }
        }

        private List<List<(string, RdfTerm)>> Combinations(List<(Parameter Parameter, List<TermHandle> Values)> values)
        {
            List<List<(string, RdfTerm)>> combinations = [[]];
            foreach ((Parameter parameter, List<TermHandle> found) in values)
            {
                if (found.Count == 0)
                {
                    continue;
                }

                List<List<(string, RdfTerm)>> next = [];
                foreach (List<(string, RdfTerm)> partial in combinations)
                {
                    foreach (TermHandle value in found)
                    {
                        next.Add([.. partial, (parameter.Name, view.Term(value))]);
                    }
                }

                combinations = next;
            }

            return combinations;
        }

        /// <summary>
        /// The query: prefixes, <c>$PATH</c> substituted for a property shape,
        /// parsed, and checked against Appendix A's restrictions. Any failure
        /// refuses the shapes graph, as SHACL-SPARQL requires a failure.
        /// </summary>
        private Query? Parse(ShapeContext shape, TermHandle node, RdfTerm owner, string text, string? path, List<string> prebound, bool expectSelect)
        {
            string? prefixes = QueryText.Prefixes(view, node, out string? problem);
            if (prefixes is null)
            {
                shape.Refuse(Problem(owner, problem!));
                return null;
            }

            string full = prefixes + (path is null ? text : QueryText.SubstitutePath(text, path));
            if (!SparqlParser.TryParseQuery(full.AsSpan(), new SparqlParseOptions(), out Query? query, out SparqlParseError error))
            {
                shape.Refuse(Problem(owner, "the query is not a valid SPARQL 1.2 query (§2): " + error));
                return null;
            }

            if (expectSelect ? query is not SelectQuery : query is not AskQuery)
            {
                shape.Refuse(Problem(owner, "the query is not a SPARQL " + (expectSelect ? "SELECT" : "ASK") + " query (§3.2, §4.2.3)"));
                return null;
            }

            if (PreBinding.Violation(query!, new HashSet<string>(prebound, StringComparer.Ordinal)) is { } violation)
            {
                shape.Refuse(Problem(owner, violation + " (Appendix A)"));
                return null;
            }

            return query;
        }

        private List<RdfTerm> Terms(List<TermHandle> handles) => handles.ConvertAll(view.Term);
    }
}
