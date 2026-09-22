// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Rdf;

/// <summary>
/// The backing store for a quad's worth of term views: a reusable set of term
/// slots and a scratch buffer for bytes that do not occur in the input.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0024. A <see cref="RdfTermView"/> is <c>(arena, text, index)</c> rather
/// than a recursive value, because a <c>ref struct</c> cannot contain itself
/// and triple terms nest. The arena is what the index indexes.
/// </para>
/// <para>
/// A parser owns one arena, calls <see cref="Reset"/> once per quad and lets
/// both buffers grow to the largest quad it has seen. After the first few
/// quads nothing is allocated, which is what makes the streaming path
/// allocation-free.
/// </para>
/// <para>
/// This type is public so that a parser in layer 2 can build views over a
/// representation that layer 1 owns. The slot layout stays private, so the
/// representation is still this package's business.
/// </para>
/// </remarks>
public sealed class TermArena
{
    private Slot[] _slots = new Slot[8];
    private byte[] _scratch = new byte[256];
    /// <summary>How many slots are in use.</summary>
    public int Count { get; private set; }

    /// <summary>How many scratch bytes are in use.</summary>
    public int ScratchLength { get; private set; }

    /// <summary>
    /// Drops every slot and every scratch byte, keeping the buffers. Call once
    /// per quad; every view handed out before it becomes meaningless.
    /// </summary>
    public void Reset()
    {
        Count = 0;
        ScratchLength = 0;
    }

    /// <summary>
    /// Space at the tail of the scratch buffer for at least
    /// <paramref name="minimumLength"/> bytes. Write into it, then call
    /// <see cref="CommitScratch"/> with how many bytes were written.
    /// </summary>
    public Span<byte> ReserveScratch(int minimumLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimumLength);

        if (_scratch.Length - ScratchLength < minimumLength)
        {
            int wanted = Math.Max(_scratch.Length * 2, ScratchLength + minimumLength);
            Array.Resize(ref _scratch, wanted);
        }

        return _scratch.AsSpan(ScratchLength);
    }

    /// <summary>
    /// Takes the bytes just written into <see cref="ReserveScratch"/> and
    /// returns the span naming them.
    /// </summary>
    public TermSpan CommitScratch(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, _scratch.Length - ScratchLength);

        TermSpan span = TermSpan.FromScratch(ScratchLength, length);
        ScratchLength += length;
        return span;
    }

    /// <summary>Copies bytes into the scratch buffer in one step.</summary>
    public TermSpan AppendScratch(ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(ReserveScratch(bytes.Length));
        return CommitScratch(bytes.Length);
    }

    /// <summary>Adds an IRI slot and returns its index.</summary>
    public int AddIri(TermSpan lexical) =>
        Add(RdfTermKind.Iri, lexical, TermSpan.None, TermSpan.None, TextDirection.None);

    /// <summary>Adds a blank node slot, from its label without <c>_:</c>.</summary>
    public int AddBlankNode(TermSpan label) =>
        Add(RdfTermKind.BlankNode, label, TermSpan.None, TermSpan.None, TextDirection.None);

    /// <summary>
    /// Adds a literal slot. Pass <see cref="TermSpan.None"/> for an absent
    /// datatype or language; both present is not a well-formed literal and is
    /// the caller's to reject.
    /// </summary>
    public int AddLiteral(
        TermSpan lexical,
        TermSpan datatype,
        TermSpan language,
        TextDirection direction) =>
        Add(RdfTermKind.Literal, lexical, datatype, language, direction);

    /// <summary>Adds a triple term slot over three slots already added.</summary>
    public int AddTripleTerm(int subject, int predicate, int @object)
    {
        ValidateIndex(subject);
        ValidateIndex(predicate);
        ValidateIndex(@object);

        int index = Add(RdfTermKind.TripleTerm, TermSpan.None, TermSpan.None, TermSpan.None, TextDirection.None);
        _slots[index].Subject = subject;
        _slots[index].Predicate = predicate;
        _slots[index].Object = @object;
        return index;
    }

    /// <summary>A view of one slot over the input the parser is reading.</summary>
    public RdfTermView View(ReadOnlySpan<byte> text, int index)
    {
        ValidateIndex(index);
        return new RdfTermView(this, text, index);
    }

    /// <summary>
    /// A view of a quad over four slots. Pass <c>-1</c> for
    /// <paramref name="graph"/> to mean the default graph.
    /// </summary>
    public QuadView Quad(ReadOnlySpan<byte> text, int subject, int predicate, int @object, int graph)
    {
        ValidateIndex(subject);
        ValidateIndex(predicate);
        ValidateIndex(@object);

        if (graph >= 0)
        {
            ValidateIndex(graph);
        }

        return new QuadView(this, text, subject, predicate, @object, graph);
    }

    /// <summary>
    /// The bytes a <see cref="TermSpan"/> names, over the text it was taken
    /// from. Public because a parser at layer 2 has to read back a span it just
    /// recorded — a datatype IRI, say, to check it is not one the grammar
    /// permits and the model forbids.
    /// </summary>
    public ReadOnlySpan<byte> Bytes(ReadOnlySpan<byte> text, TermSpan span) => Resolve(text, span);

    internal RdfTermKind KindOf(int index) => _slots[index].Kind;

    internal TextDirection DirectionOf(int index) => _slots[index].Direction;

    internal TermSpan LexicalOf(int index) => _slots[index].Lexical;

    internal TermSpan DatatypeOf(int index) => _slots[index].Datatype;

    internal TermSpan LanguageOf(int index) => _slots[index].Language;

    internal int SubjectOf(int index) => _slots[index].Subject;

    internal int PredicateOf(int index) => _slots[index].Predicate;

    internal int ObjectOf(int index) => _slots[index].Object;

    internal ReadOnlySpan<byte> Resolve(ReadOnlySpan<byte> text, TermSpan span) =>
        !span.IsPresent
            ? default
            : span.IsScratch
                ? _scratch.AsSpan(span.Start, span.Length)
                : text.Slice(span.Start, span.Length);

    private int Add(
        RdfTermKind kind,
        TermSpan lexical,
        TermSpan datatype,
        TermSpan language,
        TextDirection direction)
    {
        if (Count == _slots.Length)
        {
            Array.Resize(ref _slots, _slots.Length * 2);
        }

        ref Slot slot = ref _slots[Count];
        slot.Kind = kind;
        slot.Lexical = lexical;
        slot.Datatype = datatype;
        slot.Language = language;
        slot.Direction = direction;
        slot.Subject = -1;
        slot.Predicate = -1;
        slot.Object = -1;
        return Count++;
    }

    private void ValidateIndex(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
    }

    private struct Slot
    {
        public RdfTermKind Kind;
        public TextDirection Direction;
        public TermSpan Lexical;
        public TermSpan Datatype;
        public TermSpan Language;
        public int Subject;
        public int Predicate;
        public int Object;
    }
}
