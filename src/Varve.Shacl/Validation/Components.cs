// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Derivation.Model;
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
    internal override void Validate(ValidationRun run, Shape shape, Node focus, List<Node> values, ResultSink sink)
    {
        for (int i = 0; i < values.Count; i++)
        {
            Node value = values[i];
            bool ok = false;
            if (run.Data.Term(value).Kind != RdfTermKind.Literal)
            {
                for (int c = 0; c < classes.Length && !ok; c++)
                {
                    ok = run.IsInstanceOf(value, run.Data.Resolve(classes[c]));
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
    internal override void Validate(ValidationRun run, Shape shape, Node focus, List<Node> values, ResultSink sink)
    {
        for (int i = 0; i < values.Count; i++)
        {
            Node value = values[i];
            RdfTerm term = run.Data.Term(value);
            bool ok = false;
            for (int d = 0; d < datatypes.Length && !ok; d++)
            {
                ok = Literals.Matches(term, datatypes[d]);
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
    internal override void Validate(ValidationRun run, Shape shape, Node focus, List<Node> values, ResultSink sink)
    {
        for (int i = 0; i < values.Count; i++)
        {
            Node value = values[i];
            RdfTermKind kind = run.Data.Term(value).Kind;
            bool ok = false;
            for (int k = 0; k < kinds.Length && !ok; k++)
            {
                ok = Matches(kind, kinds[k]);
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

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
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
    internal override void Validate(ValidationRun run, Shape shape, Node focus, List<Node> values, ResultSink sink)
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
    internal override void Validate(ValidationRun run, Shape shape, Node focus, List<Node> values, ResultSink sink)
    {
        for (int i = 0; i < values.Count; i++)
        {
            Node value = values[i];

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
    internal override void Validate(ValidationRun run, Shape shape, Node focus, List<Node> values, ResultSink sink)
    {
        for (int i = 0; i < values.Count; i++)
        {
            Node value = values[i];
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
    internal override void Validate(ValidationRun run, Shape shape, Node focus, List<Node> values, ResultSink sink)
    {
        Regex regex = run.Pattern(shape, translation);
        for (int i = 0; i < values.Count; i++)
        {
            Node value = values[i];
            RdfTerm term = run.Data.Term(value);
            if (term.Kind is not (RdfTermKind.Iri or RdfTermKind.Literal) || !IsMatch(run, shape, focus, regex, term.Lexical))
            {
                sink.Add(shape, this, focus, value);
            }
        }
    }

    /// <summary>The value's string, transcoded into a pooled buffer, against the pattern; past the timeout, the validation fails (ADR 0151).</summary>
    [DesignDecision(typeof(ShaclHotPathScope.PatternsMatchThroughTheBclRegex), Scope = ExceptionScope.HotPath)]
    private static bool IsMatch(ValidationRun run, Shape shape, Node focus, Regex regex, ReadOnlySpan<byte> utf8)
    {
        // A UTF-16 string is never longer, in code units, than its UTF-8 bytes.
        char[] buffer = ArrayPool<char>.Shared.Rent(Math.Max(1, utf8.Length));
        try
        {
            Utf8.ToUtf16(utf8, buffer, out _, out int written);
            return regex.IsMatch(buffer.AsSpan(0, written));
        }
        catch (RegexMatchTimeoutException)
        {
            throw new ShaclFailureException(new ShaclProblem(
                ShaclProblemKind.PatternTimeout,
                shape.Node,
                run.Data.Term(focus),
                "sh:pattern (SHACL 1.2 Core §7.4.3) exceeded ShaclOptions.RegexTimeout on one value."));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }
}

/// <summary>§7.4.4 <c>sh:singleLine</c>.</summary>
internal sealed class SingleLineConstraint() : Constraint(Vocabulary.SingleLineConstraintComponent)
{
    internal override void Validate(ValidationRun run, Shape shape, Node focus, List<Node> values, ResultSink sink)
    {
        for (int i = 0; i < values.Count; i++)
        {
            Node value = values[i];
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
    private readonly byte[][] _ranges = Encode(ranges);

    internal override void Validate(ValidationRun run, Shape shape, Node focus, List<Node> values, ResultSink sink)
    {
        for (int i = 0; i < values.Count; i++)
        {
            Node value = values[i];
            RdfTerm term = run.Data.Term(value);
            bool ok = false;
            if (term.Kind == RdfTermKind.Literal && !term.Language.IsEmpty)
            {
                for (int r = 0; r < _ranges.Length && !ok; r++)
                {
                    ok = Strings.LangMatches(term.Language, _ranges[r]);
                }
            }

            if (!ok)
            {
                sink.Add(shape, this, focus, value);
            }
        }
    }

    /// <summary>The ranges as UTF-8, once, when the shape is read.</summary>
    private static byte[][] Encode(ImmutableArray<string> ranges)
    {
        byte[][] encoded = new byte[ranges.Length][];
        for (int i = 0; i < ranges.Length; i++)
        {
            encoded[i] = Encoding.UTF8.GetBytes(ranges[i]);
        }

        return encoded;
    }
}

/// <summary>§7.4.6 <c>sh:uniqueLang</c>: one result per language tag (with its direction) used twice.</summary>
internal sealed class UniqueLangConstraint() : Constraint(Vocabulary.UniqueLangConstraintComponent)
{
    internal override void Validate(ValidationRun run, Shape shape, Node focus, List<Node> values, ResultSink sink)
    {
        // Pairwise over one focus node's values, in place: a result for each tag at its first use, when a later value uses it too.
        for (int i = 0; i < values.Count; i++)
        {
            RdfTerm term = run.Data.Term(values[i]);
            if (!IsTagged(term))
            {
                continue;
            }

            bool seenBefore = false;
            for (int j = 0; j < i && !seenBefore; j++)
            {
                seenBefore = SameTag(term, run.Data.Term(values[j]));
            }

            for (int j = i + 1; j < values.Count && !seenBefore; j++)
            {
                if (SameTag(term, run.Data.Term(values[j])))
                {
                    sink.Add(shape, this, focus, null);
                    break;
                }
            }
        }
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private static bool IsTagged(RdfTerm term) => term.Kind == RdfTermKind.Literal && !term.Language.IsEmpty;

    /// <summary>Language tags compare case-insensitively (BCP 47), and the direction counts (1.2).</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private static bool SameTag(RdfTerm term, RdfTerm other) =>
        IsTagged(other) && term.Direction == other.Direction && Strings.AsciiEqualsIgnoreCase(term.Language, other.Language);
}

/// <summary>§7.5: the four list components; a value node that is not a SHACL list is a result for each.</summary>
internal sealed class ListConstraint(RdfTerm component, int? memberShape, long? minLength, long? maxLength, bool uniqueMembers) : Constraint(component)
{
    internal override IEnumerable<ShapeReference> References =>
        memberShape is int s ? [new ShapeReference(s, Vocabulary.MemberShape, Polarity.Positive)] : [];

    [DesignDecision(typeof(ShaclHotPathScope.ComparisonsGatherTheirOtherSide), Scope = ExceptionScope.HotPath)]
    internal override void Validate(ValidationRun run, Shape shape, Node focus, List<Node> values, ResultSink sink)
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
    /// <summary>The second path, read from the focus node.</summary>
    internal ShapePath Other => other;

    [DesignDecision(typeof(ShaclHotPathScope.ComparisonsGatherTheirOtherSide), Scope = ExceptionScope.HotPath)]
    internal override void Validate(ValidationRun run, Shape shape, Node focus, List<Node> values, ResultSink sink)
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

    internal override void Validate(ValidationRun run, Shape shape, Node focus, List<Node> values, ResultSink sink)
    {
        for (int i = 0; i < values.Count; i++)
        {
            Node value = values[i];
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

    internal override void Validate(ValidationRun run, Shape shape, Node focus, List<Node> values, ResultSink sink)
    {
        for (int i = 0; i < values.Count; i++)
        {
            Node value = values[i];
            int conforming = 0;
            for (int m = 0; m < members.Length; m++)
            {
                if (run.Conforms(members[m], value))
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

    internal override void Validate(ValidationRun run, Shape shape, Node focus, List<Node> values, ResultSink sink)
    {
        for (int i = 0; i < values.Count; i++)
        {
            Node value = values[i];
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

    internal override void Validate(ValidationRun run, Shape shape, Node focus, List<Node> values, ResultSink sink)
    {
        Shape propertyShape = run.Shape(property);
        for (int i = 0; i < values.Count; i++)
        {
            run.ValidateFocus(propertyShape, values[i], sink);
        }
    }
}

/// <summary>§7.8.3 <c>sh:someValue</c>.</summary>
internal sealed class SomeValueConstraint(int target) : Constraint(Vocabulary.SomeValueConstraintComponent)
{
    internal override IEnumerable<ShapeReference> References => [new ShapeReference(target, Vocabulary.SomeValue, Polarity.Positive)];

    internal override void Validate(ValidationRun run, Shape shape, Node focus, List<Node> values, ResultSink sink)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (run.Conforms(target, values[i]))
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

    internal override void Validate(ValidationRun run, Shape shape, Node focus, List<Node> values, ResultSink sink)
    {
        long count = 0;
        for (int i = 0; i < values.Count; i++)
        {
            Node value = values[i];
            if (!run.Conforms(qualified, value))
            {
                continue;
            }

            bool sibling = false;
            for (int s = 0; s < siblings.Length && !sibling; s++)
            {
                sibling = run.Conforms(siblings[s], value);
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
    /// <summary>The shape the reifiers are checked against.</summary>
    internal int ReifierShape => reifierShape;

    /// <summary>The predicate of the reified triples, the property shape's path.</summary>
    internal RdfTerm Predicate => predicate;

    internal override IEnumerable<ShapeReference> References => [new ShapeReference(reifierShape, Vocabulary.ReifierShape, Polarity.Positive)];

    internal override void Validate(ValidationRun run, Shape shape, Node focus, List<Node> values, ResultSink sink)
    {
        Node p = run.Data.Resolve(predicate);
        for (int i = 0; i < values.Count; i++)
        {
            Node value = values[i];
            List<Node>? reifiers = run.Reifiers(focus, p, value);
            if (reifiers is null)
            {
                if (required)
                {
                    sink.Add(shape, this, focus, value);
                }

                continue;
            }

            for (int r = 0; r < reifiers.Count; r++)
            {
                if (!run.Conforms(reifierShape, reifiers[r]))
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
    /// <summary><c>sh:ByTypes</c>: the permitted properties follow the value's types.</summary>
    internal bool ByTypes => byTypes;

    // Read, never written, by validations that may run at once.
    private readonly HashSet<RdfTerm> _allowed = new(allowed, RdfTerm.Comparer);

    [DesignDecision(typeof(ShaclHotPathScope.ComparisonsGatherTheirOtherSide), Scope = ExceptionScope.HotPath)]
    internal override void Validate(ValidationRun run, Shape shape, Node focus, List<Node> values, ResultSink sink)
    {
        foreach (Node value in values)
        {
            HashSet<RdfTerm> permitted = _allowed;
            if (byTypes)
            {
                permitted = new(_allowed, RdfTerm.Comparer) { Vocabulary.RdfType };
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
    internal override void Validate(ValidationRun run, Shape shape, Node focus, List<Node> values, ResultSink sink)
    {
        Node node = run.Data.Resolve(required);
        for (int i = 0; i < values.Count; i++)
        {
            if (run.Data.Nodes.Equals(values[i], node))
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
    internal override void Validate(ValidationRun run, Shape shape, Node focus, List<Node> values, ResultSink sink)
    {
        for (int i = 0; i < values.Count; i++)
        {
            Node value = values[i];
            if (!run.IsMember(this, members, value))
            {
                sink.Add(shape, this, focus, value);
            }
        }
    }
}

/// <summary>§7.9.4 <c>sh:rootClass</c>: the class itself or a subclass of one of the roots, in the data graph.</summary>
internal sealed class RootClassConstraint(ImmutableArray<RdfTerm> roots) : Constraint(Vocabulary.RootClassConstraintComponent)
{
    internal override void Validate(ValidationRun run, Shape shape, Node focus, List<Node> values, ResultSink sink)
    {
        for (int i = 0; i < values.Count; i++)
        {
            Node value = values[i];
            bool ok = false;
            if (run.Data.Term(value).Kind == RdfTermKind.Iri)
            {
                for (int r = 0; r < roots.Length && !ok; r++)
                {
                    ok = run.IsSubclassOf(value, run.Data.Resolve(roots[r]));
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
    /// <summary>The properties whose values make a node's key.</summary>
    internal ImmutableArray<RdfTerm> Properties => properties;

    internal override void Validate(ValidationRun run, Shape shape, Node focus, List<Node> values, ResultSink sink)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (run.SharesKey(this, shape, values[i]))
            {
                // §7.9.5: "there is a validation result", without a value.
                sink.Add(shape, this, focus, null);
            }
        }
    }

    /// <summary>The node's values for the properties, as a comparable key; null when it has none for any.</summary>
    internal string? Key(ValidationRun run, Node node)
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
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static long Length(RdfTerm term)
    {
        // A lexical form is well-formed UTF-8, so its characters are the bytes that start a code point.
        ReadOnlySpan<byte> text = term.Lexical;
        long length = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if ((text[i] & 0xC0) != 0x80)
            {
                length++;
            }
        }

        return length;
    }

    /// <summary>SPARQL's <c>langMatches</c>: RFC 4647 §3.3.1 basic filtering; <c>*</c> matches any non-empty tag.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static bool LangMatches(ReadOnlySpan<byte> tag, ReadOnlySpan<byte> range)
    {
        if (range.Length == 1 && range[0] == (byte)'*')
        {
            return tag.Length > 0;
        }

        return tag.Length >= range.Length
            && (tag.Length == range.Length || tag[range.Length] == (byte)'-')
            && AsciiEqualsIgnoreCase(tag[..range.Length], range);
    }

    /// <summary>Equality of two byte strings with ASCII letters folded; a language tag and a range are ASCII (BCP 47).</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static bool AsciiEqualsIgnoreCase(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            if (Fold(left[i]) != Fold(right[i]))
            {
                return false;
            }
        }

        return true;

        static byte Fold(byte b) => b is >= (byte)'A' and <= (byte)'Z' ? (byte)(b | 0x20) : b;
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
