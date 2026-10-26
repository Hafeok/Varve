// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Varve.Rdf;
using Varve.Shacl.Graphs;
using Varve.Shacl.Model;
using Varve.Shacl.Validation;
using Varve.Xsd;

namespace Varve.Shacl.Shapes;

/// <summary>
/// Reads a shapes graph into the shapes model: finds its shapes (SHACL 1.2
/// Core §3.1), checks the syntax rules of Appendix A and of each component,
/// and builds the constraints. An ill-formed shape is a problem naming the
/// shape and the rule, and a shapes graph with any is refused (ADR 0151).
/// </summary>
internal sealed class ShapesReader
{
    private static readonly ImmutableArray<RdfTerm> TargetPredicates =
    [
        Vocabulary.TargetClass, Vocabulary.TargetNode, Vocabulary.TargetObjectsOf, Vocabulary.TargetSubjectsOf, Vocabulary.TargetWhere,
    ];

    /// <summary>Every parameter of a Core component (§7): its subject is a shape (§3.1).</summary>
    private static readonly ImmutableArray<RdfTerm> CoreParameters =
    [
        Vocabulary.Class, Vocabulary.Datatype, Vocabulary.NodeKind, Vocabulary.MinCount, Vocabulary.MaxCount,
        Vocabulary.MinExclusive, Vocabulary.MinInclusive, Vocabulary.MaxExclusive, Vocabulary.MaxInclusive,
        Vocabulary.MinLength, Vocabulary.MaxLength, Vocabulary.Pattern, Vocabulary.Flags, Vocabulary.SingleLine,
        Vocabulary.LanguageIn, Vocabulary.UniqueLang, Vocabulary.MemberShape, Vocabulary.MinListLength,
        Vocabulary.MaxListLength, Vocabulary.UniqueMembers, Vocabulary.EqualsParameter, Vocabulary.Disjoint, Vocabulary.SubsetOf,
        Vocabulary.LessThan, Vocabulary.LessThanOrEquals, Vocabulary.Not, Vocabulary.And, Vocabulary.Or, Vocabulary.Xone,
        Vocabulary.Node, Vocabulary.Property, Vocabulary.SomeValue, Vocabulary.QualifiedValueShape,
        Vocabulary.QualifiedMinCount, Vocabulary.QualifiedMaxCount, Vocabulary.QualifiedValueShapesDisjoint,
        Vocabulary.ReifierShape, Vocabulary.ReificationRequired, Vocabulary.Closed, Vocabulary.IgnoredProperties,
        Vocabulary.HasValue, Vocabulary.In, Vocabulary.RootClass, Vocabulary.UniqueValuesFor,
    ];

    private readonly ShapesSource _source;
    private readonly IReadOnlyList<IShaclExtension> _extensions;
    private readonly List<ShaclProblem> _problems = [];
    private readonly Dictionary<RdfTerm, int> _index = new(RdfTerm.Comparer);
    private readonly List<Shape> _shapes = [];
    private readonly Dictionary<(RdfTerm, RdfTerm, RdfTerm), List<RdfTerm>> _reifiers = new(new TripleComparer());

    private readonly bool _refuseNonDeterministic;

    internal ShapesReader(ShapesSource source, IReadOnlyList<IShaclExtension> extensions, bool refuseNonDeterministic = false)
    {
        _source = source;
        _extensions = extensions;
        _refuseNonDeterministic = refuseNonDeterministic;
    }

    internal IReadOnlyList<ShaclProblem> Problems => _problems;

    /// <summary>Reads every shape; the problems say whether the shapes graph is refused.</summary>
    internal List<Shape> Read()
    {
        // §1.4: an entailment regime other than simple entailment is a failure.
        foreach ((RdfTerm _, RdfTerm regime) in _source.Pairs(Vocabulary.Entailment))
        {
            if (!regime.Equals(Vocabulary.SimpleEntailment))
            {
                _problems.Add(new ShaclProblem(
                    ShaclProblemKind.UnsupportedEntailment,
                    null,
                    null,
                    "sh:entailment " + Strings.NTriples(regime) + " is not supported; only simple entailment is (SHACL 1.2 Core §1.4, ADR 0151)."));
            }
        }

        List<IShaclExtensionScope> scopes = [];
        List<RdfTerm> extensionParameters = [];
        foreach (IShaclExtension extension in _extensions)
        {
            ShapesGraphContext context = new(_source.Source, _source.Graph, _problems) { RefuseNonDeterministicFunctions = _refuseNonDeterministic };
            scopes.Add(extension.Begin(context));
            extensionParameters.AddRange(context.Parameters);
        }

        if (_extensions.Count == 0)
        {
            RefuseWithoutExtension();
        }

        IndexReifiers();
        foreach (RdfTerm node in FindShapes(extensionParameters))
        {
            _index[node] = _shapes.Count;
            _shapes.Add(new Shape(_shapes.Count, node));
        }

        foreach (Shape shape in _shapes)
        {
            ReadShape(shape);
        }

        foreach (Shape shape in _shapes)
        {
            RdfTerm? path = _source.Object(shape.Node, Vocabulary.Path, out _);
            ShapeContext context = new(_source, shape.Node, path, _problems);
            foreach (IShaclExtensionScope scope in scopes)
            {
                scope.Load(context);
            }

            foreach (IShaclConstraint constraint in context.Constraints)
            {
                shape.Constraints.Add(new ExtensionConstraint(constraint));
            }
        }

        return _shapes;
    }

    /// <summary>§3.1's four ways to be a shape, in the order of their canonical text, so that an index is stable.</summary>
    private List<RdfTerm> FindShapes(IReadOnlyList<RdfTerm> extensionParameters)
    {
        HashSet<RdfTerm> found = new(RdfTerm.Comparer);
        void Add(RdfTerm node)
        {
            if (node.Kind is RdfTermKind.Iri or RdfTermKind.BlankNode)
            {
                found.Add(node);
            }
        }

        foreach (RdfTerm cls in ShapeTypes())
        {
            foreach (RdfTerm instance in _source.Subjects(Vocabulary.RdfType, cls))
            {
                Add(instance);
            }
        }

        List<RdfTerm> predicates = [.. TargetPredicates, .. CoreParameters, .. extensionParameters];
        foreach (RdfTerm predicate in predicates)
        {
            foreach (RdfTerm subject in _source.SubjectsOf(predicate))
            {
                Add(subject);
            }
        }

        // Values of shape-expecting parameters, and the members of their lists.
        foreach (RdfTerm parameter in (ReadOnlySpan<RdfTerm>)[Vocabulary.Node, Vocabulary.Property, Vocabulary.Not, Vocabulary.QualifiedValueShape, Vocabulary.MemberShape, Vocabulary.ReifierShape, Vocabulary.SomeValue, Vocabulary.TargetWhere])
        {
            foreach ((RdfTerm _, RdfTerm value) in _source.Pairs(parameter))
            {
                Add(value);
            }
        }

        foreach (RdfTerm parameter in (ReadOnlySpan<RdfTerm>)[Vocabulary.And, Vocabulary.Or, Vocabulary.Xone])
        {
            foreach ((RdfTerm _, RdfTerm list) in _source.Pairs(parameter))
            {
                foreach (RdfTerm member in _source.List(list) ?? [])
                {
                    Add(member);
                }
            }
        }

        List<RdfTerm> ordered = [.. found];
        ordered.Sort((a, b) => string.CompareOrdinal(Strings.NTriples(a), Strings.NTriples(b)));
        return ordered;
    }

    /// <summary><c>sh:NodeShape</c>, <c>sh:PropertyShape</c>, <c>sh:ShapeClass</c>, and every class declared their subclass here.</summary>
    private HashSet<RdfTerm> ShapeTypes()
    {
        HashSet<RdfTerm> types = new(RdfTerm.Comparer) { Vocabulary.NodeShapeClass, Vocabulary.PropertyShapeClass, Vocabulary.ShapeClassType };
        Queue<RdfTerm> pending = new(types);
        while (pending.Count > 0)
        {
            foreach (RdfTerm sub in _source.Subjects(Vocabulary.RdfsSubClassOf, pending.Dequeue()))
            {
                if (types.Add(sub))
                {
                    pending.Enqueue(sub);
                }
            }
        }

        return types;
    }

    private bool IsShaclInstance(RdfTerm node, RdfTerm cls)
    {
        if (_source.IsInstanceOf(node, cls))
        {
            return true;
        }

        // sh:ShapeClass is a subclass of sh:NodeShape and rdfs:Class (§3.1.3.3), whether or not the graph says so.
        return (cls.Equals(Vocabulary.NodeShapeClass) || cls.Equals(Vocabulary.RdfsClass))
            && _source.IsInstanceOf(node, Vocabulary.ShapeClassType);
    }

    private void ReadShape(Shape shape)
    {
        RdfTerm node = shape.Node;

        // §3.3: at most one sh:path, a well-formed path.
        List<RdfTerm> paths = _source.Objects(node, Vocabulary.Path);
        if (paths.Count > 1)
        {
            IllFormed(shape, "§3.3", "a shape has at most one value for sh:path");
        }
        else if (paths.Count == 1)
        {
            shape.Path = ReadPath(shape, paths[0], "sh:path");
        }

        if (shape.Path is not null && _source.IsInstanceOf(node, Vocabulary.NodeShapeClass))
        {
            IllFormed(shape, "§3.2", "SHACL instances of sh:NodeShape cannot have a value for sh:path");
        }

        if (shape.Path is null && paths.Count == 0 && _source.IsInstanceOf(node, Vocabulary.PropertyShapeClass))
        {
            IllFormed(shape, "§3.3", "SHACL instances of sh:PropertyShape have one value for sh:path");
        }

        ReadTargets(shape);
        ReadCharacteristics(shape);
        ReadConstraints(shape);
        foreach (Constraint constraint in shape.Constraints)
        {
            ApplyReifiers(shape, constraint);
        }
    }

    private void ReadTargets(Shape shape)
    {
        RdfTerm node = shape.Node;
        foreach (RdfTerm target in _source.Objects(node, Vocabulary.TargetNode))
        {
            // §5.1, §5.2: a constant. A blank node is a constant here too, as the suite uses one; it
            // names a node of the data only when the shapes graph is the data (ADR 0151).
            shape.TargetNodes.Add(target);
        }

        foreach (RdfTerm cls in _source.Objects(node, Vocabulary.TargetClass))
        {
            if (cls.Kind == RdfTermKind.Iri)
            {
                shape.TargetClasses.Add(cls);
            }
            else
            {
                IllFormed(shape, "§3.1.3.2", "each value of sh:targetClass is an IRI");
            }
        }

        // §3.1.3.3: a shape that is also a class targets its instances.
        bool isShape = IsShaclInstance(node, Vocabulary.NodeShapeClass) || IsShaclInstance(node, Vocabulary.PropertyShapeClass);
        if (isShape && IsShaclInstance(node, Vocabulary.RdfsClass))
        {
            if (node.Kind == RdfTermKind.Iri)
            {
                shape.TargetClasses.Add(node);
            }
            else
            {
                IllFormed(shape, "§3.1.3.3", "a shape that is also a SHACL instance of rdfs:Class is an IRI");
            }
        }

        foreach ((RdfTerm predicate, List<RdfTerm> into, string section) in (ReadOnlySpan<(RdfTerm, List<RdfTerm>, string)>)
            [(Vocabulary.TargetSubjectsOf, shape.TargetSubjectsOf, "§3.1.3.4"), (Vocabulary.TargetObjectsOf, shape.TargetObjectsOf, "§3.1.3.5")])
        {
            foreach (RdfTerm p in _source.Objects(node, predicate))
            {
                if (p.Kind == RdfTermKind.Iri)
                {
                    into.Add(p);
                }
                else
                {
                    IllFormed(shape, section, "the values of " + Name(predicate) + " are IRIs");
                }
            }
        }

        foreach (RdfTerm where in _source.Objects(node, Vocabulary.TargetWhere))
        {
            if (Reference(shape, where, "§3.1.3.6", Vocabulary.TargetWhere) is int index)
            {
                shape.TargetWhere.Add(index);
            }
        }
    }

    private void ReadCharacteristics(Shape shape)
    {
        RdfTerm node = shape.Node;
        if (AtMostOne(shape, Vocabulary.Severity, "§3.1.4") is { } severity)
        {
            if (severity.Kind == RdfTermKind.Iri)
            {
                shape.Severity = severity;
            }
            else
            {
                IllFormed(shape, "§3.1.4", "each value of sh:severity is an IRI");
            }
        }

        List<RdfTerm> messages = _source.Objects(node, Vocabulary.Message);
        foreach (RdfTerm message in messages)
        {
            if (message.Kind != RdfTermKind.Literal)
            {
                IllFormed(shape, "§3.1.5", "the values of sh:message are literals");
            }
        }

        shape.Messages = [.. messages];

        if (AtMostOne(shape, Vocabulary.Deactivated, "§3.1.6") is { } deactivated)
        {
            if (Boolean(deactivated) is bool value)
            {
                shape.Deactivated = value;
            }
            else
            {
                IllFormed(shape, "§3.1.6", "sh:deactivated must have true or false as its only output node");
            }
        }

        foreach ((RdfTerm predicate, bool isDefault) in (ReadOnlySpan<(RdfTerm, bool)>)[(Vocabulary.Values, false), (Vocabulary.DefaultValue, true)])
        {
            if (AtMostOne(shape, predicate, "§3.3") is not { } expression)
            {
                continue;
            }

            if (shape.Path is not PredicatePath)
            {
                IllFormed(shape, "§3.3", "a property shape can only have values for sh:values and sh:defaultValue when its sh:path is a predicate path");
            }
            else if (expression.Kind is not (RdfTermKind.Iri or RdfTermKind.Literal))
            {
                _problems.Add(new ShaclProblem(
                    ShaclProblemKind.MissingExtension,
                    node,
                    null,
                    Name(predicate) + " at " + Strings.NTriples(node) + " is a node expression beyond the constants of SHACL Core (§5); SHACL 1.2 Node Expressions are not supported (ADR 0149)."));
            }
            else if (isDefault)
            {
                shape.DefaultValue = expression;
            }
            else
            {
                shape.Values = expression;
            }
        }
    }

    private void ReadConstraints(Shape shape)
    {
        RdfTerm node = shape.Node;
        bool property = shape.Path is not null;

        foreach (RdfTerm value in _source.Objects(node, Vocabulary.Class))
        {
            if (IriOrIriList(shape, value, "§7.1.1", "sh:class") is { } classes)
            {
                Add(shape, new ClassConstraint(classes), (Vocabulary.Class, value));
            }
        }

        if (AtMostOne(shape, Vocabulary.Datatype, "§7.1.2") is { } datatype && IriOrIriList(shape, datatype, "§7.1.2", "sh:datatype") is { } datatypes)
        {
            Add(shape, new DatatypeConstraint(datatypes), (Vocabulary.Datatype, datatype));
        }

        if (AtMostOne(shape, Vocabulary.NodeKind, "§7.1.3") is { } nodeKind && IriOrIriList(shape, nodeKind, "§7.1.3", "sh:nodeKind") is { } kinds)
        {
            bool known = true;
            foreach (RdfTerm kind in kinds)
            {
                known &= NodeKindConstraint.IsNodeKind(kind)
                    && (nodeKind.Kind == RdfTermKind.Iri || kind.Equals(Vocabulary.IRI) || kind.Equals(Vocabulary.BlankNode) || kind.Equals(Vocabulary.Literal) || kind.Equals(Vocabulary.TripleTerm));
            }

            if (known)
            {
                Add(shape, new NodeKindConstraint(kinds), (Vocabulary.NodeKind, nodeKind));
            }
            else
            {
                IllFormed(shape, "§7.1.3", "the values of sh:nodeKind are instances of sh:NodeKind");
            }
        }

        foreach ((RdfTerm parameter, bool minimum) in (ReadOnlySpan<(RdfTerm, bool)>)[(Vocabulary.MinCount, true), (Vocabulary.MaxCount, false)])
        {
            string section = minimum ? "§7.2.1" : "§7.2.2";
            if (AtMostOne(shape, parameter, section) is not { } count)
            {
                continue;
            }

            if (!property)
            {
                IllFormed(shape, section, "node shapes cannot have any value for " + Name(parameter));
            }
            else if (Integer(count) is long bound)
            {
                Add(shape, new CountConstraint(minimum, bound), (parameter, count));
            }
            else
            {
                IllFormed(shape, section, "the values of " + Name(parameter) + " are literals with datatype xsd:integer");
            }
        }

        foreach ((RdfTerm parameter, RdfTerm component, bool lower, bool inclusive, string section) in (ReadOnlySpan<(RdfTerm, RdfTerm, bool, bool, string)>)
        [
            (Vocabulary.MinExclusive, Vocabulary.MinExclusiveConstraintComponent, true, false, "§7.3.1"),
            (Vocabulary.MinInclusive, Vocabulary.MinInclusiveConstraintComponent, true, true, "§7.3.2"),
            (Vocabulary.MaxExclusive, Vocabulary.MaxExclusiveConstraintComponent, false, false, "§7.3.3"),
            (Vocabulary.MaxInclusive, Vocabulary.MaxInclusiveConstraintComponent, false, true, "§7.3.4"),
        ])
        {
            if (AtMostOne(shape, parameter, section) is not { } bound)
            {
                continue;
            }

            if (bound.Kind == RdfTermKind.Literal)
            {
                Add(shape, new RangeConstraint(component, bound, lower, inclusive), (parameter, bound));
            }
            else
            {
                IllFormed(shape, section, "the values of " + Name(parameter) + " are literals");
            }
        }

        foreach ((RdfTerm parameter, bool minimum, string section) in (ReadOnlySpan<(RdfTerm, bool, string)>)
            [(Vocabulary.MinLength, true, "§7.4.1"), (Vocabulary.MaxLength, false, "§7.4.2")])
        {
            if (AtMostOne(shape, parameter, section) is not { } length)
            {
                continue;
            }

            if (Integer(length) is long bound)
            {
                Add(shape, new LengthConstraint(minimum, bound), (parameter, length));
            }
            else
            {
                IllFormed(shape, section, "the values of " + Name(parameter) + " are literals with datatype xsd:integer");
            }
        }

        ReadPattern(shape);

        if (AtMostOne(shape, Vocabulary.SingleLine, "§7.4.4") is { } singleLine)
        {
            if (Boolean(singleLine) is not bool on)
            {
                IllFormed(shape, "§7.4.4", "the values of sh:singleLine are literals with datatype xsd:boolean");
            }
            else if (on)
            {
                Add(shape, new SingleLineConstraint(), (Vocabulary.SingleLine, singleLine));
            }
        }

        if (AtMostOne(shape, Vocabulary.LanguageIn, "§7.4.5") is { } languageIn)
        {
            List<RdfTerm>? ranges = _source.List(languageIn);
            List<string> texts = [];
            foreach (RdfTerm range in ranges ?? [])
            {
                if (IsString(range))
                {
                    texts.Add(Encoding.UTF8.GetString(range.Lexical));
                }
            }

            if (ranges is null || texts.Count != ranges.Count)
            {
                IllFormed(shape, "§7.4.5", "each value of sh:languageIn is a SHACL list of literals with datatype xsd:string");
            }
            else
            {
                Add(shape, new LanguageInConstraint([.. texts]), (Vocabulary.LanguageIn, languageIn));
            }
        }

        if (AtMostOne(shape, Vocabulary.UniqueLang, "§7.4.6") is { } uniqueLang)
        {
            if (!property)
            {
                IllFormed(shape, "§7.4.6", "node shapes cannot have any value for sh:uniqueLang");
            }
            else if (Boolean(uniqueLang) is not bool on)
            {
                IllFormed(shape, "§7.4.6", "the values of sh:uniqueLang are literals with datatype xsd:boolean");
            }
            else if (on)
            {
                Add(shape, new UniqueLangConstraint(), (Vocabulary.UniqueLang, uniqueLang));
            }
        }

        ReadLists(shape);
        ReadPairs(shape, property);
        ReadLogical(shape, property);
        ReadQualified(shape, property);
        ReadReifierShape(shape);
        ReadClosed(shape);

        foreach (RdfTerm value in _source.Objects(node, Vocabulary.HasValue))
        {
            Add(shape, new HasValueConstraint(value), (Vocabulary.HasValue, value));
        }

        if (AtMostOne(shape, Vocabulary.In, "§7.9.3") is { } inList)
        {
            if (_source.List(inList) is { } members)
            {
                Add(shape, new InConstraint([.. members]), (Vocabulary.In, inList));
            }
            else
            {
                IllFormed(shape, "§7.9.3", "each value of sh:in is a SHACL list");
            }
        }

        foreach (RdfTerm value in _source.Objects(node, Vocabulary.RootClass))
        {
            if (IriOrIriList(shape, value, "§7.9.4", "sh:rootClass") is { } roots)
            {
                Add(shape, new RootClassConstraint(roots), (Vocabulary.RootClass, value));
            }
        }

        foreach (RdfTerm value in _source.Objects(node, Vocabulary.UniqueValuesFor))
        {
            if (IriOrIriList(shape, value, "§7.9.5", "sh:uniqueValuesFor") is { } properties)
            {
                Add(shape, new UniqueValuesForConstraint(properties), (Vocabulary.UniqueValuesFor, value));
            }
        }
    }

    private void ReadPattern(Shape shape)
    {
        RdfTerm? pattern = AtMostOne(shape, Vocabulary.Pattern, "§7.4.3");
        RdfTerm? flags = AtMostOne(shape, Vocabulary.Flags, "§7.4.3");
        if (pattern is null)
        {
            return;
        }

        if (!IsString(pattern) || (flags is not null && !IsString(flags)))
        {
            IllFormed(shape, "§7.4.3", "the values of sh:pattern and sh:flags are literals with datatype xsd:string");
            return;
        }

        string flagText = flags is null ? string.Empty : Encoding.UTF8.GetString(flags.Lexical);
        if (!XPathRegex.TryTranslate(Encoding.UTF8.GetString(pattern.Lexical), flagText, out XPathRegexTranslation translation, out XPathRegexError error))
        {
            IllFormed(shape, "§7.4.3", "the values of sh:pattern are valid pattern arguments for the SPARQL REGEX function: " + error);
            return;
        }

        PatternConstraint constraint = new(translation);
        Add(shape, constraint, flags is null ? [(Vocabulary.Pattern, pattern)] : [(Vocabulary.Pattern, pattern), (Vocabulary.Flags, flags)]);
    }

    private void ReadLists(Shape shape)
    {
        RdfTerm node = shape.Node;
        foreach (RdfTerm member in _source.Objects(node, Vocabulary.MemberShape))
        {
            if (Reference(shape, member, "§7.5.1", Vocabulary.MemberShape) is int index)
            {
                Add(shape, new ListConstraint(Vocabulary.MemberShapeConstraintComponent, index, null, null, false), (Vocabulary.MemberShape, member));
            }
        }

        foreach ((RdfTerm parameter, RdfTerm component, bool minimum, string section) in (ReadOnlySpan<(RdfTerm, RdfTerm, bool, string)>)
        [
            (Vocabulary.MinListLength, Vocabulary.MinListLengthConstraintComponent, true, "§7.5.2"),
            (Vocabulary.MaxListLength, Vocabulary.MaxListLengthConstraintComponent, false, "§7.5.3"),
        ])
        {
            if (AtMostOne(shape, parameter, section) is not { } length)
            {
                continue;
            }

            if (Integer(length) is long bound && bound >= 0)
            {
                Add(shape, new ListConstraint(component, null, minimum ? bound : null, minimum ? null : bound, false), (parameter, length));
            }
            else
            {
                IllFormed(shape, section, "the values of " + Name(parameter) + " are integers greater than or equal to 0");
            }
        }

        if (AtMostOne(shape, Vocabulary.UniqueMembers, "§7.5.4") is { } unique)
        {
            if (Boolean(unique) is bool on)
            {
                Add(shape, new ListConstraint(Vocabulary.UniqueMembersConstraintComponent, null, null, null, on), (Vocabulary.UniqueMembers, unique));
            }
            else
            {
                IllFormed(shape, "§7.5.4", "the values of sh:uniqueMembers are literals with datatype xsd:boolean");
            }
        }
    }

    private void ReadPairs(Shape shape, bool property)
    {
        foreach ((RdfTerm parameter, PairKind kind, string section) in (ReadOnlySpan<(RdfTerm, PairKind, string)>)
        [
            (Vocabulary.EqualsParameter, PairKind.Equals, "§7.6.1"),
            (Vocabulary.Disjoint, PairKind.Disjoint, "§7.6.2"),
            (Vocabulary.SubsetOf, PairKind.SubsetOf, "§7.6.3"),
            (Vocabulary.LessThan, PairKind.LessThan, "§7.6.4"),
            (Vocabulary.LessThanOrEquals, PairKind.LessThanOrEquals, "§7.6.5"),
        ])
        {
            foreach (RdfTerm value in _source.Objects(shape.Node, parameter))
            {
                if (!property && kind is PairKind.LessThan or PairKind.LessThanOrEquals)
                {
                    IllFormed(shape, section, "node shapes cannot have any value for " + Name(parameter));
                    continue;
                }

                if (ReadPath(shape, value, Name(parameter)) is { } path)
                {
                    Add(shape, new PairConstraint(kind, path), (parameter, value));
                }
            }
        }
    }

    private void ReadLogical(Shape shape, bool property)
    {
        RdfTerm node = shape.Node;
        foreach (RdfTerm value in _source.Objects(node, Vocabulary.Not))
        {
            if (Reference(shape, value, "§7.7.1", Vocabulary.Not) is int index)
            {
                Add(shape, new NotConstraint(index), (Vocabulary.Not, value));
            }
        }

        foreach ((RdfTerm parameter, LogicalKind kind, string section) in (ReadOnlySpan<(RdfTerm, LogicalKind, string)>)
            [(Vocabulary.And, LogicalKind.And, "§7.7.2"), (Vocabulary.Or, LogicalKind.Or, "§7.7.3"), (Vocabulary.Xone, LogicalKind.Xone, "§7.7.4")])
        {
            foreach (RdfTerm list in _source.Objects(node, parameter))
            {
                if (_source.List(list) is not { } members)
                {
                    IllFormed(shape, section, "each value of " + Name(parameter) + " is a SHACL list");
                    continue;
                }

                List<int> indexes = [];
                foreach (RdfTerm member in members)
                {
                    if (Reference(shape, member, section, parameter) is int index)
                    {
                        indexes.Add(index);
                    }
                }

                if (indexes.Count == members.Count)
                {
                    Add(shape, new LogicalConstraint(kind, [.. indexes]), (parameter, list));
                }
            }
        }

        foreach (RdfTerm value in _source.Objects(node, Vocabulary.Node))
        {
            if (Reference(shape, value, "§7.8.1", Vocabulary.Node) is int index)
            {
                if (_shapes[index].Path is not null || _source.Objects(value, Vocabulary.Path).Count > 0)
                {
                    IllFormed(shape, "§7.8.1", "the values of sh:node are well-formed node shapes");
                }
                else
                {
                    Add(shape, new NodeConstraint(index), (Vocabulary.Node, value));
                }
            }
        }

        foreach (RdfTerm value in _source.Objects(node, Vocabulary.Property))
        {
            if (Reference(shape, value, "§7.8.2", Vocabulary.Property) is int index)
            {
                if (_source.Objects(value, Vocabulary.Path).Count != 1)
                {
                    IllFormed(shape, "§7.8.2", "each value of sh:property is a well-formed property shape");
                }
                else
                {
                    Add(shape, new PropertyConstraint(index), (Vocabulary.Property, value));
                }
            }
        }

        foreach (RdfTerm value in _source.Objects(node, Vocabulary.SomeValue))
        {
            if (Reference(shape, value, "§7.8.3", Vocabulary.SomeValue) is int index)
            {
                Add(shape, new SomeValueConstraint(index), (Vocabulary.SomeValue, value));
            }
        }
    }

    private void ReadQualified(Shape shape, bool property)
    {
        RdfTerm? qualified = AtMostOne(shape, Vocabulary.QualifiedValueShape, "§7.8.4");
        RdfTerm? min = AtMostOne(shape, Vocabulary.QualifiedMinCount, "§7.8.4");
        RdfTerm? max = AtMostOne(shape, Vocabulary.QualifiedMaxCount, "§7.8.4");
        RdfTerm? disjoint = AtMostOne(shape, Vocabulary.QualifiedValueShapesDisjoint, "§7.8.4");
        if (qualified is null || (min is null && max is null))
        {
            return;
        }

        if (!property)
        {
            IllFormed(shape, "§7.8.4", "node shapes cannot have any value for sh:qualifiedValueShape");
            return;
        }

        if (Reference(shape, qualified, "§7.8.4", Vocabulary.QualifiedValueShape) is not int target)
        {
            return;
        }

        bool isDisjoint = false;
        if (disjoint is not null)
        {
            if (Boolean(disjoint) is bool b)
            {
                isDisjoint = b;
            }
            else
            {
                IllFormed(shape, "§7.8.4", "the values of sh:qualifiedValueShapesDisjoint are literals with datatype xsd:boolean");
            }
        }

        // §7.8.4's sibling shapes: sh:property/sh:qualifiedValueShape of every shape with this one as sh:property, but its own.
        List<int> siblings = [];
        if (isDisjoint)
        {
            foreach (RdfTerm parent in _source.Subjects(Vocabulary.Property, shape.Node))
            {
                foreach (RdfTerm sibling in _source.Objects(parent, Vocabulary.Property))
                {
                    foreach (RdfTerm other in _source.Objects(sibling, Vocabulary.QualifiedValueShape))
                    {
                        if (!other.Equals(qualified) && _index.TryGetValue(other, out int index) && !siblings.Contains(index))
                        {
                            siblings.Add(index);
                        }
                    }
                }
            }
        }

        List<(RdfTerm, RdfTerm)> common = [(Vocabulary.QualifiedValueShape, qualified)];
        if (disjoint is not null)
        {
            common.Add((Vocabulary.QualifiedValueShapesDisjoint, disjoint));
        }

        foreach ((RdfTerm? bound, bool minimum) in (ReadOnlySpan<(RdfTerm?, bool)>)[(min, true), (max, false)])
        {
            if (bound is null)
            {
                continue;
            }

            if (Integer(bound) is long count)
            {
                RdfTerm parameter = minimum ? Vocabulary.QualifiedMinCount : Vocabulary.QualifiedMaxCount;
                Add(shape, new QualifiedConstraint(target, [.. siblings], minimum, count), [.. common, (parameter, bound)]);
            }
            else
            {
                IllFormed(shape, "§7.8.4", "the values of sh:qualifiedMinCount and sh:qualifiedMaxCount are literals with datatype xsd:integer");
            }
        }
    }

    private void ReadReifierShape(Shape shape)
    {
        List<RdfTerm> reifierShapes = _source.Objects(shape.Node, Vocabulary.ReifierShape);
        RdfTerm? required = AtMostOne(shape, Vocabulary.ReificationRequired, "§7.8.5");
        bool isRequired = false;
        if (required is not null)
        {
            if (Boolean(required) is bool b)
            {
                isRequired = b;
            }
            else
            {
                IllFormed(shape, "§7.8.5", "the values of sh:reificationRequired are literals with datatype xsd:boolean");
            }
        }

        if (reifierShapes.Count == 0)
        {
            return;
        }

        if (shape.Path is not PredicatePath predicate)
        {
            IllFormed(shape, "§7.8.5", "if a value for sh:reifierShape is given, sh:path values are constrained to IRIs");
            return;
        }

        foreach (RdfTerm value in reifierShapes)
        {
            if (Reference(shape, value, "§7.8.5", Vocabulary.ReifierShape) is int index)
            {
                List<(RdfTerm, RdfTerm)> triples = [(Vocabulary.ReifierShape, value)];
                if (required is not null)
                {
                    triples.Add((Vocabulary.ReificationRequired, required));
                }

                Add(shape, new ReifierShapeConstraint(index, isRequired, predicate.Predicate), [.. triples]);
            }
        }
    }

    private void ReadClosed(Shape shape)
    {
        RdfTerm? closed = AtMostOne(shape, Vocabulary.Closed, "§7.9.1");
        RdfTerm? ignored = AtMostOne(shape, Vocabulary.IgnoredProperties, "§7.9.1");
        if (closed is null)
        {
            return;
        }

        bool byTypes = closed.Equals(Vocabulary.ByTypes);
        if (!byTypes && Boolean(closed) is not bool)
        {
            IllFormed(shape, "§7.9.1", "the values of sh:closed are literals with datatype xsd:boolean or the IRI sh:ByTypes");
            return;
        }

        if (!byTypes && Boolean(closed) == false)
        {
            return;
        }

        List<RdfTerm> allowed = [];
        if (!byTypes)
        {
            foreach (RdfTerm property in _source.Objects(shape.Node, Vocabulary.Property))
            {
                foreach (RdfTerm path in _source.Objects(property, Vocabulary.Path))
                {
                    if (path.Kind == RdfTermKind.Iri)
                    {
                        allowed.Add(path);
                    }
                }
            }
        }

        if (ignored is not null)
        {
            List<RdfTerm>? members = _source.List(ignored);
            if (members is null || members.Exists(m => m.Kind != RdfTermKind.Iri))
            {
                IllFormed(shape, "§7.9.1", "the values of sh:ignoredProperties are SHACL lists of IRIs");
                return;
            }

            allowed.AddRange(members);
        }

        List<(RdfTerm, RdfTerm)> triples = [(Vocabulary.Closed, closed)];
        if (ignored is not null)
        {
            triples.Add((Vocabulary.IgnoredProperties, ignored));
        }

        Add(shape, new ClosedConstraint([.. allowed], byTypes), [.. triples]);
    }

    // ----------------------------------------------------------------- paths

    /// <summary>§4: a well-formed SHACL property path, or null with the problem recorded.</summary>
    private ShapePath? ReadPath(Shape shape, RdfTerm node, string where)
    {
        ShapePath? path = ReadPath(node, new HashSet<RdfTerm>(RdfTerm.Comparer), out string? problem);
        if (path is null)
        {
            IllFormed(shape, "§4", "the value of " + where + " is a well-formed SHACL property path: " + problem);
        }

        return path;
    }

    private ShapePath? ReadPath(RdfTerm node, HashSet<RdfTerm> visiting, out string? problem)
    {
        problem = null;
        if (node.Kind == RdfTermKind.Iri)
        {
            return new PredicatePath(node);
        }

        if (node.Kind != RdfTermKind.BlankNode)
        {
            problem = Strings.NTriples(node) + " is neither an IRI nor a blank node";
            return null;
        }

        if (!visiting.Add(node))
        {
            problem = "a blank node path references itself (§4)";
            return null;
        }

        try
        {
            // §4.2: a SHACL list of at least two members.
            if (_source.Objects(node, Vocabulary.RdfFirst).Count > 0)
            {
                List<RdfTerm>? members = _source.List(node);
                if (members is null || members.Count < 2)
                {
                    problem = "a sequence path is a SHACL list with at least two members (§4.2)";
                    return null;
                }

                List<ShapePath> steps = [];
                foreach (RdfTerm member in members)
                {
                    if (ReadPath(member, visiting, out problem) is not { } step)
                    {
                        return null;
                    }

                    steps.Add(step);
                }

                return new SequencePath([.. steps]);
            }

            List<(RdfTerm Predicate, RdfTerm Object)> triples = _source.Triples(node);
            if (triples.Count != 1)
            {
                problem = "a blank node path is the subject of exactly one triple (§4.3–§4.7)";
                return null;
            }

            (RdfTerm predicate, RdfTerm value) = triples[0];
            if (predicate.Equals(Vocabulary.AlternativePath))
            {
                List<RdfTerm>? members = _source.List(value);
                if (members is null || members.Count < 2)
                {
                    problem = "an alternative path's list has at least two members (§4.3)";
                    return null;
                }

                List<ShapePath> alternatives = [];
                foreach (RdfTerm member in members)
                {
                    if (ReadPath(member, visiting, out problem) is not { } alternative)
                    {
                        return null;
                    }

                    alternatives.Add(alternative);
                }

                return new AlternativePath([.. alternatives]);
            }

            UnaryPathKind? kind = predicate.Equals(Vocabulary.InversePath) ? UnaryPathKind.Inverse
                : predicate.Equals(Vocabulary.ZeroOrMorePath) ? UnaryPathKind.ZeroOrMore
                : predicate.Equals(Vocabulary.OneOrMorePath) ? UnaryPathKind.OneOrMore
                : predicate.Equals(Vocabulary.ZeroOrOnePath) ? UnaryPathKind.ZeroOrOne
                : null;
            if (kind is null)
            {
                problem = Name(predicate) + " is not a path predicate (§4)";
                return null;
            }

            return ReadPath(value, visiting, out problem) is { } inner ? new UnaryPath(kind.Value, inner) : null;
        }
        finally
        {
            visiting.Remove(node);
        }
    }

    // --------------------------------------------------------------- helpers

    /// <summary>A reference to a shape through a shape-expecting parameter: its index, or null with the problem recorded.</summary>
    private int? Reference(Shape shape, RdfTerm value, string section, RdfTerm parameter)
    {
        if (_index.TryGetValue(value, out int index))
        {
            return index;
        }

        IllFormed(shape, section, "the values of " + Name(parameter) + " must be well-formed shapes");
        return null;
    }

    private void Add(Shape shape, Constraint constraint, (RdfTerm Parameter, RdfTerm Value) triple) => Add(shape, constraint, [triple]);

    private void Add(Shape shape, Constraint constraint, ImmutableArray<(RdfTerm Parameter, RdfTerm Value)> triples)
    {
        _declared[constraint] = triples;
        shape.Constraints.Add(constraint);
    }

    private readonly Dictionary<Constraint, ImmutableArray<(RdfTerm, RdfTerm)>> _declared = [];

    /// <summary>§3.1.4–§3.1.6: the reifiers of the triples that declare a constraint set its severity, message and activation.</summary>
    private void ApplyReifiers(Shape shape, Constraint constraint)
    {
        if (!_declared.TryGetValue(constraint, out ImmutableArray<(RdfTerm, RdfTerm)> triples))
        {
            return;
        }

        List<RdfTerm> severities = [], deactivations = [];
        List<RdfTerm> messages = [];
        foreach ((RdfTerm parameter, RdfTerm value) in triples)
        {
            if (!_reifiers.TryGetValue((shape.Node, parameter, value), out List<RdfTerm>? reifiers))
            {
                continue;
            }

            foreach (RdfTerm reifier in reifiers)
            {
                severities.AddRange(_source.Objects(reifier, Vocabulary.Severity));
                messages.AddRange(_source.Objects(reifier, Vocabulary.Message));
                deactivations.AddRange(_source.Objects(reifier, Vocabulary.Deactivated));
            }
        }

        if (severities.Count > 1)
        {
            IllFormed(shape, "§3.1.4", "a shapes graph specifies at most one value for sh:severity in the reifiers of a constraint's triples");
        }
        else if (severities.Count == 1)
        {
            constraint.Severity = severities[0];
        }

        if (messages.Count > 0)
        {
            constraint.Messages = [.. messages];
        }

        if (deactivations.Count > 1)
        {
            IllFormed(shape, "§3.1.6", "a constraint's triple has at most one reifier with a value for sh:deactivated");
        }
        else if (deactivations.Count == 1)
        {
            constraint.Deactivated = Boolean(deactivations[0]) == true;
        }
    }

    private void IndexReifiers()
    {
        foreach ((RdfTerm reifier, RdfTerm triple) in _source.Pairs(Vocabulary.RdfReifies))
        {
            if (triple.Kind != RdfTermKind.TripleTerm)
            {
                continue;
            }

            (RdfTerm, RdfTerm, RdfTerm) key = (triple.Subject!, triple.Predicate!, triple.Object!);
            if (!_reifiers.TryGetValue(key, out List<RdfTerm>? list))
            {
                list = [];
                _reifiers[key] = list;
            }

            list.Add(reifier);
        }
    }

    /// <summary>Without an extension, a shapes graph that uses SHACL-SPARQL is refused rather than validated without it (ADR 0148).</summary>
    private void RefuseWithoutExtension()
    {
        RdfTerm sparql = Vocabulary.Sparql;
        RdfTerm componentClass = RdfTerm.Iri(Encoding.UTF8.GetBytes(Vocabulary.ShaclNamespace + "ConstraintComponent"));
        List<RdfTerm> users = _source.SubjectsOf(sparql);
        users.AddRange(_source.Subjects(Vocabulary.RdfType, componentClass));
        foreach (RdfTerm user in users)
        {
            _problems.Add(new ShaclProblem(
                ShaclProblemKind.MissingExtension,
                user,
                null,
                Strings.NTriples(user) + " uses SHACL-SPARQL, and no extension implements it: pass Varve.Shacl.Sparql's extension in ShaclOptions.Extensions (ADR 0148)."));
        }
    }

    private RdfTerm? AtMostOne(Shape shape, RdfTerm parameter, string section)
    {
        List<RdfTerm> values = _source.Objects(shape.Node, parameter);
        if (values.Count > 1)
        {
            IllFormed(shape, section, "a shape has at most one value for " + Name(parameter));
        }

        return values.Count == 0 ? null : values[0];
    }

    private ImmutableArray<RdfTerm>? IriOrIriList(Shape shape, RdfTerm value, string section, string parameter)
    {
        if (value.Kind == RdfTermKind.Iri && !value.Equals(Vocabulary.RdfNil))
        {
            return [value];
        }

        if (value.Kind is RdfTermKind.BlankNode or RdfTermKind.Iri && _source.List(value) is { } members && members.TrueForAll(m => m.Kind == RdfTermKind.Iri))
        {
            return [.. members];
        }

        IllFormed(shape, section, "the values of " + parameter + " are IRIs or well-formed SHACL lists of IRIs");
        return null;
    }

    private void IllFormed(Shape shape, string section, string rule) =>
        _problems.Add(new ShaclProblem(
            ShaclProblemKind.IllFormedShape,
            shape.Node,
            null,
            "The shape " + Strings.NTriples(shape.Node) + " is ill-formed: SHACL 1.2 Core " + section + ", " + rule + "."));

    /// <summary>
    /// A boolean parameter: null when it is not an <c>xsd:boolean</c> literal,
    /// true only for the literal <c>true</c>. The suite's uniqueLang-002 holds
    /// that <c>"1"^^xsd:boolean</c>, a well-formed true, does not activate a
    /// constraint, since the specification speaks of <c>true</c> only.
    /// </summary>
    private static bool? Boolean(RdfTerm term) =>
        term.Kind == RdfTermKind.Literal && term.DatatypeIri.SequenceEqual(Vocabulary.XsdBoolean.Lexical) && XsdBoolean.TryParse(term.Lexical, out _)
            ? term.Lexical.SequenceEqual("true"u8)
            : null;

    private static long? Integer(RdfTerm term) =>
        term.Kind == RdfTermKind.Literal && term.DatatypeIri.SequenceEqual(Vocabulary.XsdInteger.Lexical) && XsdInteger.TryParse(term.Lexical, out XsdInteger value)
            ? value.Value
            : null;

    private static bool IsString(RdfTerm term) =>
        term.Kind == RdfTermKind.Literal && term.Language.IsEmpty && term.DatatypeIri.SequenceEqual(Vocabulary.XsdString.Lexical);

    private static string Name(RdfTerm iri)
    {
        string text = Encoding.UTF8.GetString(iri.Lexical);
        return text.StartsWith(Vocabulary.ShaclNamespace, StringComparison.Ordinal) ? "sh:" + text[Vocabulary.ShaclNamespace.Length..] : "<" + text + ">";
    }

    private sealed class TripleComparer : IEqualityComparer<(RdfTerm, RdfTerm, RdfTerm)>
    {
        public bool Equals((RdfTerm, RdfTerm, RdfTerm) x, (RdfTerm, RdfTerm, RdfTerm) y) =>
            RdfTerm.Comparer.Equals(x.Item1, y.Item1) && RdfTerm.Comparer.Equals(x.Item2, y.Item2) && RdfTerm.Comparer.Equals(x.Item3, y.Item3);

        public int GetHashCode((RdfTerm, RdfTerm, RdfTerm) obj) =>
            HashCode.Combine(RdfTerm.Comparer.GetHashCode(obj.Item1), RdfTerm.Comparer.GetHashCode(obj.Item2), RdfTerm.Comparer.GetHashCode(obj.Item3));
    }
}
