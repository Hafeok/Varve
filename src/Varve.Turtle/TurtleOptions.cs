// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Turtle;

/// <summary>Receives a prefix binding as the document declares it.</summary>
/// <remarks>
/// The prefix is given without its trailing colon. A document may bind the same
/// prefix twice, and both bindings are reported: the sequence is the fact, and
/// a caller that wants a map builds the one it needs (ADR 0030).
/// </remarks>
[Contract(typeof(TurtleRecoveryAndPrefixes.PrefixesReportedAsDeclared), Role = "receives each prefix binding as the document declares it")]
public delegate void PrefixHandler(ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> iri);

/// <summary>Receives a base IRI as the document declares it, already resolved.</summary>
[Contract(typeof(TurtleRecoveryAndPrefixes.PrefixesReportedAsDeclared), Role = "receives each base IRI as the document declares it")]
public delegate void BaseHandler(ReadOnlySpan<byte> iri);

/// <summary>How to parse Turtle or TriG.</summary>
/// <remarks>
/// <c>default(TurtleOptions)</c> is Turtle with IRIs validated, no recovery and
/// no base — which is why <see cref="ValidateIris"/> is stored inverted, as in
/// <see cref="ParseOptions"/>.
/// </remarks>
public readonly struct TurtleOptions
{
    // IDE0032 wants an auto property here and cannot have one: the field is
    // stored inverted precisely so that default(TurtleOptions) validates.
#pragma warning disable IDE0032
    private readonly bool _skipIriValidation;
#pragma warning restore IDE0032

    /// <summary>Turtle or TriG. Turtle by default.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public RdfSyntax Syntax { get; init; }

    /// <summary>
    /// The document's retrieval IRI, against which a relative IRI is resolved
    /// until a <c>@base</c> directive says otherwise (Turtle §6.3). Empty means
    /// there is none, and a relative IRI is then an error rather than something
    /// silently accepted.
    /// </summary>
    public ReadOnlyMemory<byte> BaseIri { get; init; }

    /// <summary>
    /// Called for each rejected statement. Null — the default — means the first
    /// error ends the parse.
    /// </summary>
    public ErrorHandler? OnError { get; init; }

    /// <summary>Called for each prefix binding, in document order.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public PrefixHandler? OnPrefix { get; init; }

    /// <summary>Called for each base directive, in document order.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public BaseHandler? OnBase { get; init; }

    /// <summary>
    /// Whether each IRI is checked against RFC 3987 and required to have a
    /// scheme once resolved. True by default.
    /// </summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool ValidateIris
    {
        get => !_skipIriValidation;
        init => _skipIriValidation = !value;
    }
}
