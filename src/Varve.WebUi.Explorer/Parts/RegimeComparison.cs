// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Delta.Model;
using Varve.Protocol.Client.Model;
using Varve.Rdf;

namespace Varve.WebUi.Explorer.Parts;

/// <summary>How one statement of the regime's answer is marked (ADR 0254 point 3).</summary>
public enum StatementMark
{
    /// <summary>In <c>simple</c>'s answer too: stored, not added by the regime.</summary>
    Asserted,

    /// <summary>Added by the regime, and asserted by a readable TBox graph.</summary>
    FromTheOntology,

    /// <summary>Added by the regime, and asserted by no readable graph.</summary>
    Inferred,

    /// <summary>
    /// It holds a blank node the query made (a <c>CONSTRUCT</c> template's or
    /// <c>BNODE()</c>'s), whose label names a node of one execution only, so
    /// nothing matches it in the other read.
    /// </summary>
    NotCompared,
}

/// <summary>
/// What the regime added, as the view marks it: one mark per statement of the
/// regime's answer, in its order, or none at all and the reason.
/// </summary>
/// <param name="Marks">A mark per statement of the regime's answer, or null when no statement is marked.</param>
/// <param name="Unmarked">Why no statement is marked, when <paramref name="Marks"/> is null; else null.</param>
/// <param name="NotCompared">How many statements hold a blank node the query made, and so are not compared.</param>
public sealed record RegimeMarks(IReadOnlyList<StatementMark>? Marks, string? Unmarked, int NotCompared)
{
    /// <summary>The line the view shows under the marked graph when some statements were not compared; null when none was.</summary>
    public string? NotComparedNote => NotCompared == 0
        ? null
        : NotCompared.ToString("N0", CultureInfo.InvariantCulture)
            + (NotCompared == 1 ? " statement holds" : " statements hold")
            + " a blank node this query made, which two reads cannot match, so "
            + (NotCompared == 1 ? "it is" : "they are")
            + " not marked.";
}

/// <summary>
/// What a regime added (ADR 0254 point 3): the regime's answer, read A, less
/// <c>simple</c>'s at the same position, read B, as statements. Of what A
/// adds, a statement a readable TBox graph asserts is from the ontology, and
/// the rest inferred.
/// </summary>
/// <remarks>
/// <para>
/// <b>Blank nodes.</b> A blank node of the store is written with the label
/// its identity gives it, <c>b</c> and a number, the same in every response
/// and at every position (ADR 0098 point 1). So a statement whose blank nodes
/// are all the store's is compared by its terms, labels included, like any
/// other. A blank node the evaluator makes is labelled <c>q</c> and a number,
/// unique in one execution only (<c>sparql-evaluation.md</c> §7.9): <c>q3</c>
/// in read A and <c>q3</c> in read B are unrelated nodes. Nothing makes them
/// the same, so a statement that holds one anywhere, inside a triple term
/// too, is not compared: it is marked <see cref="StatementMark.NotCompared"/>,
/// drawn plain, and counted in the note under the graph. Any label that is
/// not a store's is treated so: only a store's label is known to name one
/// node across reads. Entailment adds no blank node of its own: the regimes
/// rewrite the query over stored terms.
/// </para>
/// <para>
/// <b>Cut reads.</b> A − B means something only when both reads ran to their
/// end at one position. When either stopped at the ceiling, was cancelled,
/// broke before the server finished it, or the two were read at different
/// positions, no statement is marked, and the view says why.
/// </para>
/// </remarks>
[Contract(typeof(TheDataExplorersReads.RegimeAddedIsTwoReadsAtOnePosition), Role = "the marks of what a regime added, from two whole reads at one position")]
public static class RegimeComparison
{
    /// <summary>Marks the regime's answer against <c>simple</c>'s; see the remarks for blank nodes and for reads that did not end.</summary>
    /// <param name="regime">Read A: the statements the regime answered, in their order.</param>
    /// <param name="regimeEnd">How read A ended.</param>
    /// <param name="regimeAt">The position read A was read at.</param>
    /// <param name="simple">Read B: the statements <c>simple</c> answered.</param>
    /// <param name="simpleEnd">How read B ended.</param>
    /// <param name="simpleAt">The position read B was read at.</param>
    /// <param name="ontology">The statements the readable TBox graphs assert.</param>
    public static RegimeMarks Compare(
        IReadOnlyList<Statement> regime,
        QueryEnd regimeEnd,
        FeedPosition? regimeAt,
        IReadOnlyCollection<Statement> simple,
        QueryEnd simpleEnd,
        FeedPosition? simpleAt,
        IReadOnlySet<Statement> ontology)
    {
        ArgumentNullException.ThrowIfNull(regime);
        ArgumentNullException.ThrowIfNull(simple);
        ArgumentNullException.ThrowIfNull(ontology);

        if ((Why(regimeEnd, "under the regime") ?? Why(simpleEnd, "under simple")) is string why)
        {
            return new RegimeMarks(null, "Nothing is marked as added: " + why + ", so what the regime added cannot be told.", 0);
        }

        if (regimeAt is null || simpleAt is null || regimeAt != simpleAt)
        {
            return new RegimeMarks(null, "Nothing is marked as added: the two reads were not at one position, so what the regime added cannot be told.", 0);
        }

        HashSet<Statement> stored = [.. simple.Where(Comparable)];
        StatementMark[] marks = new StatementMark[regime.Count];
        int notCompared = 0;

        for (int i = 0; i < regime.Count; i++)
        {
            Statement statement = regime[i];

            if (!Comparable(statement))
            {
                marks[i] = StatementMark.NotCompared;
                notCompared++;
            }
            else if (stored.Contains(statement))
            {
                marks[i] = StatementMark.Asserted;
            }
            else
            {
                marks[i] = ontology.Contains(statement) ? StatementMark.FromTheOntology : StatementMark.Inferred;
            }
        }

        return new RegimeMarks(marks, null, notCompared);
    }

    /// <summary>Whether a blank node's label is a store's, <c>b</c> and a decimal number, which names one node in every read (ADR 0098).</summary>
    public static bool IsStoreLabel(ReadOnlySpan<byte> label)
    {
        if (label.Length < 2 || label[0] != (byte)'b')
        {
            return false;
        }

        foreach (byte b in label[1..])
        {
            if (b is < (byte)'0' or > (byte)'9')
            {
                return false;
            }
        }

        return true;
    }

    // Why a read that did not run to its end leaves nothing marked; null when it did.
    private static string? Why(QueryEnd end, string read) => end switch
    {
        QueryEnd.Complete => null,
        QueryEnd.Closed => "the read " + read + " stopped before its end, at the ceiling or by a cancel",
        QueryEnd.Cut => "the read " + read + " ended before the server finished it",
        _ => "the read " + read + " has not ended",
    };

    private static bool Comparable(Statement statement) =>
        Comparable(statement.Subject) && Comparable(statement.Predicate) && Comparable(statement.Object);

    private static bool Comparable(RdfTerm term) => term.Kind switch
    {
        RdfTermKind.BlankNode => IsStoreLabel(term.Lexical),
        RdfTermKind.TripleTerm => Comparable(term.Subject!) && Comparable(term.Predicate!) && Comparable(term.Object!),
        _ => true,
    };
}
