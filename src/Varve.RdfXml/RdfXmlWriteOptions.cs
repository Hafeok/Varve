// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.RdfXml;

/// <summary>How to write RDF/XML.</summary>
/// <remarks>
/// <c>default(RdfXmlWriteOptions)</c> indents, which is why <see cref="Indent"/>
/// is stored inverted, as the other writers' options are.
/// </remarks>
public readonly struct RdfXmlWriteOptions
{
#pragma warning disable IDE0032 // stored inverted so that default(RdfXmlWriteOptions) indents
    private readonly bool _compact;
#pragma warning restore IDE0032

    /// <summary>
    /// Whether to indent one element per line, two spaces a level. True by
    /// default. Whitespace between elements is not content in RDF/XML, so
    /// both forms denote the same graph; the compact form is for a document
    /// nobody reads.
    /// </summary>
    public bool Indent
    {
        get => !_compact;
        init => _compact = !value;
    }
}
