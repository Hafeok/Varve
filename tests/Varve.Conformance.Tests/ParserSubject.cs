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
/// The quads read, in canonical N-Quads form, when the subject can supply them.
/// Empty until there is something that can. Evaluation tests will need it;
/// syntax tests do not, which is why milestone 1 wires syntax suites only.
/// </param>
internal sealed record ParseOutcome(bool Succeeded, string? Error, IReadOnlyList<string> Quads)
{
    internal static ParseOutcome Parsed(IReadOnlyList<string> quads) => new(true, null, quads);

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
    ParseOutcome Parse(RdfFormat format, string path);
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
