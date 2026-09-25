// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.Rdf;

namespace Varve.Sparql.Evaluation.Operators;

internal enum PositionKind : byte
{
    /// <summary>A constant the source holds, by handle.</summary>
    Constant,

    /// <summary>A variable, by slot.</summary>
    Slot,

    /// <summary>A constant the source does not hold: nothing can match.</summary>
    Never,

    /// <summary>A triple term with a variable inside (1.2): matched by externalising and unifying.</summary>
    Nested,
}

/// <summary>One position of a triple pattern, resolved once per execution (§5.3).</summary>
internal readonly struct PatternPosition
{
    private PatternPosition(PositionKind kind, TermHandle handle, int slot, RdfTerm? constant, NestedPattern? nested)
    {
        Kind = kind;
        Handle = handle;
        Slot = slot;
        Constant = constant;
        Nested = nested;
    }

    internal PositionKind Kind { get; }

    internal TermHandle Handle { get; }

    internal int Slot { get; }

    /// <summary>The constant's term, for unification inside a triple term.</summary>
    internal RdfTerm? Constant { get; }

    internal NestedPattern? Nested { get; }

    internal static PatternPosition OfConstant(TermHandle handle, RdfTerm term) => new(PositionKind.Constant, handle, -1, term, null);

    internal static PatternPosition OfSlot(int slot) => new(PositionKind.Slot, default, slot, null, null);

    internal static PatternPosition OfNever(RdfTerm term) => new(PositionKind.Never, default, -1, term, null);

    internal static PatternPosition OfNested(NestedPattern nested) => new(PositionKind.Nested, default, -1, null, nested);
}

/// <summary>A triple term pattern with variables: three positions, any of which may nest again.</summary>
internal sealed class NestedPattern(PatternPosition subject, PatternPosition predicate, PatternPosition @object)
{
    internal PatternPosition Subject { get; } = subject;

    internal PatternPosition Predicate { get; } = predicate;

    internal PatternPosition Object { get; } = @object;
}

/// <summary>A triple pattern, resolved.</summary>
internal sealed class TriplePatternSpec(PatternPosition subject, PatternPosition predicate, PatternPosition @object)
{
    internal PatternPosition Subject { get; } = subject;

    internal PatternPosition Predicate { get; } = predicate;

    internal PatternPosition Object { get; } = @object;

    internal bool CanMatch =>
        Subject.Kind != PositionKind.Never && Predicate.Kind != PositionKind.Never && Object.Kind != PositionKind.Never;
}
