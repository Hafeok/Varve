// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.RdfXml.Model;

/// <summary>
/// Why an RDF/XML document was rejected, and where.
/// </summary>
/// <remarks>
/// <para>
/// The position is the XML reader's: a 1-based line and a 1-based column
/// <strong>counted in UTF-16 characters</strong>, with no byte offset. That
/// is a departure from the other syntax packages, whose positions are byte
/// offsets and byte columns (<c>syntax-model-surfaces</c>), and it is the
/// stated cost of reading through <c>System.Xml.XmlReader</c> (ADR 0111):
/// the reader decodes the document's encoding before this package sees a
/// character, so a byte offset would be a number this package would have to
/// invent. A column in characters is what an XML editor shows, which is
/// where a position in an XML document is used.
/// </para>
/// </remarks>
public sealed class RdfXmlParseError
{
    internal RdfXmlParseError(RdfXmlErrorKind kind, int line, int column, string message)
    {
        Kind = kind;
        Line = line;
        Column = column;
        Message = message;
    }

    /// <summary>What was wrong.</summary>
    public RdfXmlErrorKind Kind { get; }

    /// <summary>The 1-based line, or 0 when the reader could not say.</summary>
    [DesignDecision(typeof(RdfXmlOverSystemXml.PositionsAreTheXmlReaders), Scope = ExceptionScope.Boundary)]
    public int Line { get; }

    /// <summary>The 1-based column in UTF-16 characters, or 0 when the reader could not say.</summary>
    [DesignDecision(typeof(RdfXmlOverSystemXml.PositionsAreTheXmlReaders), Scope = ExceptionScope.Boundary)]
    public int Column { get; }

    /// <summary>Text for a person. Nothing parses it; <see cref="Kind"/> is the half a program reads.</summary>
    [DesignDecision(typeof(SyntaxModelSurfaces.ErrorMessagesAreDisplayText), Scope = ExceptionScope.Boundary)]
    public string Message { get; }

    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Line}:{Column}: {Kind}: {Message}");
}
