// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.JsonLd.Model;

namespace Varve.JsonLd;

/// <summary>The outcome of processing one JSON-LD document.</summary>
public readonly struct JsonLdResult
{
    internal JsonLdResult(long quadCount, JsonLdError? error)
    {
        QuadCount = quadCount;
        Error = error;
    }

    /// <summary>How many quads were handed to the handler before the end or the first error; zero for expansion.</summary>
    public long QuadCount { get; }

    /// <summary>The first error, or null.</summary>
    public JsonLdError? Error { get; }

    /// <summary>Whether the document was processed whole.</summary>
    public bool Succeeded => Error is null;
}
