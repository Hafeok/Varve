// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Turtle;

/// <summary>Directives, triples, and TriG's blocks.</summary>
internal ref partial struct TurtleScanner
{
    private static ReadOnlySpan<byte> RdfType => "http://www.w3.org/1999/02/22-rdf-syntax-ns#type"u8;

    private StatementStatus TurtleStatement()
    {
        if (MayGrow && MightBeADirective())
        {
            return StatementStatus.Incomplete;
        }

        if (Peek == (byte)'@' || LooksIgnoringCase("PREFIX"u8) || LooksIgnoringCase("BASE"u8))
        {
            return Directive() ? StatementStatus.Complete : Rejected();
        }

        return Triples(graph: -1) ? StatementStatus.Complete : Rejected();
    }

    /// <summary>
    /// Whether the buffer ends part-way through what could be a SPARQL-style
    /// keyword. <c>PRE</c> is either <c>PREFIX</c> or the start of a prefixed
    /// name, and nothing here can tell which, so the statement waits.
    /// </summary>
    private readonly bool MightBeADirective() =>
        MightBeIgnoringCase("PREFIX"u8) || MightBeIgnoringCase("BASE"u8);

    /// <summary>
    /// TriG [2g]. The awkward one: a leading IRI or blank node is a graph label
    /// if <c>{</c> follows and a subject otherwise, and there is no way to tell
    /// without parsing the term first.
    /// </summary>
    private StatementStatus TriGBlock()
    {
        if (MayGrow && (MightBeADirective() || MightBeIgnoringCase("GRAPH"u8)))
        {
            return StatementStatus.Incomplete;
        }

        if (Peek == (byte)'@' || LooksIgnoringCase("PREFIX"u8) || LooksIgnoringCase("BASE"u8))
        {
            return Directive() ? StatementStatus.Complete : Rejected();
        }

        if (Peek == (byte)'{')
        {
            return WrappedGraph(graph: -1) ? StatementStatus.Complete : Rejected();
        }

        if (LooksIgnoringCase("GRAPH"u8))
        {
            Consumed += 5;
            SkipIgnorable();

            if (!TryLabelOrSubject(out int label))
            {
                return Rejected();
            }

            SkipIgnorable();

            if (AtEnd)
            {
                return Truncated() ? StatementStatus.Incomplete : StatementStatus.Incomplete;
            }

            if (Peek != (byte)'{')
            {
                Fail(ParseErrorKind.ExpectedGraphLabel, Consumed);
                return Rejected();
            }

            return WrappedGraph(label) ? StatementStatus.Complete : Rejected();
        }

        // [7g] labelOrSubject is iri | BlankNode, and BlankNode includes ANON,
        // so "[]" here is a graph label when a "{" follows and a subject
        // otherwise — the same one-term lookahead an IRI needs. A "[" that
        // opens a property list is not an ANON and is [4g] triples2.
        if (IsAnon())
        {
            if (!TryLabelOrSubject(out int anon))
            {
                return Rejected();
            }

            SkipIgnorable();

            if (AtEnd)
            {
                IsTruncated = true;
                return StatementStatus.Incomplete;
            }

            return Peek == (byte)'{'
                ? WrappedGraph(anon) ? StatementStatus.Complete : Rejected()
                : TriplesFromSubject(anon, graph: -1) ? StatementStatus.Complete : Rejected();
        }

        // [4g] triples2's two shapes.
        if (Peek is (byte)'[' or (byte)'(')
        {
            return Triples(graph: -1) ? StatementStatus.Complete : Rejected();
        }

        if (!TryLabelOrSubject(out int term))
        {
            return Rejected();
        }

        SkipIgnorable();

        if (AtEnd)
        {
            IsTruncated = true;
            return StatementStatus.Incomplete;
        }

        if (Peek == (byte)'{')
        {
            return WrappedGraph(term) ? StatementStatus.Complete : Rejected();
        }

        return TriplesFromSubject(term, graph: -1) ? StatementStatus.Complete : Rejected();
    }

    private readonly StatementStatus Rejected() => IsTruncated ? StatementStatus.Incomplete : StatementStatus.Error;

    /// <summary>[5g] <c>'{' triplesBlock? '}'</c>.</summary>
    private bool WrappedGraph(int graph)
    {
        Consumed++;
        Graph = graph;

        while (true)
        {
            SkipIgnorable();

            if (AtEnd)
            {
                return Truncated();
            }

            if (Peek == (byte)'}')
            {
                Consumed++;
                return true;
            }

            if (!TriplesInGraph(graph))
            {
                return false;
            }

            SkipIgnorable();

            if (AtEnd)
            {
                return Truncated();
            }

            if (Peek == (byte)'.')
            {
                Consumed++;
                continue;
            }

            if (Peek != (byte)'}')
            {
                return Fail(ParseErrorKind.ExpectedDot, Consumed);
            }
        }
    }

    /// <summary>Triples inside <c>{ }</c>, which are terminated by <c>.</c> or by the brace.</summary>
    private bool TriplesInGraph(int graph)
    {
        if (Peek is (byte)'[' or (byte)'(')
        {
            return SubjectAndPredicates(graph, terminated: false);
        }

        if (!TrySubject(out int subject))
        {
            return false;
        }

        SkipIgnorable();
        return PredicateObjectList(subject, graph);
    }

    /// <summary>[2] <c>triples '.'</c>.</summary>
    private bool Triples(int graph)
    {
        if (!SubjectAndPredicates(graph, terminated: true))
        {
            return false;
        }

        return true;
    }

    private bool TriplesFromSubject(int subject, int graph)
    {
        if (!PredicateObjectList(subject, graph))
        {
            return false;
        }

        return ExpectStatementDot();
    }

    private bool SubjectAndPredicates(int graph, bool terminated)
    {
        // [6] triples ::= subject predicateObjectList
        //               | blankNodePropertyList predicateObjectList?
        bool bareBlankNodeList = Peek == (byte)'[' && !IsAnon();

        if (!TrySubject(out int subject))
        {
            return false;
        }

        SkipIgnorable();

        if (AtEnd)
        {
            return Truncated();
        }

        // A bare blank node property list is a whole statement: [ :p :o ] .
        // Inside a graph the brace closes it instead, because [6g] triplesBlock
        // makes the final '.' optional.
        if (bareBlankNodeList && (Peek == (byte)'.' || (!terminated && Peek == (byte)'}')))
        {
            return !terminated || ExpectStatementDot();
        }

        if (!PredicateObjectList(subject, graph))
        {
            return false;
        }

        return !terminated || ExpectStatementDot();
    }

    private bool ExpectStatementDot()
    {
        SkipIgnorable();

        if (AtEnd)
        {
            return Truncated();
        }

        if (Peek != (byte)'.')
        {
            return Fail(ParseErrorKind.ExpectedDot, Consumed);
        }

        Consumed++;
        return true;
    }

    /// <summary>[7] <c>verb objectList (';' (verb objectList)?)*</c>.</summary>
    private bool PredicateObjectList(int subject, int graph)
    {
        while (true)
        {
            SkipIgnorable();

            if (AtEnd)
            {
                return Truncated();
            }

            if (!TryVerb(out int verb))
            {
                return false;
            }

            if (!ObjectList(subject, verb, graph))
            {
                return false;
            }

            SkipIgnorable();

            if (AtEnd)
            {
                return Truncated();
            }

            if (Peek != (byte)';')
            {
                return true;
            }

            // A trailing ';' is allowed, and so is a run of them.
            while (!AtEnd && Peek == (byte)';')
            {
                Consumed++;
                SkipIgnorable();
            }

            if (AtEnd)
            {
                return Truncated();
            }

            if (Peek is (byte)'.' or (byte)']' or (byte)'}')
            {
                return true;
            }
        }
    }

    /// <summary>[8] <c>object (',' object)*</c>.</summary>
    private bool ObjectList(int subject, int verb, int graph)
    {
        while (true)
        {
            SkipIgnorable();

            if (AtEnd)
            {
                return Truncated();
            }

            if (!TryObject(out int obj))
            {
                return false;
            }

            _state.Add(subject, verb, obj, graph);

            SkipIgnorable();

            if (AtEnd)
            {
                return Truncated();
            }

            if (Peek != (byte)',')
            {
                return true;
            }

            Consumed++;
        }
    }

    /// <summary>[9] <c>predicate | 'a'</c>.</summary>
    private bool TryVerb(out int slot)
    {
        slot = -1;

        // 'a' is rdf:type only as a verb, and only when it is a whole token —
        // "a:b" is a prefixed name and "ab" is not a verb at all.
        if (Peek == (byte)'a' && Consumed + 1 >= _text.Length && MayGrow)
        {
            // Whether this is rdf:type or the start of a prefixed name is
            // decided by the next byte, which is not here yet.
            return Truncated();
        }

        if (Peek == (byte)'a' && IsTokenBoundary(_text[Consumed + 1]))
        {
            Consumed++;
            slot = _state.Arena.AddIri(_state.Arena.AppendScratch(RdfType));
            return true;
        }

        if (!TryIriTerm(out TermSpan span, ParseErrorKind.ExpectedPredicate))
        {
            return false;
        }

        slot = _state.Arena.AddIri(span);
        return true;
    }

    private static bool IsTokenBoundary(byte b) =>
        b is 0x20 or 0x09 or 0x0A or 0x0D or (byte)'<' or (byte)'"' or (byte)'\''
            or (byte)'_' or (byte)'[' or (byte)'(' or (byte)'#';

    /// <summary>[3] the four directive forms.</summary>
    [DesignDecision(typeof(HotPathScope.DirectivesAreNotPerQuad), Scope = ExceptionScope.HotPath)]
    private bool Directive()
    {
        bool sparqlStyle;
        bool isPrefix;

        if (Peek == (byte)'@')
        {
            if (Looks("@prefix"u8))
            {
                Consumed += 7;
                isPrefix = true;
            }
            else if (Looks("@base"u8))
            {
                Consumed += 5;
                isPrefix = false;
            }
            else if (MayGrow && (MightBe("@prefix"u8) || MightBe("@base"u8)))
            {
                return Truncated();
            }
            else
            {
                return Fail(ParseErrorKind.UnknownDirective, Consumed);
            }

            sparqlStyle = false;
        }
        else if (LooksIgnoringCase("PREFIX"u8))
        {
            Consumed += 6;
            isPrefix = true;
            sparqlStyle = true;
        }
        else
        {
            Consumed += 4;
            isPrefix = false;
            sparqlStyle = true;
        }

        SkipIgnorable();

        if (AtEnd)
        {
            return Truncated();
        }

        ReadOnlySpan<byte> prefixName = default;
        int prefixStart = Consumed;
        int prefixLength = 0;

        if (isPrefix)
        {
            if (!TryPrefixNamespace(out prefixLength))
            {
                return false;
            }

            prefixName = _text.Slice(prefixStart, prefixLength);
            SkipIgnorable();

            if (AtEnd)
            {
                return Truncated();
            }
        }

        if (Peek != (byte)'<')
        {
            return Fail(ParseErrorKind.ExpectedIri, Consumed);
        }

        if (!TryIriRef(out TermSpan iriSpan, resolve: true))
        {
            return false;
        }

        ReadOnlySpan<byte> iri = _state.Arena.Bytes(_text, iriSpan);

        if (!sparqlStyle && !ExpectStatementDot())
        {
            return false;
        }

        if (isPrefix)
        {
            _state.BindPrefix(prefixName, iri);
            _onPrefix?.Invoke(prefixName, iri);
        }
        else
        {
            _state.SetBase(iri);
            _onBase?.Invoke(iri);
        }

        return true;
    }

    /// <summary>[139s] <c>PN_PREFIX? ':'</c>, measured but not consumed as a term.</summary>
    private bool TryPrefixNamespace(out int length)
    {
        int start = Consumed;

        while (Consumed < _text.Length && _text[Consumed] != (byte)':')
        {
            if (_text[Consumed] is 0x20 or 0x09 or 0x0A or 0x0D or (byte)'<')
            {
                length = 0;
                return Fail(ParseErrorKind.InvalidPrefix, Consumed);
            }

            Consumed++;
        }

        if (AtEnd)
        {
            length = 0;
            return Truncated();
        }

        length = Consumed - start;
        Consumed++;

        return length == 0 || TurtleChars.IsPrefixName(_text.Slice(start, length))
            ? true
            : Fail(ParseErrorKind.InvalidPrefix, start);
    }
}
