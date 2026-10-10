// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Turtle.Model;

namespace Varve.Turtle;

/// <summary>
/// RDF 1.2's additions (ADR 0121): reified triples [29], triple terms [32],
/// annotations [35] and the version directive [6], [9].
/// </summary>
/// <remarks>
/// <para>
/// The parsing rules are RDF 1.2 Turtle §7.3, kept to the letter because the
/// suite tests them to the letter. One reifier variable, <c>curReifier</c>,
/// is cleared when an annotation begins; a <c>~</c> sets it and yields
/// <c>curReifier rdf:reifies tt</c> at once; an annotation block takes it, or
/// mints a fresh blank node and yields that triple when none is set, then
/// clears it. So <c>:s :p :o ~ :r {| :a :b |} {| :c :d |}</c> gives two
/// reifiers, <c>:r</c> and a fresh node, each reifying the same triple term.
/// </para>
/// <para>
/// A reified triple <c>&lt;&lt; s p o &gt;&gt;</c> does <em>not</em> assert
/// <c>s p o</c>; it asserts only that its reifier reifies the triple term, and
/// the reifier is the node the production stands for. An annotated triple
/// <em>is</em> asserted, by the object list that carries it.
/// </para>
/// </remarks>
internal ref partial struct TurtleScanner
{
    private static ReadOnlySpan<byte> RdfReifies => "http://www.w3.org/1999/02/22-rdf-syntax-ns#reifies"u8;

    /// <summary>
    /// Whether the cursor is at <c>&lt;&lt;</c>: a reified triple or a triple
    /// term. <paramref name="tripleTerm"/> says which when it can be told;
    /// <paramref name="incomplete"/> says the buffer ended before it could.
    /// </summary>
    private readonly bool AtDoubleAngle(out bool tripleTerm, out bool incomplete)
    {
        tripleTerm = false;
        incomplete = false;

        if (Consumed >= _text.Length || _text[Consumed] != (byte)'<')
        {
            return false;
        }

        if (Consumed + 1 >= _text.Length)
        {
            incomplete = MayGrow;
            return false;
        }

        if (_text[Consumed + 1] != (byte)'<')
        {
            return false;
        }

        if (Consumed + 2 >= _text.Length)
        {
            incomplete = MayGrow;
            return !MayGrow;
        }

        tripleTerm = _text[Consumed + 2] == (byte)'(';
        return true;
    }

    /// <summary>
    /// [29] <c>'&lt;&lt;' rtSubject verb rtObject reifier? '&gt;&gt;'</c>. The
    /// slot returned is the reifier, which is what the production denotes.
    /// </summary>
    private bool TryReifiedTriple(out int slot)
    {
        slot = -1;
        Consumed += 2;
        SkipIgnorable();

        if (AtEnd)
        {
            return Truncated();
        }

        // [30] rtSubject ::= iri | BlankNode | reifiedTriple. No collection, no
        // property list: "[" is only an ANON here.
        if (!TryReifiedTripleSubject(out int subject))
        {
            return false;
        }

        SkipIgnorable();

        if (AtEnd)
        {
            return Truncated();
        }

        if (!TryVerb(out int verb))
        {
            return false;
        }

        SkipIgnorable();

        if (AtEnd)
        {
            return Truncated();
        }

        // [31] rtObject ::= iri | BlankNode | literal | tripleTerm | reifiedTriple.
        if (!TryReifiedTripleObject(out int obj))
        {
            return false;
        }

        SkipIgnorable();

        if (AtEnd)
        {
            return Truncated();
        }

        int reifier = -1;

        if (Peek == (byte)'~')
        {
            if (!TryReifier(out reifier))
            {
                return false;
            }

            SkipIgnorable();

            if (AtEnd)
            {
                return Truncated();
            }
        }

        if (Consumed + 1 >= _text.Length)
        {
            return MayGrow ? Truncated() : Fail(ParseErrorKind.UnterminatedReifiedTriple, Consumed);
        }

        if (_text[Consumed] != (byte)'>' || _text[Consumed + 1] != (byte)'>')
        {
            return Fail(ParseErrorKind.UnterminatedReifiedTriple, Consumed);
        }

        Consumed += 2;

        int tripleTerm = _state.Arena.AddTripleTerm(subject, verb, obj);

        if (reifier < 0)
        {
            reifier = FreshBlankNode();
        }

        _state.Add(reifier, Iri(RdfReifies), tripleTerm, Graph);
        slot = reifier;
        return true;
    }

    private bool TryReifiedTripleSubject(out int slot)
    {
        slot = -1;

        if (AtDoubleAngle(out bool tripleTerm, out bool incomplete))
        {
            return tripleTerm ? Fail(ParseErrorKind.ExpectedSubject, Consumed) : TryReifiedTriple(out slot);
        }

        if (incomplete)
        {
            return Truncated();
        }

        if (Peek == (byte)'[')
        {
            if (IsAnon(out bool anonIncomplete))
            {
                return TryBlankNode(out slot);
            }

            return anonIncomplete && MayGrow ? Truncated() : Fail(ParseErrorKind.ExpectedSubject, Consumed);
        }

        if (Peek == (byte)'_')
        {
            return TryBlankNode(out slot);
        }

        if (Peek is (byte)'(' or (byte)'"' or (byte)'\'' or (byte)'+' or (byte)'-' || NTriplesChars.IsAsciiDigit(Peek))
        {
            return Fail(ParseErrorKind.ExpectedSubject, Consumed);
        }

        return TryIriSlot(out slot, ParseErrorKind.ExpectedSubject);
    }

    private bool TryReifiedTripleObject(out int slot)
    {
        slot = -1;

        if (AtDoubleAngle(out bool tripleTerm, out bool incomplete))
        {
            return tripleTerm ? TryTripleTerm(out slot) : TryReifiedTriple(out slot);
        }

        if (incomplete)
        {
            return Truncated();
        }

        if (Peek == (byte)'[')
        {
            if (IsAnon(out bool anonIncomplete))
            {
                return TryBlankNode(out slot);
            }

            return anonIncomplete && MayGrow ? Truncated() : Fail(ParseErrorKind.ExpectedObject, Consumed);
        }

        if (Peek == (byte)'(')
        {
            return Fail(ParseErrorKind.ExpectedObject, Consumed);
        }

        return TryObject(out slot);
    }

    /// <summary>
    /// [32] <c>'&lt;&lt;(' ttSubject verb ttObject ')&gt;&gt;'</c>, which is a
    /// term and yields no triple of its own.
    /// </summary>
    private bool TryTripleTerm(out int slot)
    {
        slot = -1;
        Consumed += 3;
        SkipIgnorable();

        if (AtEnd)
        {
            return Truncated();
        }

        // [33] ttSubject ::= iri | BlankNode.
        if (AtDoubleAngle(out _, out bool incomplete))
        {
            return Fail(ParseErrorKind.ExpectedSubject, Consumed);
        }

        if (incomplete)
        {
            return Truncated();
        }

        int subject;

        if (Peek == (byte)'[')
        {
            if (!IsAnon(out bool anonIncomplete))
            {
                return anonIncomplete && MayGrow ? Truncated() : Fail(ParseErrorKind.ExpectedSubject, Consumed);
            }

            if (!TryBlankNode(out subject))
            {
                return false;
            }
        }
        else if (Peek == (byte)'_')
        {
            if (!TryBlankNode(out subject))
            {
                return false;
            }
        }
        else if (Peek is (byte)'(' or (byte)'"' or (byte)'\'' or (byte)'+' or (byte)'-' || NTriplesChars.IsAsciiDigit(Peek))
        {
            return Fail(ParseErrorKind.ExpectedSubject, Consumed);
        }
        else if (!TryIriSlot(out subject, ParseErrorKind.ExpectedSubject))
        {
            return false;
        }

        SkipIgnorable();

        if (AtEnd)
        {
            return Truncated();
        }

        if (!TryVerb(out int verb))
        {
            return false;
        }

        SkipIgnorable();

        if (AtEnd)
        {
            return Truncated();
        }

        // [34] ttObject ::= iri | BlankNode | literal | tripleTerm.
        int obj;

        if (AtDoubleAngle(out bool nestedTripleTerm, out incomplete))
        {
            if (!nestedTripleTerm)
            {
                return Fail(ParseErrorKind.ExpectedObject, Consumed);
            }

            if (!TryTripleTerm(out obj))
            {
                return false;
            }
        }
        else if (incomplete)
        {
            return Truncated();
        }
        else if (Peek == (byte)'[')
        {
            if (!IsAnon(out bool anonIncomplete))
            {
                return anonIncomplete && MayGrow ? Truncated() : Fail(ParseErrorKind.ExpectedObject, Consumed);
            }

            if (!TryBlankNode(out obj))
            {
                return false;
            }
        }
        else if (Peek == (byte)'(')
        {
            return Fail(ParseErrorKind.ExpectedObject, Consumed);
        }
        else if (!TryObject(out obj))
        {
            return false;
        }

        SkipIgnorable();

        if (Consumed + 2 >= _text.Length)
        {
            return MayGrow ? Truncated() : Fail(ParseErrorKind.UnterminatedTripleTerm, Consumed);
        }

        if (_text[Consumed] != (byte)')' || _text[Consumed + 1] != (byte)'>' || _text[Consumed + 2] != (byte)'>')
        {
            return Fail(ParseErrorKind.UnterminatedTripleTerm, Consumed);
        }

        Consumed += 3;
        slot = _state.Arena.AddTripleTerm(subject, verb, obj);
        return true;
    }

    /// <summary>
    /// [28] <c>'~' (iri | BlankNode)?</c>: the reifier named, or -1 for one the
    /// parser must mint.
    /// </summary>
    private bool TryReifier(out int slot)
    {
        slot = -1;
        Consumed++;
        SkipIgnorable();

        if (AtEnd)
        {
            return MayGrow ? Truncated() : true;
        }

        byte b = Peek;

        // Whatever may legitimately follow a bare reifier: the end of the
        // statement, the next object, the next verb, an annotation block, the
        // close of a reified triple, a property list or a graph, or another
        // reifier.
        if (b is (byte)'.' or (byte)',' or (byte)';' or (byte)'{' or (byte)'|' or (byte)'>' or (byte)']'
            or (byte)'}' or (byte)'~' or (byte)')')
        {
            return true;
        }

        if (b == (byte)'[')
        {
            if (IsAnon(out bool incomplete))
            {
                return TryBlankNode(out slot);
            }

            return incomplete && MayGrow ? Truncated() : Fail(ParseErrorKind.ExpectedReifier, Consumed);
        }

        if (b == (byte)'_')
        {
            return TryBlankNode(out slot);
        }

        if (b is (byte)'"' or (byte)'\'' or (byte)'(' or (byte)'+' or (byte)'-' || NTriplesChars.IsAsciiDigit(b))
        {
            return Fail(ParseErrorKind.ExpectedReifier, Consumed);
        }

        if (AtDoubleAngle(out _, out bool angleIncomplete))
        {
            return Fail(ParseErrorKind.ExpectedReifier, Consumed);
        }

        if (angleIncomplete)
        {
            return Truncated();
        }

        return TryIriSlot(out slot, ParseErrorKind.ExpectedReifier);
    }

    /// <summary>
    /// [35] <c>(reifier | annotationBlock)*</c> after an object, for the triple
    /// <paramref name="subject"/> <paramref name="verb"/> <paramref name="obj"/>
    /// that the object list has just asserted.
    /// </summary>
    private bool Annotation(int subject, int verb, int obj)
    {
        int tripleTerm = -1;
        int reifier = -1;

        while (true)
        {
            SkipIgnorable();

            if (AtEnd)
            {
                // Nothing is pending here: an annotation is optional, and a
                // statement that ends after its object is the caller's to
                // finish. But a buffer that can still grow may be about to
                // deliver a "~" or a "{|", so the decision waits.
                return MayGrow ? Truncated() : true;
            }

            if (Peek == (byte)'~')
            {
                if (!TryReifier(out reifier))
                {
                    return false;
                }

                if (reifier < 0)
                {
                    reifier = FreshBlankNode();
                }

                tripleTerm = tripleTerm >= 0 ? tripleTerm : _state.Arena.AddTripleTerm(subject, verb, obj);
                _state.Add(reifier, Iri(RdfReifies), tripleTerm, Graph);
                continue;
            }

            if (Peek != (byte)'{')
            {
                return true;
            }

            if (Consumed + 1 >= _text.Length)
            {
                return MayGrow ? Truncated() : true;
            }

            if (_text[Consumed + 1] != (byte)'|')
            {
                return true;
            }

            // [36] annotationBlock.
            Consumed += 2;

            if (reifier < 0)
            {
                reifier = FreshBlankNode();
                tripleTerm = tripleTerm >= 0 ? tripleTerm : _state.Arena.AddTripleTerm(subject, verb, obj);
                _state.Add(reifier, Iri(RdfReifies), tripleTerm, Graph);
            }

            if (!PredicateObjectList(reifier, Graph))
            {
                return false;
            }

            SkipIgnorable();

            if (Consumed + 1 >= _text.Length)
            {
                return MayGrow ? Truncated() : Fail(ParseErrorKind.UnterminatedAnnotation, Consumed);
            }

            if (_text[Consumed] != (byte)'|' || _text[Consumed + 1] != (byte)'}')
            {
                return Fail(ParseErrorKind.UnterminatedAnnotation, Consumed);
            }

            Consumed += 2;
            reifier = -1;
        }
    }

    /// <summary>
    /// [6] <c>'@version' VersionSpecifier '.'</c> and [9] <c>"VERSION"
    /// VersionSpecifier</c>. The value is reported and nothing is refused on
    /// it: the specification makes the announcement a hint, and a parser that
    /// reads 1.2 reads every announced version.
    /// </summary>
    [DesignDecision(typeof(HotPathScope.DirectivesAreNotPerQuad), Scope = ExceptionScope.HotPath)]
    private bool VersionDirective(bool sparqlStyle)
    {
        SkipIgnorable();

        if (AtEnd)
        {
            return Truncated();
        }

        // [10] VersionSpecifier ::= STRING_LITERAL_QUOTE | STRING_LITERAL_SINGLE_QUOTE —
        // the two short forms, and neither long one.
        if (Peek is not ((byte)'"' or (byte)'\''))
        {
            return Fail(ParseErrorKind.InvalidVersion, Consumed);
        }

        if (Consumed + 2 >= _text.Length && MayGrow)
        {
            return Truncated();
        }

        if (Consumed + 2 < _text.Length && _text[Consumed + 1] == Peek && _text[Consumed + 2] == Peek)
        {
            return Fail(ParseErrorKind.InvalidVersion, Consumed);
        }

        if (!TryString(out TermSpan version))
        {
            return false;
        }

        if (!sparqlStyle && !ExpectStatementDot())
        {
            return false;
        }

        _onVersion?.Invoke(_state.Arena.Bytes(_text, version));
        return true;
    }
}
