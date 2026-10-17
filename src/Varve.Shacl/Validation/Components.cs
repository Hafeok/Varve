// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Varve.Rdf;
using Varve.Shacl.Model;
using Varve.Shacl.Shapes;
using Varve.Xsd;

namespace Varve.Shacl.Validation;

// The core constraint components of SHACL 1.2 Core §7, one class each, every
// validator its textual definition. Section numbers are the Working Draft of
// 14 October 2026 (ADR 0149).

/// <summary>§7.1.1 <c>sh:class</c>: a list is a union of classes.</summary>
internal sealed class ClassConstraint(ImmutableArray<RdfTerm> classes) : Constraint(Vocabulary.ClassConstraintComponent)
{
    internal override void Validate(ValidationRun run, Shape shape, Node focus, IReadOnlyList<Node> values, ResultSink sink)
    {
        foreach (Node value in values)
        {
            bool ok = false;
            if (run.Data.Term(value).Kind != RdfTermKind.Literal)
            {
                foreach (RdfTerm cls in classes)
                {
                    if (run.IsInstanceOf(value, run.Data.Resolve(cls)))
                    {
                        ok = true;
                        break;
                    }
                }
            }

            if (!ok)
            {
                sink.Add(shape, this, focus, value);
            }
        }
    }
}

/// <summary>§7.1.2 <c>sh:datatype</c>.</summary>
internal sealed class DatatypeConstraint(ImmutableArray<RdfTerm> datatypes) : Constraint(Vocabulary.DatatypeConstraintComponent)
{
    internal override void Validate(ValidationRun run, Shape shape, Node focus, IReadOnlyList<Node> values, ResultSink sink)
    {
        foreach (Node value in values)
        {
            RdfTerm term = run.Data.Term(value);
            bool ok = false;
            foreach (RdfTerm datatype in datatypes)
            {
                if (Literals.Matches(term, datatype))
                {
                    ok = true;
                    break;
                }
            }

            if (!ok)
            {
                sink.Add(shape, this, focus, value);
            }
        }
    }
}

/// <summary>§7.1.3 <c>sh:nodeKind</c>.</summary>
internal sealed class NodeKindConstraint(ImmutableArray<RdfTerm> kinds) : Constraint(Vocabulary.NodeKindConstraintComponent)
{
    internal override void Validate(ValidationRun run, Shape shape, Node focus, IReadOnlyList<Node> values, ResultSink sink)
    {
        foreach (Node value in values)
        {
            RdfTermKind kind = run.Data.Term(value).Kind;
            bool ok = false;
            foreach (RdfTerm allowed in kinds)
            {
                if (Matches(kind, allowed))
                {
                    ok = true;
                    break;
                }
            }

            if (!ok)
            {
                sink.Add(shape, this, focus, value);
            }
        }
    }

    internal static bool IsNodeKind(RdfTerm term) =>
        term.Equals(Vocabulary.IRI) || term.Equals(Vocabulary.BlankNode) || term.Equals(Vocabulary.Literal)
        || term.Equals(Vocabulary.BlankNodeOrIRI) || term.Equals(Vocabulary.BlankNodeOrLiteral)
        || term.Equals(Vocabulary.IRIOrLiteral) || term.Equals(Vocabulary.TripleTerm);

    private static bool Matches(RdfTermKind kind, RdfTerm allowed) => kind switch
    {
        RdfTermKind.Iri => allowed.Equals(Vocabulary.IRI) || allowed.Equals(Vocabulary.BlankNodeOrIRI) || allowed.Equals(Vocabulary.IRIOrLiteral),
        RdfTermKind.BlankNode => allowed.Equals(Vocabulary.BlankNode) || allowed.Equals(Vocabulary.BlankNodeOrIRI) || allowed.Equals(Vocabulary.BlankNodeOrLiteral),
        RdfTermKind.Literal => allowed.Equals(Vocabulary.Literal) || allowed.Equals(Vocabulary.BlankNodeOrLiteral) || allowed.Equals(Vocabulary.IRIOrLiteral),
        _ => allowed.Equals(Vocabulary.TripleTerm),
    };
}

/// <summary>§7.2.1 and §7.2.2: <c>sh:minCount</c> and <c>sh:maxCount</c>.</summary>
internal sealed class CountConstraint(bool minimum, long bound)
    : Constraint(minimum ? Vocabulary.MinCountConstraintComponent : Vocabulary.MaxCountConstraintComponent)
{
    internal override void Validate(ValidationRun run, Shape shape, Node focus, IReadOnlyList<Node> values, ResultSink sink)
    {
        if (minimum ? values.Count < bound : values.Count > bound)
        {
            sink.Add(shape, this, focus, null);
        }
    }
}

/// <summary>§7.3: the four value range components, by SPARQL's operators.</summary>
internal sealed class RangeConstraint(RdfTerm component, RdfTerm bound, bool lower, bool inclusive) : Constraint(component)
{
    internal override void Validate(ValidationRun run, Shape shape, Node focus, IReadOnlyList<Node> values, ResultSink sink)
    {
        foreach (Node value in values)
        {
            // $min < v, $min <= v, $max > v, $max >= v: v compared with the bound.
            PartialOrdering order = Literals.Compare(run.Data.Term(value), bound);
            bool ok = lower
                ? order == PartialOrdering.Greater || (inclusive && order == PartialOrdering.Equal)
                : order == PartialOrdering.Less || (inclusive && order == PartialOrdering.Equal);
            if (!ok)
            {
                sink.Add(shape, this, focus, value);
            }
        }
    }
}

/// <summary>§7.4.1 and §7.4.2: <c>sh:minLength</c> and <c>sh:maxLength</c>, SPARQL's <c>STRLEN</c> of <c>str</c>.</summary>
internal sealed class LengthConstraint(bool minimum, long bound)
    : Constraint(minimum ? Vocabulary.MinLengthConstraintComponent : Vocabulary.MaxLengthConstraintComponent)
{
    internal override void Validate(ValidationRun run, Shape shape, Node focus, IReadOnlyList<Node> values, ResultSink sink)
    {
        foreach (Node value in values)
        {
            RdfTerm term = run.Data.Term(value);
            bool ok = term.Kind is RdfTermKind.Iri or RdfTermKind.Literal
                && (minimum ? Strings.Length(term) >= bound : Strings.Length(term) <= bound);
            if (!ok)
            {
                sink.Add(shape, this, focus, value);
            }
        }
    }
}

/// <summary>§7.4.3 <c>sh:pattern</c> and <c>sh:flags</c>, by <c>Varve.Xsd</c>'s XPath translation (ADR 0153).</summary>
internal sealed class PatternConstraint(XPathRegexTranslation translation) : Constraint(Vocabulary.PatternConstraintComponent)
{
    internal override void Validate(ValidationRun run, Shape shape, Node focus, IReadOnlyList<Node> values, ResultSink sink)
    {
        Regex regex = run.Pattern(shape, translation);
        foreach (Node value in values)
        {
            RdfTerm term = run.Data.Term(value);
            bool ok;
            if (term.Kind is not (RdfTermKind.Iri or RdfTermKind.Literal))
            {
                ok = false;
            }
            else
            {
                try
                {
                    ok = regex.IsMatch(Encoding.UTF8.GetString(term.Lexical));
                }
                catch (RegexMatchTimeoutException)
                {
                    // ADR 0151: a pattern that runs past the timeout fails the validation.
                    throw new ShaclFailureException(new ShaclProblem(
                        ShaclProblemKind.PatternTimeout,
                        shape.Node,
                        run.Data.Term(focus),
                        "sh:pattern (SHACL 1.2 Core §7.4.3) exceeded ShaclOptions.RegexTimeout on one value."));
                }
            }

            if (!ok)
            {
                sink.Add(shape, this, focus, value);
            }
        }
    }
}

/// <summary>§7.4.4 <c>sh:singleLine</c>.</summary>
internal sealed class SingleLineConstraint() : Constraint(Vocabulary.SingleLineConstraintComponent)
{
    internal override void Validate(ValidationRun run, Shape shape, Node focus, IReadOnlyList<Node> values, ResultSink sink)
    {
        foreach (Node value in values)
        {
            RdfTerm term = run.Data.Term(value);
            if (term.Kind == RdfTermKind.Literal && term.Lexical.IndexOfAny("\n\v\f\r"u8) >= 0)
            {
                sink.Add(shape, this, focus, value);
            }
        }
    }
}

/// <summary>§7.4.5 <c>sh:languageIn</c>, by SPARQL's <c>langMatches</c> (RFC 4647 basic filtering).</summary>
internal sealed class LanguageInConstraint(ImmutableArray<string> ranges) : Constraint(Vocabulary.LanguageInConstraintComponent)
{
    internal override void Validate(ValidationRun run, Shape shape, Node focus, IReadOnlyList<Node> values, ResultSink sink)
    {
        foreach (Node value in values)
        {
            RdfTerm term = run.Data.Term(value);
            bool ok = false;
            if (term.Kind == RdfTermKind.Literal && !term.Language.IsEmpty)
            {
                string tag = Encoding.UTF8.GetString(term.Language);
                foreach (string range in ranges)
                {
                    if (Strings.LangMatches(tag, range))
                    {
                        ok = true;
                        break;
                    }
                }
            }

            if (!ok)
            {
                sink.Add(shape, this, focus, value);
            }
        }
    }
}

/// <summary>§7.4.6 <c>sh:uniqueLang</c>: one result per language tag (with its direction) used twice.</summary>
internal sealed class UniqueLangConstraint() : Constraint(Vocabulary.UniqueLangConstraintComponent)
{
    internal override void Validate(ValidationRun run, Shape shape, Node focus, IReadOnlyList<Node> values, ResultSink sink)
    {
        Dictionary<string, int> uses = new(StringComparer.Ordinal);
        List<string> order = [];
        foreach (Node value in values)
        {
            RdfTerm term = run.Data.Term(value);
            if (term.Kind != RdfTermKind.Literal || term.Language.IsEmpty)
            {
                continue;
            }

            // Language tags compare case-insensitively (BCP 47); the direction counts (1.2).
            string key = Encoding.UTF8.GetString(term.Language).ToLowerInvariant() + "--" + term.Direction;
            if (uses.TryGetValue(key, out int count))
            {
                uses[key] = count + 1;
            }
            else
            {
                uses[key] = 1;
                order.Add(key);
            }
        }

        foreach (string key in order)
        {
            if (uses[key] > 1)
            {
                sink.Add(shape, this, focus, null);
            }
        }
    }
}

/// <summary>§7.5: the four list components; a value node that is not a SHACL list is a result for each.</summary>
internal sealed class ListConstraint(RdfTerm component, int? memberShape, long? minLength, long? maxLength, bool uniqueMembers) : Constraint(component)
{
    internal override IEnumerable<ShapeReference> References =>
        memberShape is int s ? [new ShapeReference(s, Vocabulary.MemberShape, Polarity.Positive)] : [];

    internal override void Validate(ValidationRun run, Shape shape, Node focus, IReadOnlyList<Node> values, ResultSink sink)
    {
        foreach (Node value in values)
        {
            List<Node>? members = Lists.Members(run, value);
            bool ok = members is not null;
            if (ok && memberShape is int s)
            {
                foreach (Node member in members!)
                {
                    if (!run.Conforms(s, member))
                    {
                        ok = false;
                        break;
                    }
                }
            }

            if (ok && minLength is long min)
            {
                ok = members!.Count >= min;
            }

            if (ok && maxLength is long max)
            {
                ok = members!.Count <= max;
            }

            if (ok && uniqueMembers)
            {
                HashSet<Node> seen = new(run.Data.Nodes);
                foreach (Node member in members!)
                {
                    if (!seen.Add(member))
                    {
                        ok = false;
                        break;
                    }
                }
            }

            if (!ok)
            {
                sink.Add(shape, this, focus, value);
            }
        }
    }
}

/// <summary>The kinds of property pair component (§7.6).</summary>
internal enum PairKind
{
    Equals,
    Disjoint,
    SubsetOf,
    LessThan,
    LessThanOrEquals,
}

/// <summary>§7.6: the property pair components, over a SHACL path (1.2).</summary>
internal sealed class PairConstraint(PairKind kind, ShapePath other) : Constraint(kind switch
{
    PairKind.Equals => Vocabulary.EqualsConstraintComponent,
    PairKind.Disjoint => Vocabulary.DisjointConstraintComponent,
    PairKind.SubsetOf => Vocabulary.SubsetOfConstraintComponent,
    PairKind.LessThan => Vocabulary.LessThanConstraintComponent,
    _ => Vocabulary.LessThanOrEqualsConstraintComponent,
})
{
    internal override void Validate(ValidationRun run, Shape shape, Node focus, IReadOnlyList<Node> values, ResultSink sink)
    {
        List<Node> others = run.PathValues(other, focus);
        HashSet<Node> otherSet = new(others, run.Data.Nodes);
        HashSet<Node> valueSet = new(values, run.Data.Nodes);
        switch (kind)
        {
            case PairKind.Equals:
                foreach (Node value in values)
                {
                    if (!otherSet.Contains(value))
                    {
                        sink.Add(shape, this, focus, value);
                    }
                }

                foreach (Node node in others)
                {
                    if (!valueSet.Contains(node))
                    {
                        sink.Add(shape, this, focus, node);
                    }
                }

                return;
            case PairKind.Disjoint:
                foreach (Node value in values)
                {
                    if (otherSet.Contains(value))
                    {
                        sink.Add(shape, this, focus, value);
                    }
                }

                return;
            case PairKind.SubsetOf:
                foreach (Node value in values)
                {
                    if (!otherSet.Contains(value))
                    {
                        sink.Add(shape, this, focus, value);
                    }
                }

                return;
            default:
                foreach (Node value in values)
                {
                    RdfTerm v = run.Data.Term(value);
                    foreach (Node node in others)
                    {
                        PartialOrdering order = Literals.Compare(v, run.Data.Term(node));
                        bool ok = order == PartialOrdering.Less || (kind == PairKind.LessThanOrEquals && order == PartialOrdering.Equal);
                        if (!ok)
                        {
                            // §7.6.4: a result for each pair.
                            sink.Add(shape, this, focus, value);
                        }
                    }
                }

                return;
        }
    }
}

/// <summary>§7.7.1 <c>sh:not</c>.</summary>
internal sealed class NotConstraint(int negated) : Constraint(Vocabulary.NotConstraintComponent)
{
    internal override IEnumerable<ShapeReference> References => [new ShapeReference(negated, Vocabulary.Not, Polarity.Negative)];

    internal override void Validate(ValidationRun run, Shape shape, Node focus, IReadOnlyList<Node> values, ResultSink sink)
    {
        foreach (Node value in values)
        {
            if (run.Conforms(negated, value))
            {
                sink.Add(shape, this, focus, value);
            }
        }
    }
}

/// <summary>The kinds of logical component over a list of shapes (§7.7.2–§7.7.4).</summary>
internal enum LogicalKind
{
    And,
    Or,
    Xone,
}

/// <summary>§7.7.2–§7.7.4 <c>sh:and</c>, <c>sh:or</c>, <c>sh:xone</c>.</summary>
internal sealed class LogicalConstraint(LogicalKind kind, ImmutableArray<int> members) : Constraint(kind switch
{
    LogicalKind.And => Vocabulary.AndConstraintComponent,
    LogicalKind.Or => Vocabulary.OrConstraintComponent,
    _ => Vocabulary.XoneConstraintComponent,
})
{
    internal override IEnumerable<ShapeReference> References
    {
        get
        {
            RdfTerm parameter = kind switch { LogicalKind.And => Vocabulary.And, LogicalKind.Or => Vocabulary.Or, _ => Vocabulary.Xone };
            Polarity polarity = kind == LogicalKind.Xone ? Polarity.Both : Polarity.Positive;
            foreach (int member in members)
            {
                yield return new ShapeReference(member, parameter, polarity);
            }
        }
    }

    internal override void Validate(ValidationRun run, Shape shape, Node focus, IReadOnlyList<Node> values, ResultSink sink)
    {
        foreach (Node value in values)
        {
            int conforming = 0;
            foreach (int member in members)
            {
                if (run.Conforms(member, value))
                {
                    conforming++;
                }
            }

            bool ok = kind switch
            {
                LogicalKind.And => conforming == members.Length,
                LogicalKind.Or => conforming > 0,
                _ => conforming == 1,
            };
            if (!ok)
            {
                sink.Add(shape, this, focus, value);
            }
        }
    }
}

/// <summary>§7.8.1 <c>sh:node</c>.</summary>
internal sealed class NodeConstraint(int target) : Constraint(Vocabulary.NodeConstraintComponent)
{
    internal override IEnumerable<ShapeReference> References => [new ShapeReference(target, Vocabulary.Node, Polarity.Positive)];

    internal override void Validate(ValidationRun run, Shape shape, Node focus, IReadOnlyList<Node> values, ResultSink sink)
    {
        foreach (Node value in values)
        {
            if (!run.Conforms(target, value))
            {
                sink.Add(shape, this, focus, value);
            }
        }
    }
}

/// <summary>§7.8.2 <c>sh:property</c>: the results are the property shape's own, for each value node as focus node.</summary>
internal sealed class PropertyConstraint(int property) : Constraint(Vocabulary.PropertyConstraintComponent)
{
    internal int Property => property;

    internal override IEnumerable<ShapeReference> References => [new ShapeReference(property, Vocabulary.Property, Polarity.Positive)];

    internal override void Validate(ValidationRun run, Shape shape, Node focus, IReadOnlyList<Node> values, ResultSink sink)
    {
        Shape propertyShape = run.Shape(property);
        foreach (Node value in values)
        {
            run.ValidateFocus(propertyShape, value, sink);
        }
    }
}

/// <summary>§7.8.3 <c>sh:someValue</c>.</summary>
internal sealed class SomeValueConstraint(int target) : Constraint(Vocabulary.SomeValueConstraintComponent)
{
    internal override IEnumerable<ShapeReference> References => [new ShapeReference(target, Vocabulary.SomeValue, Polarity.Positive)];

    internal override void Validate(ValidationRun run, Shape shape, Node focus, IReadOnlyList<Node> values, ResultSink sink)
    {
        foreach (Node value in values)
        {
            if (run.Conforms(target, value))
            {
                return;
            }
        }

        sink.Add(shape, this, focus, null);
    }
}

/// <summary>§7.8.4: <c>sh:qualifiedValueShape</c> with <c>sh:qualifiedMinCount</c> or <c>sh:qualifiedMaxCount</c>.</summary>
internal sealed class QualifiedConstraint(int qualified, ImmutableArray<int> siblings, bool minimum, long bound)
    : Constraint(minimum ? Vocabulary.QualifiedMinCountConstraintComponent : Vocabulary.QualifiedMaxCountConstraintComponent)
{
    internal override IEnumerable<ShapeReference> References
    {
        get
        {
            yield return new ShapeReference(qualified, Vocabulary.QualifiedValueShape, minimum ? Polarity.Positive : Polarity.Both);
            foreach (int sibling in siblings)
            {
                yield return new ShapeReference(sibling, Vocabulary.QualifiedValueShapesDisjoint, Polarity.Both);
            }
        }
    }

    internal override void Validate(ValidationRun run, Shape shape, Node focus, IReadOnlyList<Node> values, ResultSink sink)
    {
        long count = 0;
        foreach (Node value in values)
        {
            if (!run.Conforms(qualified, value))
            {
                continue;
            }

            bool sibling = false;
            foreach (int other in siblings)
            {
                if (run.Conforms(other, value))
                {
                    sibling = true;
                    break;
                }
            }

            if (!sibling)
            {
                count++;
            }
        }

        if (minimum ? count < bound : count > bound)
        {
            sink.Add(shape, this, focus, null);
        }
    }
}

/// <summary>§7.8.5 <c>sh:reifierShape</c> and <c>sh:reificationRequired</c>.</summary>
internal sealed class ReifierShapeConstraint(int reifierShape, bool required, RdfTerm predicate) : Constraint(Vocabulary.ReifierShapeConstraintComponent)
{
    internal override IEnumerable<ShapeReference> References => [new ShapeReference(reifierShape, Vocabulary.ReifierShape, Polarity.Positive)];

    internal override void Validate(ValidationRun run, Shape shape, Node focus, IReadOnlyList<Node> values, ResultSink sink)
    {
        Node p = run.Data.Resolve(predicate);
        foreach (Node value in values)
        {
            List<Node> reifiers = run.Reifiers(focus, p, value);
            if (required && reifiers.Count == 0)
            {
                sink.Add(shape, this, focus, value);
                continue;
            }

            foreach (Node reifier in reifiers)
            {
                if (!run.Conforms(reifierShape, reifier))
                {
                    sink.Add(shape, this, focus, value);
                }
            }
        }
    }
}

/// <summary>§7.9.1 <c>sh:closed</c> (true or <c>sh:ByTypes</c>) with <c>sh:ignoredProperties</c>.</summary>
internal sealed class ClosedConstraint(ImmutableArray<RdfTerm> allowed, bool byTypes) : Constraint(Vocabulary.ClosedConstraintComponent)
{
    internal override void Validate(ValidationRun run, Shape shape, Node focus, IReadOnlyList<Node> values, ResultSink sink)
    {
        foreach (Node value in values)
        {
            HashSet<RdfTerm> permitted = new(allowed, RdfTerm.Comparer);
            if (byTypes)
            {
                permitted.Add(Vocabulary.RdfType);
                HashSet<RdfTerm> visited = new(RdfTerm.Comparer);
                foreach (Node type in run.TypesOf(value))
                {
                    CollectProperties(run, run.Data.Term(type), permitted, visited);
                }
            }

            foreach ((Node predicate, Node obj) in run.Data.Triples(value))
            {
                RdfTerm p = run.Data.Term(predicate);
                if (!permitted.Contains(p))
                {
                    sink.Add(shape, this, focus, obj, new PredicatePath(p));
                }
            }
        }
    }

    /// <summary>§7.9.1's <c>collectProperties</c>, over the shapes graph, each node visited once.</summary>
    private static void CollectProperties(ValidationRun run, RdfTerm node, HashSet<RdfTerm> into, HashSet<RdfTerm> visited)
    {
        if (!visited.Add(node))
        {
            return;
        }

        Graphs.ShapesSource shapes = run.Shapes.Source;
        foreach (RdfTerm property in shapes.Objects(node, Vocabulary.Property))
        {
            foreach (RdfTerm path in shapes.Objects(property, Vocabulary.Path))
            {
                if (path.Kind == RdfTermKind.Iri)
                {
                    into.Add(path);
                }
            }
        }

        if (shapes.IsInstanceOf(node, Vocabulary.RdfsClass))
        {
            foreach (RdfTerm parent in shapes.Objects(node, Vocabulary.RdfsSubClassOf))
            {
                CollectProperties(run, parent, into, visited);
            }

            foreach (RdfTerm targeting in shapes.Subjects(Vocabulary.TargetClass, node))
            {
                CollectProperties(run, targeting, into, visited);
            }
        }

        if (shapes.IsInstanceOf(node, Vocabulary.NodeShapeClass))
        {
            foreach (RdfTerm other in shapes.Objects(node, Vocabulary.Node))
            {
                CollectProperties(run, other, into, visited);
            }
        }
    }
}

/// <summary>§7.9.2 <c>sh:hasValue</c>.</summary>
internal sealed class HasValueConstraint(RdfTerm required) : Constraint(Vocabulary.HasValueConstraintComponent)
{
    internal override void Validate(ValidationRun run, Shape shape, Node focus, IReadOnlyList<Node> values, ResultSink sink)
    {
        Node node = run.Data.Resolve(required);
        foreach (Node value in values)
        {
            if (run.Data.Nodes.Equals(value, node))
            {
                return;
            }
        }

        sink.Add(shape, this, focus, null);
    }
}

/// <summary>§7.9.3 <c>sh:in</c>.</summary>
internal sealed class InConstraint(ImmutableArray<RdfTerm> members) : Constraint(Vocabulary.InConstraintComponent)
{
    internal override void Validate(ValidationRun run, Shape shape, Node focus, IReadOnlyList<Node> values, ResultSink sink)
    {
        HashSet<Node> allowed = new(run.Data.Nodes);
        foreach (RdfTerm member in members)
        {
            allowed.Add(run.Data.Resolve(member));
        }

        foreach (Node value in values)
        {
            if (!allowed.Contains(value))
            {
                sink.Add(shape, this, focus, value);
            }
        }
    }
}

/// <summary>§7.9.4 <c>sh:rootClass</c>: the class itself or a subclass of one of the roots, in the data graph.</summary>
internal sealed class RootClassConstraint(ImmutableArray<RdfTerm> roots) : Constraint(Vocabulary.RootClassConstraintComponent)
{
    internal override void Validate(ValidationRun run, Shape shape, Node focus, IReadOnlyList<Node> values, ResultSink sink)
    {
        foreach (Node value in values)
        {
            bool ok = false;
            if (run.Data.Term(value).Kind == RdfTermKind.Iri)
            {
                HashSet<Node> ancestors = run.SuperclassesOf(value);
                foreach (RdfTerm root in roots)
                {
                    if (ancestors.Contains(run.Data.Resolve(root)))
                    {
                        ok = true;
                        break;
                    }
                }
            }

            if (!ok)
            {
                sink.Add(shape, this, focus, value);
            }
        }
    }
}

/// <summary>§7.9.5 <c>sh:uniqueValuesFor</c>: no two target nodes of the shape share all the properties' values.</summary>
internal sealed class UniqueValuesForConstraint(ImmutableArray<RdfTerm> properties) : Constraint(Vocabulary.UniqueValuesForConstraintComponent)
{
    internal override void Validate(ValidationRun run, Shape shape, Node focus, IReadOnlyList<Node> values, ResultSink sink)
    {
        List<Node> targets = run.Targets(shape);
        foreach (Node value in values)
        {
            string? key = Key(run, value);
            if (key is null)
            {
                continue;
            }

            foreach (Node other in targets)
            {
                if (!run.Data.Nodes.Equals(other, value) && string.Equals(Key(run, other), key, StringComparison.Ordinal))
                {
                    // §7.9.5: "there is a validation result", without a value.
                    sink.Add(shape, this, focus, null);
                    break;
                }
            }
        }
    }

    /// <summary>The node's values for the properties, as a comparable key; null when it has none for any.</summary>
    private string? Key(ValidationRun run, Node node)
    {
        StringBuilder key = new();
        bool any = false;
        foreach (RdfTerm property in properties)
        {
            List<Node> found = [];
            if (run.Data.TryHandle(property, out TermHandle p))
            {
                run.Data.Objects(node, p, found, new HashSet<Node>(run.Data.Nodes));
            }

            any |= found.Count > 0;
            List<string> texts = found.ConvertAll(v => Strings.NTriples(run.Data.Term(v)));
            texts.Sort(StringComparer.Ordinal);
            key.Append(string.Join(" ", texts)).Append('\u0001');
        }

        return any ? key.ToString() : null;
    }
}

/// <summary>Reading SHACL lists in the data graph (§1.1).</summary>
internal static class Lists
{
    /// <summary>The members of the SHACL list at <paramref name="head"/> in the data graph, or null when it is not one.</summary>
    internal static List<Node>? Members(ValidationRun run, Node head)
    {
        DataGraph data = run.Data;
        Node nil = data.Resolve(Vocabulary.RdfNil);
        if (!data.TryHandle(Vocabulary.RdfFirst, out TermHandle first) || !data.TryHandle(Vocabulary.RdfRest, out TermHandle rest))
        {
            return data.Nodes.Equals(head, nil) ? [] : null;
        }

        List<Node> members = [];
        HashSet<Node> visited = new(data.Nodes);
        Node cursor = head;
        while (!data.Nodes.Equals(cursor, nil))
        {
            if (data.Term(cursor).Kind is not (RdfTermKind.BlankNode or RdfTermKind.Iri) || !visited.Add(cursor))
            {
                return null;
            }

            List<Node> firsts = [], rests = [];
            data.Objects(cursor, first, firsts, new HashSet<Node>(data.Nodes));
            data.Objects(cursor, rest, rests, new HashSet<Node>(data.Nodes));
            if (firsts.Count != 1 || rests.Count != 1)
            {
                return null;
            }

            members.Add(firsts[0]);
            cursor = rests[0];
        }

        return members;
    }
}

/// <summary>String functions SHACL takes from SPARQL.</summary>
internal static class Strings
{
    /// <summary>SPARQL's <c>STRLEN(str(t))</c>: characters, not UTF-16 units.</summary>
    internal static long Length(RdfTerm term)
    {
        string text = Encoding.UTF8.GetString(term.Lexical);
        long length = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (!char.IsHighSurrogate(text[i]) || i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
            {
                length++;
            }
            else
            {
                length++;
                i++;
            }
        }

        return length;
    }

    /// <summary>SPARQL's <c>langMatches</c>: RFC 4647 §3.3.1 basic filtering; <c>*</c> matches any non-empty tag.</summary>
    internal static bool LangMatches(string tag, string range)
    {
        if (range == "*")
        {
            return tag.Length > 0;
        }

        return tag.Equals(range, StringComparison.OrdinalIgnoreCase)
            || (tag.Length > range.Length && tag[range.Length] == '-' && tag.StartsWith(range, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A term as N-Triples-like text, for keys and the order of results.</summary>
    internal static string NTriples(RdfTerm term) => term.Kind switch
    {
        RdfTermKind.Iri => "<" + Encoding.UTF8.GetString(term.Lexical) + ">",
        RdfTermKind.BlankNode => "_:" + Encoding.UTF8.GetString(term.Lexical),
        RdfTermKind.TripleTerm => "<<( " + NTriples(term.Subject!) + " " + NTriples(term.Predicate!) + " " + NTriples(term.Object!) + " )>>",
        _ => "\"" + Encoding.UTF8.GetString(term.Lexical) + "\""
            + (!term.Language.IsEmpty
                ? "@" + Encoding.UTF8.GetString(term.Language) + (term.Direction == TextDirection.None ? string.Empty : term.Direction == TextDirection.LeftToRight ? "--ltr" : "--rtl")
                : "^^<" + Encoding.UTF8.GetString(term.DatatypeIri) + ">"),
    };
}
