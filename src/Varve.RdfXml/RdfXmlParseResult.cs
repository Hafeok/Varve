// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.RdfXml.Model;

namespace Varve.RdfXml;

/// <summary>What a parse came to.</summary>
public readonly struct RdfXmlParseResult
{
    internal RdfXmlParseResult(long quadCount, RdfXmlParseError? error)
    {
        QuadCount = quadCount;
        Error = error;
    }

    /// <summary>How many quads were handed to the callback before the parse ended.</summary>
    public long QuadCount { get; }

    /// <summary>
    /// Why the parse stopped, or null when it read the whole document. RDF/XML
    /// has no recovery unit (<c>rdf-xml.md</c> §5): the first error ends the
    /// parse, and the quads already handed out stand.
    /// </summary>
    public RdfXmlParseError? Error { get; }

    /// <summary>Whether the whole document was read without error.</summary>
    public bool Succeeded => Error is null;
}
