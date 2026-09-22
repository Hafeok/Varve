// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;

namespace Varve.Conformance.Tests;

/// <summary>What happened when a subject was asked to parse a file.</summary>
/// <param name="Succeeded">Whether the input was accepted.</param>
/// <param name="Error">
/// Why it was rejected, when it was. A negative syntax test cares only that a
/// rejection happened, but the message is what makes a surprising rejection
/// diagnosable.
/// </param>
/// <param name="Quads">
/// The quads read, each term in canonical N-Triples term syntax. What an
/// evaluation test compares, and what a syntax test's failure message can show
/// so that a surprising result says what was parsed rather than only that
/// something was.
/// </param>
internal sealed record ParseOutcome(bool Succeeded, string? Error, IReadOnlyList<ParsedQuad> Quads)
{
    internal static ParseOutcome Parsed(IReadOnlyList<ParsedQuad> quads) => new(true, null, quads);

    internal static ParseOutcome Rejected(string error) => new(false, error, []);
}

/// <summary>
/// The thing under test, as the harness needs to see it.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately <em>not</em> a design for the parser API. It is test
/// code, and the adapter that implements it will wrap whatever the real API
/// becomes. Designing the parser here, before a parser exists, would fix its
/// shape against the convenience of a test harness — which is the wrong way
/// round.
/// </para>
/// </remarks>
internal interface IParserSubject
{
    /// <summary>
    /// Parses <paramref name="path"/> as <paramref name="format"/>, reporting
    /// whether it was accepted rather than throwing.
    /// </summary>
    /// <param name="format">The syntax to read it as.</param>
    /// <param name="path">The file on disk.</param>
    /// <param name="baseIri">
    /// The IRI the file is published at. Several suite inputs are relative
    /// throughout and do not parse without it.
    /// </param>
    ParseOutcome Parse(RdfFormat format, string path, string baseIri);

    /// <summary>
    /// The same parse, with the input delivered as two segments split at
    /// <paramref name="at"/>.
    /// </summary>
    /// <remarks>
    /// The answer must not depend on where the split falls, and a subject that
    /// cannot be fed in pieces has no business claiming to stream. Separate
    /// from <see cref="Parse"/> so that a subject which genuinely has only a
    /// whole-document API can say so by throwing, rather than by quietly
    /// reporting agreement it never tested.
    /// </remarks>
    ParseOutcome ParseSplit(RdfFormat format, string path, string baseIri, int at);
}

/// <summary>
/// The registry of parser subjects.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Nothing is registered, and that is the correct state for milestone
/// 1.</strong> There is no parser: the repository deliberately contains no
/// production types yet. Every manifest entry therefore fails with "no parser
/// registered", which is this harness's own first test — a run reporting
/// anything else, including zero cases, is a harness defect and not a parser
/// result.
/// </para>
/// <para>
/// When <c>Varve.Rdf</c> and the N-Triples reader land at milestone 3, an
/// adapter is written here and this property is set once, at startup.
/// </para>
/// </remarks>
internal static class ParserSubjects
{
    /// <summary>
    /// The registered subject, or <see langword="null"/> when there is none.
    /// </summary>
    internal static IParserSubject? Current { get; set; }
}
