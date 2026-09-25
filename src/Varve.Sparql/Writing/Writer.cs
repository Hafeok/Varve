// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using Varve.Rdf;
using Varve.Sparql.Algebra;

namespace Varve.Sparql.Writing;

/// <summary>
/// The algebra back to text that parses to the identical tree
/// (<c>docs/spec/sparql-algebra.md</c> §6). One canonical layout: every
/// join operand in its own braces, every binary expression parenthesised,
/// every IRI in full.
/// </summary>
internal ref partial struct Writer
{
    private static readonly RdfTerm XsdIntegerTerm = RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#integer"u8);
    private static readonly RdfTerm XsdDecimalTerm = RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#decimal"u8);
    private static readonly RdfTerm XsdDoubleTerm = RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#double"u8);
    private static readonly RdfTerm XsdBooleanTerm = RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#boolean"u8);

    private Output _out;

    // Whether the last element written in the current group was a run of
    // triples: the next Bgp then needs braces of its own, or the two would
    // merge on re-parse (§18.3.2.6). Nothing else merges.
    private bool _lastTriples;

    internal Writer(IBufferWriter<byte> writer)
    {
        _out = new Output(writer);
        _lastTriples = false;
    }

    internal void Complete() => _out.Complete();

    // --- queries -----------------------------------------------------------

    internal void Write(Query query)
    {
        WritePrologue(query.Prologue);

        switch (query)
        {
            case SelectQuery select:
                WriteSelectLevel(select.Pattern, query.Dataset);
                break;
            case ConstructQuery construct:
                _out.Write("CONSTRUCT {"u8);
                WriteTemplate(construct.Template);
                _out.Write("}\n"u8);
                WriteDataset(query.Dataset);
                WriteLevel(construct.Pattern, projecting: false);
                break;
            case AskQuery ask:
                _out.Write("ASK\n"u8);
                WriteDataset(query.Dataset);
                WriteLevel(ask.Pattern, projecting: false);
                break;
            case DescribeQuery describe:
                _out.Write("DESCRIBE"u8);

                if (describe.Resources.IsEmpty)
                {
                    _out.Write(" *"u8);
                }

                foreach (PatternTerm resource in describe.Resources)
                {
                    _out.Write((byte)' ');
                    WritePatternTerm(resource);
                }

                _out.Write((byte)'\n');
                WriteDataset(query.Dataset);
                WriteLevel(describe.Pattern, projecting: false);
                break;
            default:
                throw new InvalidOperationException("Unknown query form " + query.GetType().Name + ".");
        }
    }

    private void WritePrologue(Prologue prologue)
    {
        if (prologue.Version is SparqlVersion version)
        {
            _out.Write("VERSION \""u8);
            _out.Write(version switch
            {
                SparqlVersion.Sparql11 => "1.1",
                SparqlVersion.Sparql12Basic => "1.2-basic",
                _ => "1.2",
            });
            _out.Write("\"\n"u8);
        }

        if (prologue.Base is not null)
        {
            _out.Write("BASE "u8);
            WriteIri(prologue.Base);
            _out.Write((byte)'\n');
        }

        foreach (PrefixDeclaration prefix in prologue.Prefixes)
        {
            _out.Write("PREFIX "u8);
            _out.Write(prefix.Prefix);
            _out.Write(": "u8);
            WriteIri(prefix.Iri);
            _out.Write((byte)'\n');
        }
    }

    private void WriteDataset(DatasetSpec? dataset)
    {
        if (dataset is null)
        {
            return;
        }

        foreach (RdfTerm graph in dataset.DefaultGraphs)
        {
            _out.Write("FROM "u8);
            WriteIri(graph);
            _out.Write((byte)'\n');
        }

        foreach (RdfTerm graph in dataset.NamedGraphs)
        {
            _out.Write("FROM NAMED "u8);
            WriteIri(graph);
            _out.Write((byte)'\n');
        }
    }

    /// <summary>A SELECT level: the modifier chain peeled from the outside in the parser's order (§4.7), then the pattern.</summary>
    private void WriteSelectLevel(QueryPattern pattern, DatasetSpec? dataset)
    {
        _out.Write("SELECT"u8);
        WriteLevel(pattern, projecting: true, dataset);
    }

    private void WriteLevel(QueryPattern pattern, bool projecting, DatasetSpec? dataset = null)
    {
        long? offset = null;
        long? limit = null;

        if (pattern is Slice slice)
        {
            offset = slice.Offset;
            limit = slice.Limit;
            pattern = slice.Inner;
        }

        if (projecting)
        {
            if (pattern is Distinct distinct)
            {
                _out.Write(" DISTINCT"u8);
                pattern = distinct.Inner;
            }
            else if (pattern is Reduced reduced)
            {
                _out.Write(" REDUCED"u8);
                pattern = reduced.Inner;
            }
        }

        AlgebraList<Variable> projected = default;
        bool hasProject = false;

        if (projecting && pattern is Project project)
        {
            projected = project.Variables;
            hasProject = true;
            pattern = project.Inner;
        }

        AlgebraList<OrderCondition> order = default;

        if (pattern is OrderBy orderBy)
        {
            order = orderBy.Conditions;
            pattern = orderBy.Inner;
        }

        // SELECT expressions: the Extends directly under the projection whose
        // variables are projected. Outermost first, so collected then reversed.
        Extend[] extends = ArrayPool<Extend>.Shared.Rent(16);
        int extendCount = 0;

        try
        {
            while (hasProject && pattern is Extend extend && Contains(projected, extend.Variable))
            {
                if (extendCount == extends.Length)
                {
                    Extend[] grown = ArrayPool<Extend>.Shared.Rent(extends.Length * 2);
                    extends.AsSpan(0, extendCount).CopyTo(grown);
                    ArrayPool<Extend>.Shared.Return(extends, clearArray: true);
                    extends = grown;
                }

                extends[extendCount++] = extend;
                pattern = extend.Inner;
            }

            // Below the Extends: an optional trailing VALUES, HAVING filters, and
            // the Group — recognised only when a Group is actually there.
            (QueryPattern? where, Values? values, AlgebraList<GroupKey>? keys, Filter[]? havingArray, int havingCount) = PeelGrouping(pattern);
            ReadOnlySpan<Filter> having = havingArray is null ? default : havingArray.AsSpan(0, havingCount);

            try
            {
                if (projecting)
                {
                    if (hasProject)
                    {
                        foreach (Variable variable in projected)
                        {
                            Extend? bound = null;

                            for (int i = 0; i < extendCount; i++)
                            {
                                if (extends[i].Variable == variable)
                                {
                                    bound = extends[i];
                                    break;
                                }
                            }

                            _out.Write((byte)' ');

                            if (bound is null)
                            {
                                WriteVariable(variable);
                            }
                            else
                            {
                                _out.Write((byte)'(');
                                WriteExpression(bound.Expression);
                                _out.Write(" AS "u8);
                                WriteVariable(variable);
                                _out.Write((byte)')');
                            }
                        }

                        if (projected.IsEmpty)
                        {
                            // Nothing projected: SELECT needs an item; '*' over an
                            // empty scope is the only text that parses to none.
                            _out.Write(" *"u8);
                        }
                    }
                    else
                    {
                        _out.Write(" *"u8);
                    }

                    _out.Write((byte)'\n');
                    WriteDataset(dataset);
                }

                _out.Write("WHERE {"u8);
                WriteGroupBody(where ?? pattern);
                _out.Write("}\n"u8);

                if (keys is AlgebraList<GroupKey> groupKeys && !groupKeys.IsEmpty)
                {
                    _out.Write("GROUP BY"u8);

                    foreach (GroupKey key in groupKeys)
                    {
                        _out.Write((byte)' ');

                        if (key.Variable is Variable named && key.Expression is VariableExpression { Variable: var plain } && plain == named)
                        {
                            WriteVariable(named);
                        }
                        else
                        {
                            _out.Write((byte)'(');
                            WriteExpression(key.Expression);

                            if (key.Variable is Variable asVariable)
                            {
                                _out.Write(" AS "u8);
                                WriteVariable(asVariable);
                            }

                            _out.Write((byte)')');
                        }
                    }

                    _out.Write((byte)'\n');
                }

                if (!having.IsEmpty)
                {
                    _out.Write("HAVING"u8);

                    // Peeled outermost first; the parser wrapped the first HAVING innermost.
                    for (int i = having.Length - 1; i >= 0; i--)
                    {
                        _out.Write(" ("u8);
                        WriteExpression(having[i].Condition);
                        _out.Write((byte)')');
                    }

                    _out.Write((byte)'\n');
                }

                if (!order.IsEmpty)
                {
                    _out.Write("ORDER BY"u8);

                    foreach (OrderCondition condition in order)
                    {
                        _out.Write(condition.Descending ? " DESC("u8 : " ASC("u8);
                        WriteExpression(condition.Expression);
                        _out.Write((byte)')');
                    }

                    _out.Write((byte)'\n');
                }

                if (limit is long l)
                {
                    _out.Write("LIMIT "u8);
                    _out.Write(l);
                    _out.Write((byte)'\n');
                }

                if (offset is long o && (o != 0 || limit is null))
                {
                    _out.Write("OFFSET "u8);
                    _out.Write(o);
                    _out.Write((byte)'\n');
                }

                if (values is not null)
                {
                    WriteValues(values);
                    _out.Write((byte)'\n');
                }
            }
            finally
            {
                if (havingArray is not null)
                {
                    ArrayPool<Filter>.Shared.Return(havingArray, clearArray: true);
                }
            }
        }
        finally
        {
            ArrayPool<Extend>.Shared.Return(extends, clearArray: true);
        }
    }

    /// <summary>
    /// Below the SELECT expressions: <c>Join(Filter*(Group(P)), Values)?</c>. The
    /// trailing VALUES and the HAVING filters are only such when a Group is
    /// underneath; otherwise the whole thing is the WHERE pattern.
    /// </summary>
    private static (QueryPattern? Where, Values? Values, AlgebraList<GroupKey>? Keys, Filter[]? Having, int HavingCount) PeelGrouping(QueryPattern pattern)
    {
        QueryPattern current = pattern;
        Values? values = null;

        if (current is Join { Right: Values trailing } join)
        {
            values = trailing;
            current = join.Left;
        }

        Filter[]? having = null;
        int count = 0;

        while (current is Filter filter)
        {
            having ??= ArrayPool<Filter>.Shared.Rent(8);

            if (count == having.Length)
            {
                Filter[] grown = ArrayPool<Filter>.Shared.Rent(having.Length * 2);
                having.AsSpan(0, count).CopyTo(grown);
                ArrayPool<Filter>.Shared.Return(having, clearArray: true);
                having = grown;
            }

            having[count++] = filter;
            current = filter.Inner;
        }

        if (current is Group group)
        {
            return (group.Inner, values, group.Keys, having, count);
        }

        if (having is not null)
        {
            ArrayPool<Filter>.Shared.Return(having, clearArray: true);
        }

        return (null, null, null, null, 0);
    }

    private static bool Contains(AlgebraList<Variable> variables, Variable variable)
    {
        foreach (Variable candidate in variables)
        {
            if (candidate == variable)
            {
                return true;
            }
        }

        return false;
    }

    // --- patterns ----------------------------------------------------------

    /// <summary>The contents of a group: its elements, then its own FILTER if the pattern is one.</summary>
    private void WriteGroupBody(QueryPattern pattern)
    {
        bool saved = _lastTriples;
        _lastTriples = false;

        if (pattern is Filter filter)
        {
            WriteElements(filter.Inner, wrapFilter: true);
            _out.Write(" FILTER("u8);
            WriteExpression(filter.Condition);
            _out.Write(") "u8);
        }
        else
        {
            WriteElements(pattern, wrapFilter: true);
        }

        _lastTriples = saved;
    }

    /// <summary>
    /// A pattern as the sequence of elements a group folds left into it:
    /// a left-nested chain of Join, LeftJoin, Minus and Extend flattens, and
    /// anything else is one element.
    /// </summary>
    private void WriteElements(QueryPattern pattern, bool wrapFilter)
    {
        switch (pattern)
        {
            case Join join:
                WriteElements(join.Left, wrapFilter: true);
                WriteElement(join.Right);
                break;
            case LeftJoin leftJoin:
                WriteElements(leftJoin.Left, wrapFilter: true);
                _out.Write(" OPTIONAL { "u8);
                _lastTriples = false;
                WriteElement(leftJoin.Right);

                if (leftJoin.Condition is not null)
                {
                    _out.Write(" FILTER("u8);
                    WriteExpression(leftJoin.Condition);
                    _out.Write((byte)')');
                }

                _out.Write(" } "u8);
                _lastTriples = false;
                break;
            case Minus minus:
                WriteElements(minus.Left, wrapFilter: true);
                _out.Write(" MINUS { "u8);
                WriteGroupBody(minus.Right);
                _out.Write(" } "u8);
                _lastTriples = false;
                break;
            case Extend extend:
                WriteElements(extend.Inner, wrapFilter: true);
                _out.Write(" BIND("u8);
                WriteExpression(extend.Expression);
                _out.Write(" AS "u8);
                WriteVariable(extend.Variable);
                _out.Write(") "u8);
                _lastTriples = false;
                break;
            case Bgp bgp:
                WriteTriples(bgp.Triples);
                _lastTriples = true;
                break;
            case PathPattern path:
                _out.Write((byte)' ');
                WritePatternTerm(path.Subject);
                _out.Write((byte)' ');
                WritePath(path.Path);
                _out.Write((byte)' ');
                WritePatternTerm(path.Object);
                _out.Write(" . "u8);
                _lastTriples = false;
                break;
            case Union union:
                WriteUnion(union);
                _lastTriples = false;
                break;
            case Graph graph:
                _out.Write(" GRAPH "u8);
                WritePatternTerm(graph.Name);
                _out.Write(" { "u8);
                WriteGroupBody(graph.Inner);
                _out.Write(" } "u8);
                _lastTriples = false;
                break;
            case Service service:
                _out.Write(service.Silent ? " SERVICE SILENT "u8 : " SERVICE "u8);
                WritePatternTerm(service.Name);
                _out.Write(" { "u8);
                WriteGroupBody(service.Inner);
                _out.Write(" } "u8);
                _lastTriples = false;
                break;
            case Values values:
                _out.Write((byte)' ');
                WriteValues(values);
                _out.Write((byte)' ');
                _lastTriples = false;
                break;
            case Filter when wrapFilter:
                WriteElement(pattern);
                break;
            case Group or OrderBy or Project or Distinct or Reduced or Slice:
                _out.Write(" { SELECT"u8);
                WriteLevel(pattern, projecting: true);
                _out.Write(" } "u8);
                _lastTriples = false;
                break;
            default:
                throw new InvalidOperationException("Unknown pattern " + pattern.GetType().Name + ".");
        }
    }

    /// <summary>
    /// One element. A run of triples goes bare unless the previous element was
    /// one too; a path goes bare; everything else gets braces of its own so
    /// that its structure survives the group fold.
    /// </summary>
    private void WriteElement(QueryPattern pattern)
    {
        if (pattern is Bgp { Triples.IsEmpty: false } bgp && !_lastTriples)
        {
            WriteTriples(bgp.Triples);
            _lastTriples = true;
            return;
        }

        if (pattern is PathPattern)
        {
            WriteElements(pattern, wrapFilter: true);
            return;
        }

        _out.Write(" { "u8);
        WriteGroupBody(pattern);
        _out.Write(" } "u8);
        _lastTriples = false;
    }

    private void WriteUnion(Union union)
    {
        if (union.Left is Union left)
        {
            WriteUnion(left);
        }
        else
        {
            WriteBraced(union.Left);
        }

        _out.Write(" UNION "u8);
        WriteBraced(union.Right);
    }

    /// <summary>A pattern in braces of its own, whatever it is.</summary>
    private void WriteBraced(QueryPattern pattern)
    {
        _out.Write(" { "u8);
        WriteGroupBody(pattern);
        _out.Write(" } "u8);
        _lastTriples = false;
    }

    private void WriteValues(Values values)
    {
        _out.Write("VALUES ("u8);

        foreach (Variable variable in values.Variables)
        {
            _out.Write((byte)' ');
            WriteVariable(variable);
        }

        _out.Write(" ) {"u8);

        foreach (AlgebraList<RdfTerm?> row in values.Rows)
        {
            _out.Write(" ("u8);

            foreach (RdfTerm? value in row)
            {
                _out.Write((byte)' ');

                if (value is null)
                {
                    _out.Write("UNDEF"u8);
                }
                else
                {
                    WriteTerm(value);
                }
            }

            _out.Write(" )"u8);
        }

        _out.Write(" }"u8);
    }

    private void WriteTriples(AlgebraList<TriplePattern> triples)
    {
        foreach (TriplePattern triple in triples)
        {
            _out.Write((byte)' ');
            WritePatternTerm(triple.Subject);
            _out.Write((byte)' ');
            WritePatternTerm(triple.Predicate);
            _out.Write((byte)' ');
            WritePatternTerm(triple.Object);
            _out.Write(" ."u8);
        }

        _out.Write((byte)' ');
    }

    private void WriteTemplate(AlgebraList<TriplePattern> template) => WriteTriples(template);

    // --- paths -------------------------------------------------------------

    private void WritePath(PropertyPath path)
    {
        switch (path)
        {
            case PredicatePath predicate:
                WriteIri(predicate.Predicate);
                break;
            case InversePath inverse:
                _out.Write("^("u8);
                WritePath(inverse.Inner);
                _out.Write((byte)')');
                break;
            case SequencePath sequence:
                _out.Write((byte)'(');
                WritePath(sequence.Left);
                _out.Write(" / "u8);
                WritePath(sequence.Right);
                _out.Write((byte)')');
                break;
            case AlternativePath alternative:
                _out.Write((byte)'(');
                WritePath(alternative.Left);
                _out.Write(" | "u8);
                WritePath(alternative.Right);
                _out.Write((byte)')');
                break;
            case ZeroOrMorePath zeroOrMore:
                _out.Write((byte)'(');
                WritePath(zeroOrMore.Inner);
                _out.Write(")*"u8);
                break;
            case OneOrMorePath oneOrMore:
                _out.Write((byte)'(');
                WritePath(oneOrMore.Inner);
                _out.Write(")+"u8);
                break;
            case ZeroOrOnePath zeroOrOne:
                _out.Write((byte)'(');
                WritePath(zeroOrOne.Inner);
                _out.Write(")?"u8);
                break;
            case NegatedPropertySet negated:
                _out.Write("!("u8);
                bool first = true;

                foreach (RdfTerm iri in negated.Forward)
                {
                    if (!first)
                    {
                        _out.Write((byte)'|');
                    }

                    WriteIri(iri);
                    first = false;
                }

                foreach (RdfTerm iri in negated.Inverse)
                {
                    if (!first)
                    {
                        _out.Write((byte)'|');
                    }

                    _out.Write((byte)'^');
                    WriteIri(iri);
                    first = false;
                }

                _out.Write((byte)')');
                break;
            default:
                throw new InvalidOperationException("Unknown path " + path.GetType().Name + ".");
        }
    }

    // --- terms -------------------------------------------------------------

    private void WritePatternTerm(PatternTerm term)
    {
        switch (term)
        {
            case VariablePattern variable:
                WriteVariable(variable.Variable);
                break;
            case TermPattern constant:
                WriteTerm(constant.Term);
                break;
            case BlankNodePattern blank:
                _out.Write("_:"u8);
                _out.Write(blank.Label);
                break;
            case TripleTermPattern triple:
                _out.Write("<<( "u8);
                WritePatternTerm(triple.Subject);
                _out.Write((byte)' ');
                WritePatternTerm(triple.Predicate);
                _out.Write((byte)' ');
                WritePatternTerm(triple.Object);
                _out.Write(" )>>"u8);
                break;
            default:
                throw new InvalidOperationException("Unknown pattern term " + term.GetType().Name + ".");
        }
    }

    private void WriteVariable(Variable variable)
    {
        _out.Write((byte)'?');
        _out.Write(variable.Name);
    }

    private void WriteIri(RdfTerm iri)
    {
        _out.Write((byte)'<');
        _out.Write(iri.Lexical);
        _out.Write((byte)'>');
    }

    private void WriteTerm(RdfTerm term)
    {
        switch (term.Kind)
        {
            case RdfTermKind.Iri:
                WriteIri(term);
                break;
            case RdfTermKind.BlankNode:
                _out.Write("_:"u8);
                _out.Write(term.Lexical);
                break;
            case RdfTermKind.TripleTerm:
                _out.Write("<<( "u8);
                WriteTerm(term.Subject!);
                _out.Write((byte)' ');
                WriteTerm(term.Predicate!);
                _out.Write((byte)' ');
                WriteTerm(term.Object!);
                _out.Write(" )>>"u8);
                break;
            default:
                WriteLiteral(term);
                break;
        }
    }

    private void WriteLiteral(RdfTerm literal)
    {
        RdfTerm? datatype = literal.Datatype;

        if (datatype is not null)
        {
            // The short form only when the lexical form is exactly the terminal;
            // otherwise re-parsing would give a different lexical form.
            if (datatype.Equals(XsdIntegerTerm) && Lexical.IsInteger(literal.Lexical)
                || datatype.Equals(XsdDecimalTerm) && Lexical.IsDecimal(literal.Lexical)
                || datatype.Equals(XsdDoubleTerm) && Lexical.IsDouble(literal.Lexical)
                || datatype.Equals(XsdBooleanTerm) && (literal.Lexical.SequenceEqual("true"u8) || literal.Lexical.SequenceEqual("false"u8)))
            {
                _out.Write(literal.Lexical);
                return;
            }
        }

        WriteString(literal.Lexical);

        if (literal.Language.Length > 0)
        {
            _out.Write((byte)'@');
            _out.Write(literal.Language);

            if (literal.Direction == TextDirection.LeftToRight)
            {
                _out.Write("--ltr"u8);
            }
            else if (literal.Direction == TextDirection.RightToLeft)
            {
                _out.Write("--rtl"u8);
            }
        }
        else if (datatype is not null)
        {
            _out.Write("^^"u8);
            WriteIri(datatype);
        }
    }

    private void WriteString(ReadOnlySpan<byte> lexical)
    {
        _out.Write((byte)'"');

        foreach (byte b in lexical)
        {
            switch (b)
            {
                case (byte)'"':
                    _out.Write("\\\""u8);
                    break;
                case (byte)'\\':
                    _out.Write("\\\\"u8);
                    break;
                case 0x0A:
                    _out.Write("\\n"u8);
                    break;
                case 0x0D:
                    _out.Write("\\r"u8);
                    break;
                default:
                    _out.Write(b);
                    break;
            }
        }

        _out.Write((byte)'"');
    }
}
