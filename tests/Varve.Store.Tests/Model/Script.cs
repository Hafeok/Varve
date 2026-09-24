// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Linq;
using CsCheck;

namespace Varve.Store.Tests.Model;

/// <summary>A generated request sequence: what both the store and the reference model are fed (ADR 0043).</summary>
internal sealed record Script(Step[] Steps)
{
    public override string ToString() => string.Join("\n", Steps.Select(s => s.ToString()));
}

internal abstract record Step;

/// <summary>A commit request. <see cref="Pin"/> takes a pinned read after it, kept to the end (R1).</summary>
internal sealed record CommitStep(OpSpec[] Ops, int Agent, Expect Expected, ValidatorKind Validator, bool Pin) : Step
{
    public override string ToString() =>
        "commit agent=" + Agent + " expect=" + Expected + " validator=" + Validator + (Pin ? " pin" : string.Empty)
        + "\n  " + string.Join("\n  ", Ops.Select(o => o.ToString()));
}

internal sealed record SettingsStep(AccessScope Scope, Expect Expected) : Step;

/// <summary>Moves the injected clock, backwards when negative — I5 must hold anyway.</summary>
internal sealed record ClockStep(int Seconds) : Step;

internal sealed record CheckpointStep(int Choice) : Step;

internal enum Expect
{
    None,
    Right,
    Stale,
}

internal enum ValidatorKind
{
    None,
    Accept,
    RejectForbidden,
    AttachCount,
    RejectCrowded,
}

/// <summary>
/// One operation. <see cref="Existing"/> at or above zero picks a quad the
/// model's graph holds at execution time, so retractions and redundant
/// assertions actually meet something; below zero, the terms are used.
/// </summary>
internal sealed record OpSpec(bool Assert, TermSpec S, TermSpec P, TermSpec O, int Graph, int Existing)
{
    public override string ToString() =>
        (Assert ? "+ " : "- ") + (Existing >= 0 ? "existing#" + Existing : S + " " + P + " " + O + " g" + Graph);
}

internal abstract record TermSpec;

internal sealed record IriSpec(string Local) : TermSpec
{
    public override string ToString() => ":" + Local;
}

/// <summary>A literal from a pool chosen to meet the inline rule from both sides.</summary>
internal sealed record LiteralSpec(int Which) : TermSpec
{
    public override string ToString() => "lit" + Which;
}

/// <summary>A blank node label, scoped to its request.</summary>
internal sealed record LabelSpec(char Label) : TermSpec
{
    public override string ToString() => "_:" + Label;
}

/// <summary>An existing blank node, by handle (ADR 0044).</summary>
internal sealed record ExistingBlankSpec(int Index) : TermSpec
{
    public override string ToString() => "existing-blank#" + Index;
}

/// <summary>A triple term. Its components are never blank: a request cannot wrap an existing node (ADR 0044).</summary>
internal sealed record TripleSpec(TermSpec S, TermSpec P, TermSpec O) : TermSpec
{
    public override string ToString() => "<<( " + S + " " + P + " " + O + " )>>";
}

internal static class Generators
{
    private static readonly Gen<TermSpec> Subjects = Gen.Frequency(
        (6, Gen.Int[0, 3].Select(i => (TermSpec)new IriSpec("s" + i))),
        (2, Gen.Char['a', 'b'].Select(c => (TermSpec)new LabelSpec(c))),
        (2, Gen.Int[0, 5].Select(i => (TermSpec)new ExistingBlankSpec(i))));

    private static readonly Gen<TermSpec> Predicates = Gen.Frequency(
        (6, Gen.Int[0, 1].Select(i => (TermSpec)new IriSpec("p" + i))),
        (1, Gen.Const((TermSpec)new IriSpec("forbidden"))));

    private static readonly Gen<TermSpec> Plain = Gen.Frequency(
        (3, Gen.Int[0, 3].Select(i => (TermSpec)new IriSpec("s" + i))),
        (4, Gen.Int[0, Terms.LiteralCount - 1].Select(i => (TermSpec)new LiteralSpec(i))));

    private static readonly Gen<TermSpec> Triples =
        Gen.Select(Gen.Int[0, 3], Gen.Int[0, 1], Plain, (s, p, o) =>
            (TermSpec)new TripleSpec(new IriSpec("s" + s), new IriSpec("p" + p), o));

    private static readonly Gen<TermSpec> Objects = Gen.Frequency(
        (5, Plain),
        (2, Gen.Char['a', 'b'].Select(c => (TermSpec)new LabelSpec(c))),
        (1, Gen.Int[0, 5].Select(i => (TermSpec)new ExistingBlankSpec(i))),
        (1, Triples),
        (1, Gen.Select(Triples, t => (TermSpec)new TripleSpec(new IriSpec("s0"), new IriSpec("p1"), t))));

    private static readonly Gen<OpSpec> Operations =
        Gen.Select(Gen.Bool, Subjects, Predicates, Objects, Gen.Int[0, 2], Gen.Frequency((3, Gen.Const(-1)), (2, Gen.Int[0, 50])))
            .Select(x => new OpSpec(x.Item1, x.Item2, x.Item3, x.Item4, x.Item5, x.Item6));

    /// <summary>
    /// Operations, sometimes followed by the retraction of one they asserted —
    /// the assert-then-retract of an absent quad that must contribute nothing.
    /// </summary>
    private static readonly Gen<OpSpec[]> OperationLists =
        Gen.Select(Operations.Array[1, 6], Gen.Int[0, 3], (ops, undo) =>
        {
            int index = System.Array.FindIndex(ops, o => o.Assert && o.Existing < 0);
            return undo == 0 && index >= 0 ? [.. ops, ops[index] with { Assert = false }] : ops;
        });

    private static readonly Gen<Expect> Expectations =
        Gen.Frequency((6, Gen.Const(Expect.None)), (3, Gen.Const(Expect.Right)), (1, Gen.Const(Expect.Stale)));

    private static readonly Gen<ValidatorKind> Validators = Gen.Frequency(
        (5, Gen.Const(ValidatorKind.None)),
        (1, Gen.Const(ValidatorKind.Accept)),
        (1, Gen.Const(ValidatorKind.RejectForbidden)),
        (1, Gen.Const(ValidatorKind.AttachCount)),
        (1, Gen.Const(ValidatorKind.RejectCrowded)));

    private static readonly Gen<Step> Steps = Gen.Frequency(
        (12, Gen.Select(OperationLists, Gen.Int[0, 3], Expectations, Validators, Gen.Bool)
            .Select(x => (Step)new CommitStep(x.Item1, x.Item2, x.Item3, x.Item4, x.Item5))),
        (1, Gen.Select(Gen.Enum<AccessScope>(), Expectations, (s, e) => (Step)new SettingsStep(s, e))),
        (2, Gen.Int[-30, 90].Select(s => (Step)new ClockStep(s))),
        (1, Gen.Int[0, 100].Select(c => (Step)new CheckpointStep(c))));

    /// <summary>Request sequences of up to 24 steps.</summary>
    internal static readonly Gen<Script> Scripts = Steps.Array[1, 24].Select(steps => new Script(steps));

    /// <summary>Only commits, no checkpoints: for properties about the log's bytes.</summary>
    internal static readonly Gen<Script> CommitsOnly =
        Gen.Frequency(
            (8, Gen.Select(OperationLists, Gen.Int[0, 3], Validators)
                .Select(x => (Step)new CommitStep(x.Item1, x.Item2, Expect.None, x.Item3, false))),
            (1, Gen.Int[-30, 90].Select(s => (Step)new ClockStep(s))),
            (1, Gen.Enum<AccessScope>().Select(s => (Step)new SettingsStep(s, Expect.None))))
        .Array[1, 16].Select(steps => new Script(steps));
}
