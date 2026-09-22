// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.Rdf;

namespace Varve.Turtle;

/// <summary>Receives one quad. The view is valid only for the duration of the call.</summary>
/// <remarks>
/// A custom delegate rather than <c>Action&lt;QuadView&gt;</c>, because a
/// <c>ref struct</c> cannot be a generic type argument.
/// </remarks>
public delegate void QuadHandler(in QuadView quad);

/// <summary>Receives one rejected line and says whether to carry on.</summary>
public delegate ErrorAction ErrorHandler(in ParseError error);

/// <summary>How to parse.</summary>
/// <remarks>
/// <para>
/// <c>default(ParseOptions)</c> is the intended configuration: N-Triples, IRIs
/// validated, no recovery. That is why <see cref="ValidateIris"/> is stored
/// inverted — a bool field defaults to false, and the safe default here is
/// true.
/// </para>
/// </remarks>
public readonly struct ParseOptions
{
    // IDE0032 wants an auto property here and cannot have one: the field is
    // stored inverted precisely so that default(ParseOptions) validates.
#pragma warning disable IDE0032
    private readonly bool _skipIriValidation;
#pragma warning restore IDE0032

    /// <summary>Which syntax to read. N-Triples by default.</summary>
    public RdfSyntax Syntax { get; init; }

    /// <summary>
    /// Called for each rejected line. Null — the default — means the first
    /// error ends the parse. Supplying a handler is how a caller opts in to
    /// reading a damaged file, and it is then told about every line it lost.
    /// </summary>
    public ErrorHandler? OnError { get; init; }

    /// <summary>
    /// Whether each IRI is checked against RFC 3987 and required to have a
    /// scheme. True by default: N-Triples requires absolute IRIs, and a parser
    /// that accepted relative ones would produce terms no RDF consumer can use.
    /// </summary>
    public bool ValidateIris
    {
        get => !_skipIriValidation;
        init => _skipIriValidation = !value;
    }
}
