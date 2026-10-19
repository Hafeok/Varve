// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Varve.Rdf;

namespace Varve.Shacl.Shapes;

/// <summary>
/// A SHACL property path (SHACL 1.2 Core §4), parsed once at load. It holds
/// terms of the shapes graph only, never a handle of any source (ADR 0155).
/// </summary>
internal abstract class ShapePath
{
    private protected ShapePath()
    {
    }

    /// <summary>The path in SPARQL 1.2 property path syntax: the order key of a result (ADR 0154) and a readable form.</summary>
    internal string Sparql
    {
        get
        {
            StringBuilder text = new();
            Write(text);
            return text.ToString();
        }
    }

    internal abstract void Write(StringBuilder text);

    /// <summary>Writes the path's SHACL syntax into <paramref name="triples"/>, its own nodes fresh, and returns its root.</summary>
    internal abstract RdfTerm WriteRdf(List<(RdfTerm, RdfTerm, RdfTerm)> triples, ref int blank);

    private protected static RdfTerm Fresh(ref int blank) =>
        RdfTerm.BlankNode(Encoding.UTF8.GetBytes("p" + (blank++).ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private protected static RdfTerm WriteList(IReadOnlyList<ShapePath> members, List<(RdfTerm, RdfTerm, RdfTerm)> triples, ref int blank)
    {
        RdfTerm head = Fresh(ref blank);
        RdfTerm cell = head;
        for (int i = 0; i < members.Count; i++)
        {
            triples.Add((cell, Vocabulary.RdfFirst, members[i].WriteRdf(triples, ref blank)));
            RdfTerm next = i == members.Count - 1 ? Vocabulary.RdfNil : Fresh(ref blank);
            triples.Add((cell, Vocabulary.RdfRest, next));
            cell = next;
        }

        return head;
    }
}

/// <summary>§4.1: an IRI.</summary>
internal sealed class PredicatePath(RdfTerm predicate) : ShapePath
{
    internal RdfTerm Predicate { get; } = predicate;

    internal override void Write(StringBuilder text) => text.Append('<').Append(Encoding.UTF8.GetString(Predicate.Lexical)).Append('>');

    internal override RdfTerm WriteRdf(List<(RdfTerm, RdfTerm, RdfTerm)> triples, ref int blank) => Predicate;
}

/// <summary>§4.2: a list of two or more paths, in order.</summary>
internal sealed class SequencePath(ImmutableArray<ShapePath> steps) : ShapePath
{
    internal ImmutableArray<ShapePath> Steps { get; } = steps;

    internal override void Write(StringBuilder text)
    {
        text.Append('(');
        for (int i = 0; i < Steps.Length; i++)
        {
            if (i > 0)
            {
                text.Append('/');
            }

            Steps[i].Write(text);
        }

        text.Append(')');
    }

    internal override RdfTerm WriteRdf(List<(RdfTerm, RdfTerm, RdfTerm)> triples, ref int blank) => WriteList(Steps, triples, ref blank);
}

/// <summary>§4.3: <c>sh:alternativePath</c> with a list of two or more paths.</summary>
internal sealed class AlternativePath(ImmutableArray<ShapePath> alternatives) : ShapePath
{
    internal ImmutableArray<ShapePath> Alternatives { get; } = alternatives;

    internal override void Write(StringBuilder text)
    {
        text.Append('(');
        for (int i = 0; i < Alternatives.Length; i++)
        {
            if (i > 0)
            {
                text.Append('|');
            }

            Alternatives[i].Write(text);
        }

        text.Append(')');
    }

    internal override RdfTerm WriteRdf(List<(RdfTerm, RdfTerm, RdfTerm)> triples, ref int blank)
    {
        RdfTerm node = Fresh(ref blank);
        triples.Add((node, Vocabulary.AlternativePath, WriteList(Alternatives, triples, ref blank)));
        return node;
    }
}

/// <summary>The kinds of path with one argument (§4.4–§4.7).</summary>
internal enum UnaryPathKind
{
    Inverse,
    ZeroOrMore,
    OneOrMore,
    ZeroOrOne,
}

/// <summary>§4.4–§4.7: inverse and the three closures.</summary>
internal sealed class UnaryPath(UnaryPathKind kind, ShapePath inner) : ShapePath
{
    internal UnaryPathKind Kind { get; } = kind;

    internal ShapePath Inner { get; } = inner;

    internal override void Write(StringBuilder text)
    {
        if (Kind == UnaryPathKind.Inverse)
        {
            text.Append("^(");
            Inner.Write(text);
            text.Append(')');
            return;
        }

        text.Append('(');
        Inner.Write(text);
        text.Append(')').Append(Kind switch
        {
            UnaryPathKind.ZeroOrMore => '*',
            UnaryPathKind.OneOrMore => '+',
            _ => '?',
        });
    }

    internal override RdfTerm WriteRdf(List<(RdfTerm, RdfTerm, RdfTerm)> triples, ref int blank)
    {
        RdfTerm node = Fresh(ref blank);
        RdfTerm predicate = Kind switch
        {
            UnaryPathKind.Inverse => Vocabulary.InversePath,
            UnaryPathKind.ZeroOrMore => Vocabulary.ZeroOrMorePath,
            UnaryPathKind.OneOrMore => Vocabulary.OneOrMorePath,
            _ => Vocabulary.ZeroOrOnePath,
        };
        triples.Add((node, predicate, Inner.WriteRdf(triples, ref blank)));
        return node;
    }
}
